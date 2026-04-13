using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Floating picker for Project folder color and badge icon.
    /// </summary>
    internal sealed class ProjectFolderStylePickerWindow : EditorWindow
    {
        private const float SwatchSize     = 36f;
        private const float SwatchSpacing  = 4f;
        private const float Padding        = 8f;
        private const int   Columns        = 4;
        private const float RowHeight      = 22f;
        private const float IconRowHeight  = 32f;
        private const float SepHeight      = 1f;
        private const float ScrollbarWidth = 14f;
        private const float ScrollAreaH    = 6 * SwatchSize + 5 * SwatchSpacing;

        private static float GridWidth  => Columns * SwatchSize + (Columns - 1) * SwatchSpacing;
        private static float TotalWidth => GridWidth + ScrollbarWidth;
        private static float ColorSwatchW => (TotalWidth - (Columns - 1) * SwatchSpacing) / Columns;
        private static int ColorRows => Mathf.CeilToInt(ColorPresets.Length / (float)Columns);

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

        private string[] _folderGuids;
        private Color _customColor = Color.white;
        private Texture2D _customIcon;
        private string _search = string.Empty;
        private Vector2 _scroll;

        private static Vector2 CalcSize()
        {
            float sw = ColorSwatchW;
            float w  = Padding + TotalWidth + Padding;
            float h  = Padding
                     + ColorRows * sw + (ColorRows - 1) * SwatchSpacing
                     + SwatchSpacing + RowHeight
                     + SwatchSpacing + RowHeight
                     + SwatchSpacing + SepHeight + SwatchSpacing
                     + RowHeight + SwatchSpacing
                     + ScrollAreaH + Padding
                     + IconRowHeight
                     + Padding;
            return new Vector2(w, h);
        }

        internal static void Open(string[] folderGuids, Vector2 screenPos)
        {
            var all = Resources.FindObjectsOfTypeAll<ProjectFolderStylePickerWindow>();
            foreach (var w in all) w.Close();

            var size = CalcSize();
            var win  = CreateInstance<ProjectFolderStylePickerWindow>();
            win._folderGuids = folderGuids;
            win.titleContent = new GUIContent("Folder Style");
            win.minSize = size;
            win.maxSize = size;
            win.position = new Rect(screenPos.x - size.x * 0.5f, screenPos.y - size.y * 0.5f, size.x, size.y);
            win.ShowUtility();
        }

        private void OnGUI()
        {
            if (_folderGuids == null || _folderGuids.Length == 0) { Close(); return; }

            float y = Padding;

            y = DrawGrid(y, ColorPresets.Length, ColorSwatchW, (i, rect) =>
            {
                var (label, color) = ColorPresets[i];
                EditorGUI.DrawRect(rect, color);
                Hover(rect);
                if (GUI.Button(rect, new GUIContent(string.Empty, label), GUIStyle.none))
                {
                    ProjectFolderStyleManager.ApplyColor(_folderGuids, color);
                    Close();
                }
            });

            float applyW = 46f;
            float fieldW = TotalWidth - applyW - SwatchSpacing;
            _customColor = EditorGUI.ColorField(new Rect(Padding, y, fieldW, RowHeight),
                GUIContent.none, _customColor, true, false, false);
            if (GUI.Button(new Rect(Padding + fieldW + SwatchSpacing, y, applyW, RowHeight), "Apply"))
            {
                ProjectFolderStyleManager.ApplyColor(_folderGuids, _customColor);
                Close();
            }
            y += RowHeight + SwatchSpacing;

            var prevColor = GUI.color;
            GUI.color = new Color(1f, 0.4f, 0.4f);
            if (GUI.Button(new Rect(Padding, y, TotalWidth, RowHeight), "✕  Clear color"))
            {
                ProjectFolderStyleManager.ClearColor(_folderGuids);
                Close();
            }
            GUI.color = prevColor;
            y += RowHeight + SwatchSpacing;

            EditorGUI.DrawRect(new Rect(Padding, y, TotalWidth, SepHeight), new Color(0.5f, 0.5f, 0.5f, 0.4f));
            y += SepHeight + SwatchSpacing;

            GUI.SetNextControlName("FolderIconSearch");
            _search = EditorGUI.TextField(new Rect(Padding, y, TotalWidth, RowHeight), _search, EditorStyles.toolbarSearchField);
            y += RowHeight + SwatchSpacing;

            var filtered = GetFilteredIcons();
            int iconRows = Mathf.Max(1, Mathf.CeilToInt(filtered.Count / (float)Columns));
            float contentH = iconRows * SwatchSize + (iconRows - 1) * SwatchSpacing;

            var scrollRect = new Rect(Padding, y, TotalWidth, ScrollAreaH);
            var contentRect = new Rect(0, 0, GridWidth, contentH);
            _scroll = GUI.BeginScrollView(scrollRect, _scroll, contentRect, false, false);

            for (int i = 0; i < filtered.Count; i++)
            {
                int col = i % Columns;
                int row = i / Columns;
                float ix = col * (SwatchSize + SwatchSpacing);
                float iy = row * (SwatchSize + SwatchSpacing);
                var rect = new Rect(ix, iy, SwatchSize, SwatchSize);

                var option = filtered[i];
                if (option.Icon != null) GUI.DrawTexture(rect, option.Icon, ScaleMode.ScaleToFit);
                Hover(rect);
                if (GUI.Button(rect, new GUIContent(string.Empty, option.Name), GUIStyle.none))
                {
                    ProjectFolderStyleManager.ApplyCustomIcon(_folderGuids, option.Id);
                    Close();
                }
            }

            GUI.EndScrollView();
            y += ScrollAreaH + Padding;

            float clearW = 70f;
            float texW   = TotalWidth - clearW - SwatchSpacing;
            var prevIcon = _customIcon;
            _customIcon = (Texture2D)EditorGUI.ObjectField(new Rect(Padding, y, texW, IconRowHeight),
                _customIcon, typeof(Texture2D), false);
            if (_customIcon != prevIcon && _customIcon != null)
            {
                string iconId = null;

                // Prefer global object id: works for project assets and Unity built-in resources.
                string globalId = GlobalObjectId.GetGlobalObjectIdSlow(_customIcon).ToString();
                if (!string.IsNullOrEmpty(globalId) &&
                    GlobalObjectId.TryParse(globalId, out var parsedGlobal) &&
                    GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedGlobal) != null)
                {
                    iconId = "global:" + globalId;
                }

                // Fallback for plain project assets.
                if (string.IsNullOrEmpty(iconId))
                {
                    string path = AssetDatabase.GetAssetPath(_customIcon);
                    string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
                    if (!string.IsNullOrEmpty(guid))
                        iconId = "asset:" + guid;
                }

                if (!string.IsNullOrEmpty(iconId))
                {
                    ProjectFolderStyleManager.ApplyCustomIcon(_folderGuids, iconId);
                    EditorApplication.delayCall += Close;
                }
            }

            if (GUI.Button(new Rect(Padding + texW + SwatchSpacing, y, clearW, IconRowHeight), "Clear icon"))
            {
                ProjectFolderStyleManager.ClearCustomIcon(_folderGuids);
                Close();
            }

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
                Close();

            Repaint();
        }

        private List<ProjectFolderStyleManager.BadgeIconOption> GetFilteredIcons()
        {
            var all = ProjectFolderStyleManager.GetBadgeIconOptions();
            var result = new List<ProjectFolderStyleManager.BadgeIconOption>(all.Length);
            bool hasSearch = !string.IsNullOrWhiteSpace(_search);

            foreach (var item in all)
            {
                if (!hasSearch || item.Name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0)
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
                draw(i, new Rect(Padding + col * (sw + SwatchSpacing), startY + row * (sw + SwatchSpacing), sw, sw));
            }
            return startY + rows * sw + (rows - 1) * SwatchSpacing + SwatchSpacing;
        }

        private static void Hover(Rect rect)
        {
            if (rect.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(rect, new Color(1f, 1f, 1f, 0.20f));
        }
    }
}
