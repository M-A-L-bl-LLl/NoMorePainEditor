using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Hierarchy Icons:
    ///   • Replaces the default cube on the LEFT with the primary component icon.
    ///   • Draws component icons on the RIGHT — click any to open a quick-edit window.
    ///   • Disabled components render at reduced opacity.
    ///   • Tag / Layer badges shown for non-default values.
    ///   • Zebra striping for readability.
    /// Transform is always skipped. Cache clears on hierarchy change.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyIconsManager
    {
        // ── Layout constants ──────────────────────────────────────────────────
        private const int   MaxRightIcons = 7;
        private const float IconSize      = 15f;
        private const float IconSpacing   = 1f;
        private const float IndentWidth   = 14f;
        private const float MinNameWidth  = 80f;
        private const float DisabledAlpha = 0.35f;
        private const float InactiveAlpha = 0.40f;

        // ── Bootstrap ─────────────────────────────────────────────────────────

        static HierarchyIconsManager()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
            EditorApplication.hierarchyChanged         += IconCache.Clear;
            // postprocessModifications fires before the new value is committed,
            // so defer the repaint one frame via delayCall.
            Undo.postprocessModifications += OnUndoModification;
        }

        private static UndoPropertyModification[] OnUndoModification(UndoPropertyModification[] mods)
        {
            EditorApplication.delayCall += EditorApplication.RepaintHierarchyWindow;
            return mods;
        }

        // ── Public API ────────────────────────────────────────────────────────

        internal static void InvalidateCache() => IconCache.Clear();

        /// <summary>
        /// Returns the primary icon for a GameObject:
        /// custom icon → collider icon → first component icon.
        /// </summary>
        internal static Texture GetPrimaryIcon(GameObject go)
        {
            Texture icon = EditorGUIUtility.GetIconForObject(go);
            if (icon != null) return icon;

            var collider = go.GetComponent<Collider>();
            if (collider != null) { icon = ResolveComponentIcon(collider); if (icon != null) return icon; }

            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null || comp is Transform) continue;
                icon = ResolveComponentIcon(comp);
                if (icon != null) return icon;
            }

            return null;
        }

        // ── Main GUI callback ─────────────────────────────────────────────────

        private static void OnHierarchyItemGUI(int instanceId, Rect rowRect)
        {
            var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null) return;

            var   globalId = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            float alpha    = go.activeInHierarchy ? 1f : InactiveAlpha;

            if (NMPSettings.HierarchyZebra)       Zebra.Draw(rowRect);
            if (NMPSettings.HierarchyColors)      HierarchyColorManager.DrawForItem(globalId, rowRect);
            if (NMPSettings.HierarchyTreeLines)   TreeLines.Draw(go.transform, rowRect);
                                                  HandleColorPickerClick(go, rowRect);
            if (NMPSettings.HierarchyActiveToggle) DrawActiveToggle(go, rowRect);
            if (NMPSettings.HierarchyHoverPreview) HierarchyHoverPreviewWindow.HandleHierarchyRow(go, rowRect);
            else                                   HierarchyHoverPreviewWindow.HideIfShown();

            if (HierarchyFolderManager.IsFolder(globalId))
            {
                DrawFolderRow(go, rowRect, globalId, alpha);
                return;
            }

            DrawNormalRow(go, instanceId, rowRect, globalId, alpha);
        }

        private static void DrawFolderRow(GameObject go, Rect rowRect, string globalId, float alpha)
        {
            var icon = IconCache.FolderIcon;
            if (icon != null && NMPSettings.HierarchyLeftIcon)
                DrawObjectIcon(go, rowRect, icon, alpha, globalId);
            DrawHighlightLabel(go, rowRect, globalId, alpha);
        }

        private static void DrawNormalRow(GameObject go, int instanceId, Rect rowRect, string globalId, float alpha)
        {
            var data   = IconCache.GetOrBuild(instanceId, go);
            var layout = new RowLayout(go, data.RightIcons, rowRect);

            if (data.Primary != null && NMPSettings.HierarchyLeftIcon)
                DrawObjectIcon(go, rowRect, data.Primary, alpha, globalId);

            DrawHighlightLabel(go, rowRect, globalId, alpha);

            if (layout.MaxIcons > 0 && NMPSettings.HierarchyRightIcons)
                DrawRightIcons(data.RightIcons, rowRect, alpha, layout.MaxIcons);

            if (layout.ShowBadges && NMPSettings.HierarchyTagLayerBadges)
                Badge.Draw(go, rowRect, alpha, layout.BadgeRightEdge);
        }

        // ── Color picker interaction ──────────────────────────────────────────

        private static void HandleColorPickerClick(GameObject go, Rect rowRect)
        {
            if (!NMPSettings.HierarchyColors) return;
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0 || !evt.alt) return;
            if (!rowRect.Contains(evt.mousePosition)) return;

            HierarchyColorPickerWindow.Open(new[] { go }, GUIUtility.GUIToScreenPoint(evt.mousePosition));
            evt.Use();
        }

        // ═════════════════���════════════════════════════════════════════════════
        //  Data types
        // ══════════════════════════════════════════════════════════════════════

        private readonly struct RightIcon
        {
            public readonly GUIContent Content;
            public readonly int        ComponentId;

            public RightIcon(Texture icon, int componentId, string tooltip)
            {
                Content     = new GUIContent(string.Empty, icon, tooltip);
                ComponentId = componentId;
            }
        }

        private readonly struct IconData
        {
            public readonly Texture     Primary;
            public readonly RightIcon[] RightIcons;

            public IconData(Texture primary, RightIcon[] rightIcons)
            {
                Primary    = primary;
                RightIcons = rightIcons;
            }
        }

        /// <summary>
        /// Pre-computes row layout so OnHierarchyItemGUI has no inline arithmetic.
        /// </summary>
        private readonly struct RowLayout
        {
            public readonly int   MaxIcons;
            public readonly bool  ShowBadges;
            public readonly float BadgeRightEdge;

            public RowLayout(GameObject go, RightIcon[] icons, Rect rowRect)
            {
                float nameStartX  = rowRect.x + rowRect.height;
                float available   = rowRect.xMax - nameStartX - MinNameWidth;

                MaxIcons = icons.Length > 0
                    ? Mathf.Clamp(Mathf.FloorToInt(available / (IconSize + IconSpacing)), 0, icons.Length)
                    : 0;

                float iconsTotalW = MaxIcons * (IconSize + IconSpacing);
                float badgeW      = Badge.Measure(go);
                ShowBadges        = badgeW > 0 && (available - iconsTotalW) >= badgeW;
                BadgeRightEdge    = rowRect.xMax - iconsTotalW;
            }
        }

        // ════════════════════════════════���═════════════════════════════════════
        //  Cache
        // ══════════════════════════════════════════════════════════════════════

        private static class IconCache
        {
            private static readonly Dictionary<int, IconData> _items = new();
            private static Texture _folderIcon;

            public static Texture FolderIcon =>
                _folderIcon ??= EditorGUIUtility.IconContent("Folder Icon").image;

            public static void Clear() => _items.Clear();

            public static IconData GetOrBuild(int instanceId, GameObject go)
            {
                if (!_items.TryGetValue(instanceId, out var data))
                    _items[instanceId] = data = BuildData(go);
                return data;
            }

            private static IconData BuildData(GameObject go)
            {
                var components = go.GetComponents<Component>();
                var right      = new List<RightIcon>(components.Length);

                // Priority: custom icon → collider icon → first component icon
                Texture primary = EditorGUIUtility.GetIconForObject(go);

                if (primary == null)
                {
                    var collider = go.GetComponent<Collider>();
                    if (collider != null) primary = ResolveComponentIcon(collider);
                }

                foreach (var comp in components)
                {
                    if (comp == null || comp is Transform) continue;
                    var icon = ResolveComponentIcon(comp);
                    if (icon == null) continue;
                    if (primary == null) primary = icon;
                    right.Add(new RightIcon(icon, comp.GetInstanceID(), ObjectNames.GetInspectorTitle(comp)));
                    if (right.Count >= MaxRightIcons) break;
                }

                return new IconData(primary, right.ToArray());
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Theme — all colors in one place
        // ══════════════════════════════════════════════════════════════════════

        private static class Theme
        {
            // Skin-aware: accessed as properties so isProSkin is evaluated at draw time
            public static Color ZebraStripe   => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.07f);
            public static Color TreeLine      => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.18f) : new Color(0f, 0f, 0f, 0.22f);
            public static Color DefaultRowBg  => EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
            public static Color SelectionBg   => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.69f);

            public static Color RowBg(GameObject go) =>
                Selection.Contains(go) ? SelectionBg : DefaultRowBg;

            // Fixed colors (skin-independent)
            public static readonly Color TagBadge    = new Color(0.55f, 0.28f, 0.10f, 0.55f);
            public static readonly Color LayerBadge  = new Color(0.15f, 0.40f, 0.60f, 0.55f);
            public static readonly Color IconHover   = new Color(1f, 1f, 1f, 0.15f);
            public static readonly Color HighlightTint = new Color(1f, 1f, 1f, 0.00f); // placeholder used via TryGetColor
        }

        // ══════════════════════════════════════════════════════════════════════
        //  GUIColor scope — restores GUI.color on dispose
        // ═════════════════════════════════════════════��════════════════════════

        private readonly struct GUIColorScope : System.IDisposable
        {
            private readonly Color _previous;
            public GUIColorScope(Color color) { _previous = GUI.color; GUI.color = color; }
            public void Dispose() => GUI.color = _previous;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Zebra striping
        // ══════════════════════════════════════════════════════════════════════

        private static class Zebra
        {
            public static void Draw(Rect rowRect)
            {
                if (Event.current.type != EventType.Repaint) return;
                if (Mathf.RoundToInt(rowRect.y / rowRect.height) % 2 == 0) return;
                EditorGUI.DrawRect(new Rect(0, rowRect.y, Screen.width, rowRect.height), Theme.ZebraStripe);
            }
        }

        // ══════════════════════════════════════════════════════��═══════════════
        //  Tree lines
        // ══════════════════════════════════════════════════════════════════════

        private static class TreeLines
        {
            public static void Draw(Transform transform, Rect rowRect)
            {
                if (Event.current.type != EventType.Repaint) return;
                if (transform.parent == null) return;

                var   chain = BuildAncestorChain(transform);
                int   depth = chain.Count;
                float midY  = rowRect.y + rowRect.height * 0.5f;
                var   color = Theme.TreeLine;

                for (int i = 0; i < chain.Count; i++)
                {
                    float lineX    = rowRect.x + (i + 1 - depth) * IndentWidth - IndentWidth * 1.5f - 1f;
                    bool  isLast   = chain[i].GetSiblingIndex() == chain[i].parent.childCount - 1;
                    bool  isCurrent = i == chain.Count - 1;

                    if (isCurrent)
                        DrawCurrentNodeLines(rowRect, lineX, midY, color, isLast);
                    else if (!isLast)
                        EditorGUI.DrawRect(new Rect(lineX, rowRect.y, 2f, rowRect.height), color);
                }
            }

            private static List<Transform> BuildAncestorChain(Transform transform)
            {
                var chain = new List<Transform>();
                var t = transform;
                while (t.parent != null) { chain.Add(t); t = t.parent; }
                chain.Reverse();
                return chain;
            }

            private static void DrawCurrentNodeLines(Rect rowRect, float lineX, float midY, Color color, bool isLast)
            {
                float arrowCenterX = rowRect.x - IndentWidth * 0.5f - 4f;
                EditorGUI.DrawRect(new Rect(lineX, rowRect.y,   2f,                     midY - rowRect.y - 1f), color);
                EditorGUI.DrawRect(new Rect(lineX, midY - 1f,   arrowCenterX - lineX,   2f),                   color);
                if (!isLast)
                    EditorGUI.DrawRect(new Rect(lineX, midY + 1f, 2f, rowRect.yMax - midY - 1f), color);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Tag / Layer badges
        // ══════════════════════════════════════════════════════════════════════

        private static class Badge
        {
            private const float HPad = 4f;
            private const float Gap  = 3f;

            private static GUIStyle _style;
            private static GUIStyle Style => _style ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping  = TextClipping.Clip,
            };

            /// <summary>Returns total pixel width needed for all visible badges, or 0 if none.</summary>
            public static float Measure(GameObject go)
            {
                float total = 0f;
                if (go.layer != 0)        total += BadgeWidth(LayerMask.LayerToName(go.layer));
                if (go.tag != "Untagged") total += BadgeWidth(go.tag);
                return total;
            }

            public static void Draw(GameObject go, Rect rowRect, float alpha, float rightEdge)
            {
                float x      = rightEdge;
                float badgeH = rowRect.height - 4f;
                float badgeY = rowRect.y + 2f;

                // Layer badge is closest to the component icons
                if (go.layer != 0)
                    DrawOne(new GUIContent(LayerMask.LayerToName(go.layer)), Theme.LayerBadge, ref x, badgeY, badgeH, alpha);
                if (go.tag != "Untagged")
                    DrawOne(new GUIContent(go.tag), Theme.TagBadge, ref x, badgeY, badgeH, alpha);
            }

            private static void DrawOne(GUIContent content, Color bgColor, ref float x, float y, float h, float alpha)
            {
                float w = Style.CalcSize(content).x + HPad * 2f;
                x -= w + Gap;

                if (Event.current.type != EventType.Repaint) return;

                EditorGUI.DrawRect(new Rect(x, y, w, h),
                    new Color(bgColor.r, bgColor.g, bgColor.b, bgColor.a * alpha));

                using (new GUIColorScope(new Color(1f, 1f, 1f, alpha)))
                    GUI.Label(new Rect(x, y, w, h), content, Style);
            }

            private static float BadgeWidth(string text) =>
                Style.CalcSize(new GUIContent(text)).x + HPad * 2f + Gap;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Draw helpers
        // ══════════════════════════════════════════════════════════════════════

        private static void DrawActiveToggle(GameObject go, Rect rowRect)
        {
            var fullRowRect = new Rect(0, rowRect.y, Screen.width, rowRect.height);
            if (!fullRowRect.Contains(Event.current.mousePosition)) return;

            var toggleRect = new Rect(32f, rowRect.y + (rowRect.height - 13f) * 0.5f, 13f, 13f);
            EditorGUI.BeginChangeCheck();
            bool newValue = GUI.Toggle(toggleRect, go.activeSelf, GUIContent.none);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(go, newValue ? "Enable GameObject" : "Disable GameObject");
            go.SetActive(newValue);
            EditorUtility.SetDirty(go);
        }

        private static void DrawObjectIcon(GameObject go, Rect rowRect, Texture icon, float alpha, string globalId)
        {
            var iconRect = new Rect(rowRect.x, rowRect.y, rowRect.height, rowRect.height);
            EditorGUI.DrawRect(iconRect, Theme.RowBg(go));

            // Re-apply zebra tint — solid RowBg above would otherwise cover it
            if (NMPSettings.HierarchyZebra && Mathf.RoundToInt(rowRect.y / rowRect.height) % 2 != 0)
                EditorGUI.DrawRect(iconRect, Theme.ZebraStripe);

            if (NMPSettings.HierarchyColors && HierarchyColorManager.TryGetColor(globalId, out var hlColor))
                EditorGUI.DrawRect(iconRect, new Color(hlColor.r, hlColor.g, hlColor.b, 0.30f));

            using (new GUIColorScope(new Color(1f, 1f, 1f, alpha)))
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        }

        private static GUIStyle _highlightLabel;

        private static void DrawHighlightLabel(GameObject go, Rect rowRect, string globalId, float alpha)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!NMPSettings.HierarchyColors) return;
            if (!HierarchyColorManager.TryGetColor(globalId, out var hlColor)) return;

            _highlightLabel ??= new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
            _highlightLabel.normal.textColor = new Color(1f, 1f, 1f, alpha);

            var textRect = new Rect(rowRect.x + rowRect.height, rowRect.y, Screen.width, rowRect.height);
            EditorGUI.DrawRect(textRect, Theme.RowBg(go));
            EditorGUI.DrawRect(textRect, new Color(hlColor.r, hlColor.g, hlColor.b, 0.30f));
            GUI.Label(textRect, go.name, _highlightLabel);
        }

        private static void DrawRightIcons(RightIcon[] icons, Rect rowRect, float alpha, int maxVisible)
        {
            float x   = rowRect.xMax - IconSize;
            float y   = rowRect.y + (rowRect.height - IconSize) * 0.5f;
            var   evt = Event.current;

            for (int i = 0; i < maxVisible; i++)
            {
                var   comp      = EditorUtility.InstanceIDToObject(icons[i].ComponentId) as Component;
                float iconAlpha = (comp == null || IsComponentEnabled(comp)) ? alpha : alpha * DisabledAlpha;
                var   iconRect  = new Rect(x, y, IconSize, IconSize);
                bool  hovered   = iconRect.Contains(evt.mousePosition);

                if (hovered && evt.type == EventType.Repaint)
                    EditorGUI.DrawRect(iconRect, Theme.IconHover);

                using (new GUIColorScope(new Color(1f, 1f, 1f, iconAlpha)))
                    GUI.DrawTexture(iconRect, icons[i].Content.image, ScaleMode.ScaleToFit);

                if (hovered)
                    GUI.Label(iconRect, icons[i].Content); // renders tooltip

                if (hovered && evt.type == EventType.MouseDown && evt.button == 0)
                {
                    if (comp != null)
                        ComponentQuickEditWindow.Open(comp, GUIUtility.GUIToScreenPoint(evt.mousePosition));
                    evt.Use();
                }

                x -= IconSize + IconSpacing;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Component utilities
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns false only when the component has an enabled toggle AND it is off.
        /// Behaviour, Renderer and Collider each have .enabled but share no common base.
        /// </summary>
        private static bool IsComponentEnabled(Component comp)
        {
            if (comp is Behaviour b) return b.enabled;
            if (comp is Renderer  r) return r.enabled;
            if (comp is Collider  c) return c.enabled;
            return true;
        }

        private static Texture ResolveComponentIcon(Component comp)
        {
            var content = EditorGUIUtility.ObjectContent(comp, comp.GetType());
            if (content?.image != null) return content.image;

            var thumb = AssetPreview.GetMiniTypeThumbnail(comp.GetType());
            if (thumb != null) return thumb;

            return EditorGUIUtility.IconContent("cs Script Icon").image;
        }
    }
}
