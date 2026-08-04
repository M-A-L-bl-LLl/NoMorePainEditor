using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
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
            public string globalId;
            public string typeName;
            public int typeIndex;
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
        private const string SessionKey = "NoMorePain.PlayModeSave.Snapshots";
        private const int MaxRestoreAttempts = 30;

        private static List<ObjectSnapshot> _snapshots;
        private static bool _restoreScheduled;
        private static int _restoreAttempt;

        static PlayModeSaveManager()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Recover pending snapshots even if a domain reload happened around EnteredEditMode
            // before this class had a chance to receive the state-change callback.
            if (!EditorApplication.isPlayingOrWillChangePlaymode && HasStoredSnapshots())
                ScheduleRestore(resetAttempts: true);
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

        private static bool HasStoredSnapshots()
        {
            return !string.IsNullOrEmpty(SessionState.GetString(SessionKey, string.Empty));
        }

        private static void SaveSnapshots()
        {
            if (_snapshots == null || _snapshots.Count == 0)
            {
                SessionState.EraseString(SessionKey);
                return;
            }

            SessionState.SetString(SessionKey, JsonUtility.ToJson(new SnapshotList { items = _snapshots }));
        }

        private static void ClearSnapshotStorage()
        {
            _snapshots = null;
            _restoreAttempt = 0;
            SessionState.EraseString(SessionKey);
        }

        internal static bool CanShowInline(UnityEditor.Editor editor)
        {
            if (!NMPSettings.PlayModeSave) return false;
            if (!EditorApplication.isPlaying) return false;
            if (editor.target is not GameObject go) return false;
            return go.scene.IsValid(); // Skip project assets
        }

        internal static void DrawInlineSaveControls(GameObject go)
        {
            if (go == null) return;
            if (!NMPSettings.PlayModeSave) return;
            if (!EditorApplication.isPlaying) return;
            if (!go.scene.IsValid()) return;

            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            int savedIndex = Snapshots.FindIndex(s => s.globalId == globalId);
            bool isSaved = savedIndex >= 0;

            var prevColor = GUI.color;
            GUI.color = isSaved ? new Color(0.4f, 1f, 0.4f) : Color.white;

            var iconName  = isSaved ? "GreenCheckmark" : "SaveActive";
            var labelText = isSaved ? " Saved"         : " Save";
            var tooltip   = isSaved
                ? "Values captured - will apply on Play Mode exit.\nClick to re-capture."
                : "Save component values.\nThey will be applied when exiting Play Mode.";

            var icon    = EditorGUIUtility.IconContent(iconName).image;
            var content = new GUIContent(labelText, icon, tooltip);

            if (GUILayout.Button(content, NMPStyles.SaveButton, GUILayout.Height(NMPStyles.TabHeight + 8f)))
                CaptureGameObject(go, globalId, savedIndex);

            GUI.color = prevColor;

            if (isSaved)
            {
                if (GUILayout.Button(new GUIContent("x", "Remove snapshot - won't apply on exit"),
                        NMPStyles.ToolbarButton, GUILayout.Width(22), GUILayout.Height(NMPStyles.TabHeight + 8f)))
                {
                    Snapshots.RemoveAt(savedIndex);
                    SaveSnapshots();
                }
            }
        }
        private static void CaptureGameObject(GameObject go, string globalId, int existingIndex)
        {
            if (!GlobalObjectId.TryParse(globalId, out var parsedObjectId) ||
                GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedObjectId) != go)
            {
                Debug.LogWarning($"[NoMorePain] Play Mode Save cannot persist runtime-created object '{go.name}'.");
                return;
            }

            var snapshot = new ObjectSnapshot
            {
                globalId = globalId,
                objectName = go.name
            };
            var typeOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                try
                {
                    string typeName = comp.GetType().AssemblyQualifiedName;
                    typeOccurrences.TryGetValue(typeName, out int typeIndex);
                    typeOccurrences[typeName] = typeIndex + 1;

                    var componentId = GlobalObjectId.GetGlobalObjectIdSlow(comp);
                    string componentGlobalId =
                        GlobalObjectId.GlobalObjectIdentifierToObjectSlow(componentId) == comp
                            ? componentId.ToString()
                            : null;

                    snapshot.components.Add(new ComponentSnapshot
                    {
                        globalId = componentGlobalId,
                        typeName = typeName,
                        typeIndex = typeIndex,
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
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                // Capture writes immediately, but persisting again here closes the small
                // window between the last Inspector click and the exit domain reload.
                if (_snapshots != null && _snapshots.Count > 0)
                    SaveSnapshots();
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
                ScheduleRestore(resetAttempts: true);
        }

        private static void ScheduleRestore(bool resetAttempts)
        {
            if (resetAttempts)
                _restoreAttempt = 0;
            if (_restoreScheduled)
                return;

            _restoreScheduled = true;
            EditorApplication.delayCall -= RestorePendingSnapshots;
            EditorApplication.delayCall += RestorePendingSnapshots;
        }

        private static void RestorePendingSnapshots()
        {
            _restoreScheduled = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleRestore(resetAttempts: false);
                return;
            }

            if (Snapshots.Count == 0)
            {
                ClearSnapshotStorage();
                return;
            }

            _restoreAttempt++;
            int restoredObjects = 0;
            int restoredComponents = 0;
            int undoGroup = -1;
            var pending = new List<ObjectSnapshot>(Snapshots);

            foreach (var snapshot in pending)
            {
                if (!TryResolveGameObject(snapshot, out var go))
                    continue;

                if (undoGroup < 0)
                {
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    Undo.SetCurrentGroupName("Play Mode Save");
                }

                restoredComponents += RestoreComponents(snapshot, go);
                restoredObjects++;
                Snapshots.Remove(snapshot);
            }

            if (undoGroup >= 0)
                Undo.CollapseUndoOperations(undoGroup);

            if (restoredComponents > 0)
            {
                Debug.Log(
                    $"[NoMorePain] Play Mode Save: restored {restoredComponents} components " +
                    $"on {restoredObjects} object(s). (Ctrl+Z to undo)");
            }

            if (Snapshots.Count == 0)
            {
                ClearSnapshotStorage();
                return;
            }

            SaveSnapshots();
            if (_restoreAttempt < MaxRestoreAttempts)
            {
                ScheduleRestore(resetAttempts: false);
                return;
            }

            foreach (var snapshot in Snapshots)
            {
                Debug.LogWarning(
                    $"[NoMorePain] Could not restore scene object '{snapshot.objectName}' " +
                    $"after {MaxRestoreAttempts} editor updates. Was it deleted or was its scene unloaded?");
            }

            ClearSnapshotStorage();
        }

        private static bool TryResolveGameObject(ObjectSnapshot snapshot, out GameObject go)
        {
            go = null;
            if (!GlobalObjectId.TryParse(snapshot.globalId, out var globalId))
                return false;

            go = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as GameObject;
            return go != null;
        }

        private static int RestoreComponents(ObjectSnapshot snapshot, GameObject go)
        {
            var components = go.GetComponents<Component>();
            int applied = 0;

            for (int i = 0; i < snapshot.components.Count; i++)
            {
                var compSnap = snapshot.components[i];
                var component = ResolveComponent(go, components, compSnap, i);
                if (component == null)
                {
                    Debug.LogWarning(
                        $"[NoMorePain] Could not find {compSnap.typeName} on '{snapshot.objectName}'.");
                    continue;
                }

                try
                {
                    Undo.RecordObject(component, "Play Mode Save");
                    EditorJsonUtility.FromJsonOverwrite(compSnap.json, component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    EditorUtility.SetDirty(component);
                    applied++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NoMorePain] Failed to restore {compSnap.typeName}: {e.Message}");
                }
            }

            if (applied > 0)
            {
                EditorUtility.SetDirty(go);
                if (go.scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(go.scene);
            }

            return applied;
        }

        private static Component ResolveComponent(
            GameObject go,
            Component[] components,
            ComponentSnapshot snapshot,
            int legacyIndex)
        {
            if (!string.IsNullOrEmpty(snapshot.globalId) &&
                GlobalObjectId.TryParse(snapshot.globalId, out var componentId))
            {
                var resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(componentId) as Component;
                if (resolved != null && resolved.gameObject == go && IsMatchingComponent(resolved, snapshot.typeName))
                    return resolved;
            }

            // Backward-compatible fallback for snapshots captured by older package versions.
            if (legacyIndex >= 0 &&
                legacyIndex < components.Length &&
                IsMatchingComponent(components[legacyIndex], snapshot.typeName))
            {
                return components[legacyIndex];
            }

            int occurrence = 0;
            foreach (var component in components)
            {
                if (!IsMatchingComponent(component, snapshot.typeName))
                    continue;
                if (occurrence == snapshot.typeIndex)
                    return component;
                occurrence++;
            }

            return null;
        }

        private static bool IsMatchingComponent(Component component, string typeName)
        {
            return component != null &&
                   string.Equals(component.GetType().AssemblyQualifiedName, typeName, StringComparison.Ordinal);
        }
    }
}

