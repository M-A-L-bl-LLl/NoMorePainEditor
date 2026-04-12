using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Inspector Tabs - Draws a pinned-objects strip directly inside the Inspector.
    /// Tabs are saved per scene. Click "+" to pin the current object.
    /// Click a tab icon to switch the Inspector to it.
    /// Right-click a tab to remove or ping it. Drag any object onto the strip to add it.
    /// </summary>
    [InitializeOnLoad]
    internal static class InspectorTabsManager
    {
        [Serializable]
        private class PinnedEntry
        {
            public string globalId;
            public string displayName;
        }

        [Serializable]
        private class SceneTabData
        {
            public string scenePath;
            public List<PinnedEntry> pinned = new List<PinnedEntry>();
        }

        [Serializable]
        private class AllTabsData
        {
            public List<SceneTabData> scenes = new List<SceneTabData>();
        }

        private static readonly string DataPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../ProjectSettings/NoMorePainTabs.json"));

        private static AllTabsData _allData = new AllTabsData();

        // ─────────────────────────────────────────────────────────────
        //  Current scene helpers
        // ─────────────────────────────────────────────────────────────

        private static string CurrentSceneKey =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

        private static List<PinnedEntry> CurrentPinned
        {
            get
            {
                var key  = CurrentSceneKey;
                var data = _allData.scenes.Find(s => s.scenePath == key);
                if (data == null)
                {
                    data = new SceneTabData { scenePath = key };
                    _allData.scenes.Add(data);
                }
                return data.pinned;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Init
        // ─────────────────────────────────────────────────────────────

        static InspectorTabsManager()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnInspectorHeaderGUI;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            LoadData();
        }

        private static void OnSceneChanged(UnityEngine.SceneManagement.Scene _, UnityEngine.SceneManagement.Scene __)
        {
            _scrollPos = Vector2.zero;
            InternalEditorUtility.RepaintAllViews();
        }

        private static void OnInspectorHeaderGUI(UnityEditor.Editor editor)
        {
            if (!NMPSettings.InspectorTabs) return;
            if (editor.target != Selection.activeObject) return;

            DrawTabStrip(editor.target);
        }

        // ─────────────────────────────────────────────────────────────
        //  Tab strip
        // ─────────────────────────────────────────────────────────────

        private static Vector2 _scrollPos;

        // Stored each frame so ComponentCopyPasteManager can call inline draw methods
        private static string              _navCurrentId;
        private static List<PinnedEntry>   _navPinned;
        private static UnityEngine.Object  _navCurrentObj;
        private static bool                _navIsPinned;

        /// <summary>Draws &lt; &gt; nav buttons inline inside another manager's toolbar row.</summary>
        internal static void DrawInlineNavButtons()
        {
            if (!NMPSettings.InspectorTabs) return;
            if (_navPinned == null) return;
            DrawNavButton("<", () => NavigateTab(_navCurrentId, -1, _navPinned));
            DrawNavButton(">", () => NavigateTab(_navCurrentId, +1, _navPinned));
        }

        /// <summary>Draws the Add Tab / Remove button inline inside another manager's toolbar row.</summary>
        internal static void DrawInlinePinButton()
        {
            if (!NMPSettings.InspectorTabs) return;
            if (_navPinned == null || _navCurrentObj == null) return;

            if (_navIsPinned)
            {
                if (GUILayout.Button(new GUIContent("− Remove", "Remove current object from tabs"),
                        NMPStyles.ToolbarButton, GUILayout.Height(NMPStyles.TabHeight + 8f)))
                {
                    _navPinned.RemoveAll(p => p.globalId == _navCurrentId);
                    SaveData();
                }
            }
            else
            {
                if (GUILayout.Button(new GUIContent("+ Add Tab", "Pin current object to tabs"),
                        NMPStyles.ToolbarButton, GUILayout.Height(NMPStyles.TabHeight + 8f)))
                {
                    PinObject(_navCurrentObj, _navPinned);
                    SaveData();
                }
            }
        }

        private static void DrawTabStrip(UnityEngine.Object currentObj)
        {
            var pinned    = CurrentPinned;
            var currentId = GlobalObjectId.GetGlobalObjectIdSlow(currentObj).ToString();
            bool currentIsPinned = pinned.Exists(p => p.globalId == currentId);

            _navCurrentId  = currentId;
            _navPinned     = pinned;
            _navCurrentObj = currentObj;
            _navIsPinned   = currentIsPinned;

            // Outer strip row — background + drop target
            var stripRect = EditorGUILayout.BeginHorizontal(NMPStyles.TabStrip, GUILayout.Height(NMPStyles.TabHeight));
            HandleDrop(stripRect, pinned);

            // Convert vertical scroll wheel → horizontal scroll when hovering the strip
            var evt = Event.current;
            if (evt.type == EventType.ScrollWheel && stripRect.Contains(evt.mousePosition))
            {
                _scrollPos.x += evt.delta.y * 20f;
                evt.Use();
            }

            // Scrollable tabs area
            _scrollPos = EditorGUILayout.BeginScrollView(
                _scrollPos,
                false, false,
                GUIStyle.none, GUIStyle.none,
                GUIStyle.none,
                GUILayout.Height(NMPStyles.TabHeight));

            EditorGUILayout.BeginHorizontal();

            string pendingRemoveId = null;

            for (int i = 0; i < pinned.Count; i++)
            {
                var entry    = pinned[i];
                bool isActive = entry.globalId == currentId;

                if (DrawTab(entry, isActive, pinned, out string removeId))
                {
                    var obj = ResolveEntry(entry);
                    if (obj != null) Selection.activeObject = obj;
                }

                if (removeId != null)
                    pendingRemoveId = removeId;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndHorizontal();

            if (pendingRemoveId != null)
            {
                pinned.RemoveAll(p => p.globalId == pendingRemoveId);
                SaveData();
            }
        }

        // Returns true when the tab was clicked. Sets removeId when user chose Remove.
        private static bool DrawTab(PinnedEntry entry, bool isActive, List<PinnedEntry> pinned, out string removeId)
        {
            removeId = null;

            var obj = ResolveEntry(entry);

            Texture icon;
            if (obj == null)
                icon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
            else if (obj is GameObject pinnedGo)
                icon = HierarchyIconsManager.GetPrimaryIcon(pinnedGo) ?? AssetPreview.GetMiniThumbnail(obj);
            else
                icon = AssetPreview.GetMiniThumbnail(obj);

            string label = entry.displayName.Length > 11
                ? entry.displayName.Substring(0, 10) + "…"
                : entry.displayName;

            var content = new GUIContent(" " + label, icon, entry.displayName);
            var style   = isActive ? NMPStyles.ActiveTab : NMPStyles.InactiveTab;
            var rect    = GUILayoutUtility.GetRect(content, style,
                GUILayout.Height(NMPStyles.TabHeight), GUILayout.MaxWidth(110));

            // Accent underline for active tab
            if (isActive && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), NMPStyles.AccentColor);

            // Right-click context menu
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 1 &&
                rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();

                var capturedId  = entry.globalId;
                var capturedObj = obj;
                var capturedList = pinned;
                var menu = new GenericMenu();

                if (capturedObj != null)
                    menu.AddItem(new GUIContent("Ping"), false,
                        () => EditorGUIUtility.PingObject(capturedObj));

                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    capturedList.RemoveAll(p => p.globalId == capturedId);
                    SaveData();
                });

                menu.ShowAsContext();
            }

            return GUI.Button(rect, content, style);
        }

        private static void DrawNavButton(string label, System.Action onClick)
        {
            bool hasMultiple = CurrentPinned.Count > 1;
            using (new EditorGUI.DisabledScope(!hasMultiple))
            {
                if (GUILayout.Button(label, NMPStyles.ToolbarButton, GUILayout.Width(22), GUILayout.Height(NMPStyles.TabHeight + 8f)))
                    onClick();
            }
        }

        private static void NavigateTab(string currentId, int direction, List<PinnedEntry> pinned)
        {
            if (pinned.Count == 0) return;

            int currentIndex = pinned.FindIndex(p => p.globalId == currentId);

            int nextIndex;
            if (currentIndex < 0)
                nextIndex = direction > 0 ? 0 : pinned.Count - 1;
            else
                nextIndex = (currentIndex + direction + pinned.Count) % pinned.Count;

            var obj = ResolveEntry(pinned[nextIndex]);
            if (obj != null) Selection.activeObject = obj;
        }

        private static void DrawPinButton(UnityEngine.Object obj, string globalId, bool isPinned, List<PinnedEntry> pinned)
        {
            var tooltip = isPinned ? "Remove current object from tabs" : "Pin current object to tabs";
            var label   = isPinned ? "−" : "+";

            if (GUILayout.Button(new GUIContent(label, tooltip),
                NMPStyles.PinButton,
                GUILayout.Width(22),
                GUILayout.ExpandHeight(true)))
            {
                if (isPinned)
                    pinned.RemoveAll(p => p.globalId == globalId);
                else
                    PinObject(obj, pinned);

                SaveData();
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Drag & drop
        // ─────────────────────────────────────────────────────────────

        private static void HandleDrop(Rect stripRect, List<PinnedEntry> pinned)
        {
            var evt = Event.current;
            if (!stripRect.Contains(evt.mousePosition)) return;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var o in DragAndDrop.objectReferences)
                    PinObject(o, pinned);
                SaveData();
                evt.Use();
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────

        private static void PinObject(UnityEngine.Object obj, List<PinnedEntry> pinned)
        {
            if (obj == null) return;
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            if (pinned.Exists(p => p.globalId == globalId)) return;
            pinned.Add(new PinnedEntry { globalId = globalId, displayName = obj.name });
        }

        private static UnityEngine.Object ResolveEntry(PinnedEntry entry)
        {
            if (!GlobalObjectId.TryParse(entry.globalId, out var id)) return null;
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
        }

        // ─────────────────────────────────────────────────────────────
        //  Persistence
        // ─────────────────────────────────────────────────────────────

        private static void LoadData()
        {
            try
            {
                if (File.Exists(DataPath))
                    _allData = JsonUtility.FromJson<AllTabsData>(File.ReadAllText(DataPath)) ?? new AllTabsData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NoMorePain] Failed to load tabs: {e.Message}");
                _allData = new AllTabsData();
            }
        }

        private static void SaveData()
        {
            try { File.WriteAllText(DataPath, JsonUtility.ToJson(_allData, true)); }
            catch (Exception e) { Debug.LogWarning($"[NoMorePain] Failed to save tabs: {e.Message}"); }
        }
    }
}
