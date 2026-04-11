using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Hierarchy Icons:
    ///   • Replaces the default cube on the LEFT with the primary component icon.
    ///   • Draws additional component icons on the RIGHT — click any to open a quick-edit window.
    /// Transform is always skipped. Cache clears on hierarchy change.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyIconsManager
    {
        private const int   MaxRightIcons = 7;
        private const float IconSize      = 15f;
        private const float IconSpacing   = 1f;
        private const float IndentWidth   = 14f;

        private struct RightIcon
        {
            public GUIContent content;
            public int        componentId;
        }

        private struct IconData
        {
            public Texture     primary;
            public RightIcon[] rightIcons;
        }

        private static readonly Dictionary<int, IconData> _cache = new();
        private static Texture _folderIcon;

        static HierarchyIconsManager()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
            EditorApplication.hierarchyChanged         += ClearCache;
        }

        private static void ClearCache() => _cache.Clear();
        internal static void InvalidateCache() => _cache.Clear();

        /// <summary>
        /// Returns the primary icon for a GameObject using the same priority as the Hierarchy:
        /// custom icon → collider → first component.
        /// </summary>
        internal static Texture GetPrimaryIcon(GameObject go)
        {
            Texture primary = EditorGUIUtility.GetIconForObject(go);

            if (primary == null)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null)
                    primary = GetIcon(collider);
            }

            if (primary == null)
            {
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null || comp is Transform) continue;
                    var icon = GetIcon(comp);
                    if (icon != null) { primary = icon; break; }
                }
            }

            return primary;
        }

        private static void OnHierarchyItemGUI(int instanceId, Rect rowRect)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();

            // ── Color fill (behind everything) ────────────────────────
            HierarchyColorManager.DrawForItem(globalId, rowRect);

            // ── Tree lines ────────────────────────────────────────────
            DrawHierarchyLines(go.transform, rowRect);

            // ── Alt + LMB → color picker ──────────────────────────────
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 0 &&
                Event.current.alt &&
                rowRect.Contains(Event.current.mousePosition))
            {
                var screenPos = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                HierarchyColorPickerWindow.Open(new[] { go }, screenPos);
                Event.current.Use();
            }

            float alpha = go.activeInHierarchy ? 1f : 0.4f;

            DrawActiveToggle(go, rowRect);

            // ── Folder: draw folder icon, skip component icons ────────
            if (HierarchyFolderManager.IsFolder(globalId))
            {
                if (_folderIcon == null)
                    _folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;

                if (_folderIcon != null)
                    DrawObjectIcon(go, rowRect, _folderIcon, alpha, globalId);

                DrawHighlightLabel(go, rowRect, globalId, alpha);
                return;
            }

            // ── Normal object ─────────────────────────────────────────
            if (!_cache.TryGetValue(instanceId, out var data))
            {
                data = BuildData(go);
                _cache[instanceId] = data;
            }

            if (data.primary != null)
                DrawObjectIcon(go, rowRect, data.primary, alpha, globalId);

            DrawHighlightLabel(go, rowRect, globalId, alpha);

            if (data.rightIcons.Length > 0)
                DrawRightIcons(data.rightIcons, rowRect, alpha);
        }

        // ─────────────────────────────────────────────────────────────
        //  Tree lines
        // ─────────────────────────────────────────────────────────────

        private static void DrawHierarchyLines(Transform transform, Rect rowRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (transform.parent == null) return;

            // Build ancestor chain bottom-up, then reverse:
            // chain[0] = shallowest ancestor (depth 1), chain[last] = transform itself
            var chain = new System.Collections.Generic.List<Transform>();
            var t = transform;
            while (t.parent != null) { chain.Add(t); t = t.parent; }
            chain.Reverse();

            int   depth  = chain.Count;
            float midY   = rowRect.y + rowRect.height * 0.5f;
            var   color  = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.18f)
                : new Color(0f, 0f, 0f, 0.22f);

            for (int i = 0; i < chain.Count; i++)
            {
                int   d      = i + 1; // 1-based depth of this node
                // Line center is at the PARENT's expand-arrow center; subtract 1 so the 2px line is centred on it
                float lineX  = rowRect.x + (d - depth) * IndentWidth - IndentWidth * 1.5f - 1f;
                bool  isLast = chain[i].GetSiblingIndex() == chain[i].parent.childCount - 1;
                bool  isCur  = (i == chain.Count - 1);

                // Horizontal ends with a 4px gap before the current item's expand arrow
                float arrowCenterX = rowRect.x - IndentWidth * 0.5f - 4f;

                if (isCur)
                {
                    // Vertical top: stops 1px above the horizontal
                    EditorGUI.DrawRect(new Rect(lineX, rowRect.y, 2f, midY - rowRect.y - 1f), color);
                    // Horizontal: from lineX to the expand arrow center
                    EditorGUI.DrawRect(new Rect(lineX, midY - 1f, arrowCenterX - lineX, 2f), color);
                    // Vertical bottom: continues down if not last sibling
                    if (!isLast)
                        EditorGUI.DrawRect(new Rect(lineX, midY + 1f, 2f, rowRect.yMax - midY - 1f), color);
                }
                else
                {
                    // Full-height pass-through vertical line if ancestor is not last child
                    if (!isLast)
                        EditorGUI.DrawRect(new Rect(lineX, rowRect.y, 2f, rowRect.height), color);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Active / inactive toggle
        // ─────────────────────────────────────────────────────────────

        private static void DrawActiveToggle(GameObject go, Rect rowRect)
        {
            float size       = rowRect.height;
            var   toggleRect = new Rect(32f, rowRect.y + (size - 13f) * 0.5f, 13f, 13f);

            EditorGUI.BeginChangeCheck();
            bool newValue = GUI.Toggle(toggleRect, go.activeSelf, GUIContent.none);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(go, newValue ? "Enable GameObject" : "Disable GameObject");
                go.SetActive(newValue);
                EditorUtility.SetDirty(go);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Left icon
        // ─────────────────────────────────────────────────────────────

        private static void DrawObjectIcon(GameObject go, Rect rowRect, Texture icon, float alpha, string globalId)
        {
            float size   = rowRect.height;
            var iconRect = new Rect(rowRect.x, rowRect.y, size, size);

            EditorGUI.DrawRect(iconRect, GetRowBgColor(go));

            if (HierarchyColorManager.TryGetColor(globalId, out var hlColor))
                EditorGUI.DrawRect(iconRect, new Color(hlColor.r, hlColor.g, hlColor.b, 0.30f));

            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            GUI.color = prev;
        }

        private static Color GetRowBgColor(GameObject go)
        {
            bool selected = Selection.Contains(go);
            if (selected)
                return EditorGUIUtility.isProSkin
                    ? new Color(0.17f, 0.36f, 0.53f)
                    : new Color(0.23f, 0.45f, 0.69f);

            return EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f)
                : new Color(0.76f, 0.76f, 0.76f);
        }

        // ─────────────────────────────────────────────────────────────
        //  Right icons with click support
        // ─────────────────────────────────────────────────────────────

        private static void DrawRightIcons(RightIcon[] icons, Rect rowRect, float alpha)
        {
            float x   = rowRect.xMax - IconSize;
            float y   = rowRect.y + (rowRect.height - IconSize) * 0.5f;
            var   evt = Event.current;

            for (int i = 0; i < icons.Length; i++)
            {
                var iconRect = new Rect(x, y, IconSize, IconSize);

                bool hovered = iconRect.Contains(evt.mousePosition);
                if (hovered && evt.type == EventType.Repaint)
                    EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.15f));

                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTexture(iconRect, icons[i].content.image, ScaleMode.ScaleToFit);
                GUI.color = prev;

                if (hovered)
                    GUI.Label(iconRect, icons[i].content);

                if (hovered && evt.type == EventType.MouseDown && evt.button == 0)
                {
                    var comp = EditorUtility.InstanceIDToObject(icons[i].componentId) as Component;
                    if (comp != null)
                    {
                        var screenPos = GUIUtility.GUIToScreenPoint(evt.mousePosition);
                        ComponentQuickEditWindow.Open(comp, screenPos);
                    }
                    evt.Use();
                }

                x -= IconSize + IconSpacing;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Highlight label
        // ─────────────────────────────────────────────────────────────

        private static GUIStyle _highlightLabel;

        private static void DrawHighlightLabel(GameObject go, Rect rowRect, string globalId, float alpha)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!HierarchyColorManager.TryGetColor(globalId, out var hlColor)) return;

            if (_highlightLabel == null)
                _highlightLabel = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };

            _highlightLabel.normal.textColor = new Color(1f, 1f, 1f, alpha);

            float iconW    = rowRect.height;
            var   textRect = new Rect(rowRect.x + iconW, rowRect.y, Screen.width, rowRect.height);

            EditorGUI.DrawRect(textRect, GetRowBgColor(go));
            EditorGUI.DrawRect(textRect, new Color(hlColor.r, hlColor.g, hlColor.b, 0.30f));
            GUI.Label(textRect, go.name, _highlightLabel);
        }

        // ─────────────────────────────────────────────────────────────
        //  Data building
        // ─────────────────────────────────────────────────────────────

        private static IconData BuildData(GameObject go)
        {
            var components = go.GetComponents<Component>();
            var right      = new List<RightIcon>(components.Length);

            // Priority: custom icon → collider icon → first component icon
            Texture primary = EditorGUIUtility.GetIconForObject(go);

            if (primary == null)
            {
                var collider = go.GetComponent<Collider>();
                if (collider != null)
                    primary = GetIcon(collider);
            }

            foreach (var comp in components)
            {
                if (comp == null || comp is Transform) continue;

                var icon = GetIcon(comp);
                if (icon == null) continue;

                if (primary == null)
                    primary = icon;

                right.Add(new RightIcon
                {
                    content     = new GUIContent(string.Empty, icon, ObjectNames.GetInspectorTitle(comp)),
                    componentId = comp.GetInstanceID()
                });

                if (right.Count >= MaxRightIcons) break;
            }

            return new IconData { primary = primary, rightIcons = right.ToArray() };
        }

        private static Texture GetIcon(Component comp)
        {
            var content = EditorGUIUtility.ObjectContent(comp, comp.GetType());
            if (content?.image != null) return content.image;

            var thumb = AssetPreview.GetMiniTypeThumbnail(comp.GetType());
            if (thumb != null) return thumb;

            return EditorGUIUtility.IconContent("cs Script Icon").image;
        }
    }
}
