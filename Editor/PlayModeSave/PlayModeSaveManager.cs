using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Play Mode Save - Adds a "Save to Scene" button in the Inspector header during Play Mode.
    /// Clicking it captures all component values of the selected GameObject.
    /// On Play Mode exit, captured values are applied back to the scene objects with Undo support.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeSaveManager
    {
        [Serializable]
        private class ComponentSnapshot
        {
            public string typeName;
            public string json;
        }

        [Serializable]
        private class ObjectSnapshot
        {
            public string globalId;
            public string objectName;
            public List<ComponentSnapshot> components = new List<ComponentSnapshot>();
        }

        // Kept alive across domain reloads via SessionState JSON
        private static readonly string SessionKey = "NoMorePain.PlayModeSave.Snapshots";
        private static List<ObjectSnapshot> _snapshots;

        static PlayModeSaveManager()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnInspectorHeaderGUI;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static List<ObjectSnapshot> Snapshots
        {
            get
            {
                if (_snapshots == null)
                {
                    var json = SessionState.GetString(SessionKey, "");
                    _snapshots = string.IsNullOrEmpty(json)
                        ? new List<ObjectSnapshot>()
                        : JsonUtility.FromJson<SnapshotList>(json)?.items ?? new List<ObjectSnapshot>();
                }
                return _snapshots;
            }
        }

        [Serializable]
        private class SnapshotList { public List<ObjectSnapshot> items; }

        private static void SaveSnapshots()
        {
            SessionState.SetString(SessionKey, JsonUtility.ToJson(new SnapshotList { items = _snapshots }));
        }

        private static void OnInspectorHeaderGUI(UnityEditor.Editor editor)
        {
            if (!NMPSettings.PlayModeSave) return;
            if (!EditorApplication.isPlaying) return;
            if (editor.target is not GameObject go) return;
            if (!go.scene.IsValid()) return; // Skip project assets

            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            int savedIndex = Snapshots.FindIndex(s => s.globalId == globalId);
            bool isSaved = savedIndex >= 0;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                var prevColor = GUI.color;
                GUI.color = isSaved ? new Color(0.4f, 1f, 0.4f) : Color.white;

                var iconName  = isSaved ? "GreenCheckmark" : "SaveActive";
                var labelText = isSaved ? " Saved"         : " Save";
                var tooltip   = isSaved
                    ? "Values captured — will apply on Play Mode exit.\nClick to re-capture."
                    : "Save component values.\nThey will be applied when exiting Play Mode.";

                var icon    = EditorGUIUtility.IconContent(iconName).image;
                var content = new GUIContent(labelText, icon, tooltip);

                if (GUILayout.Button(content, NMPStyles.SaveButton, GUILayout.Height(22)))
                    CaptureGameObject(go, globalId, savedIndex);

                GUI.color = prevColor;

                if (isSaved)
                {
                    if (GUILayout.Button(new GUIContent("✕", "Remove snapshot — won't apply on exit"),
                            EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(22)))
                    {
                        Snapshots.RemoveAt(savedIndex);
                        SaveSnapshots();
                    }
                }
            }
        }

        private static void CaptureGameObject(GameObject go, string globalId, int existingIndex)
        {
            var snapshot = new ObjectSnapshot
            {
                globalId = globalId,
                objectName = go.name
            };

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                try
                {
                    snapshot.components.Add(new ComponentSnapshot
                    {
                        typeName = comp.GetType().AssemblyQualifiedName,
                        json = EditorJsonUtility.ToJson(comp, false)
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NoMorePain] Could not capture {comp.GetType().Name} on '{go.name}': {e.Message}");
                }
            }

            if (existingIndex >= 0)
                Snapshots[existingIndex] = snapshot;
            else
                Snapshots.Add(snapshot);

            SaveSnapshots();
            Debug.Log($"[NoMorePain] Saved {snapshot.components.Count} components on '{go.name}'.");
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            if (Snapshots.Count == 0) return;

            int restoredObjects = 0;
            int restoredComponents = 0;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Play Mode Save");

            foreach (var snapshot in Snapshots)
            {
                if (!GlobalObjectId.TryParse(snapshot.globalId, out var globalId))
                {
                    Debug.LogWarning($"[NoMorePain] Could not parse GlobalObjectId for '{snapshot.objectName}'.");
                    continue;
                }

                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
                if (obj is not GameObject go)
                {
                    Debug.LogWarning($"[NoMorePain] Could not find scene object '{snapshot.objectName}'. Was it deleted?");
                    continue;
                }

                var components = go.GetComponents<Component>();
                int applied = 0;

                for (int i = 0; i < Mathf.Min(components.Length, snapshot.components.Count); i++)
                {
                    if (components[i] == null) continue;
                    var compSnap = snapshot.components[i];
                    if (components[i].GetType().AssemblyQualifiedName != compSnap.typeName) continue;

                    try
                    {
                        Undo.RecordObject(components[i], "Play Mode Save");
                        EditorJsonUtility.FromJsonOverwrite(compSnap.json, components[i]);
                        applied++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[NoMorePain] Failed to restore {compSnap.typeName}: {e.Message}");
                    }
                }

                EditorUtility.SetDirty(go);
                restoredObjects++;
                restoredComponents += applied;
            }

            _snapshots = null;
            SessionState.EraseString(SessionKey);

            if (restoredComponents > 0)
                Debug.Log($"[NoMorePain] Play Mode Save: restored {restoredComponents} components on {restoredObjects} object(s). (Ctrl+Z to undo)");
        }
    }
}
