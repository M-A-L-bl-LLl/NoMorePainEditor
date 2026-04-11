using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Inspector Tabs - Draws a pinned-objects strip directly inside the Inspector.
    /// Click "+" to pin the current object. Click a tab icon to switch the Inspector to it.
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
        private class PinnedData
        {
            public List<PinnedEntry> pinned = new List<PinnedEntry>();
        }

        private static readonly string DataPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../ProjectSettings/NoMorePainTabs.json"));

        private static PinnedData _data = new PinnedData();

        static InspectorTabsManager()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnInspectorHeaderGUI;
            LoadData();
        }

        private static void OnInspectorHeaderGUI(UnityEditor.Editor editor)
        {
            // Draw the strip once per inspector refresh — only for the primary selected object.
            // Component inspectors have editor.target == the component, not the selected object.
            if (editor.target != Selection.activeObject) return;

            DrawTabStrip(editor.target);
        }

        // ─────────────────────────────────────────────────────────────
        //  Tab strip
        // ─────────────────────────────────────────────────────────────

        private static Vector2 _scrollPos;

        private static void DrawTabStrip(UnityEngine.Object currentObj)
        {
            var currentId = GlobalObjectId.GetGlobalObjectIdSlow(currentObj).ToString();
            bool currentIsPinned = _data.pinned.Exists(p => p.globalId == currentId);

            // Outer strip row — background + drop target
            var stripRect = EditorGUILayout.BeginHorizontal(NMPStyles.TabStrip, GUILayout.Height(NMPStyles.TabHeight));
            HandleDrop(stripRect);

            // Convert vertical scroll wheel → horizontal scroll when hovering the strip
            var evt = Event.current;
            if (evt.type == EventType.ScrollWheel && stripRect.Contains(evt.mousePosition))
            {
                _scrollPos.x += evt.delta.y * 20f;
                evt.Use();
            }

            // < prev button
            DrawNavButton("<", () => NavigateTab(currentId, -1));

            // Scrollable tabs area (fills all space except the pin button)
            _scrollPos = EditorGUILayout.BeginScrollView(
                _scrollPos,
                false, false,
                GUIStyle.none, GUIStyle.none,   // hide both scrollbars
                GUIStyle.none,
                GUILayout.Height(NMPStyles.TabHeight));

            EditorGUILayout.BeginHorizontal();

            string pendingRemoveId = null;

            for (int i = 0; i < _data.pinned.Count; i++)
            {
                var entry = _data.pinned[i];
                bool isActive = entry.globalId == currentId;

                if (DrawTab(entry, isActive, out string removeId))
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

            // > next button
            DrawNavButton(">", () => NavigateTab(currentId, +1));

            // Pin button — always visible, outside the scroll area
            DrawPinButton(currentObj, currentId, currentIsPinned);

            EditorGUILayout.EndHorizontal();

            if (pendingRemoveId != null)
            {
                _data.pinned.RemoveAll(p => p.globalId == pendingRemoveId);
                SaveData();
            }
        }

        // Returns true when the tab was clicked. Sets removeId when user chose Remove.
        private static bool DrawTab(PinnedEntry entry, bool isActive, out string removeId)
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
            var style = isActive ? NMPStyles.ActiveTab : NMPStyles.InactiveTab;
            var rect = GUILayoutUtility.GetRect(content, style,
                GUILayout.Height(NMPStyles.TabHeight), GUILayout.MaxWidth(110));

            // Accent underline for active tab
            if (isActive && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), NMPStyles.AccentColor);

            // Right-click — must be checked BEFORE GUI.Button so we intercept it first
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 1 &&
                rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();

                var capturedId  = entry.globalId;
                var capturedObj = obj;
                var menu = new GenericMenu();

                if (capturedObj != null)
                    menu.AddItem(new GUIContent("Ping"), false,
                        () => EditorGUIUtility.PingObject(capturedObj));

                menu.AddItem(new GUIContent("Delete"), false, () =>
                {
                    _data.pinned.RemoveAll(p => p.globalId == capturedId);
                    SaveData();
                });

                menu.ShowAsContext();
            }

            bool clicked = GUI.Button(rect, content, style);

            return clicked;
        }

        private static void DrawNavButton(string label, System.Action onClick)
        {
            bool hasMultiple = _data.pinned.Count > 1;
            using (new EditorGUI.DisabledScope(!hasMultiple))
            {
                if (GUILayout.Button(label, NMPStyles.PinButton, GUILayout.Width(18), GUILayout.ExpandHeight(true)))
                    onClick();
            }
        }

        private static void NavigateTab(string currentId, int direction)
        {
            if (_data.pinned.Count == 0) return;

            int currentIndex = _data.pinned.FindIndex(p => p.globalId == currentId);

            int nextIndex;
            if (currentIndex < 0)
                nextIndex = direction > 0 ? 0 : _data.pinned.Count - 1;
            else
                nextIndex = (currentIndex + direction + _data.pinned.Count) % _data.pinned.Count;

            var obj = ResolveEntry(_data.pinned[nextIndex]);
            if (obj != null) Selection.activeObject = obj;
        }

        private static void DrawPinButton(UnityEngine.Object obj, string globalId, bool isPinned)
        {
            var tooltip = isPinned ? "Remove current object from tabs" : "Pin current object to tabs";
            var label   = isPinned ? "−" : "+";

            if (GUILayout.Button(new GUIContent(label, tooltip),
                NMPStyles.PinButton,
                GUILayout.Width(22),
                GUILayout.ExpandHeight(true)))
            {
                if (isPinned)
                    _data.pinned.RemoveAll(p => p.globalId == globalId);
                else
                    PinObject(obj);

                SaveData();
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Drag & drop
        // ─────────────────────────────────────────────────────────────

        private static void HandleDrop(Rect stripRect)
        {
            var evt = Event.current;
            if (!stripRect.Contains(evt.mousePosition)) return;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var o in DragAndDrop.objectReferences)
                    PinObject(o);
                SaveData();
                evt.Use();
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────

        private static void PinObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            if (_data.pinned.Exists(p => p.globalId == globalId)) return;
            _data.pinned.Add(new PinnedEntry { globalId = globalId, displayName = obj.name });
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
                    _data = JsonUtility.FromJson<PinnedData>(File.ReadAllText(DataPath)) ?? new PinnedData();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NoMorePain] Failed to load tabs: {e.Message}");
                _data = new PinnedData();
            }
        }

        private static void SaveData()
        {
            try { File.WriteAllText(DataPath, JsonUtility.ToJson(_data, true)); }
            catch (Exception e) { Debug.LogWarning($"[NoMorePain] Failed to save tabs: {e.Message}"); }
        }
    }
}
