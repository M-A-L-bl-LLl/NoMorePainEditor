using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Draws a folder-navigation bar over the Hierarchy window's search area.
    /// Each button pings and selects the corresponding folder in the current scene.
    /// A loupe button on the right side reveals a search field that filters the hierarchy.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyFolderNavbar
    {
        private const float NavH = 22f;

        // ── Folder cache ───────────────────────────────────────────────

        private static readonly List<(string name, GameObject go, string globalId)> _folders = new();
        private static bool _dirty = true;

        // ── Overlay ────────────────────────────────────────────────────

        private static EditorWindow   _hierarchyWindow;
        private static IMGUIContainer _overlay;

        // ── Search state ───────────────────────────────────────────────

        private static bool   _searchMode;
        private static bool   _searchJustOpened;
        private static string _searchText    = "";
        private static float  _scrollOffsetX = 0f;
        private static float  _createBtnW    = 0f;

        // ── Reflection cache ───────────────────────────────────────────

        private static MethodInfo _setSearchFilterMethod;
        private static object     _sceneHierarchy;
        private static MethodInfo _expandMethod;
        private static MethodInfo _frameMethod;

        // ── Init ───────────────────────────────────────────────────────

        static HierarchyFolderNavbar()
        {
            EditorApplication.update += EnsureOverlay;
            EditorApplication.hierarchyChanged += () => _dirty = true;
            EditorSceneManager.activeSceneChangedInEditMode += (_, __) => _dirty = true;
            HierarchyFolderManager.OnFolderDataChanged += () => _dirty = true;
        }

        // ── Overlay management ─────────────────────────────────────────

        private static void EnsureOverlay()
        {
            if (!NMPSettings.HierarchyFolderNavbar)
            {
                if (_overlay != null) { _overlay.RemoveFromHierarchy(); _overlay = null; }
                return;
            }

            // Fast path: overlay already attached (but keep geometry synced on resize)
            if (_overlay != null && _overlay.parent != null)
            {
                float hierarchyWidth = GetHierarchyWindowWidth();
                _overlay.style.left  = GetOverlayLeft(hierarchyWidth);
                _overlay.style.right = 0;
                return;
            }

            // Find or reuse the hierarchy window reference
            if (_hierarchyWindow == null || _hierarchyWindow.Equals(null))
                _hierarchyWindow = FindHierarchyWindow();

            if (_hierarchyWindow == null) return;

            _overlay?.RemoveFromHierarchy();
            _sceneHierarchy        = null; // re-resolve after window recreate
            _expandMethod          = null;
            _frameMethod           = null;
            _setSearchFilterMethod = null;
            // EditorStyles throws NullReferenceException (not just returns null) during early
            // editor init — catch it and defer until styles are fully initialized.
            try
            {
                // The hierarchy "Create" button uses the internal toolbarCreateAddNewDropDown style,
                // followed by a GUILayout.Space(6f). Measuring with that style gives the correct
                // overlay start position so that the native search field (which starts right after
                // the space on narrow windows when FlexibleSpace collapses) is always fully covered.
                // Fallback to toolbarDropDown in case the internal property is ever removed.
                var createStyleProp = typeof(EditorStyles).GetProperty(
                    "toolbarCreateAddNewDropDown",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var createStyle = createStyleProp?.GetValue(null) as GUIStyle
                               ?? EditorStyles.toolbarDropDown;
                var createContent = EditorGUIUtility.TrTextContent("Create");
                _createBtnW = createStyle.CalcSize(createContent).x + 6f; // 6f = GUILayout.Space(6f)
            }
            catch { return; }

            _overlay = new IMGUIContainer(DrawNavbar);
            _overlay.style.position = Position.Absolute;
            _overlay.style.top      = 0;
            _overlay.style.left     = GetOverlayLeft(GetHierarchyWindowWidth());
            _overlay.style.right    = 0;
            _overlay.style.height   = NavH;
            _overlay.pickingMode    = PickingMode.Position;
            _hierarchyWindow.rootVisualElement.Add(_overlay);
        }

        private static EditorWindow FindHierarchyWindow()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w.GetType().Name == "SceneHierarchyWindow") return w;
            return null;
        }

        private static float GetHierarchyWindowWidth()
        {
            if (_hierarchyWindow != null && _hierarchyWindow?.position.width > 0f)
                return (float)_hierarchyWindow.position.width;
            return EditorGUIUtility.currentViewWidth;
        }

        private static float GetOverlayLeft(float hierarchyWidth)
        {
            // On narrow hierarchy widths Unity shifts native search layout to the left.
            // Add adaptive left shift so our overlay keeps covering it.
            const float kShiftStartWidth = 430f;
            const float kShiftRange      = 180f;
            const float kMaxNarrowShift  = 28f;
            float t           = Mathf.Clamp01((kShiftStartWidth - hierarchyWidth) / kShiftRange);
            float narrowShift = t * kMaxNarrowShift;
            return Mathf.Max(0f, _createBtnW - narrowShift);
        }

        // ── IMGUI ──────────────────────────────────────────────────────

        private static void DrawNavbar()
        {
            if (!NMPSettings.HierarchyFolderNavbar)
            {
                _overlay?.RemoveFromHierarchy();
                _overlay = null;
                return;
            }

            if (_dirty) Refresh();

            // Determine width based on the Hierarchy window size (overlay is drawn on top of it)
            float baseWidth = GetHierarchyWindowWidth();
            // The overlay is positioned to the right of the native Create button.
            // Use that region width directly for both masking and content.
            float overlayLeft  = GetOverlayLeft(baseWidth);
            float overlayWidth = Mathf.Max(0f, baseWidth - overlayLeft);
            float width        = overlayWidth;
            float inlineFieldWidth = width - NavH - 6f;
            bool canInlineSearch = inlineFieldWidth >= 70f;
            if (_searchMode && !canInlineSearch)
            {
                _searchMode = false;
                _searchJustOpened = false;
            }
            var   full  = new Rect(0, 0, overlayWidth, NavH);

            // Standard Unity toolbar background
            if (Event.current.type == EventType.Repaint)
                EditorStyles.toolbar.Draw(full, false, false, false, false);

            // Mask the Unity native search bar — must cover the FULL overlay width.
            // On narrow windows the search field may occupy the entire overlay area, so
            // leaving even the loupe-button zone uncovered lets the search bar show through.
            // The loupe button and folder buttons are drawn later, on top of this rect.
            var toolbarBg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f);
            EditorGUI.DrawRect(new Rect(0, 0, overlayWidth, NavH), toolbarBg);

            // Vertical edge separators to clearly delimit the folder-toolbar zone.
            var edgeColor = EditorGUIUtility.isProSkin
                ? new Color(0.52f, 0.52f, 0.52f, 1f)
                : new Color(0.58f, 0.58f, 0.58f, 1f);
            const float edgeW = 2f;
            const float edgePadY = 3f;
            const float leftEdgeOffset = -0.5f;
            float edgeY = Mathf.Round(edgePadY);
            float edgeH = Mathf.Max(0f, Mathf.Round(NavH - edgePadY * 2f));
            EditorGUI.DrawRect(new Rect(leftEdgeOffset, edgeY, edgeW, edgeH), edgeColor);

            // ── Loupe button (always visible, right side) ──────────────
            var loupeIcon = _searchMode
                ? EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_clear" : "clear").image
                : EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_Search Icon" : "Search Icon").image;
            var loupeBtnRect = new Rect(width - NavH + 2f, 2f, NavH - 4f, NavH - 4f);
            var loupeContent = new GUIContent(loupeIcon, _searchMode ? "Close search" : "Search hierarchy");
            EditorGUI.DrawRect(new Rect(Mathf.Max(0f, loupeBtnRect.x - edgeW), edgeY, edgeW, edgeH), edgeColor);

            if (GUI.Button(loupeBtnRect, loupeContent, NMPStyles.IconButton))
            {
                _searchMode = !_searchMode;
                if (_searchMode) _searchJustOpened = true;
                else
                {
                    _searchText = "";
                    SetHierarchySearch("");
                }
            }

            // ── Search mode ────────────────────────────────────────────
            // Disable search mode if the window is too narrow to accommodate the field
            if (_searchMode && !canInlineSearch)
            {
                _searchMode = false;
                _searchJustOpened = false;
            }
            if (_searchMode && canInlineSearch)
            {
                // Must call FocusTextInControl BEFORE drawing the control
                if (_searchJustOpened)
                {
                    EditorGUI.FocusTextInControl("NMPHierarchySearch");
                    _searchJustOpened = false;
                }

                var fieldRect = new Rect(4f, 2f, inlineFieldWidth, NavH - 4f);
                GUI.SetNextControlName("NMPHierarchySearch");

                EditorGUI.BeginChangeCheck();
                _searchText = EditorGUI.TextField(fieldRect, _searchText, EditorStyles.toolbarSearchField);
                if (EditorGUI.EndChangeCheck())
                    SetHierarchySearch(_searchText);
                return;
            }

            // ── Folder buttons (horizontally scrollable) ──────────────
            if (_folders.Count == 0) return;

            var   style      = NMPStyles.ToolbarButton;
            var   folderIcon = EditorGUIUtility.IconContent("Folder Icon").image;
            const float contentLeftInset = 3f; // keep folder buttons clear of the left separator
            float areaWidth  = Mathf.Max(0f, loupeBtnRect.x - 4f - contentLeftInset);
            var   areaRect   = new Rect(contentLeftInset, 0f, areaWidth, NavH);

            // Handle scroll wheel over the buttons area
            var evt = Event.current;
            if (evt.type == EventType.ScrollWheel && areaRect.Contains(evt.mousePosition))
            {
                _scrollOffsetX += evt.delta.y * 20f;
                evt.Use();
                _hierarchyWindow?.Repaint();
            }

            // Compute total content width to clamp scroll
            float totalW = 2f;
            foreach (var (name, _, _) in _folders)
            {
                var lbl = name.Length > 16 ? name.Substring(0, 15) + "\u2026" : name;
                totalW += Mathf.Clamp(style.CalcSize(new GUIContent(" " + lbl, folderIcon)).x + 6f, 50f, 120f) + 2f;
            }
            _scrollOffsetX = Mathf.Clamp(_scrollOffsetX, 0f, Mathf.Max(0f, totalW - areaWidth));

            // Clip drawing to the buttons area
            GUI.BeginClip(areaRect);

            float x = 2f - _scrollOffsetX;
            foreach (var (name, go, globalId) in _folders)
            {
                var label   = name.Length > 16 ? name.Substring(0, 15) + "\u2026" : name;
                var content = new GUIContent(" " + label, folderIcon, name);
                float btnW  = Mathf.Clamp(style.CalcSize(content).x + 6f, 50f, 120f);

                var btnRect = new Rect(x, 2f, btnW, NavH - 4f);

                // Only draw if at least partially visible
                if (x + btnW > 0f && x < areaWidth)
                {
                    bool hasColor = HierarchyColorManager.TryGetColor(globalId, out var folderColor);

                    // 1. Button for interaction + default background shape
                    bool clicked = GUI.Button(btnRect, hasColor ? GUIContent.none : content, style);

                    if (Event.current.type == EventType.Repaint && hasColor)
                    {
                        // 2. Color rect on top of button background
                        EditorGUI.DrawRect(btnRect, new Color(folderColor.r, folderColor.g, folderColor.b, 0.55f));
                        // 3. Icon + label in white on top of color
                        NMPStyles.ToolbarButtonLabel.Draw(btnRect, content, false, false, false, false);
                    }

                    if (clicked) ExpandAndFrame(go);
                }

                x += btnW + 2f;
            }

            GUI.EndClip();
        }

        // ── Folder cache ───────────────────────────────────────────────

        private static void Refresh()
        {
            _folders.Clear();
            var scene = SceneManager.GetActiveScene();

            foreach (var id in HierarchyFolderManager.FolderIds)
            {
                if (!GlobalObjectId.TryParse(id, out var gid)) continue;
                var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                if (obj is GameObject go && go.scene == scene)
                    _folders.Add((go.name, go, id));
            }
            _dirty = false;
        }

        // ── Folder expand & frame ──────────────────────────────────────

        private static void ExpandAndFrame(GameObject go)
        {
            Selection.activeGameObject = go;

            EnsureSceneHierarchyReflection();

            if (_sceneHierarchy != null)
            {
                int id = go.GetInstanceID();
                try { _expandMethod?.Invoke(_sceneHierarchy, new object[] { id, true }); }
                catch { /* ignore if unavailable */ }
                try { _frameMethod?.Invoke(_sceneHierarchy, new object[] { id, true }); }
                catch { /* ignore if unavailable */ }
            }
            else
            {
                EditorGUIUtility.PingObject(go);
            }

            _hierarchyWindow?.Repaint();
        }

        private static void EnsureSceneHierarchyReflection()
        {
            if (_sceneHierarchy != null && _expandMethod != null) return;
            if (_hierarchyWindow == null) return;

            _sceneHierarchy = _hierarchyWindow.GetType()
                .GetField("m_SceneHierarchy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(_hierarchyWindow);

            if (_sceneHierarchy == null) return;

            var shType = _sceneHierarchy.GetType();
            _expandMethod = shType.GetMethod("ExpandTreeViewItem",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int), typeof(bool) }, null);
            _frameMethod = shType.GetMethod("FrameObject",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int), typeof(bool) }, null);
        }

        // ── Hierarchy search ───────────────────────────────────────────

        private static void SetHierarchySearch(string filter)
        {
            if (_hierarchyWindow == null) return;

            if (_setSearchFilterMethod == null)
                _setSearchFilterMethod = FindSetSearchFilterMethod(_hierarchyWindow.GetType());

            if (_setSearchFilterMethod != null)
            {
                try
                {
                    // Parameter 1 is SearchMode enum — get value 0 (All) via its actual type
                    var searchModeType = _setSearchFilterMethod.GetParameters()[1].ParameterType;
                    var searchModeAll  = System.Enum.ToObject(searchModeType, 0);
                    _setSearchFilterMethod.Invoke(_hierarchyWindow, new object[] { filter, searchModeAll, true, false });
                }
                catch { /* ignore if unavailable on this Unity version */ }
            }

            _hierarchyWindow.Repaint();
        }

        private static MethodInfo FindSetSearchFilterMethod(System.Type t)
        {
            while (t != null && t != typeof(object))
            {
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != "SetSearchFilter") continue;
                    var p = m.GetParameters();
                    if (p.Length == 4 && p[0].ParameterType == typeof(string) && p[2].ParameterType == typeof(bool))
                        return m;
                }
                t = t.BaseType;
            }
            return null;
        }

    }
}
