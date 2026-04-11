using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Floating picker for Hierarchy color highlight and object icon.
    /// </summary>
    internal class HierarchyColorPickerWindow : EditorWindow
    {
        // ── Layout ────────────────────────────────────────────────────
        private const float SwatchSize     = 36f;
        private const float SwatchSpacing  = 4f;
        private const float Padding        = 8f;
        private const int   Columns        = 4;
        private const float RowHeight      = 22f;
        private const float IconRowHeight  = 32f;
        private const float SepHeight      = 1f;
        private const float ScrollbarWidth = 14f;
        private const float ScrollAreaH    = 6 * SwatchSize + 5 * SwatchSpacing; // 6 rows visible

        // Icon grid fits exactly inside scroll (no scrollbar overlap)
        private static float GridWidth    => Columns * SwatchSize + (Columns - 1) * SwatchSpacing;
        // Full window content width (includes scrollbar space)
        private static float TotalWidth   => GridWidth + ScrollbarWidth;
        // Color swatch stretches to fill TotalWidth
        private static float ColorSwatchW => (TotalWidth - (Columns - 1) * SwatchSpacing) / Columns;
        private static int   ColorRows    => Mathf.CeilToInt(ColorPresets.Length / (float)Columns);

        // ── Color presets ─────────────────────────────────────────────
        private static readonly (string label, Color color)[] ColorPresets =
        {
            ("Red",    new Color(0.90f, 0.30f, 0.30f)),
            ("Orange", new Color(0.90f, 0.58f, 0.20f)),
            ("Yellow", new Color(0.88f, 0.85f, 0.22f)),
            ("Green",  new Color(0.28f, 0.78f, 0.35f)),
            ("Blue",   new Color(0.28f, 0.52f, 0.90f)),
            ("Purple", new Color(0.65f, 0.30f, 0.90f)),
            ("Pink",   new Color(0.90f, 0.40f, 0.70f)),
            ("White",  new Color(0.85f, 0.85f, 0.85f)),
        };

        // ── All-component icon cache (static, built once) ─────────────
        private static (string name, Texture2D icon)[] _allIcons;

        private static void EnsureAllIcons()
        {
            if (_allIcons != null) return;

            var list = new List<(string name, Texture2D icon)>();
            var seen = new HashSet<Texture2D>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (type.IsAbstract || type == typeof(Transform)) continue;

                Texture2D tex = AssetPreview.GetMiniTypeThumbnail(type) as Texture2D;
                if (tex == null)
                    tex = EditorGUIUtility.ObjectContent(null, type).image as Texture2D;
                if (tex == null || !seen.Add(tex)) continue;

                list.Add((type.Name, tex));
            }

            list.Sort((a, b) => string.Compare(a.Item1, b.Item1, System.StringComparison.OrdinalIgnoreCase));
            _allIcons = list.ToArray();
        }

        // ── State ─────────────────────────────────────────────────────
        private GameObject[] _targets;
        private Color        _customColor = Color.white;
        private Texture2D    _customIcon;
        private string       _search      = string.Empty;
        private Vector2      _scroll;

        // ── Window size ───────────────────────────────────────────────
        private static Vector2 CalcSize()
        {
            float sw = ColorSwatchW;
            float w  = Padding + TotalWidth + Padding;
            float h  = Padding
                    + ColorRows * sw + (ColorRows - 1) * SwatchSpacing  // color grid (stretched)
                    + SwatchSpacing + RowHeight                          // custom color + Apply
                    + SwatchSpacing + RowHeight                          // Clear color
                    + SwatchSpacing + SepHeight + SwatchSpacing          // separator
                    + RowHeight + SwatchSpacing                          // search field
                    + ScrollAreaH + Padding                              // icon scroll
                    + IconRowHeight                                       // custom icon + Clear
                    + Padding;
            return new Vector2(w, h);
        }

        // ── Open ──────────────────────────────────────────────────────
        internal static void Open(GameObject[] targets, Vector2 screenPos)
        {
            var all = Resources.FindObjectsOfTypeAll<HierarchyColorPickerWindow>();
            foreach (var w in all) w.Close();

            EnsureAllIcons();

            var size = CalcSize();
            var win  = CreateInstance<HierarchyColorPickerWindow>();
            win._targets     = targets;
            win.titleContent = new GUIContent("Highlight & Icon");
            win.minSize      = size;
            win.maxSize      = size;
            win.position     = new Rect(screenPos.x - size.x * 0.5f,
                                        screenPos.y - size.y * 0.5f, size.x, size.y);
            win.ShowUtility();
        }

        // ── GUI ───────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (_targets == null || _targets.Length == 0) { Close(); return; }
            EnsureAllIcons();

            float y = Padding;

            // ── Color swatches (stretched to full window width) ───────
            y = DrawGrid(y, ColorPresets.Length, ColorSwatchW, (i, rect) =>
            {
                var (label, color) = ColorPresets[i];
                EditorGUI.DrawRect(rect, color);
                Hover(rect);
                if (GUI.Button(rect, new GUIContent(string.Empty, label), GUIStyle.none))
                {
                    HierarchyColorManager.ApplyColor(_targets, color);
                    Close();
                }
            });

            // Custom color
            float applyW = 46f;
            float fieldW = TotalWidth - applyW - SwatchSpacing;
            _customColor = EditorGUI.ColorField(
                new Rect(Padding, y, fieldW, RowHeight),
                GUIContent.none, _customColor, true, false, false);
            if (GUI.Button(new Rect(Padding + fieldW + SwatchSpacing, y, applyW, RowHeight), "Apply"))
            {
                HierarchyColorManager.ApplyColor(_targets, _customColor);
                Close();
            }
            y += RowHeight + SwatchSpacing;

            // Clear color
            var prevC = GUI.color;
            GUI.color = new Color(1f, 0.4f, 0.4f);
            if (GUI.Button(new Rect(Padding, y, TotalWidth, RowHeight), "✕  Clear color"))
            {
                HierarchyColorManager.ClearColor(_targets);
                Close();
            }
            GUI.color = prevC;
            y += RowHeight + SwatchSpacing;

            // ── Separator ─────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(Padding, y, TotalWidth, SepHeight),
                new Color(0.5f, 0.5f, 0.5f, 0.4f));
            y += SepHeight + SwatchSpacing;

            // ── Icon search field ─────────────────────────────────────
            GUI.SetNextControlName("IconSearch");
            _search = EditorGUI.TextField(new Rect(Padding, y, TotalWidth, RowHeight),
                _search, EditorStyles.toolbarSearchField);
            y += RowHeight + SwatchSpacing;

            // ── Scrollable icon grid ──────────────────────────────────
            var filtered = GetFiltered();
            int iconRows = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)Columns));
            float contentH = iconRows * SwatchSize + (iconRows - 1) * SwatchSpacing;

            var scrollRect    = new Rect(Padding, y, TotalWidth, ScrollAreaH);
            var contentRect   = new Rect(0, 0, GridWidth, contentH);
            _scroll = GUI.BeginScrollView(scrollRect, _scroll, contentRect, false, false);

            for (int i = 0; i < filtered.Count; i++)
            {
                int   col  = i % Columns;
                int   row  = i / Columns;
                float ix   = col * (SwatchSize + SwatchSpacing);
                float iy   = row * (SwatchSize + SwatchSpacing);
                var   rect = new Rect(ix, iy, SwatchSize, SwatchSize);

                var (name, tex) = filtered[i];
                if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit);
                Hover(rect);
                if (GUI.Button(rect, new GUIContent(string.Empty, name), GUIStyle.none))
                {
                    SetIcon(tex);
                    Close();
                }
            }

            GUI.EndScrollView();
            y += ScrollAreaH + Padding;

            // ── Custom texture field + Clear icon ─────────────────────
            float clearW = 70f;
            float texW   = TotalWidth - clearW - SwatchSpacing;
            var prevIcon = _customIcon;
            _customIcon  = (Texture2D)EditorGUI.ObjectField(
                new Rect(Padding, y, texW, IconRowHeight),
                _customIcon, typeof(Texture2D), false);
            if (_customIcon != prevIcon && _customIcon != null)
            {
                SetIcon(_customIcon);
                EditorApplication.delayCall += Close;
            }

            if (GUI.Button(new Rect(Padding + texW + SwatchSpacing, y, clearW, IconRowHeight), "Clear icon"))
            {
                SetIcon(null); Close();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                Close();

            Repaint();
        }

        // ── Helpers ───────────────────────────────────────────────────
        private List<(string name, Texture2D icon)> GetFiltered()
        {
            var result = new List<(string name, Texture2D icon)>();
            bool hasSearch = !string.IsNullOrWhiteSpace(_search);
            foreach (var item in _allIcons)
            {
                if (!hasSearch || item.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(item);
            }
            return result;
        }

        private float DrawGrid(float startY, int count, float sw, System.Action<int, Rect> draw)
        {
            int rows = Mathf.CeilToInt(count / (float)Columns);
            for (int i = 0; i < count; i++)
            {
                int col = i % Columns;
                int row = i / Columns;
                draw(i, new Rect(Padding + col * (sw + SwatchSpacing),
                                 startY  + row * (sw + SwatchSpacing),
                                 sw, sw));
            }
            return startY + rows * sw + (rows - 1) * SwatchSpacing + SwatchSpacing;
        }

        private static void Hover(Rect rect)
        {
            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.20f));
        }

        private void SetIcon(Texture2D icon)
        {
            foreach (var go in _targets)
            {
                EditorGUIUtility.SetIconForObject(go, icon);
                EditorUtility.SetDirty(go);
            }
            HierarchyIconsManager.InvalidateCache();
            EditorApplication.RepaintHierarchyWindow();
        }
    }
}
