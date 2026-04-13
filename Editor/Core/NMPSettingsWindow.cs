using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace NoMorePain.Editor
{
    internal sealed class NMPSettingsWindow : EditorWindow
    {
        // -- Menu entry --

        [MenuItem("Tools/No More Pain/Settings", priority = 1)]
        public static void Open() =>
            GetWindow<NMPSettingsWindow>(utility: true, title: "No More Pain", focus: true);

        // -- Styles (lazy) --

        private GUIStyle _sectionLabel;
        private GUIStyle _featureLabel;
        private GUIStyle _descLabel;

        private void EnsureStyles()
        {
            if (_sectionLabel != null && _featureLabel != null && _descLabel != null) return;

            _sectionLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft,
            };
            _sectionLabel.normal.textColor = NMPStyles.AccentColor;

            _featureLabel = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
            };

            _descLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
            };
            var dc = _descLabel.normal.textColor;
            _descLabel.normal.textColor = new Color(dc.r, dc.g, dc.b, 0.5f);
        }

        // -- Lifecycle --

        private void OnEnable()
        {
            minSize = new Vector2(340f, 460f);
            maxSize = new Vector2(600f, 600f);
        }

        // -- GUI --

        private void OnGUI()
        {
            EnsureStyles();
            EditorGUILayout.Space(8f);

            // -- Hierarchy --
            DrawSectionHeader("HIERARCHY");

            EditorGUI.BeginChangeCheck();
            NMPSettings.HierarchyLeftIcon       = Toggle(NMPSettings.HierarchyLeftIcon,       "Auto GameObject Icon",  "Replaces the default cube with the primary component icon");
            NMPSettings.HierarchyRightIcons     = Toggle(NMPSettings.HierarchyRightIcons,     "Right Component Icons", "Component icons on the right side; click to quick-edit");
            NMPSettings.HierarchyTagLayerBadges = Toggle(NMPSettings.HierarchyTagLayerBadges, "Tag / Layer Badges",    "Shows non-default tag and layer as colored badges");
            NMPSettings.HierarchyZebra          = Toggle(NMPSettings.HierarchyZebra,          "Zebra Striping",        "Alternating row tint for easier scanning");
            NMPSettings.HierarchyTreeLines      = Toggle(NMPSettings.HierarchyTreeLines,      "Tree Lines",            "Parent-child connection lines");
            NMPSettings.HierarchyActiveToggle   = Toggle(NMPSettings.HierarchyActiveToggle,   "Active Toggle",         "Enable/disable checkbox on hover");
            NMPSettings.HierarchyColors         = Toggle(NMPSettings.HierarchyColors,         "Row Colors",            "Color-highlight rows (Alt+Click to pick color)");
            NMPSettings.HierarchyFolderNavbar   = Toggle(NMPSettings.HierarchyFolderNavbar,   "Folder Navbar",         "Quick-jump buttons for folders in the hierarchy search bar");
            if (EditorGUI.EndChangeCheck())
            {
                HierarchyIconsManager.InvalidateCache();
                EditorApplication.RepaintHierarchyWindow();
            }

            EditorGUILayout.Space(10f);

            // Project
            DrawSectionHeader("PROJECT");

            EditorGUI.BeginChangeCheck();
            NMPSettings.ProjectFolderColors  = Toggle(NMPSettings.ProjectFolderColors,  "Folder Colors",  "Color folder icons and rows in the right Assets pane");
            NMPSettings.ProjectRowColors     = Toggle(NMPSettings.ProjectRowColors,     "Row Colors",     "Color rows and tree lines in the left Project pane");
            NMPSettings.ProjectBadgeIcons    = Toggle(NMPSettings.ProjectBadgeIcons,    "Badge Icons",    "Show folder badge icons in the bottom-right corner");
            NMPSettings.ProjectTreeLines     = Toggle(NMPSettings.ProjectTreeLines,     "Tree Lines",     "Parent-child connection lines in the folder tree");
            NMPSettings.ProjectZebra         = Toggle(NMPSettings.ProjectZebra,         "Zebra Striping", "Alternating row tint for non-colored folders");
            if (EditorGUI.EndChangeCheck())
                EditorApplication.RepaintProjectWindow();

            EditorGUILayout.Space(10f);

            // -- Inspector --
            DrawSectionHeader("INSPECTOR");

            EditorGUI.BeginChangeCheck();
            NMPSettings.InspectorTabs      = Toggle(NMPSettings.InspectorTabs,      "Tabs",                 "Pin objects and switch between them from the Inspector");
            NMPSettings.PlayModeSave       = Toggle(NMPSettings.PlayModeSave,       "Play Mode Save",       "Capture and re-apply component values after Play Mode");
            NMPSettings.ComponentCopyPaste = Toggle(NMPSettings.ComponentCopyPaste, "Component Copy/Paste", "Batch copy and paste components between GameObjects");
            if (EditorGUI.EndChangeCheck())
                RepaintAllInspectors();

            EditorGUILayout.Space(8f);
        }

        // -- Drawing helpers --

        private void DrawSectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, _sectionLabel);
            var rect = GUILayoutUtility.GetLastRect();
            var lineColor = new Color(NMPStyles.AccentColor.r, NMPStyles.AccentColor.g, NMPStyles.AccentColor.b, 0.35f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax + 1f, rect.width, 1f), lineColor);
            EditorGUILayout.Space(4f);
        }

        private const float RowHeight    = 22f;
        private const float SwitchWidth  = 34f;
        private const float SwitchHeight = 18f;
        private const float LabelWidth   = 178f;

        private bool Toggle(bool current, string label, string description)
        {
            var rowRect = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                // Feature label
                var labelRect = new Rect(rowRect.x + SwitchWidth + 8f, rowRect.y, LabelWidth, RowHeight);
                _featureLabel.Draw(labelRect, label, false, false, false, false);

                // Description label
                var descRect = new Rect(labelRect.xMax + 4f, rowRect.y, rowRect.xMax - labelRect.xMax - 4f, RowHeight);
                _descLabel.Draw(descRect, description, false, false, false, false);
            }

            // Custom toggle switch
            var switchRect = new Rect(rowRect.x + 2f, rowRect.y + (RowHeight - SwitchHeight) * 0.5f, SwitchWidth, SwitchHeight);
            bool next = SwitchControl.Draw(switchRect, current);

            return next;
        }

        private static void RepaintAllInspectors() => InternalEditorUtility.RepaintAllViews();

        // -- Modern pill-shaped toggle switch --

        private static class SwitchControl
        {
            private const float KnobSize    = 14f;
            private const float KnobPad     = 2f;   // gap between knob and track edge
            private const int   TexW        = 34;
            private const int   TexH        = 18;
            private const int   TexRadius   = 9;    // half of TexH -> full pill

            private static Texture2D _trackOn;
            private static Texture2D _trackOff;
            private static Texture2D _knob;

            private static Texture2D TrackOn  => _trackOn  ??= MakePill(TexW, TexH, TexRadius, NMPStyles.AccentColor);
            private static Texture2D TrackOff => _trackOff ??= MakePill(TexW, TexH, TexRadius,
                EditorGUIUtility.isProSkin ? new Color(0.30f, 0.30f, 0.30f) : new Color(0.65f, 0.65f, 0.65f));
            private static Texture2D Knob     => _knob     ??= MakeCircle((int)KnobSize, Color.white);

            public static bool Draw(Rect rect, bool value)
            {
                int controlId = GUIUtility.GetControlID(FocusType.Passive);

                switch (Event.current.type)
                {
                    case EventType.Repaint:
                        DrawTrack(rect, value);
                        DrawKnob(rect, value);
                        break;

                    case EventType.MouseDown when rect.Contains(Event.current.mousePosition):
                        GUIUtility.hotControl = controlId;
                        Event.current.Use();
                        break;

                    case EventType.MouseUp when GUIUtility.hotControl == controlId:
                        GUIUtility.hotControl = 0;
                        if (rect.Contains(Event.current.mousePosition))
                        {
                            Event.current.Use();
                            GUI.changed = true;
                            return !value;
                        }
                        break;
                }

                return value;
            }

            private static void DrawTrack(Rect rect, bool on)
            {
                var tex = on ? TrackOn : TrackOff;
                if (tex != null)
                    GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, alphaBlend: true);
            }

            private static void DrawKnob(Rect rect, bool on)
            {
                if (Knob == null) return;
                float x = on
                    ? rect.xMax - KnobSize - KnobPad
                    : rect.x    + KnobPad;
                float y = rect.y + (rect.height - KnobSize) * 0.5f;
                GUI.DrawTexture(new Rect(x, y, KnobSize, KnobSize), Knob,
                    ScaleMode.StretchToFill, alphaBlend: true);
            }

            // -- Texture generation --

            private static Texture2D MakePill(int w, int h, int radius, Color color)
            {
                var tex = new Texture2D(w, h, TextureFormat.ARGB32, mipChain: false)
                {
                    hideFlags   = HideFlags.DontSave,
                    filterMode  = FilterMode.Bilinear,
                    wrapMode    = TextureWrapMode.Clamp,
                };

                var pixels = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = PillAlpha(x + 0.5f, y + 0.5f, w, h, radius);
                    pixels[y * w + x] = new Color(color.r, color.g, color.b, color.a * a);
                }

                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            private static Texture2D MakeCircle(int size, Color color)
            {
                var tex = new Texture2D(size, size, TextureFormat.ARGB32, mipChain: false)
                {
                    hideFlags  = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode   = TextureWrapMode.Clamp,
                };

                var pixels = new Color[size * size];
                float r  = size * 0.5f;
                float cx = r, cy = r;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;
                    float a  = CircleAlpha(Mathf.Sqrt(dx * dx + dy * dy), r);
                    pixels[y * size + x] = new Color(color.r, color.g, color.b, a);
                }

                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }

            /// <summary>Smooth alpha for a pill (two semicircles + rectangle).</summary>
            private static float PillAlpha(float px, float py, int w, int h, int r)
            {
                // Find closest point on the pill boundary
                float cx = Mathf.Clamp(px, r, w - r);
                float cy = Mathf.Clamp(py, r, h - r);
                float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                return CircleAlpha(dist, r);
            }

            /// <summary>Returns 1 inside, 0 outside, smooth at the edge (1px AA).</summary>
            private static float CircleAlpha(float dist, float radius) =>
                Mathf.Clamp01(radius - dist + 0.5f);
        }
    }
}


