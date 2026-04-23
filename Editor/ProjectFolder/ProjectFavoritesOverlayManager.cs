using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Alt-held favorites overlay for Project window (left pane).
    /// Supports drag-and-drop and context-menu add.
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectFavoritesOverlayManager
    {
        [Serializable]
        private class Entry
        {
            public string guid;
            public string path;
            public string globalId;
            public string displayName;
            public int page;
        }

        [Serializable]
        private class Data
        {
            public List<Entry> entries = new();
            public List<PageNameEntry> pageNames = new();
            public int minPageCount = 1;
        }

        [Serializable]
        private class PageNameEntry
        {
            public int index;
            public string name;
        }

        private readonly struct FavoriteItem
        {
            public readonly string Guid;
            public readonly string Path;
            public readonly string GlobalId;
            public readonly string DisplayName;

            public FavoriteItem(string guid, string path, string globalId, string displayName)
            {
                Guid = guid;
                Path = path;
                GlobalId = globalId;
                DisplayName = displayName;
            }
        }

        private static readonly string DataPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../ProjectSettings/NoMorePainProjectFavorites.json"));

        private static readonly List<FavoriteItem> Favorites = new();
        private static readonly List<int> FavoritePages = new();
        private static readonly Dictionary<string, Texture2D> IconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> PageNames = new();
        private static readonly Dictionary<int, float> PageScrollStarts = new();
        private static readonly Dictionary<int, float> PageScrollVisualStarts = new();
        private static readonly Dictionary<int, float> PageScrollVelocities = new();
        private static readonly BindingFlags ProjectBrowserBindingFlags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static EditorWindow _projectWindow;
        private static IMGUIContainer _overlay;
        private static VisualElement _cachedLeftPaneHost;
        private static GUIStyle _titleStyle;
        private static GUIStyle _rowStyle;
        private static GUIStyle _rowScaledStyle;
        private static int _rowScaledFontSize = -1;
        private static GUIStyle _hintStyle;
        private static GUIStyle _pageStyle;
        private static GUIStyle _pageHoverStyle;
        private static GUIStyle _pageEditStyle;
        private static GUIStyle _pageArrowStyle;
        private static GUIStyle _pageArrowHoverStyle;
        private static GUIStyle _pageArrowDisabledStyle;
        private static Texture2D _pagerPillTexture;
        private static bool _pagerPillTextureForPro;
        private static FieldInfo _projectBrowserTreeViewRectField;
        private static MethodInfo _projectBrowserFrameObjectMethod;

        private static float _leftTreeRightXEstimate = 220f;
        private static bool _hasLeftTreeRightXEstimate;
        private static float _topYEstimate = 36f;
        private static float _topYSecondEstimate = float.PositiveInfinity;
        private static float _bottomYEstimate = 560f;
        private static float _rowHeightEstimate = 18f;
        private static int _leftRowsSampleCount;
        private static int _leftMetricsFrame = -1;
        private static Rect _leftTreeFrameRectEstimate;
        private static bool _hasLeftTreeFrameRectEstimate;
        private static string _rightPanePingGuid;
        private static double _rightPanePingUntil;

        private static bool _wasAltPressed;
        private static int _page;
        private static float _rowHeightSlider;
        private static int _pressedRowIndex = -1;
        private static Vector2 _pressedMousePos;
        private static bool _isRowReorderDrag;
        private static int _dragSourceIndex = -1;
        private static int _dragSourcePage = -1;
        private static int _dragInsertIndex = -1;
        private static Vector2 _dragMousePos;
        private static int _dragArrowDirection;
        private static bool _wasOutsideFavoritesDuringCurrentDrag;
        private static bool _externalDragStartedInCurrentGesture;
        private static bool _isEditingPageName;
        private static int _editingPageIndex = -1;
        private static string _editingPageName = string.Empty;
        private static bool _focusPageNameFieldNextRepaint;
        private static int _manualMinPageCount = 1;
        private static int _currentItemsPerPage = 1;
        private static int _currentPageCount = 1;
        private static Rect _pagerPrevRect;
        private static Rect _pagerNextRect;
        private static bool _isScrollbarDrag;
        private static int _scrollbarDragPage = -1;
        private static float _scrollbarDragGrabOffsetY;
        private static double _lastScrollAnimationTime = -1d;
        private static Rect _favoritesPanelRect;

        private const float PanelMinWidth = 120f;
        private const float HeaderHeight = 24f;
        private const float BaseRowHeight = 20f;
        private const float MaxRowHeight = 72f;
        private const float FooterHeight = 32f;
        private const float RowHeightSliderWidth = 74f;
        private const float FavoritesScrollbarWidth = 6f;
        private const float FavoritesScrollbarGap = 3f;
        private const float ScrollVisualSmooth = 18f;
        private const float ScrollDragSmooth = 26f;
        private const float ScrollInertiaDamping = 24f;
        private const float ScrollMinVelocity = 0.05f;
        private const float ReorderAutoScrollRowsPerSecondMin = 0.95f;
        private const float ReorderAutoScrollRowsPerSecondMax = 4.2f;
        private const string RowHeightSliderPrefKey = "NoMorePain.ProjectFavoritesOverlay.RowHeightSlider";
        private const float ReorderDragStartDistance = 4f;
        private const string PageNameFieldControlName = "NMP_Favorites_PageName";

        private static float CurrentRowHeight => Mathf.Lerp(BaseRowHeight, MaxRowHeight, _rowHeightSlider);

        static ProjectFavoritesOverlayManager()
        {
            LoadData();
            _rowHeightSlider = Mathf.Clamp01(EditorPrefs.GetFloat(RowHeightSliderPrefKey, 0f));
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.update += EnsureOverlay;
        }

        private static void OnProjectChanged()
        {
            CompactMissingEntries();
            IconCache.Clear();
            _hasLeftTreeRightXEstimate = false;
            _leftTreeRightXEstimate = 220f;
            _topYEstimate = 36f;
            _topYSecondEstimate = float.PositiveInfinity;
            _bottomYEstimate = 560f;
            _rowHeightEstimate = 18f;
            _leftRowsSampleCount = 0;
            _leftMetricsFrame = -1;
            _leftTreeFrameRectEstimate = default;
            _hasLeftTreeFrameRectEstimate = false;
            _cachedLeftPaneHost = null;
            PageScrollStarts.Clear();
            PageScrollVisualStarts.Clear();
            PageScrollVelocities.Clear();
            _lastScrollAnimationTime = -1d;
            EditorApplication.RepaintProjectWindow();
        }

        private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            var evt = Event.current;
            if (evt == null) return;

            TryTrackProjectWindow();

            string path = AssetDatabase.GUIDToAssetPath(guid);
            int depth = GetAssetDepth(path);
            bool isGridTile = IsGridTileRect(selectionRect);
            bool isLeftTreePaneRow = IsProjectLeftTreeRow(selectionRect, depth, isGridTile);
            UpdateLeftTreeWidthFromRootRow(path, selectionRect, isGridTile);
            UpdateLeftTreeMetrics(selectionRect, isLeftTreePaneRow);
            DrawRightPanePingHighlight(guid, selectionRect, isLeftTreePaneRow, evt);

            if (NMPSettings.ProjectFavoritesOverlay)
            {
                bool altPressed = evt.alt;
                if (altPressed != _wasAltPressed)
                {
                    _wasAltPressed = altPressed;
                    EditorApplication.RepaintProjectWindow();
                }
            }
        }

        private static void EnsureOverlay()
        {
            if (!NMPSettings.ProjectFavoritesOverlay)
            {
                if (_overlay != null)
                {
                    _overlay.RemoveFromHierarchy();
                    _overlay = null;
                }
                EndPageNameEdit();
                return;
            }

            if (_projectWindow == null || _projectWindow.Equals(null))
                _projectWindow = FindProjectWindow();
            if (_projectWindow == null) return;

            if ((_isRowReorderDrag || _pressedRowIndex >= 0) && EditorWindow.mouseOverWindow != _projectWindow)
            {
                // Cursor is outside Project window: keep pressed-state alive so reorder
                // can resume when cursor returns to Favorites, but disable reorder visuals.
                _isRowReorderDrag = false;
                _dragInsertIndex = -1;
                _dragArrowDirection = 0;
                _wasOutsideFavoritesDuringCurrentDrag = true;

                // Full reset only when overlay is not active anymore.
                if (!_wasAltPressed && !_isEditingPageName)
                    ClearReorderState();
            }

            var root = _projectWindow.rootVisualElement;
            if (root == null) return;

            if (_overlay == null || _overlay.parent != root)
            {
                _overlay?.RemoveFromHierarchy();

                _overlay = new IMGUIContainer(DrawOverlayContainer);
                _overlay.style.position = Position.Absolute;
                _overlay.style.left = 0f;
                _overlay.style.top = 0f;
                _overlay.style.bottom = 0f;
                _overlay.style.right = StyleKeyword.Auto;
                _overlay.pickingMode = PickingMode.Position;

                root.Add(_overlay);
            }

            float windowW = Mathf.Max(PanelMinWidth, _projectWindow.position.width);
            float maxOverlayW = Mathf.Max(PanelMinWidth, windowW - PanelMinWidth);
            float overlayW;
            if (TryGetNativeTreeViewRect(out var nativeTreeRect))
            {
                CacheLeftTreeFrameRect(nativeTreeRect);
                overlayW = Mathf.Clamp(nativeTreeRect.xMax, PanelMinWidth, maxOverlayW);
                _leftTreeRightXEstimate = nativeTreeRect.xMax;
                _hasLeftTreeRightXEstimate = true;
                _topYEstimate = nativeTreeRect.yMin;
                _bottomYEstimate = nativeTreeRect.yMax;
            }
            else if (TryGetLeftTreeFrameRect(out var leftTreeRect))
            {
                CacheLeftTreeFrameRect(leftTreeRect);
                overlayW = Mathf.Clamp(leftTreeRect.xMax + 1f, PanelMinWidth, maxOverlayW);
                _leftTreeRightXEstimate = leftTreeRect.xMax;
                _hasLeftTreeRightXEstimate = true;
                _topYEstimate = leftTreeRect.yMin;
                _bottomYEstimate = leftTreeRect.yMax;
            }
            else if (_hasLeftTreeFrameRectEstimate)
            {
                var cachedRect = _leftTreeFrameRectEstimate;
                overlayW = Mathf.Clamp(cachedRect.xMax + 1f, PanelMinWidth, maxOverlayW);
            }
            else
            {
                overlayW = _hasLeftTreeRightXEstimate
                    ? Mathf.Clamp(_leftTreeRightXEstimate + 1f, PanelMinWidth, maxOverlayW)
                    : Mathf.Clamp(Mathf.Max(220f, windowW * 0.36f), PanelMinWidth, maxOverlayW);
            }
            bool captureDuringRowDrag = _pressedRowIndex >= 0 || _isRowReorderDrag;
            _overlay.style.width = captureDuringRowDrag ? windowW : overlayW;

            bool overlayActive = _wasAltPressed || _isEditingPageName;
            _overlay.pickingMode = overlayActive ? PickingMode.Position : PickingMode.Ignore;
            if (_projectWindow.wantsMouseMove != overlayActive)
                _projectWindow.wantsMouseMove = overlayActive;

            if (overlayActive && EditorWindow.mouseOverWindow == _projectWindow)
            {
                _overlay.MarkDirtyRepaint();
                _projectWindow.Repaint();
            }

            _overlay.BringToFront();
        }

        private static EditorWindow FindProjectWindow()
        {
            var focused = EditorWindow.focusedWindow;
            if (IsProjectBrowser(focused)) return focused;

            var hovered = EditorWindow.mouseOverWindow;
            if (IsProjectBrowser(hovered)) return hovered;

            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (IsProjectBrowser(w))
                    return w;
            }
            return null;
        }

        private static void DrawOverlayContainer()
        {
            var evt = Event.current;
            if (evt == null) return;

            if (evt.alt != _wasAltPressed)
            {
                _wasAltPressed = evt.alt;
                _projectWindow?.Repaint();
            }

            bool overlayActive = _wasAltPressed || _isEditingPageName;
            if (!overlayActive)
            {
                ClearReorderState();
                return;
            }
            DrawOverlay(evt);
        }

        private static void DrawOverlay(Event evt)
        {
            Rect panelRect = GetOverlayRect();
            if (panelRect.width < PanelMinWidth || panelRect.height < 96f)
                return;
            _favoritesPanelRect = panelRect;

            EnsurePageListIntegrity();
            EnsureStyles();
            UpdatePagerRects(panelRect);
            HandleDragAndDrop(panelRect, evt);

            var bgColor = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f);

            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(panelRect, bgColor);
            }

            bool showHeaderSlider = panelRect.width >= 176f;
            float headerSliderSpacing = showHeaderSlider ? 8f : 0f;
            float headerSliderWidth = showHeaderSlider ? RowHeightSliderWidth : 0f;
            var titleRect = new Rect(
                panelRect.x + 8f,
                panelRect.y + 2f,
                Mathf.Max(32f, panelRect.width - 16f - headerSliderWidth - headerSliderSpacing),
                HeaderHeight - 2f);
            GUI.Label(titleRect, "Favorites", _titleStyle);
            if (showHeaderSlider)
            {
                var sliderRect = new Rect(
                    panelRect.xMax - RowHeightSliderWidth - 14f,
                    panelRect.y + Mathf.Floor((HeaderHeight - 14f) * 0.5f) + 1f,
                    RowHeightSliderWidth,
                    14f);
                DrawRowHeightSlider(sliderRect);
            }

            var listRect = new Rect(
                panelRect.x + 4f,
                panelRect.y + HeaderHeight,
                panelRect.width - 8f,
                panelRect.height - HeaderHeight - FooterHeight - 4f);

            int pageCount = DrawPagedFavorites(listRect, evt);
            DrawPager(panelRect, pageCount, evt);

            if (panelRect.Contains(evt.mousePosition) &&
                (evt.type == EventType.MouseDown ||
                 evt.type == EventType.MouseUp ||
                 evt.type == EventType.MouseDrag ||
                 evt.type == EventType.ScrollWheel))
            {
                evt.Use();
            }

            if (evt.type == EventType.MouseMove && panelRect.Contains(evt.mousePosition))
            {
                _overlay?.MarkDirtyRepaint();
                _projectWindow?.Repaint();
            }
        }

        private static int DrawPagedFavorites(Rect listRect, Event evt)
        {
            float rowHeight = CurrentRowHeight;
            _currentItemsPerPage = Mathf.Max(1, Mathf.FloorToInt(listRect.height / Mathf.Max(1f, rowHeight)));
            int basePageCount = Mathf.Max(1, GetConfiguredMinPageCount());
            _page = Mathf.Max(0, _page);
            int currentPageItems = GetPageItemCount(_page);
            bool canShowOneMoreEmptyPage = currentPageItems > 0;
            int pageCount = Mathf.Max(basePageCount, _page + 1);
            if (canShowOneMoreEmptyPage)
                pageCount = Mathf.Max(pageCount, _page + 2);
            _currentPageCount = pageCount;
            _page = Mathf.Clamp(_page, 0, pageCount - 1);

            var pageItemIndices = GetIndicesForPage(_page);
            int maxStart = Mathf.Max(0, pageItemIndices.Count - _currentItemsPerPage);
            float dt = GetScrollDeltaTime();
            float targetStart = Mathf.Clamp(GetPageScrollStart(_page, maxStart), 0f, maxStart);
            float visualStart = Mathf.Clamp(GetPageScrollVisualStart(_page, targetStart, maxStart), 0f, maxStart);
            float velocity = GetPageScrollVelocity(_page);
            bool hasScroll = maxStart > 0;
            Rect contentRect = listRect;
            if (hasScroll)
            {
                float reducedWidth = Mathf.Max(40f, listRect.width - FavoritesScrollbarWidth - FavoritesScrollbarGap);
                contentRect = new Rect(listRect.x, listRect.y, reducedWidth, listRect.height);
            }

            if (_isScrollbarDrag && _scrollbarDragPage != _page)
                ClearScrollbarDrag();

            bool targetChangedByInput = false;
            if (!_isRowReorderDrag &&
                evt.type == EventType.ScrollWheel &&
                (contentRect.Contains(evt.mousePosition) || (hasScroll && listRect.Contains(evt.mousePosition))) &&
                maxStart > 0)
            {
                int step = evt.delta.y > 0f ? 1 : (evt.delta.y < 0f ? -1 : 0);
                if (step != 0)
                {
                    float nextTarget = Mathf.Clamp(targetStart + step, 0f, maxStart);
                    if (!Mathf.Approximately(nextTarget, targetStart))
                    {
                        targetStart = nextTarget;
                        velocity = 0f;
                        targetChangedByInput = true;
                        EditorApplication.RepaintProjectWindow();
                    }
                    evt.Use();
                }
            }

            int visibleStart = Mathf.Clamp(Mathf.RoundToInt(visualStart), 0, maxStart);
            int remaining = Mathf.Max(0, pageItemIndices.Count - visibleStart);
            int visibleCount = _currentItemsPerPage > 0
                ? Mathf.Min(remaining, _currentItemsPerPage)
                : remaining;
            Rect scrollTrackRect = default;
            Rect scrollThumbRect = default;
            bool hasScrollGeometry = hasScroll &&
                                     TryGetScrollGeometry(listRect, contentRect, pageItemIndices.Count, visibleCount, visualStart, maxStart,
                                         out scrollTrackRect, out scrollThumbRect);

            if (hasScrollGeometry)
            {
                bool overTrack = scrollTrackRect.Contains(evt.mousePosition);
                bool overThumb = scrollThumbRect.Contains(evt.mousePosition);

                if (evt.type == EventType.MouseDown && evt.button == 0 && overTrack)
                {
                    _isScrollbarDrag = true;
                    _scrollbarDragPage = _page;
                    _scrollbarDragGrabOffsetY = overThumb
                        ? evt.mousePosition.y - scrollThumbRect.yMin
                        : scrollThumbRect.height * 0.5f;

                    if (!overThumb)
                    {
                        float desiredTop = evt.mousePosition.y - _scrollbarDragGrabOffsetY;
                        float nextTarget = GetScrollStartFromThumbTop(desiredTop, scrollTrackRect, scrollThumbRect.height, maxStart);
                        if (!Mathf.Approximately(nextTarget, targetStart))
                        {
                            if (dt > 0f)
                                velocity = (nextTarget - targetStart) / dt;
                            targetStart = nextTarget;
                            targetChangedByInput = true;
                            EditorApplication.RepaintProjectWindow();
                        }
                    }
                    evt.Use();
                }
                else if (evt.type == EventType.MouseDrag && _isScrollbarDrag && _scrollbarDragPage == _page)
                {
                    float desiredTop = evt.mousePosition.y - _scrollbarDragGrabOffsetY;
                    float nextTarget = GetScrollStartFromThumbTop(desiredTop, scrollTrackRect, scrollThumbRect.height, maxStart);
                    if (!Mathf.Approximately(nextTarget, targetStart))
                    {
                        if (dt > 0f)
                            velocity = (nextTarget - targetStart) / dt;
                        targetStart = nextTarget;
                        targetChangedByInput = true;
                        EditorApplication.RepaintProjectWindow();
                    }
                    evt.Use();
                }
            }

            if (evt.type == EventType.MouseUp && evt.button == 0 && _isScrollbarDrag)
                ClearScrollbarDrag();

            if (TryApplyReorderEdgeAutoScroll(contentRect, rowHeight, dt, maxStart, ref targetStart, ref velocity))
            {
                targetChangedByInput = true;
                EditorApplication.RepaintProjectWindow();
            }

            bool draggingScrollThisPage = _isScrollbarDrag && _scrollbarDragPage == _page;
            if (!draggingScrollThisPage && maxStart > 0)
            {
                if (Mathf.Abs(velocity) > ScrollMinVelocity)
                {
                    float rawTarget = targetStart + velocity * dt;
                    float clampedTarget = Mathf.Clamp(rawTarget, 0f, maxStart);
                    bool hitEdge = !Mathf.Approximately(rawTarget, clampedTarget);
                    targetStart = clampedTarget;
                    if (hitEdge)
                    {
                        velocity = 0f;
                    }
                    else
                    {
                        velocity = Mathf.MoveTowards(velocity, 0f, ScrollInertiaDamping * dt);
                    }
                }
                else if (!targetChangedByInput)
                {
                    velocity = 0f;
                }
            }
            else if (maxStart <= 0)
            {
                targetStart = 0f;
                visualStart = 0f;
                velocity = 0f;
            }

            float smoothStrength = draggingScrollThisPage ? ScrollDragSmooth : ScrollVisualSmooth;
            float smoothT = 1f - Mathf.Exp(-Mathf.Max(1f, smoothStrength) * Mathf.Max(0.0001f, dt));
            visualStart = Mathf.Lerp(visualStart, targetStart, smoothT);
            if (Mathf.Abs(targetStart - visualStart) < 0.001f)
                visualStart = targetStart;

            SetPageScrollStart(_page, targetStart);
            SetPageScrollVisualStart(_page, visualStart);
            SetPageScrollVelocity(_page, velocity);

            visibleStart = Mathf.Clamp(Mathf.FloorToInt(visualStart), 0, maxStart);
            float rowOffsetY = -(visualStart - visibleStart) * rowHeight;
            remaining = Mathf.Max(0, pageItemIndices.Count - visibleStart);
            int logicalVisibleCount = _currentItemsPerPage > 0
                ? Mathf.Min(remaining, _currentItemsPerPage)
                : remaining;
            bool hasFractionalOffset = (visualStart - visibleStart) > 0.001f;
            int drawVisibleCount = Mathf.Min(remaining, logicalVisibleCount + (hasFractionalOffset ? 1 : 0));
            hasScrollGeometry = hasScroll &&
                                TryGetScrollGeometry(listRect, contentRect, pageItemIndices.Count, logicalVisibleCount, visualStart, maxStart,
                                    out scrollTrackRect, out scrollThumbRect);

            if (_isRowReorderDrag && _dragSourcePage == _page)
            {
                bool nearListByX = _dragMousePos.x >= contentRect.xMin - 24f && _dragMousePos.x <= contentRect.xMax + 24f;
                bool nearListByY = _dragMousePos.y >= contentRect.yMin - 6f && _dragMousePos.y <= contentRect.yMax + 6f;
                if (nearListByX && nearListByY)
                {
                    int localInsert = GetInsertSlotFromMouseY(contentRect, rowHeight, logicalVisibleCount, _dragMousePos.y, rowOffsetY);
                    _dragInsertIndex = visibleStart + localInsert;
                }
            }

            bool needsAnimationRepaint = hasScroll &&
                                         (Mathf.Abs(targetStart - visualStart) > 0.001f || Mathf.Abs(velocity) > ScrollMinVelocity);
            if (needsAnimationRepaint)
                EditorApplication.RepaintProjectWindow();

            if (logicalVisibleCount == 0)
            {
                DrawEmptyState(contentRect);
            }
            else
            {
                if (evt.type == EventType.Repaint)
                {
                    GUI.BeginGroup(contentRect);
                    Vector2 localMouse = Event.current.mousePosition;
                    int rowIndex = 0;
                    for (int v = 0; v < drawVisibleCount; v++)
                    {
                        int pageSlot = visibleStart + v;
                        if (pageSlot < 0 || pageSlot >= pageItemIndices.Count)
                            break;

                        int itemIndex = pageItemIndices[pageSlot];
                        if (itemIndex < 0 || itemIndex >= Favorites.Count)
                            continue;

                        var item = Favorites[itemIndex];
                        var rowRect = new Rect(0f, rowIndex * rowHeight + rowOffsetY, contentRect.width, rowHeight - 1f);
                        if (rowRect.yMin > contentRect.height + 0.1f)
                            break;
                        if (rowRect.yMax < -0.1f)
                        {
                            rowIndex++;
                            continue;
                        }

                        if (!(_isRowReorderDrag && itemIndex == _dragSourceIndex))
                            DrawRowVisual(item, rowRect, localMouse, pageSlot);
                        rowIndex++;
                    }
                    GUI.EndGroup();
                }
                else
                {
                    int rowIndex = 0;
                    for (int v = 0; v < drawVisibleCount; v++)
                    {
                        int pageSlot = visibleStart + v;
                        if (pageSlot < 0 || pageSlot >= pageItemIndices.Count)
                            break;

                        int itemIndex = pageItemIndices[pageSlot];
                        if (itemIndex < 0 || itemIndex >= Favorites.Count)
                            continue;

                        var item = Favorites[itemIndex];
                        var rowRect = new Rect(contentRect.x, contentRect.y + rowIndex * rowHeight + rowOffsetY, contentRect.width, rowHeight - 1f);
                        if (rowRect.yMin > contentRect.yMax + 0.1f)
                            break;
                        if (rowRect.yMax < contentRect.yMin - 0.1f)
                        {
                            rowIndex++;
                            continue;
                        }

                        if (!(_isRowReorderDrag && itemIndex == _dragSourceIndex))
                            DrawRow(item, itemIndex, rowRect, evt);
                        rowIndex++;
                    }
                }
            }

            if (evt.type == EventType.Repaint && hasScrollGeometry)
            {
                bool thumbHovered = scrollThumbRect.Contains(evt.mousePosition);
                bool thumbDragging = _isScrollbarDrag && _scrollbarDragPage == _page;
                DrawScrollIndicator(scrollTrackRect, scrollThumbRect, thumbHovered, thumbDragging);
            }

            HandleRowReorder(contentRect, rowHeight, logicalVisibleCount, visibleStart, rowOffsetY, evt);
            return pageCount;
        }

        private static bool TryGetScrollGeometry(
            Rect listRect,
            Rect contentRect,
            int totalCount,
            int visibleCount,
            float visibleStart,
            int maxStart,
            out Rect trackRect,
            out Rect thumbRect)
        {
            trackRect = default;
            thumbRect = default;
            if (totalCount <= 0 || maxStart <= 0 || visibleCount <= 0)
                return false;

            float trackX = contentRect.xMax + FavoritesScrollbarGap;
            trackRect = new Rect(trackX, listRect.y + 1f, FavoritesScrollbarWidth, Mathf.Max(8f, listRect.height - 2f));

            float ratio = Mathf.Clamp01((float)visibleCount / Mathf.Max(1, totalCount));
            float thumbHeight = Mathf.Clamp(trackRect.height * ratio, 18f, trackRect.height);
            float travel = Mathf.Max(0f, trackRect.height - thumbHeight);
            float normalized = maxStart > 0 ? Mathf.Clamp01(visibleStart / maxStart) : 0f;
            float thumbY = trackRect.y + travel * normalized;
            thumbRect = new Rect(trackRect.x, thumbY, trackRect.width, thumbHeight);
            return true;
        }

        private static float GetScrollStartFromThumbTop(float thumbTop, Rect trackRect, float thumbHeight, int maxStart)
        {
            if (maxStart <= 0)
                return 0f;

            float travel = Mathf.Max(0.0001f, trackRect.height - thumbHeight);
            float normalized = Mathf.Clamp01((thumbTop - trackRect.y) / travel);
            return Mathf.Clamp(normalized * maxStart, 0f, maxStart);
        }

        private static bool TryApplyReorderEdgeAutoScroll(
            Rect contentRect,
            float rowHeight,
            float dt,
            int maxStart,
            ref float targetStart,
            ref float velocity)
        {
            if (!_isRowReorderDrag || _dragSourceIndex < 0)
                return false;
            if (_dragSourcePage != _page)
                return false;
            if (_isScrollbarDrag)
                return false;
            if (maxStart <= 0)
                return false;

            float edgeSize = Mathf.Clamp(rowHeight * 0.85f, 12f, 30f);
            float xPad = 24f;
            bool inXRange = _dragMousePos.x >= contentRect.xMin - xPad && _dragMousePos.x <= contentRect.xMax + xPad;
            if (!inXRange)
                return false;

            bool nearTop = _dragMousePos.y <= contentRect.yMin + edgeSize;
            bool nearBottom = _dragMousePos.y >= contentRect.yMax - edgeSize;
            if (!nearTop && !nearBottom)
                return false;

            float strength = nearTop
                ? Mathf.Clamp01((contentRect.yMin + edgeSize - _dragMousePos.y) / Mathf.Max(1f, edgeSize))
                : Mathf.Clamp01((_dragMousePos.y - (contentRect.yMax - edgeSize)) / Mathf.Max(1f, edgeSize));

            float rowsPerSecond = Mathf.Lerp(ReorderAutoScrollRowsPerSecondMin, ReorderAutoScrollRowsPerSecondMax, strength);
            float step = rowsPerSecond * Mathf.Max(0.001f, dt);
            float nextTarget = Mathf.Clamp(targetStart + (nearTop ? -step : step), 0f, maxStart);
            if (Mathf.Abs(nextTarget - targetStart) <= 0.0001f)
                return false;

            targetStart = nextTarget;
            velocity = 0f;
            return true;
        }

        private static void DrawScrollIndicator(Rect trackRect, Rect thumbRect, bool thumbHovered, bool thumbDragging)
        {
            var trackColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.08f)
                : new Color(0f, 0f, 0f, 0.12f);
            DrawRoundedFilledRect(trackRect, trackColor, trackRect.width * 0.5f);

            var thumbColor = EditorGUIUtility.isProSkin
                ? (thumbDragging
                    ? new Color(0.98f, 0.98f, 0.98f, 0.68f)
                    : (thumbHovered
                        ? new Color(0.96f, 0.96f, 0.96f, 0.58f)
                        : new Color(0.93f, 0.93f, 0.93f, 0.45f)))
                : (thumbDragging
                    ? new Color(0.12f, 0.12f, 0.12f, 0.62f)
                    : (thumbHovered
                        ? new Color(0.14f, 0.14f, 0.14f, 0.52f)
                        : new Color(0.15f, 0.15f, 0.15f, 0.42f)));
            DrawRoundedFilledRect(thumbRect, thumbColor, thumbRect.width * 0.5f);
        }

        private static void DrawRoundedFilledRect(Rect rect, Color color, float radius)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            float r = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
            if (r <= 0.01f)
            {
                EditorGUI.DrawRect(rect, color);
                return;
            }

            int arcSegments = Mathf.Clamp(Mathf.CeilToInt(r * 0.8f), 4, 12);
            var points = new List<Vector3>(arcSegments * 4 + 4);

            Vector2 tl = new Vector2(rect.xMin + r, rect.yMin + r);
            Vector2 tr = new Vector2(rect.xMax - r, rect.yMin + r);
            Vector2 br = new Vector2(rect.xMax - r, rect.yMax - r);
            Vector2 bl = new Vector2(rect.xMin + r, rect.yMax - r);

            AppendArc(points, tl, r, 180f, 270f, arcSegments, includeStart: true);
            AppendArc(points, tr, r, 270f, 360f, arcSegments, includeStart: false);
            AppendArc(points, br, r, 0f, 90f, arcSegments, includeStart: false);
            AppendArc(points, bl, r, 90f, 180f, arcSegments, includeStart: false);

            var previousColor = Handles.color;
            Handles.color = color;
            Handles.DrawAAConvexPolygon(points.ToArray());
            Handles.color = previousColor;
        }

        private static void DrawEmptyState(Rect listRect)
        {
            float lineHeight = 24f;
            var line1 = new Rect(listRect.x + 6f, listRect.center.y - lineHeight, listRect.width - 12f, lineHeight);
            var line2 = new Rect(listRect.x + 6f, listRect.center.y + 2f, listRect.width - 12f, lineHeight);
            GUI.Label(line1, "Drop folders, assets", _hintStyle);
            GUI.Label(line2, "or GameObjects", _hintStyle);
        }

        private static void EnsurePageListIntegrity()
        {
            if (FavoritePages.Count < Favorites.Count)
            {
                int addCount = Favorites.Count - FavoritePages.Count;
                for (int i = 0; i < addCount; i++)
                    FavoritePages.Add(0);
            }
            else if (FavoritePages.Count > Favorites.Count)
            {
                FavoritePages.RemoveRange(Favorites.Count, FavoritePages.Count - Favorites.Count);
            }
        }

        private static void DrawRow(FavoriteItem item, int itemIndex, Rect rowRect, Event evt)
        {
            if (evt.type == EventType.Repaint)
                DrawRowVisual(item, rowRect, evt.mousePosition);

            float removeSize = Mathf.Clamp(rowRect.height * 0.45f, 14f, 30f);
            float removeY = rowRect.y + Mathf.Floor((rowRect.height - removeSize) * 0.5f);
            var removeRect = new Rect(rowRect.xMax - removeSize - 4f, removeY, removeSize, removeSize);

            if (evt.type == EventType.MouseDown && evt.button == 0 && removeRect.Contains(evt.mousePosition))
            {
                RemoveItemAtIndex(itemIndex);
                SaveData();
                EditorApplication.RepaintProjectWindow();
                ClearReorderState();
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDown && rowRect.Contains(evt.mousePosition))
            {
                if (evt.button == 0)
                {
                    if (evt.clickCount >= 2)
                    {
                        OpenFavoriteItem(item);
                        ClearReorderState();
                        evt.Use();
                        return;
                    }

                    _pressedRowIndex = itemIndex;
                    _pressedMousePos = evt.mousePosition;
                    _isRowReorderDrag = false;
                    _dragSourceIndex = itemIndex;
                    _dragSourcePage = itemIndex >= 0 && itemIndex < FavoritePages.Count
                        ? Mathf.Max(0, FavoritePages[itemIndex])
                        : Mathf.Max(0, _page);
                    _dragInsertIndex = GetSlotInPage(itemIndex, _dragSourcePage);
                    _dragMousePos = evt.mousePosition;
                    _wasOutsideFavoritesDuringCurrentDrag = false;
                    _externalDragStartedInCurrentGesture = false;
                    FocusItem(item);
                    evt.Use();
                }
                else if (evt.button == 1)
                {
                    ShowRowContextMenu(item, itemIndex);
                    evt.Use();
                }
            }
        }

        private static void DrawRowVisual(FavoriteItem item, Rect rowRect, Vector2 mousePosition, int zebraIndex = -1)
        {
            bool isHover = rowRect.Contains(mousePosition);
            bool isSelected = IsItemSelected(item);
            string itemPath = TryResolveAssetPath(item);
            bool isFolder = !string.IsNullOrEmpty(itemPath) && AssetDatabase.IsValidFolder(itemPath);
            Color folderColor = default;
            bool hasFolderColor = isFolder && ProjectFolderStyleManager.TryGetColorForFolderPath(itemPath, out folderColor);
            bool drawRowTint = hasFolderColor && NMPSettings.ProjectRowColors;
            bool drawIconTint = hasFolderColor && NMPSettings.ProjectFolderColors;
            bool drawZebra = NMPSettings.ProjectZebra && !drawRowTint;
            if (drawZebra)
            {
                bool isOdd;
                if (zebraIndex >= 0)
                    isOdd = (zebraIndex & 1) != 0;
                else
                    isOdd = Mathf.RoundToInt(rowRect.y / Mathf.Max(1f, rowRect.height)) % 2 != 0;

                if (isOdd)
                {
                    var zebraColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.07f)
                        : new Color(0f, 0f, 0f, 0.07f);
                    EditorGUI.DrawRect(rowRect, zebraColor);
                }
            }

            if (drawRowTint)
            {
                EditorGUI.DrawRect(rowRect, new Color(folderColor.r, folderColor.g, folderColor.b, 0.30f));
                EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 3f, rowRect.height), new Color(folderColor.r, folderColor.g, folderColor.b, 1f));
            }

            if (isSelected)
            {
                var sel = EditorGUIUtility.isProSkin
                    ? new Color(NMPStyles.AccentColor.r, NMPStyles.AccentColor.g, NMPStyles.AccentColor.b, 0.35f)
                    : new Color(NMPStyles.AccentColor.r, NMPStyles.AccentColor.g, NMPStyles.AccentColor.b, 0.28f);
                EditorGUI.DrawRect(rowRect, sel);
            }
            else if (isHover)
            {
                var hover = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.08f)
                    : new Color(0f, 0f, 0f, 0.08f);
                EditorGUI.DrawRect(rowRect, hover);
            }

            float rowScale = Mathf.InverseLerp(BaseRowHeight - 1f, MaxRowHeight - 1f, rowRect.height);
            float iconSize = Mathf.Clamp(rowRect.height * 0.72f, 16f, 44f);
            float iconY = rowRect.y + Mathf.Floor((rowRect.height - iconSize) * 0.5f);
            var iconRect = new Rect(rowRect.x + 4f, iconY, iconSize, iconSize);
            float removeSize = Mathf.Clamp(rowRect.height * 0.45f, 14f, 30f);
            float removeY = rowRect.y + Mathf.Floor((rowRect.height - removeSize) * 0.5f);
            var removeRect = new Rect(rowRect.xMax - removeSize - 4f, removeY, removeSize, removeSize);
            bool removeHover = removeRect.Contains(mousePosition);

            float textX = iconRect.xMax + 5f;
            var textRect = new Rect(textX, rowRect.y, Mathf.Max(0f, removeRect.xMin - textX - 4f), rowRect.height);
            int textFontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(11f, 16f, rowScale)), 11, 16);
            GUIStyle rowTextStyle = GetScaledRowStyle(textFontSize);

            DrawFavoriteIcon(item, iconRect, itemPath, isFolder, drawIconTint, folderColor);
            if (isFolder && NMPSettings.ProjectBadgeIcons)
                DrawFolderBadge(itemPath, iconRect);
            GUI.Label(textRect, GetDisplayName(item), rowTextStyle);
            if (isHover || removeHover)
                DrawRemoveGlyph(removeRect, removeHover);
        }

        private static void HandleRowReorder(Rect listRect, float rowHeight, int visibleCount, int visibleStart, float rowOffsetY, Event evt)
        {
            if ((_isRowReorderDrag || _pressedRowIndex >= 0) && evt.type == EventType.MouseLeaveWindow)
            {
                // If user leaves the Project window while dragging a row, try promoting
                // to external Unity drag first (e.g. dropping into Scene/Hierarchy/Inspector).
                int sourceIndex = _isRowReorderDrag ? _dragSourceIndex : _pressedRowIndex;
                if (!_externalDragStartedInCurrentGesture &&
                    sourceIndex >= 0 &&
                    TryStartExternalAssetDrag(sourceIndex))
                {
                    _externalDragStartedInCurrentGesture = true;
                    _isRowReorderDrag = false;
                    _dragInsertIndex = -1;
                    _dragArrowDirection = 0;
                    _wasOutsideFavoritesDuringCurrentDrag = true;
                    evt.Use();
                    return;
                }

                // Keep press-state so user can return cursor into Favorites
                // and continue reorder if external drag wasn't started.
                _isRowReorderDrag = false;
                _dragInsertIndex = -1;
                _dragArrowDirection = 0;
                _wasOutsideFavoritesDuringCurrentDrag = true;
                EditorApplication.RepaintProjectWindow();
                evt.Use();
                return;
            }

            if ((evt.type == EventType.MouseDrag || evt.type == EventType.DragUpdated) && _pressedRowIndex >= 0)
            {
                _dragMousePos = evt.mousePosition;
                bool dragPastThreshold = HasDragExceededStartThreshold(evt.mousePosition);
                bool isOutsideFavorites = IsOutsideFavoritesForDragStart(evt.mousePosition);
                int sourceIndex = _dragSourceIndex >= 0 ? _dragSourceIndex : _pressedRowIndex;

                if (!isOutsideFavorites && _externalDragStartedInCurrentGesture)
                {
                    // User returned back into Favorites while still holding drag:
                    // cancel external payload and continue local reorder.
                    CancelExternalDragPayload();
                    _externalDragStartedInCurrentGesture = false;
                    _wasOutsideFavoritesDuringCurrentDrag = true;
                }

                if (isOutsideFavorites)
                {
                    // Outside Favorites: disable reorder mode.
                    _isRowReorderDrag = false;
                    _dragInsertIndex = -1;
                    _wasOutsideFavoritesDuringCurrentDrag = true;

                    // Start external drag when cursor reaches Project outer edge and moves outward.
                    // This lets us start drag before IMGUI events are lost on fast cursor exit.
                    if (!_externalDragStartedInCurrentGesture &&
                        sourceIndex >= 0 &&
                        dragPastThreshold &&
                        (IsOutsideProjectWindowBounds(evt.mousePosition) ||
                         IsAtProjectWindowEdgeMovingOutward(evt.mousePosition, evt.delta)) &&
                        TryStartExternalAssetDrag(sourceIndex))
                    {
                        _externalDragStartedInCurrentGesture = true;
                        evt.Use();
                        return;
                    }

                    EditorApplication.RepaintProjectWindow();
                    evt.Use();
                    return;
                }

                if (_wasOutsideFavoritesDuringCurrentDrag && !_isRowReorderDrag)
                {
                    // Cursor returned into Favorites: re-enable reorder mode for this drag.
                    _isRowReorderDrag = true;
                    _dragSourceIndex = Mathf.Clamp(_pressedRowIndex, 0, Mathf.Max(0, Favorites.Count - 1));
                    _dragSourcePage = _dragSourceIndex >= 0 && _dragSourceIndex < FavoritePages.Count
                        ? Mathf.Max(0, FavoritePages[_dragSourceIndex])
                        : Mathf.Max(0, _page);
                    _wasOutsideFavoritesDuringCurrentDrag = false;
                    _externalDragStartedInCurrentGesture = false;
                }

                if (!_isRowReorderDrag)
                {
                    if (dragPastThreshold)
                    {
                        _isRowReorderDrag = true;
                        _dragSourceIndex = Mathf.Clamp(_pressedRowIndex, 0, Mathf.Max(0, Favorites.Count - 1));
                    }
                }

                if (_isRowReorderDrag)
                {
                    if (TryHandleCrossPageDragOnPager(evt.mousePosition))
                    {
                        EditorApplication.RepaintProjectWindow();
                        evt.Use();
                        return;
                    }

                    int localInsert = GetInsertSlotFromMouseY(listRect, rowHeight, visibleCount, evt.mousePosition.y, rowOffsetY);
                    _dragInsertIndex = visibleStart + localInsert;
                    EditorApplication.RepaintProjectWindow();
                    evt.Use();
                }
            }

            if (evt.type == EventType.Repaint && _isRowReorderDrag)
            {
                DrawReorderInsertionLine(listRect, rowHeight, visibleCount, _dragInsertIndex - visibleStart, rowOffsetY);
                DrawDraggedRowPreview(listRect, rowHeight);
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                bool releasedOutsideFavorites = IsOutsideFavoritesStrict(evt.mousePosition);
                bool hadReorderDrag = _isRowReorderDrag && !releasedOutsideFavorites;
                bool didReorder = false;
                bool didCompact = false;
                if (hadReorderDrag && _dragSourceIndex >= 0 && _dragSourceIndex < Favorites.Count)
                {
                    int targetPage = Mathf.Max(0, _page);
                    int insert = Mathf.Clamp(_dragInsertIndex, 0, GetPageItemCount(targetPage));
                    didReorder = MoveFavoriteItem(_dragSourceIndex, targetPage, insert);
                }

                if (hadReorderDrag)
                    didCompact = CompactPageLayout();
                if (didReorder || didCompact)
                {
                    SaveData();
                    EditorApplication.RepaintProjectWindow();
                }

                ClearReorderState();
                if (didReorder || didCompact || releasedOutsideFavorites)
                    evt.Use();
            }

            if ((_isRowReorderDrag || _pressedRowIndex >= 0) && evt.type == EventType.DragExited)
            {
                int sourceIndex = _dragSourceIndex >= 0 ? _dragSourceIndex : _pressedRowIndex;
                if (!_externalDragStartedInCurrentGesture &&
                    sourceIndex >= 0 &&
                    TryStartExternalAssetDrag(sourceIndex))
                {
                    _externalDragStartedInCurrentGesture = true;
                    _isRowReorderDrag = false;
                    _dragInsertIndex = -1;
                    _dragArrowDirection = 0;
                    _wasOutsideFavoritesDuringCurrentDrag = true;
                    evt.Use();
                    return;
                }

                // Don't fully reset press-state here: user may move cursor back into Favorites
                // and continue reorder in the same drag gesture.
                _isRowReorderDrag = false;
                _dragInsertIndex = -1;
                _dragArrowDirection = 0;
                _wasOutsideFavoritesDuringCurrentDrag = true;
                EditorApplication.RepaintProjectWindow();
                evt.Use();
                return;
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
                ClearReorderState();
        }

        private static bool IsOutsideFavoritesForDragStart(Vector2 mousePosition)
        {
            // Use a small inward margin so external drag can start reliably
            // even on fast cursor movement near Favorites border.
            const float edgeMargin = 12f;
            return IsOutsideFavorites(mousePosition, edgeMargin);
        }

        private static bool IsOutsideFavoritesStrict(Vector2 mousePosition)
        {
            // Strict geometry check used for mouse-up commit logic.
            return IsOutsideFavorites(mousePosition, 0f);
        }

        private static bool IsOutsideFavorites(Vector2 mousePosition, float inwardMargin)
        {
            if (_favoritesPanelRect.width <= 0f || _favoritesPanelRect.height <= 0f)
                return false;

            float xMin = _favoritesPanelRect.xMin + Mathf.Max(0f, inwardMargin);
            float xMax = _favoritesPanelRect.xMax - Mathf.Max(0f, inwardMargin);
            float yMin = _favoritesPanelRect.yMin + Mathf.Max(0f, inwardMargin);
            float yMax = _favoritesPanelRect.yMax - Mathf.Max(0f, inwardMargin);

            if (xMax <= xMin || yMax <= yMin)
                return true;

            return mousePosition.x <= xMin ||
                   mousePosition.x >= xMax ||
                   mousePosition.y <= yMin ||
                   mousePosition.y >= yMax;
        }

        private static bool IsOutsideProjectWindowBounds(Vector2 mousePosition)
        {
            float width = 0f;
            float height = 0f;

            if (_overlay != null)
            {
                width = _overlay.contentRect.width;
                height = _overlay.contentRect.height;
            }

            if ((width <= 0.1f || height <= 0.1f) && _projectWindow != null)
            {
                width = Mathf.Max(width, _projectWindow.position.width);
                height = Mathf.Max(height, _projectWindow.position.height);
            }

            if (width <= 0.1f || height <= 0.1f)
                return false;

            return mousePosition.x < 0f ||
                   mousePosition.x > width ||
                   mousePosition.y < 0f ||
                   mousePosition.y > height;
        }

        private static bool IsAtProjectWindowEdgeMovingOutward(Vector2 mousePosition, Vector2 delta)
        {
            float width = 0f;
            float height = 0f;

            if (_overlay != null)
            {
                width = _overlay.contentRect.width;
                height = _overlay.contentRect.height;
            }

            if ((width <= 0.1f || height <= 0.1f) && _projectWindow != null)
            {
                width = Mathf.Max(width, _projectWindow.position.width);
                height = Mathf.Max(height, _projectWindow.position.height);
            }

            if (width <= 0.1f || height <= 0.1f)
                return false;

            const float edgeThreshold = 16f;
            bool nearLeft = mousePosition.x <= edgeThreshold && delta.x < -0.01f;
            bool nearRight = mousePosition.x >= width - edgeThreshold && delta.x > 0.01f;
            bool nearTop = mousePosition.y <= edgeThreshold && delta.y < -0.01f;
            bool nearBottom = mousePosition.y >= height - edgeThreshold && delta.y > 0.01f;
            return nearLeft || nearRight || nearTop || nearBottom;
        }

        private static bool HasDragExceededStartThreshold(Vector2 mousePosition)
        {
            Vector2 delta = mousePosition - _pressedMousePos;
            return delta.sqrMagnitude >= ReorderDragStartDistance * ReorderDragStartDistance;
        }

        private static bool TryStartExternalAssetDrag(int sourceIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= Favorites.Count)
                return false;

            var item = Favorites[sourceIndex];
            string path = TryResolveAssetPath(item);
            if (string.IsNullOrEmpty(path))
                return false;
            if (AssetDatabase.IsValidFolder(path))
                return false;

            var obj = AssetDatabase.LoadMainAssetAtPath(path);
            if (obj == null)
                return false;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new[] { obj };
            DragAndDrop.paths = new[] { path };
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            try
            {
                DragAndDrop.StartDrag(GetDisplayName(item));
                return true;
            }
            catch
            {
                CancelExternalDragPayload();
                return false;
            }
        }

        private static void CancelExternalDragPayload()
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
            DragAndDrop.paths = Array.Empty<string>();
            DragAndDrop.visualMode = DragAndDropVisualMode.None;
        }

        private static bool TryHandleCrossPageDragOnPager(Vector2 mousePosition)
        {
            bool overPrev = _page > 0 && _pagerPrevRect.Contains(mousePosition);
            bool overNext = _pagerNextRect.Contains(mousePosition);
            int direction = overPrev ? -1 : (overNext ? 1 : 0);

            if (direction == 0)
            {
                _dragArrowDirection = 0;
                return false;
            }

            if (direction == _dragArrowDirection)
                return true;

            _dragArrowDirection = direction;

            if (direction < 0)
            {
                _page = Mathf.Max(0, _page - 1);
            }
            else
            {
                if (_page >= _currentPageCount - 1)
                {
                    EnsureManualMinPageCount(_currentPageCount + 1);
                    _currentPageCount = Mathf.Max(_currentPageCount, GetConfiguredMinPageCount());
                }
                _page = Mathf.Min(_currentPageCount - 1, _page + 1);
            }

            if (_isEditingPageName && _editingPageIndex != _page)
                EndPageNameEdit();

            _dragInsertIndex = GetPageItemCount(_page);
            return true;
        }

        private static int GetInsertSlotFromMouseY(Rect listRect, float rowHeight, int visibleCount, float mouseY, float rowOffsetY)
        {
            if (visibleCount <= 0)
                return 0;

            float yMin = listRect.yMin + rowOffsetY;
            if (mouseY <= yMin + 1f)
                return 0;
            if (mouseY >= yMin + visibleCount * rowHeight - 1f)
                return visibleCount;

            float localY = mouseY - yMin;
            int relative = Mathf.Clamp(Mathf.FloorToInt(localY / rowHeight), 0, visibleCount - 1);
            float rowTop = yMin + relative * rowHeight;
            bool firstHalf = mouseY < rowTop + rowHeight * 0.5f;
            int insert = relative + (firstHalf ? 0 : 1);
            return Mathf.Clamp(insert, 0, visibleCount);
        }

        private static void DrawReorderInsertionLine(Rect listRect, float rowHeight, int visibleCount, int insertSlot, float rowOffsetY)
        {
            int clampedInsert = Mathf.Clamp(insertSlot, 0, Mathf.Max(0, visibleCount));
            float yBase = listRect.yMin + rowOffsetY;
            float y = clampedInsert <= 0
                ? yBase
                : (clampedInsert >= visibleCount ? yBase + visibleCount * rowHeight : yBase + clampedInsert * rowHeight);
            y = Mathf.Clamp(y, listRect.yMin, listRect.yMax);

            var lineColor = new Color(NMPStyles.AccentColor.r, NMPStyles.AccentColor.g, NMPStyles.AccentColor.b, 0.95f);
            EditorGUI.DrawRect(new Rect(listRect.x + 2f, y - 1f, Mathf.Max(0f, listRect.width - 4f), 2f), lineColor);
        }

        private static bool MoveFavoriteItem(int sourceIndex, int targetPage, int targetSlot)
        {
            if (sourceIndex < 0 || sourceIndex >= Favorites.Count)
                return false;
            if (sourceIndex >= FavoritePages.Count)
                return false;

            var item = Favorites[sourceIndex];
            int sourcePage = Mathf.Max(0, FavoritePages[sourceIndex]);
            int sourceSlot = GetSlotInPage(sourceIndex, sourcePage);
            string key = BuildKey(item.Guid, item.Path, item.GlobalId);
            if (string.IsNullOrEmpty(key))
                return false;

            int clampedTargetPage = Mathf.Max(0, targetPage);
            int normalizedTargetSlot = Mathf.Max(0, targetSlot);
            if (sourcePage == clampedTargetPage && normalizedTargetSlot > sourceSlot)
                normalizedTargetSlot--;

            Favorites.RemoveAt(sourceIndex);
            FavoritePages.RemoveAt(sourceIndex);

            if (PageContainsKey(clampedTargetPage, key, ignoreIndex: -1))
            {
                int restoreIndex = Mathf.Clamp(sourceIndex, 0, Favorites.Count);
                Favorites.Insert(restoreIndex, item);
                FavoritePages.Insert(restoreIndex, sourcePage);
                return false;
            }

            int insertIndex = GetGlobalInsertIndexForPageSlot(clampedTargetPage, normalizedTargetSlot);
            int targetIndex = Mathf.Clamp(insertIndex, 0, Favorites.Count);
            Favorites.Insert(targetIndex, item);
            FavoritePages.Insert(targetIndex, clampedTargetPage);

            if (sourcePage == clampedTargetPage && targetIndex == sourceIndex)
                return false;

            return true;
        }

        private static int GetConfiguredMinPageCount()
        {
            int minPages = Mathf.Max(1, _manualMinPageCount);
            for (int i = 0; i < FavoritePages.Count; i++)
                minPages = Mathf.Max(minPages, Mathf.Max(0, FavoritePages[i]) + 1);

            foreach (var kv in PageNames)
            {
                if (string.IsNullOrWhiteSpace(kv.Value))
                    continue;
                minPages = Mathf.Max(minPages, kv.Key + 1);
            }
            return minPages;
        }

        private static bool CompactPageLayout()
        {
            EnsurePageListIntegrity();

            int count = Mathf.Min(Favorites.Count, FavoritePages.Count);
            if (count <= 0)
            {
                bool changedEmpty = _manualMinPageCount != 1 ||
                                    _page != 0 ||
                                    PageNames.Count > 0 ||
                                    PageScrollStarts.Count > 0 ||
                                    PageScrollVisualStarts.Count > 0 ||
                                    PageScrollVelocities.Count > 0;
                _manualMinPageCount = 1;
                _page = 0;
                if (PageNames.Count > 0)
                    PageNames.Clear();
                if (PageScrollStarts.Count > 0)
                    PageScrollStarts.Clear();
                if (PageScrollVisualStarts.Count > 0)
                    PageScrollVisualStarts.Clear();
                if (PageScrollVelocities.Count > 0)
                    PageScrollVelocities.Clear();
                _lastScrollAnimationTime = -1d;
                return changedEmpty;
            }

            var usedPages = new List<int>();
            var usedSet = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                int page = Mathf.Max(0, FavoritePages[i]);
                if (usedSet.Add(page))
                    usedPages.Add(page);
            }
            usedPages.Sort();

            var remap = new Dictionary<int, int>(usedPages.Count);
            for (int i = 0; i < usedPages.Count; i++)
                remap[usedPages[i]] = i;

            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                int oldPage = Mathf.Max(0, FavoritePages[i]);
                int newPage = remap[oldPage];
                if (newPage != oldPage)
                {
                    FavoritePages[i] = newPage;
                    changed = true;
                }
            }

            if (PageNames.Count > 0)
            {
                var remappedNames = new Dictionary<int, string>();
                foreach (var kv in PageNames)
                {
                    if (!remap.TryGetValue(Mathf.Max(0, kv.Key), out int newIndex))
                        continue;
                    if (string.IsNullOrWhiteSpace(kv.Value))
                        continue;
                    remappedNames[newIndex] = kv.Value;
                }

                bool namesChanged = remappedNames.Count != PageNames.Count;
                if (!namesChanged)
                {
                    foreach (var kv in remappedNames)
                    {
                        if (!PageNames.TryGetValue(kv.Key, out var oldName) ||
                            !string.Equals(oldName, kv.Value, StringComparison.Ordinal))
                        {
                            namesChanged = true;
                            break;
                        }
                    }
                }

                if (namesChanged)
                {
                    PageNames.Clear();
                    foreach (var kv in remappedNames)
                        PageNames[kv.Key] = kv.Value;
                    changed = true;
                }
            }

            if (PageScrollStarts.Count > 0)
            {
                var remappedScroll = new Dictionary<int, float>();
                foreach (var kv in PageScrollStarts)
                {
                    if (!remap.TryGetValue(Mathf.Max(0, kv.Key), out int newIndex))
                        continue;

                    float value = Mathf.Max(0f, kv.Value);
                    if (value <= 0)
                        continue;

                    if (remappedScroll.TryGetValue(newIndex, out float existing))
                        remappedScroll[newIndex] = Mathf.Min(existing, value);
                    else
                        remappedScroll[newIndex] = value;
                }

                bool scrollChanged = remappedScroll.Count != PageScrollStarts.Count;
                if (!scrollChanged)
                {
                    foreach (var kv in remappedScroll)
                    {
                        if (!PageScrollStarts.TryGetValue(kv.Key, out float oldValue) || !Mathf.Approximately(oldValue, kv.Value))
                        {
                            scrollChanged = true;
                            break;
                        }
                    }
                }

                if (scrollChanged)
                {
                    PageScrollStarts.Clear();
                    foreach (var kv in remappedScroll)
                        PageScrollStarts[kv.Key] = kv.Value;
                    changed = true;
                }
            }

            if (PageScrollVisualStarts.Count > 0)
            {
                var remappedVisual = new Dictionary<int, float>();
                foreach (var kv in PageScrollVisualStarts)
                {
                    if (!remap.TryGetValue(Mathf.Max(0, kv.Key), out int newIndex))
                        continue;

                    float value = Mathf.Max(0f, kv.Value);
                    if (value <= 0f)
                        continue;

                    if (remappedVisual.TryGetValue(newIndex, out float existing))
                        remappedVisual[newIndex] = Mathf.Min(existing, value);
                    else
                        remappedVisual[newIndex] = value;
                }

                bool visualChanged = remappedVisual.Count != PageScrollVisualStarts.Count;
                if (!visualChanged)
                {
                    foreach (var kv in remappedVisual)
                    {
                        if (!PageScrollVisualStarts.TryGetValue(kv.Key, out float oldValue) || !Mathf.Approximately(oldValue, kv.Value))
                        {
                            visualChanged = true;
                            break;
                        }
                    }
                }

                if (visualChanged)
                {
                    PageScrollVisualStarts.Clear();
                    foreach (var kv in remappedVisual)
                        PageScrollVisualStarts[kv.Key] = kv.Value;
                    changed = true;
                }
            }

            if (PageScrollVelocities.Count > 0)
            {
                var remappedVelocity = new Dictionary<int, float>();
                foreach (var kv in PageScrollVelocities)
                {
                    if (!remap.TryGetValue(Mathf.Max(0, kv.Key), out int newIndex))
                        continue;

                    float value = kv.Value;
                    if (Mathf.Abs(value) <= ScrollMinVelocity)
                        continue;

                    if (remappedVelocity.TryGetValue(newIndex, out float existing))
                    {
                        remappedVelocity[newIndex] = Mathf.Abs(value) > Mathf.Abs(existing) ? value : existing;
                    }
                    else
                    {
                        remappedVelocity[newIndex] = value;
                    }
                }

                bool velocityChanged = remappedVelocity.Count != PageScrollVelocities.Count;
                if (!velocityChanged)
                {
                    foreach (var kv in remappedVelocity)
                    {
                        if (!PageScrollVelocities.TryGetValue(kv.Key, out float oldValue) || !Mathf.Approximately(oldValue, kv.Value))
                        {
                            velocityChanged = true;
                            break;
                        }
                    }
                }

                if (velocityChanged)
                {
                    PageScrollVelocities.Clear();
                    foreach (var kv in remappedVelocity)
                        PageScrollVelocities[kv.Key] = kv.Value;
                    changed = true;
                }
            }

            int desiredMinPages = Mathf.Max(1, usedPages.Count);
            if (_manualMinPageCount != desiredMinPages)
            {
                _manualMinPageCount = desiredMinPages;
                changed = true;
            }

            int clampedPage = Mathf.Clamp(_page, 0, desiredMinPages - 1);
            if (_page != clampedPage)
            {
                _page = clampedPage;
                changed = true;
            }

            return changed;
        }

        private static bool EnsureManualMinPageCount(int minCount)
        {
            int clamped = Mathf.Max(1, minCount);
            if (clamped <= _manualMinPageCount)
                return false;

            _manualMinPageCount = clamped;
            return true;
        }

        private static List<int> GetIndicesForPage(int pageIndex)
        {
            var result = new List<int>();
            int safePage = Mathf.Max(0, pageIndex);
            int count = Mathf.Min(Favorites.Count, FavoritePages.Count);
            for (int i = 0; i < count; i++)
            {
                if (Mathf.Max(0, FavoritePages[i]) == safePage)
                    result.Add(i);
            }
            return result;
        }

        private static int GetPageItemCount(int pageIndex)
        {
            int safePage = Mathf.Max(0, pageIndex);
            int count = 0;
            int total = Mathf.Min(Favorites.Count, FavoritePages.Count);
            for (int i = 0; i < total; i++)
            {
                if (Mathf.Max(0, FavoritePages[i]) == safePage)
                    count++;
            }
            return count;
        }

        private static float GetPageScrollStart(int pageIndex, int maxStart)
        {
            int safePage = Mathf.Max(0, pageIndex);
            float clampedMax = Mathf.Max(0, maxStart);
            float current = 0f;
            if (PageScrollStarts.TryGetValue(safePage, out var stored))
                current = Mathf.Clamp(stored, 0, clampedMax);

            if (current <= 0f)
            {
                if (PageScrollStarts.ContainsKey(safePage))
                    PageScrollStarts.Remove(safePage);
                return 0f;
            }

            PageScrollStarts[safePage] = current;
            return current;
        }

        private static void SetPageScrollStart(int pageIndex, float start)
        {
            int safePage = Mathf.Max(0, pageIndex);
            float value = Mathf.Max(0f, start);
            if (value <= 0f)
                PageScrollStarts.Remove(safePage);
            else
                PageScrollStarts[safePage] = value;
        }

        private static float GetPageScrollVisualStart(int pageIndex, float fallback, int maxStart)
        {
            int safePage = Mathf.Max(0, pageIndex);
            float clampedMax = Mathf.Max(0, maxStart);
            float current = fallback;
            if (PageScrollVisualStarts.TryGetValue(safePage, out var stored))
                current = stored;

            current = Mathf.Clamp(current, 0f, clampedMax);
            if (current <= 0f)
            {
                PageScrollVisualStarts.Remove(safePage);
                return 0f;
            }

            PageScrollVisualStarts[safePage] = current;
            return current;
        }

        private static void SetPageScrollVisualStart(int pageIndex, float start)
        {
            int safePage = Mathf.Max(0, pageIndex);
            float value = Mathf.Max(0f, start);
            if (value <= 0f)
                PageScrollVisualStarts.Remove(safePage);
            else
                PageScrollVisualStarts[safePage] = value;
        }

        private static float GetPageScrollVelocity(int pageIndex)
        {
            int safePage = Mathf.Max(0, pageIndex);
            if (PageScrollVelocities.TryGetValue(safePage, out var value))
                return value;
            return 0f;
        }

        private static void SetPageScrollVelocity(int pageIndex, float velocity)
        {
            int safePage = Mathf.Max(0, pageIndex);
            if (Mathf.Abs(velocity) <= ScrollMinVelocity)
                PageScrollVelocities.Remove(safePage);
            else
                PageScrollVelocities[safePage] = velocity;
        }

        private static float GetScrollDeltaTime()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_lastScrollAnimationTime < 0d)
            {
                _lastScrollAnimationTime = now;
                return 1f / 60f;
            }

            float dt = (float)(now - _lastScrollAnimationTime);
            _lastScrollAnimationTime = now;
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
                return 1f / 60f;
            return Mathf.Clamp(dt, 1f / 240f, 0.1f);
        }

        private static int GetSlotInPage(int itemIndex, int pageIndex)
        {
            int safePage = Mathf.Max(0, pageIndex);
            int total = Mathf.Min(Favorites.Count, FavoritePages.Count);
            int slot = 0;
            for (int i = 0; i < total; i++)
            {
                if (Mathf.Max(0, FavoritePages[i]) != safePage)
                    continue;
                if (i == itemIndex)
                    return slot;
                slot++;
            }
            return slot;
        }

        private static bool PageContainsKey(int pageIndex, string key, int ignoreIndex)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            int safePage = Mathf.Max(0, pageIndex);
            int total = Mathf.Min(Favorites.Count, FavoritePages.Count);
            for (int i = 0; i < total; i++)
            {
                if (i == ignoreIndex)
                    continue;
                if (Mathf.Max(0, FavoritePages[i]) != safePage)
                    continue;

                string existingKey = BuildKey(Favorites[i].Guid, Favorites[i].Path, Favorites[i].GlobalId);
                if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static int GetGlobalInsertIndexForPageSlot(int pageIndex, int slot)
        {
            int safePage = Mathf.Max(0, pageIndex);
            var pageIndices = GetIndicesForPage(safePage);
            int clampedSlot = Mathf.Clamp(slot, 0, pageIndices.Count);

            if (pageIndices.Count == 0)
            {
                int total = Mathf.Min(Favorites.Count, FavoritePages.Count);
                int lastBefore = -1;
                for (int i = 0; i < total; i++)
                {
                    if (Mathf.Max(0, FavoritePages[i]) <= safePage)
                        lastBefore = i;
                }
                return Mathf.Clamp(lastBefore + 1, 0, Favorites.Count);
            }

            if (clampedSlot <= 0)
                return Mathf.Clamp(pageIndices[0], 0, Favorites.Count);
            if (clampedSlot >= pageIndices.Count)
                return Mathf.Clamp(pageIndices[pageIndices.Count - 1] + 1, 0, Favorites.Count);
            return Mathf.Clamp(pageIndices[clampedSlot], 0, Favorites.Count);
        }

        private static string GetPageTitle(int pageIndex)
        {
            if (PageNames.TryGetValue(pageIndex, out var custom) && !string.IsNullOrWhiteSpace(custom))
                return custom.Trim();
            return $"Page {pageIndex + 1}";
        }

        private static void BeginPageNameEdit(int pageIndex)
        {
            _isEditingPageName = true;
            _editingPageIndex = Mathf.Max(0, pageIndex);
            _editingPageName = GetPageTitle(_editingPageIndex);
            _focusPageNameFieldNextRepaint = true;
            EditorApplication.RepaintProjectWindow();
        }

        private static void ShowPageContextMenu(int pageIndex)
        {
            int safeIndex = Mathf.Max(0, pageIndex);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"), false, () => BeginPageNameEdit(safeIndex));
            menu.AddSeparator(string.Empty);

            bool hasCustomName = PageNames.TryGetValue(safeIndex, out var customName) && !string.IsNullOrWhiteSpace(customName);
            if (hasCustomName)
                menu.AddItem(new GUIContent("Reset Name"), false, () => ResetPageName(safeIndex));
            else
                menu.AddDisabledItem(new GUIContent("Reset Name"));

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete Page"), false, () => DeletePage(safeIndex));

            menu.ShowAsContext();
        }

        private static void DeletePage(int pageIndex)
        {
            int safeIndex = Mathf.Max(0, pageIndex);
            bool changed = false;

            if (_isEditingPageName && _editingPageIndex == safeIndex)
                EndPageNameEdit();

            for (int i = Favorites.Count - 1; i >= 0; i--)
            {
                if (i < 0 || i >= FavoritePages.Count)
                    continue;

                int page = Mathf.Max(0, FavoritePages[i]);
                if (page == safeIndex)
                {
                    Favorites.RemoveAt(i);
                    FavoritePages.RemoveAt(i);
                    changed = true;
                }
                else if (page > safeIndex)
                {
                    FavoritePages[i] = page - 1;
                    changed = true;
                }
            }

            changed |= ShiftPageNameMapAfterDelete(safeIndex);
            changed |= ShiftPageFloatMapAfterDelete(PageScrollStarts, safeIndex, dropZeroValues: true);
            changed |= ShiftPageFloatMapAfterDelete(PageScrollVisualStarts, safeIndex, dropZeroValues: true);
            changed |= ShiftPageFloatMapAfterDelete(PageScrollVelocities, safeIndex, dropZeroValues: false);

            int oldPage = _page;
            if (_page > safeIndex)
                _page--;
            else if (_page == safeIndex)
                _page = Mathf.Max(0, _page - 1);
            if (_page != oldPage)
                changed = true;

            changed |= CompactPageLayout();

            if (changed)
                SaveData();

            EditorApplication.RepaintProjectWindow();
        }

        private static bool ShiftPageNameMapAfterDelete(int deletedPageIndex)
        {
            if (PageNames.Count == 0)
                return false;

            var remapped = new Dictionary<int, string>();
            foreach (var kv in PageNames)
            {
                int index = Mathf.Max(0, kv.Key);
                if (index == deletedPageIndex)
                    continue;

                int newIndex = index > deletedPageIndex ? index - 1 : index;
                if (string.IsNullOrWhiteSpace(kv.Value))
                    continue;
                remapped[newIndex] = kv.Value;
            }

            bool changed = remapped.Count != PageNames.Count;
            if (!changed)
            {
                foreach (var kv in remapped)
                {
                    if (!PageNames.TryGetValue(kv.Key, out var oldValue) ||
                        !string.Equals(oldValue, kv.Value, StringComparison.Ordinal))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
                return false;

            PageNames.Clear();
            foreach (var kv in remapped)
                PageNames[kv.Key] = kv.Value;
            return true;
        }

        private static bool ShiftPageFloatMapAfterDelete(Dictionary<int, float> map, int deletedPageIndex, bool dropZeroValues)
        {
            if (map == null || map.Count == 0)
                return false;

            var remapped = new Dictionary<int, float>();
            foreach (var kv in map)
            {
                int index = Mathf.Max(0, kv.Key);
                if (index == deletedPageIndex)
                    continue;

                int newIndex = index > deletedPageIndex ? index - 1 : index;
                float value = kv.Value;
                if (dropZeroValues && value <= 0f)
                    continue;
                if (!dropZeroValues && Mathf.Abs(value) <= 0f)
                    continue;

                remapped[newIndex] = value;
            }

            bool changed = remapped.Count != map.Count;
            if (!changed)
            {
                foreach (var kv in remapped)
                {
                    if (!map.TryGetValue(kv.Key, out var oldValue) || !Mathf.Approximately(oldValue, kv.Value))
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
                return false;

            map.Clear();
            foreach (var kv in remapped)
                map[kv.Key] = kv.Value;
            return true;
        }

        private static void ResetPageName(int pageIndex)
        {
            int safeIndex = Mathf.Max(0, pageIndex);
            bool removed = PageNames.Remove(safeIndex);

            if (_isEditingPageName && _editingPageIndex == safeIndex)
                EndPageNameEdit();

            if (removed)
                SaveData();

            EditorApplication.RepaintProjectWindow();
        }

        private static void CommitPageNameEdit()
        {
            if (!_isEditingPageName)
                return;

            int index = Mathf.Max(0, _editingPageIndex);
            string value = (_editingPageName ?? string.Empty).Trim();
            string defaultTitle = $"Page {index + 1}";
            bool hadValue = PageNames.TryGetValue(index, out var existing);
            string existingValue = hadValue && existing != null ? existing.Trim() : string.Empty;
            bool changed;

            if (string.IsNullOrEmpty(value) || string.Equals(value, defaultTitle, StringComparison.Ordinal))
            {
                changed = hadValue;
                PageNames.Remove(index);
            }
            else
            {
                changed = !hadValue || !string.Equals(existingValue, value, StringComparison.Ordinal);
                PageNames[index] = value;
            }

            EndPageNameEdit();
            if (changed)
                SaveData();
            EditorApplication.RepaintProjectWindow();
        }

        private static void EndPageNameEdit()
        {
            _isEditingPageName = false;
            _editingPageIndex = -1;
            _editingPageName = string.Empty;
            _focusPageNameFieldNextRepaint = false;
        }

        private static void DrawDraggedRowPreview(Rect listRect, float rowHeight)
        {
            if (_dragSourceIndex < 0 || _dragSourceIndex >= Favorites.Count)
                return;

            var item = Favorites[_dragSourceIndex];
            float y = Mathf.Clamp(_dragMousePos.y - rowHeight * 0.5f, listRect.yMin, listRect.yMax - rowHeight);
            var rowRect = new Rect(listRect.x, y, listRect.width, Mathf.Max(1f, rowHeight - 1f));

            var previewBg = EditorGUIUtility.isProSkin
                ? new Color(0.28f, 0.33f, 0.42f, 0.78f)
                : new Color(0.70f, 0.78f, 0.94f, 0.85f);
            EditorGUI.DrawRect(rowRect, previewBg);
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, 2f, rowRect.height), new Color(NMPStyles.AccentColor.r, NMPStyles.AccentColor.g, NMPStyles.AccentColor.b, 1f));

            string itemPath = TryResolveAssetPath(item);
            bool isFolder = !string.IsNullOrEmpty(itemPath) && AssetDatabase.IsValidFolder(itemPath);
            Color folderColor = default;
            bool hasFolderColor = isFolder && ProjectFolderStyleManager.TryGetColorForFolderPath(itemPath, out folderColor);
            bool drawIconTint = hasFolderColor && NMPSettings.ProjectFolderColors;

            float rowScale = Mathf.InverseLerp(BaseRowHeight - 1f, MaxRowHeight - 1f, rowRect.height);
            float iconSize = Mathf.Clamp(rowRect.height * 0.72f, 16f, 44f);
            float iconY = rowRect.y + Mathf.Floor((rowRect.height - iconSize) * 0.5f);
            var iconRect = new Rect(rowRect.x + 4f, iconY, iconSize, iconSize);
            float removeSize = Mathf.Clamp(rowRect.height * 0.45f, 14f, 30f);
            float removeY = rowRect.y + Mathf.Floor((rowRect.height - removeSize) * 0.5f);
            var removeRect = new Rect(rowRect.xMax - removeSize - 4f, removeY, removeSize, removeSize);

            float textX = iconRect.xMax + 5f;
            var textRect = new Rect(textX, rowRect.y, Mathf.Max(0f, removeRect.xMin - textX - 4f), rowRect.height);
            int textFontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(11f, 16f, rowScale)), 11, 16);
            GUIStyle rowTextStyle = GetScaledRowStyle(textFontSize);

            DrawFavoriteIcon(item, iconRect, itemPath, isFolder, drawIconTint, folderColor);
            if (isFolder && NMPSettings.ProjectBadgeIcons)
                DrawFolderBadge(itemPath, iconRect);

            var prevColor = GUI.color;
            GUI.color = Color.white;
            GUI.Label(textRect, GetDisplayName(item), rowTextStyle);
            GUI.color = prevColor;
        }

        private static void ClearReorderState()
        {
            _pressedRowIndex = -1;
            _isRowReorderDrag = false;
            _dragSourceIndex = -1;
            _dragSourcePage = -1;
            _dragInsertIndex = -1;
            _dragMousePos = default;
            _dragArrowDirection = 0;
            _wasOutsideFavoritesDuringCurrentDrag = false;
            _externalDragStartedInCurrentGesture = false;
            ClearScrollbarDrag();
        }

        private static void ClearScrollbarDrag()
        {
            _isScrollbarDrag = false;
            _scrollbarDragPage = -1;
            _scrollbarDragGrabOffsetY = 0f;
        }

        private static void UpdatePagerRects(Rect panelRect)
        {
            var pagerRect = new Rect(panelRect.center.x - 58f, panelRect.yMax - FooterHeight + 3f, 116f, 26f);
            _pagerPrevRect = new Rect(pagerRect.x + 6f, pagerRect.y + 3f, 20f, 20f);
            _pagerNextRect = new Rect(pagerRect.xMax - 26f, pagerRect.y + 3f, 20f, 20f);
        }

        private static void DrawPager(Rect panelRect, int pageCount, Event evt)
        {
            var pagerRect = new Rect(panelRect.center.x - 58f, panelRect.yMax - FooterHeight + 3f, 116f, 26f);
            var prevRect = _pagerPrevRect;
            var nextRect = _pagerNextRect;
            var labelRect = new Rect(prevRect.xMax + 4f, pagerRect.y, pagerRect.width - 60f, pagerRect.height);

            bool canPrev = _page > 0;
            bool canNext = _page < pageCount - 1;
            bool isEditingThisPage = _isEditingPageName && _editingPageIndex == _page;
            bool prevInteractive = isEditingThisPage || canPrev;
            bool nextInteractive = isEditingThisPage || canNext;
            bool prevHover = prevInteractive && prevRect.Contains(evt.mousePosition);
            bool nextHover = nextInteractive && nextRect.Contains(evt.mousePosition);

            if (_isEditingPageName && _editingPageIndex >= pageCount)
                EndPageNameEdit();

            if (evt.type == EventType.Repaint)
            {
                EnsurePagerPillTexture();
                if (_pagerPillTexture != null)
                    GUI.DrawTexture(pagerRect, _pagerPillTexture, ScaleMode.StretchToFill, true);
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount >= 2 && labelRect.Contains(evt.mousePosition))
            {
                BeginPageNameEdit(_page);
                evt.Use();
            }

            if (evt.type == EventType.MouseDown && evt.button == 1 && labelRect.Contains(evt.mousePosition))
            {
                if (_isEditingPageName)
                    CommitPageNameEdit();
                ShowPageContextMenu(_page);
                evt.Use();
            }

            if (_isEditingPageName &&
                evt.type == EventType.MouseDown &&
                evt.button == 0 &&
                !labelRect.Contains(evt.mousePosition) &&
                !prevRect.Contains(evt.mousePosition) &&
                !nextRect.Contains(evt.mousePosition))
            {
                CommitPageNameEdit();
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && prevRect.Contains(evt.mousePosition))
            {
                if (isEditingThisPage)
                {
                    EndPageNameEdit();
                }
                else if (canPrev)
                {
                    _page = Mathf.Max(0, _page - 1);
                    if (_isEditingPageName && _editingPageIndex != _page)
                        EndPageNameEdit();
                }
                EditorApplication.RepaintProjectWindow();
                evt.Use();
            }

            if (evt.type == EventType.MouseDown && evt.button == 0 && nextRect.Contains(evt.mousePosition))
            {
                if (isEditingThisPage)
                {
                    CommitPageNameEdit();
                }
                else if (canNext)
                {
                    _page = Mathf.Min(pageCount - 1, _page + 1);
                    if (_isEditingPageName && _editingPageIndex != _page)
                        EndPageNameEdit();
                }
                EditorApplication.RepaintProjectWindow();
                evt.Use();
            }

            if (isEditingThisPage)
            {
                GUI.SetNextControlName(PageNameFieldControlName);
                _editingPageName = EditorGUI.TextField(labelRect, _editingPageName ?? string.Empty, _pageEditStyle);

                if (_focusPageNameFieldNextRepaint)
                {
                    EditorGUI.FocusTextInControl(PageNameFieldControlName);
                    _focusPageNameFieldNextRepaint = false;
                }

                if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                {
                    CommitPageNameEdit();
                    evt.Use();
                }
                else if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
                {
                    EndPageNameEdit();
                    evt.Use();
                }
            }
            else
            {
                bool labelHover = labelRect.Contains(evt.mousePosition);
                GUI.Label(labelRect, GetPageTitle(_page), labelHover ? _pageHoverStyle : _pageStyle);
            }

            string prevGlyph = isEditingThisPage ? "✕" : "<";
            string nextGlyph = isEditingThisPage ? "✓" : ">";
            GUI.Label(prevRect, prevGlyph, prevInteractive ? (prevHover ? _pageArrowHoverStyle : _pageArrowStyle) : _pageArrowDisabledStyle);
            GUI.Label(nextRect, nextGlyph, nextInteractive ? (nextHover ? _pageArrowHoverStyle : _pageArrowStyle) : _pageArrowDisabledStyle);

        }

        private static void DrawRowHeightSlider(Rect sliderRect)
        {
            if (sliderRect.width <= 0f || sliderRect.height <= 0f)
                return;

            float previous = _rowHeightSlider;
            _rowHeightSlider = GUI.HorizontalSlider(sliderRect, _rowHeightSlider, 0f, 1f);
            _rowHeightSlider = Mathf.Clamp01(_rowHeightSlider);

            if (!Mathf.Approximately(previous, _rowHeightSlider))
            {
                EditorPrefs.SetFloat(RowHeightSliderPrefKey, _rowHeightSlider);
                EditorApplication.RepaintProjectWindow();
            }
        }

        private static void HandleDragAndDrop(Rect panelRect, Event evt)
        {
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;
            if (_pressedRowIndex >= 0 || _isRowReorderDrag || _externalDragStartedInCurrentGesture)
                return;
            if (!panelRect.Contains(evt.mousePosition))
                return;

            bool hasValid = false;
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (TryBuildFavoriteItem(obj, out _))
                {
                    hasValid = true;
                    break;
                }
            }

            if (!hasValid) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddFavorites(DragAndDrop.objectReferences);
            }

            evt.Use();
        }

        private static void ShowRowContextMenu(FavoriteItem item, int itemIndex)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Ping"), false, () => FocusItem(item));
            menu.AddItem(new GUIContent("Remove from Favorites"), false, () =>
            {
                RemoveItemAtIndex(itemIndex);
                SaveData();
                EditorApplication.RepaintProjectWindow();
            });
            menu.ShowAsContext();
        }

        private static bool IsItemSelected(FavoriteItem item)
        {
            var active = Selection.activeObject;
            if (active == null) return false;

            if (TryResolveObject(item, out var obj) && obj == active)
                return true;

            string path = TryResolveAssetPath(item);
            if (!string.IsNullOrEmpty(path))
            {
                string activePath = AssetDatabase.GetAssetPath(active);
                if (!string.IsNullOrEmpty(activePath) && string.Equals(activePath, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void DrawFavoriteIcon(
            FavoriteItem item,
            Rect iconRect,
            string itemPath,
            bool isFolder,
            bool drawIconTint,
            Color folderColor)
        {
            if (drawIconTint && isFolder && !string.IsNullOrEmpty(itemPath))
            {
                var tinted = ProjectFolderStyleManager.GetTintedFolderIconForPath(itemPath, folderColor, useHardAlphaMask: true);
                if (tinted != null)
                {
                    // Slightly inflate to fully cover default Unity folder border.
                    float pad = Mathf.Clamp(iconRect.width * 0.08f, 1.2f, 3.2f);
                    float bottomExtra = Mathf.Clamp(iconRect.height * 0.05f, 1f, 2.2f);
                    var drawRect = new Rect(
                        iconRect.x - pad,
                        iconRect.y - pad,
                        iconRect.width + pad * 2f + 0.5f,
                        iconRect.height + pad * 2f + bottomExtra);
                    var prevColor = GUI.color;
                    GUI.color = Color.white;
                    GUI.DrawTexture(drawRect, tinted, ScaleMode.ScaleToFit, true);
                    GUI.color = prevColor;
                    return;
                }
            }

            string key = BuildKey(item.Guid, item.Path, item.GlobalId);
            Texture2D icon = null;

            if (!string.IsNullOrEmpty(key) && IconCache.TryGetValue(key, out var cached))
            {
                icon = cached;
            }
            else
            {
                if (TryResolveObject(item, out var obj) && obj != null)
                {
                    if (EditorUtility.IsPersistent(obj))
                    {
                        string path = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(path))
                            icon = AssetDatabase.GetCachedIcon(path) as Texture2D;
                    }

                    if (icon == null)
                        icon = EditorGUIUtility.ObjectContent(obj, obj.GetType()).image as Texture2D;
                }

                if (icon != null && !string.IsNullOrEmpty(key))
                    IconCache[key] = icon;
            }

            if (icon == null)
                icon = EditorGUIUtility.IconContent("DefaultAsset Icon").image as Texture2D;

            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
        }

        private static void DrawFolderBadge(string folderPath, Rect folderIconRect)
        {
            if (string.IsNullOrEmpty(folderPath))
                return;

            Texture2D badge = ProjectFolderStyleManager.GetBadgeIconForFolderPath(folderPath);
            if (badge == null)
                return;

            float badgeSize = Mathf.Max(8f, folderIconRect.width * 0.5f);
            var badgeRect = new Rect(
                folderIconRect.xMax - badgeSize + 0.5f,
                folderIconRect.yMax - badgeSize + 0.5f,
                badgeSize,
                badgeSize);

            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            float outline = 1.75f;
            GUI.DrawTexture(new Rect(badgeRect.x - outline, badgeRect.y,           badgeRect.width, badgeRect.height), badge, ScaleMode.ScaleToFit, true);
            GUI.DrawTexture(new Rect(badgeRect.x + outline, badgeRect.y,           badgeRect.width, badgeRect.height), badge, ScaleMode.ScaleToFit, true);
            GUI.DrawTexture(new Rect(badgeRect.x,           badgeRect.y - outline, badgeRect.width, badgeRect.height), badge, ScaleMode.ScaleToFit, true);
            GUI.DrawTexture(new Rect(badgeRect.x,           badgeRect.y + outline, badgeRect.width, badgeRect.height), badge, ScaleMode.ScaleToFit, true);

            GUI.color = Color.white;
            GUI.DrawTexture(badgeRect, badge, ScaleMode.ScaleToFit, true);
            GUI.color = prevColor;
        }

        private static Rect GetOverlayRect()
        {
            if (TryGetNativeTreeViewRect(out var nativeTreeRect))
            {
                CacheLeftTreeFrameRect(nativeTreeRect);
                return nativeTreeRect;
            }

            if (TryGetLeftTreeFrameRect(out var leftTreeRect))
            {
                CacheLeftTreeFrameRect(leftTreeRect);
                return BuildOverlayRectFromTreeRect(leftTreeRect);
            }

            if (_hasLeftTreeFrameRectEstimate)
            {
                return BuildOverlayRectFromTreeRect(_leftTreeFrameRectEstimate);
            }

            float windowW = Mathf.Max(PanelMinWidth, _projectWindow != null ? _projectWindow.position.width : 600f);
            float overlayContentW = _overlay != null && _overlay.contentRect.width > 0.1f
                ? _overlay.contentRect.width
                : windowW;
            float rightX = _hasLeftTreeRightXEstimate
                ? _leftTreeRightXEstimate + 1f
                : Mathf.Max(220f, windowW * 0.36f);
            rightX = Mathf.Clamp(rightX, PanelMinWidth, overlayContentW);

            // Never cover Unity's Project toolbar row with '+' and search controls.
            float top = Mathf.Clamp(Mathf.Max(18f, EditorGUIUtility.singleLineHeight + 4f), 0f, 140f);
            float windowHeight = GetProjectWindowHeight();
            float panelH = Mathf.Max(100f, windowHeight - top);

            // Keep Favorites strictly inside Unity's native black frame of the left pane.
            const float frameInsetLeft = 0f;
            const float frameInsetRight = 1f;
            const float frameInsetTop = 0f;
            const float frameInsetBottom = 0f;
            float x = frameInsetLeft;
            float y = top + frameInsetTop;
            float w = Mathf.Max(40f, rightX - frameInsetLeft - frameInsetRight);
            float h = Mathf.Max(40f, panelH - frameInsetTop - frameInsetBottom);

            return new Rect(x, y, w, h);
        }

        private static bool TryGetNativeTreeViewRect(out Rect rect)
        {
            rect = default;
            if (_projectWindow == null || _projectWindow.Equals(null))
                return false;

            var type = _projectWindow.GetType();
            if (type == null || type.Name != "ProjectBrowser")
                return false;

            if (_projectBrowserTreeViewRectField == null || _projectBrowserTreeViewRectField.DeclaringType != type)
                _projectBrowserTreeViewRectField = type.GetField("m_TreeViewRect", ProjectBrowserBindingFlags);

            if (_projectBrowserTreeViewRectField == null || _projectBrowserTreeViewRectField.FieldType != typeof(Rect))
                return false;

            try
            {
                rect = (Rect)_projectBrowserTreeViewRectField.GetValue(_projectWindow);
                return rect.width > 40f && rect.height > 40f;
            }
            catch
            {
                rect = default;
                return false;
            }
        }

        private static void CacheLeftTreeFrameRect(Rect rect)
        {
            if (rect.width <= 40f || rect.height <= 40f)
                return;

            _leftTreeFrameRectEstimate = rect;
            _hasLeftTreeFrameRectEstimate = true;
        }

        private static Rect BuildOverlayRectFromTreeRect(Rect treeRect)
        {
            const float leftTreeInsetLeft = 0f;
            const float leftTreeInsetRight = 0f;
            const float leftTreeInsetTop = 0f;
            const float leftTreeInsetBottom = 0f;

            float leftTreeX = Mathf.Max(0f, treeRect.xMin + leftTreeInsetLeft);
            float leftTreeY = Mathf.Max(0f, treeRect.yMin + leftTreeInsetTop);
            float leftTreeWidth = Mathf.Max(40f, treeRect.width - leftTreeInsetLeft - leftTreeInsetRight);
            float leftTreeHeight = Mathf.Max(40f, treeRect.height - leftTreeInsetTop - leftTreeInsetBottom);
            return new Rect(leftTreeX, leftTreeY, leftTreeWidth, leftTreeHeight);
        }

        private static bool TryGetLeftTreeFrameRect(out Rect rect)
        {
            rect = default;
            if (_projectWindow == null) return false;

            var root = _projectWindow.rootVisualElement;
            if (root == null) return false;

            if (TryGetLeftImGuiHostRect(root, out rect))
                return true;

            // Primary source: left pane in the Project split-view (not affected by tree scroll position).
            if (TryGetLeftPaneFromSplitViewRect(root, out rect))
                return true;

            // Primary source: scroll viewport rect is stable while content is scrolled.
            if (TryGetLeftScrollViewportRect(root, out rect))
                return true;

            var treeViews = new List<VisualElement>();
            root.Query<VisualElement>(className: "unity-tree-view").ToList(treeViews);
            if (treeViews.Count == 0) return false;

            VisualElement leftMost = null;
            float bestLocalX = float.MaxValue;
            float bestArea = -1f;
            float rootH = Mathf.Max(120f, root.contentRect.height);

            foreach (var tree in treeViews)
            {
                if (tree == null) continue;
                if (tree.resolvedStyle.display == DisplayStyle.None) continue;

                Rect wb = tree.worldBound;
                if (wb.width < 40f || wb.height < 40f) continue;

                Rect local = WorldToLocalRect(root, wb);

                // Ignore moving content rects (they shift with scroll and can exceed viewport bounds).
                if (local.yMin < 0f) continue;
                if (local.height > rootH + 2f) continue;

                float localX = local.xMin;
                float area = local.width * local.height;
                if (localX < bestLocalX - 0.5f || (Mathf.Abs(localX - bestLocalX) <= 0.5f && area > bestArea))
                {
                    bestLocalX = localX;
                    bestArea = area;
                    leftMost = tree;
                }
            }

            if (leftMost == null) return false;

            rect = WorldToLocalRect(root, leftMost.worldBound);
            return rect.width > 40f && rect.height > 40f;
        }

        private static bool TryGetLeftPaneFromSplitViewRect(VisualElement root, out Rect rect)
        {
            rect = default;
            if (!TryGetLeftPaneFromSplitView(root, out var leftPane) || leftPane == null)
                return false;

            rect = WorldToLocalRect(root, leftPane.worldBound);
            return rect.width > 40f && rect.height > 40f;
        }

        private static bool TryGetLeftImGuiHostRect(VisualElement root, out Rect rect)
        {
            rect = default;
            if (!TryGetLeftImGuiHost(root, out var host) || host == null)
                return false;

            rect = WorldToLocalRect(root, host.worldBound);
            rect = ShrinkRect(rect, 1f, 1f, 1f, 1f);
            return rect.width > 40f && rect.height > 40f;
        }

        private static bool TryGetLeftImGuiHost(VisualElement root, out VisualElement host)
        {
            host = null;
            if (root == null)
                return false;

            var imguiContainers = new List<IMGUIContainer>();
            root.Query<IMGUIContainer>().ToList(imguiContainers);
            if (imguiContainers.Count == 0)
                return false;

            float rootW = Mathf.Max(200f, root.worldBound.width);
            float rootH = Mathf.Max(120f, root.worldBound.height);
            float bestX = float.MaxValue;
            float bestArea = -1f;

            foreach (var candidate in imguiContainers)
            {
                if (candidate == null) continue;
                if (ReferenceEquals(candidate, _overlay)) continue;
                if (candidate.resolvedStyle.display == DisplayStyle.None) continue;

                Rect local = WorldToLocalRect(root, candidate.worldBound);
                if (local.width < 80f || local.height < 80f) continue;
                if (local.xMin < -1f || local.yMin < -1f) continue;
                if (local.xMax > rootW + 1f || local.yMax > rootH + 1f) continue;
                if (local.width > rootW * 0.75f) continue;

                float area = local.width * local.height;
                if (local.xMin < bestX - 0.5f || (Mathf.Abs(local.xMin - bestX) <= 0.5f && area > bestArea))
                {
                    bestX = local.xMin;
                    bestArea = area;
                    host = candidate;
                }
            }

            return host != null;
        }

        private static bool TryGetLeftPaneFromSplitView(VisualElement root, out VisualElement leftPane)
        {
            leftPane = null;

            var splitContentContainers = new List<VisualElement>();
            root.Query<VisualElement>(className: "unity-two-pane-split-view__content-container")
                .ToList(splitContentContainers);
            if (splitContentContainers.Count == 0)
                return false;

            float rootW = Mathf.Max(200f, root.contentRect.width);
            float rootH = Mathf.Max(120f, root.contentRect.height);
            float bestX = float.MaxValue;
            float bestArea = -1f;

            foreach (var container in splitContentContainers)
            {
                if (container == null) continue;
                if (container.resolvedStyle.display == DisplayStyle.None) continue;
                if (container.childCount < 2) continue;

                var candidateLeftPane = container[0];
                if (candidateLeftPane == null) continue;
                if (candidateLeftPane.resolvedStyle.display == DisplayStyle.None) continue;

                Rect local = WorldToLocalRect(root, candidateLeftPane.worldBound);
                if (local.width < 80f || local.height < 80f) continue;
                if (local.xMin < -1f || local.yMin < -1f) continue;
                if (local.xMax > rootW + 1f || local.yMax > rootH + 1f) continue;
                if (local.width > rootW * 0.75f) continue;

                float area = local.width * local.height;
                if (local.xMin < bestX - 0.5f || (Mathf.Abs(local.xMin - bestX) <= 0.5f && area > bestArea))
                {
                    bestX = local.xMin;
                    bestArea = area;
                    leftPane = candidateLeftPane;
                }
            }

            return leftPane != null;
        }

        private static bool IsValidLeftPaneHost(VisualElement root, VisualElement candidate)
        {
            if (!IsElementOnSamePanel(root, candidate))
                return false;
            if (ReferenceEquals(candidate, _overlay))
                return false;
            if (candidate.resolvedStyle.display == DisplayStyle.None)
                return false;

            Rect local = WorldToLocalRect(root, candidate.worldBound);
            return local.width > 40f && local.height > 40f;
        }

        private static bool IsElementOnSamePanel(VisualElement root, VisualElement candidate)
        {
            if (root == null || candidate == null)
                return false;
            if (candidate.panel == null || root.panel == null)
                return false;
            return ReferenceEquals(candidate.panel, root.panel);
        }

        private static bool TryResolveLeftPaneHost(VisualElement root, out VisualElement leftPaneHost)
        {
            leftPaneHost = null;
            if (root == null)
                return false;

            if (IsValidLeftPaneHost(root, _cachedLeftPaneHost))
            {
                leftPaneHost = _cachedLeftPaneHost;
                return true;
            }

            if (TryGetLeftImGuiHost(root, out var imguiHost) && IsValidLeftPaneHost(root, imguiHost))
            {
                _cachedLeftPaneHost = imguiHost;
                leftPaneHost = imguiHost;
                return true;
            }

            if (TryGetLeftPaneFromSplitView(root, out var resolvedPane) && IsValidLeftPaneHost(root, resolvedPane))
            {
                _cachedLeftPaneHost = resolvedPane;
                leftPaneHost = resolvedPane;
                return true;
            }

            if (_overlay != null && _overlay.parent != null && _overlay.parent != root &&
                IsValidLeftPaneHost(root, _overlay.parent))
            {
                _cachedLeftPaneHost = _overlay.parent;
                leftPaneHost = _overlay.parent;
                return true;
            }

            return false;
        }

        private static bool TryGetLeftScrollViewportRect(VisualElement root, out Rect rect)
        {
            rect = default;

            var viewports = new List<VisualElement>();
            root.Query<VisualElement>(className: "unity-scroll-view__content-viewport").ToList(viewports);
            if (viewports.Count == 0)
                return false;

            float rootW = Mathf.Max(200f, root.contentRect.width);
            float rootH = Mathf.Max(120f, root.contentRect.height);
            float bestX = float.MaxValue;
            float bestArea = -1f;
            bool found = false;
            Rect bestRect = default;

            foreach (var viewport in viewports)
            {
                if (viewport == null) continue;
                if (viewport.resolvedStyle.display == DisplayStyle.None) continue;

                Rect wb = viewport.worldBound;
                if (wb.width < 80f || wb.height < 80f) continue;

                Rect local = WorldToLocalRect(root, wb);
                if (local.xMin < -1f || local.yMin < -1f) continue;
                if (local.xMax > rootW + 1f || local.yMax > rootH + 1f) continue;

                float area = local.width * local.height;
                if (local.xMin < bestX - 0.5f || (Mathf.Abs(local.xMin - bestX) <= 0.5f && area > bestArea))
                {
                    bestX = local.xMin;
                    bestArea = area;
                    bestRect = local;
                    found = true;
                }
            }

            if (!found)
                return false;

            rect = bestRect;
            return true;
        }

        private static Rect WorldToLocalRect(VisualElement root, Rect worldRect)
        {
            Vector2 min = root.WorldToLocal(new Vector2(worldRect.xMin, worldRect.yMin));
            Vector2 max = root.WorldToLocal(new Vector2(worldRect.xMax, worldRect.yMax));
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect ShrinkRect(Rect rect, float left, float right, float top, float bottom)
        {
            if (rect.width <= 0f || rect.height <= 0f)
                return default;

            return new Rect(
                rect.x + left,
                rect.y + top,
                Mathf.Max(40f, rect.width - left - right),
                Mathf.Max(40f, rect.height - top - bottom));
        }

        private static void DrawRemoveGlyph(Rect rect, bool hovered)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            float size = Mathf.Min(rect.width, rect.height);
            float half = Mathf.Max(2.6f, size * 0.24f);
            var center = rect.center;

            var p1 = new Vector3(center.x - half, center.y - half, 0f);
            var p2 = new Vector3(center.x + half, center.y + half, 0f);
            var p3 = new Vector3(center.x - half, center.y + half, 0f);
            var p4 = new Vector3(center.x + half, center.y - half, 0f);

            Color previousColor = Handles.color;
            Handles.color = GetRemoveGlyphColor(hovered);
            float thickness = hovered ? 3.4f : 2.8f;
            Handles.DrawAAPolyLine(thickness, p1, p2);
            Handles.DrawAAPolyLine(thickness, p3, p4);
            Handles.color = previousColor;
        }

        private static GUIStyle GetScaledRowStyle(int fontSize)
        {
            if (_rowStyle == null)
                return EditorStyles.label;

            if (_rowScaledStyle == null)
                _rowScaledStyle = new GUIStyle(_rowStyle);

            if (_rowScaledFontSize != fontSize)
            {
                _rowScaledStyle.fontSize = fontSize;
                _rowScaledFontSize = fontSize;
            }

            _rowScaledStyle.normal.textColor = _rowStyle.normal.textColor;
            _rowScaledStyle.alignment = _rowStyle.alignment;
            _rowScaledStyle.clipping = _rowStyle.clipping;
            return _rowScaledStyle;
        }

        private static Color GetRemoveGlyphColor(bool hovered)
        {
            if (EditorGUIUtility.isProSkin)
                return hovered ? new Color(0.95f, 0.95f, 0.95f, 1f) : new Color(0.62f, 0.62f, 0.62f, 1f);

            return hovered ? new Color(0.18f, 0.18f, 0.18f, 1f) : new Color(0.42f, 0.42f, 0.42f, 1f);
        }

        private static float GetProjectWindowHeight()
        {
            if (_overlay != null)
            {
                float h = _overlay.contentRect.height;
                if (h > 0.1f) return h;
            }

            if (_projectWindow != null)
            {
                float contentH = _projectWindow.rootVisualElement.contentRect.height;
                if (contentH > 0.1f) return contentH;
            }

            if (_projectWindow != null)
                return Mathf.Max(120f, _projectWindow.position.height);
            return 600f;
        }

        private static string GetDisplayName(FavoriteItem item)
        {
            string path = TryResolveAssetPath(item);
            if (!string.IsNullOrEmpty(path))
            {
                string name = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(name)) return name;
                return path;
            }

            if (TryResolveObject(item, out var obj) && obj != null)
                return obj.name;

            if (!string.IsNullOrEmpty(item.DisplayName))
                return item.DisplayName;

            return "Missing";
        }

        private static void FocusItem(FavoriteItem item)
        {
            if (!TryResolveObject(item, out var obj) || obj == null)
                return;

            bool isPersistent = EditorUtility.IsPersistent(obj);
            if (isPersistent)
                EditorUtility.FocusProjectWindow();

            Selection.activeObject = obj;

            bool favoritesOverlayActive = NMPSettings.ProjectFavoritesOverlay && (_wasAltPressed || _isEditingPageName);
            if (favoritesOverlayActive && isPersistent)
            {
                // Frame in Project window and draw our own right-pane-only ping highlight.
                if (TryFrameInProjectBrowser(obj))
                {
                    StartRightPanePing(obj);
                    _overlay?.MarkDirtyRepaint();
                    _projectWindow?.Repaint();
                    return;
                }

                // Fallback: focus/frame in project and still trigger right-only highlight.
                ProjectWindowUtil.ShowCreatedAsset(obj);
                StartRightPanePing(obj);
                _overlay?.MarkDirtyRepaint();
                _projectWindow?.Repaint();
                return;
            }

            EditorGUIUtility.PingObject(obj);
        }

        private static void OpenFavoriteItem(FavoriteItem item)
        {
            if (!TryResolveObject(item, out var obj) || obj == null)
                return;

            string path = TryResolveAssetPath(item);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
            {
                FocusItem(item);
                return;
            }

            FocusItem(item);
            if (!AssetDatabase.OpenAsset(obj))
                EditorGUIUtility.PingObject(obj);
        }

        private static bool TryFrameInProjectBrowser(UnityEngine.Object obj)
        {
            if (obj == null)
                return false;

            try
            {
                var projectBrowserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
                if (projectBrowserType == null)
                    return false;

                if (_projectBrowserFrameObjectMethod == null || _projectBrowserFrameObjectMethod.DeclaringType != projectBrowserType)
                {
                    _projectBrowserFrameObjectMethod =
                        projectBrowserType.GetMethod(
                            "FrameObjectInProjectWindow",
                            ProjectBrowserBindingFlags,
                            null,
                            new[] { typeof(int), typeof(bool) },
                            null)
                        ?? projectBrowserType.GetMethod(
                            "FrameObjectInProjectWindow",
                            ProjectBrowserBindingFlags,
                            null,
                            new[] { typeof(int) },
                            null);
                }

                if (_projectBrowserFrameObjectMethod == null)
                    return false;

                int instanceId = obj.GetInstanceID();
                var parameters = _projectBrowserFrameObjectMethod.GetParameters();
                if (parameters.Length == 2)
                    _projectBrowserFrameObjectMethod.Invoke(null, new object[] { instanceId, true });
                else
                    _projectBrowserFrameObjectMethod.Invoke(null, new object[] { instanceId });

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void StartRightPanePing(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                return;

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
                return;

            _rightPanePingGuid = guid;
            _rightPanePingUntil = EditorApplication.timeSinceStartup + 1.15d;
            EditorApplication.RepaintProjectWindow();
        }

        private static void DrawRightPanePingHighlight(string guid, Rect selectionRect, bool isLeftTreePaneRow, Event evt)
        {
            if (evt.type != EventType.Repaint)
                return;
            if (string.IsNullOrEmpty(_rightPanePingGuid) || string.IsNullOrEmpty(guid))
                return;
            if (EditorApplication.timeSinceStartup > _rightPanePingUntil)
            {
                _rightPanePingGuid = null;
                _rightPanePingUntil = 0d;
                return;
            }
            if (isLeftTreePaneRow)
                return;
            if (!string.Equals(_rightPanePingGuid, guid, StringComparison.OrdinalIgnoreCase))
                return;

            float t = (float)Mathf.Clamp01((float)((_rightPanePingUntil - EditorApplication.timeSinceStartup) / 1.15d));
            float pulse = 0.45f + 0.55f * Mathf.Sin((1f - t) * Mathf.PI * 3.2f);
            float alpha = Mathf.Lerp(0.15f, 0.45f, pulse);
            Color border = new Color(1f, 0.87f, 0.25f, alpha);
            DrawRoundedRectOutline(selectionRect, border, 3.5f, 4f);
            EditorApplication.RepaintProjectWindow();
        }

        private static void DrawRectOutline(Rect rect, Color color, float thickness)
        {
            if (rect.width <= 0f || rect.height <= 0f || thickness <= 0f)
                return;

            float t = Mathf.Min(thickness, Mathf.Min(rect.width, rect.height) * 0.5f);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, t), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - t, rect.width, t), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin + t, t, Mathf.Max(0f, rect.height - 2f * t)), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - t, rect.yMin + t, t, Mathf.Max(0f, rect.height - 2f * t)), color);
        }

        private static void DrawRoundedRectOutline(Rect rect, Color color, float thickness, float radius)
        {
            if (rect.width <= 0f || rect.height <= 0f || thickness <= 0f)
                return;

            // Keep AA stroke fully inside the item rect to avoid corner bleed artifacts.
            float half = Mathf.Max(0.5f, thickness * 0.5f);
            rect = new Rect(
                rect.x + half,
                rect.y + half,
                Mathf.Max(1f, rect.width - thickness),
                Mathf.Max(1f, rect.height - thickness));

            float r = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
            if (r <= 0.01f)
            {
                DrawRectOutline(rect, color, thickness);
                return;
            }

            int arcSegments = Mathf.Clamp(Mathf.CeilToInt(r * 0.8f), 4, 12);
            var points = new List<Vector3>(arcSegments * 4 + 12);

            Vector2 tl = new Vector2(rect.xMin + r, rect.yMin + r);
            Vector2 tr = new Vector2(rect.xMax - r, rect.yMin + r);
            Vector2 br = new Vector2(rect.xMax - r, rect.yMax - r);
            Vector2 bl = new Vector2(rect.xMin + r, rect.yMax - r);

            points.Add(new Vector3(rect.xMin + r, rect.yMin, 0f));
            points.Add(new Vector3(rect.xMax - r, rect.yMin, 0f));
            AppendArc(points, tr, r, -90f, 0f, arcSegments, includeStart: false);
            points.Add(new Vector3(rect.xMax, rect.yMax - r, 0f));
            AppendArc(points, br, r, 0f, 90f, arcSegments, includeStart: false);
            points.Add(new Vector3(rect.xMin + r, rect.yMax, 0f));
            AppendArc(points, bl, r, 90f, 180f, arcSegments, includeStart: false);
            points.Add(new Vector3(rect.xMin, rect.yMin + r, 0f));
            AppendArc(points, tl, r, 180f, 270f, arcSegments, includeStart: false);

            var previousColor = Handles.color;
            Handles.color = color;
            Handles.DrawAAPolyLine(thickness, points.ToArray());
            Handles.color = previousColor;
        }

        private static void AppendArc(List<Vector3> points, Vector2 center, float radius, float startDeg, float endDeg, int segments, bool includeStart)
        {
            int startIndex = includeStart ? 0 : 1;
            for (int i = startIndex; i <= segments; i++)
            {
                float t = segments <= 0 ? 1f : (float)i / segments;
                float angleDeg = Mathf.Lerp(startDeg, endDeg, t);
                float angleRad = angleDeg * Mathf.Deg2Rad;
                points.Add(new Vector3(
                    center.x + Mathf.Cos(angleRad) * radius,
                    center.y + Mathf.Sin(angleRad) * radius,
                    0f));
            }
        }

        private static bool TryResolveObject(FavoriteItem item, out UnityEngine.Object obj)
        {
            obj = null;

            string path = TryResolveAssetPath(item);
            if (!string.IsNullOrEmpty(path))
            {
                obj = AssetDatabase.LoadMainAssetAtPath(path);
                if (obj != null) return true;
            }

            if (!string.IsNullOrEmpty(item.GlobalId) && GlobalObjectId.TryParse(item.GlobalId, out var gid))
            {
                obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                if (obj != null) return true;
            }

            return false;
        }

        private static string TryResolveAssetPath(FavoriteItem item)
        {
            if (!string.IsNullOrEmpty(item.Guid))
            {
                string guidPath = AssetDatabase.GUIDToAssetPath(item.Guid);
                if (!string.IsNullOrEmpty(guidPath))
                    return guidPath;
            }

            if (!string.IsNullOrEmpty(item.Path))
            {
                var obj = AssetDatabase.LoadMainAssetAtPath(item.Path);
                if (obj != null || AssetDatabase.IsValidFolder(item.Path))
                    return item.Path;
            }

            return null;
        }

        private static bool TryBuildFavoriteItem(UnityEngine.Object source, out FavoriteItem item)
        {
            item = default;
            if (source == null) return false;

            UnityEngine.Object obj = source;
            if (obj is Component component)
                obj = component.gameObject;

            string displayName = obj.name;

            if (EditorUtility.IsPersistent(obj))
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) return false;

                string guid = AssetDatabase.AssetPathToGUID(path);
                item = new FavoriteItem(guid, path, null, displayName);
                return true;
            }

            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            if (string.IsNullOrEmpty(globalId)) return false;

            item = new FavoriteItem(null, null, globalId, displayName);
            return true;
        }

        private static void AddFavorites(IEnumerable<UnityEngine.Object> objects)
        {
            bool changed = false;
            foreach (var obj in objects)
            {
                if (!TryBuildFavoriteItem(obj, out var item)) continue;
                changed |= AddItem(item, _page);
            }

            if (!changed) return;

            CompactPageLayout();
            SaveData();
            EditorApplication.RepaintProjectWindow();
        }

        private static bool AddItem(FavoriteItem item, int pageIndex)
        {
            string key = BuildKey(item.Guid, item.Path, item.GlobalId);
            if (string.IsNullOrEmpty(key))
                return false;

            int targetPage = Mathf.Max(0, pageIndex);
            if (PageContainsKey(targetPage, key, ignoreIndex: -1))
                return false;

            int insertIndex = GetGlobalInsertIndexForPageSlot(targetPage, GetPageItemCount(targetPage));
            Favorites.Insert(insertIndex, item);
            FavoritePages.Insert(insertIndex, targetPage);
            _page = Mathf.Clamp(_page, 0, Mathf.Max(0, GetConfiguredMinPageCount() - 1));
            return true;
        }

        private static void RemoveItemAtIndex(int index)
        {
            if (index < 0 || index >= Favorites.Count)
                return;

            Favorites.RemoveAt(index);
            if (index >= 0 && index < FavoritePages.Count)
                FavoritePages.RemoveAt(index);

            CompactPageLayout();
            _page = Mathf.Clamp(_page, 0, Mathf.Max(0, GetConfiguredMinPageCount() - 1));
        }

        private static string BuildKey(string guid, string path, string globalId)
        {
            if (!string.IsNullOrEmpty(globalId))
                return "o:" + globalId;

            if (!string.IsNullOrEmpty(guid))
                return "g:" + guid;

            if (!string.IsNullOrEmpty(path))
                return "p:" + path.ToLowerInvariant();

            return null;
        }

        private static void CompactMissingEntries()
        {
            bool changed = false;
            for (int i = Favorites.Count - 1; i >= 0; i--)
            {
                if (TryResolveObject(Favorites[i], out _))
                    continue;

                Favorites.RemoveAt(i);
                if (i >= 0 && i < FavoritePages.Count)
                FavoritePages.RemoveAt(i);
                changed = true;
            }

            changed |= CompactPageLayout();
            if (changed)
            {
                _page = 0;
                SaveData();
            }
        }

        private static void SaveData()
        {
            var data = new Data();
            data.minPageCount = Mathf.Max(1, _manualMinPageCount);
            for (int i = 0; i < Favorites.Count; i++)
            {
                var item = Favorites[i];
                int page = (i >= 0 && i < FavoritePages.Count) ? Mathf.Max(0, FavoritePages[i]) : 0;
                data.entries.Add(new Entry
                {
                    guid = item.Guid,
                    path = item.Path,
                    globalId = item.GlobalId,
                    displayName = item.DisplayName,
                    page = page,
                });
            }

            var pageIndexes = new List<int>(PageNames.Keys);
            pageIndexes.Sort();
            foreach (int index in pageIndexes)
            {
                if (!PageNames.TryGetValue(index, out var name) || string.IsNullOrWhiteSpace(name))
                    continue;

                data.pageNames.Add(new PageNameEntry
                {
                    index = index,
                    name = name.Trim()
                });
            }

            try
            {
                File.WriteAllText(DataPath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NoMorePain] Failed to save project favorites: {e.Message}");
            }
        }

        private static void LoadData()
        {
            Favorites.Clear();
            FavoritePages.Clear();
            PageNames.Clear();
            PageScrollStarts.Clear();
            PageScrollVisualStarts.Clear();
            PageScrollVelocities.Clear();
            _lastScrollAnimationTime = -1d;
            _page = 0;
            _manualMinPageCount = 1;
            EndPageNameEdit();

            if (!File.Exists(DataPath)) return;

            try
            {
                var data = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
                if (data == null) return;

                if (data.entries != null)
                {
                    foreach (var e in data.entries)
                    {
                        var item = new FavoriteItem(e.guid, e.path, e.globalId, e.displayName);
                        if (!TryResolveObject(item, out _)) continue;
                        AddItem(item, e.page);
                    }
                }

                if (data.pageNames != null)
                {
                    foreach (var p in data.pageNames)
                    {
                        if (p == null) continue;
                        if (p.index < 0 || string.IsNullOrWhiteSpace(p.name)) continue;
                        PageNames[p.index] = p.name.Trim();
                    }
                }

                _manualMinPageCount = Mathf.Max(1, data.minPageCount);
                CompactPageLayout();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NoMorePain] Failed to load project favorites: {e.Message}");
            }
        }

        private static void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 12,
                };
                _titleStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.94f, 0.94f, 0.94f, 0.95f)
                    : new Color(0.12f, 0.12f, 0.12f, 0.95f);
            }

            if (_rowStyle == null)
            {
                _rowStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                };
                _rowStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.90f, 0.90f, 0.90f, 1f)
                    : new Color(0.08f, 0.08f, 0.08f, 1f);
            }

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip,
                    fontSize = 11,
                };
                _hintStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.80f, 0.80f, 0.80f, 0.62f)
                    : new Color(0.18f, 0.18f, 0.18f, 0.62f);
            }

            if (_pageStyle == null)
            {
                _pageStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    clipping = TextClipping.Clip,
                    fontStyle = FontStyle.Bold,
                    fontSize = 11,
                };
                _pageStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.88f, 0.88f, 0.88f, 0.95f)
                    : new Color(0.10f, 0.10f, 0.10f, 0.95f);
            }

            if (_pageHoverStyle == null)
            {
                _pageHoverStyle = new GUIStyle(_pageStyle);
                _pageHoverStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.98f, 0.98f, 0.98f, 1f)
                    : new Color(0.98f, 0.98f, 0.98f, 1f);
            }

            if (_pageEditStyle == null)
            {
                _pageEditStyle = new GUIStyle(EditorStyles.textField)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 11,
                    clipping = TextClipping.Clip
                };
                _pageEditStyle.normal.textColor = _pageStyle.normal.textColor;
                _pageEditStyle.hover.textColor = _pageStyle.normal.textColor;
                _pageEditStyle.focused.textColor = _pageStyle.normal.textColor;
                _pageEditStyle.active.textColor = _pageStyle.normal.textColor;
            }

            if (_pageArrowStyle == null)
            {
                _pageArrowStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 12,
                };
                _pageArrowStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.90f, 0.90f, 0.90f, 0.98f)
                    : new Color(0.10f, 0.10f, 0.10f, 0.98f);
            }

            if (_pageArrowHoverStyle == null)
            {
                _pageArrowHoverStyle = new GUIStyle(_pageArrowStyle);
                _pageArrowHoverStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.99f, 0.99f, 0.99f, 1f)
                    : new Color(0.98f, 0.98f, 0.98f, 1f);
            }

            if (_pageArrowDisabledStyle == null)
            {
                _pageArrowDisabledStyle = new GUIStyle(_pageArrowStyle);
                _pageArrowDisabledStyle.normal.textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.58f, 0.58f, 0.58f, 0.80f)
                    : new Color(0.35f, 0.35f, 0.35f, 0.80f);
            }

            EnsurePagerPillTexture();
        }

        private static void EnsurePagerPillTexture()
        {
            bool isPro = EditorGUIUtility.isProSkin;
            if (_pagerPillTexture != null && _pagerPillTextureForPro == isPro)
                return;

            _pagerPillTextureForPro = isPro;
            var color = isPro
                ? new Color(0.08f, 0.08f, 0.09f, 1f)
                : new Color(0.66f, 0.66f, 0.66f, 1f);
            _pagerPillTexture = BuildPillTexture(116, 26, 13, color);
        }

        private static Texture2D BuildPillTexture(int w, int h, int radius, Color color)
        {
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a = PillAlpha(x + 0.5f, y + 0.5f, w, h, radius);
                    pixels[y * w + x] = new Color(color.r, color.g, color.b, color.a * a);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static float PillAlpha(float px, float py, int w, int h, int r)
        {
            float cx = Mathf.Clamp(px, r, w - r);
            float cy = Mathf.Clamp(py, r, h - r);
            float dx = px - cx;
            float dy = py - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(r - dist + 0.5f);
        }

        private static void TryTrackProjectWindow()
        {
            var mouse = EditorWindow.mouseOverWindow;
            if (IsProjectBrowser(mouse))
            {
                _projectWindow = mouse;
                return;
            }

            var focused = EditorWindow.focusedWindow;
            if (IsProjectBrowser(focused))
                _projectWindow = focused;
        }

        private static bool IsProjectBrowser(EditorWindow window) =>
            window != null && window.GetType().Name == "ProjectBrowser";

        private static bool IsRootProjectNodePath(string path)
        {
            return string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, "Packages", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsListLikeRect(Rect rect) => rect.width > rect.height * 1.8f;
        private static bool IsGridTileRect(Rect rect) => rect.width <= rect.height * 1.6f;

        private static int GetAssetDepth(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;

            int slashCount = 0;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '/') slashCount++;
            }
            return slashCount;
        }

        private static bool IsProjectLeftTreeRow(Rect rowRect, int depth, bool isGridTile)
        {
            if (isGridTile) return false;
            if (!IsListLikeRect(rowRect)) return false;

            if (depth <= 0)
                return rowRect.x <= 48f;

            float expectedTreeX = 6f + depth * 12.5f;
            return Mathf.Abs(rowRect.x - expectedTreeX) <= 20f;
        }

        private static void UpdateLeftTreeWidthFromRootRow(string path, Rect rowRect, bool isGridTile)
        {
            if (isGridTile) return;
            if (!IsListLikeRect(rowRect)) return;
            if (!IsRootProjectNodePath(path)) return;
            if (rowRect.x > 72f) return;

            float windowW = _projectWindow != null
                ? Mathf.Max(PanelMinWidth, _projectWindow.position.width)
                : Mathf.Max(PanelMinWidth, rowRect.xMax);
            float maxOverlayW = Mathf.Max(PanelMinWidth, windowW - PanelMinWidth);
            float estimatedRight = Mathf.Clamp(rowRect.xMax + 1f, PanelMinWidth, maxOverlayW);

            _leftTreeRightXEstimate = estimatedRight;
            _hasLeftTreeRightXEstimate = true;
        }

        private static void UpdateLeftTreeMetrics(Rect rowRect, bool isLeftTreePaneRow)
        {
            if (!isLeftTreePaneRow) return;

            if (_leftMetricsFrame != Time.frameCount)
            {
                // Start a new sample from the current frame.
                _leftMetricsFrame = Time.frameCount;
                _topYEstimate = Mathf.Clamp(rowRect.y, 0f, 120f);
                _topYSecondEstimate = float.PositiveInfinity;
                _bottomYEstimate = Mathf.Max(_topYEstimate + 20f, rowRect.yMax);
                _rowHeightEstimate = Mathf.Max(1f, rowRect.height);
                _leftRowsSampleCount = 1;
                return;
            }

            // Within the same frame collect the exact visible bounds of the left pane.
            _leftRowsSampleCount++;
            _rowHeightEstimate = Mathf.Lerp(_rowHeightEstimate, Mathf.Max(1f, rowRect.height), 0.2f);

            float y = Mathf.Clamp(rowRect.y, 0f, 120f);
            if (y < _topYEstimate)
            {
                _topYSecondEstimate = _topYEstimate;
                _topYEstimate = y;
            }
            else if (y < _topYSecondEstimate)
            {
                _topYSecondEstimate = y;
            }

            _bottomYEstimate = Mathf.Max(_bottomYEstimate, rowRect.yMax);
        }

        [MenuItem("Assets/Add to Favorites", false, 1900)]
        private static void AddSelectedToFavoritesFromMenu()
        {
            AddFavorites(Selection.objects);
        }

        [MenuItem("Assets/Add to Favorites", true)]
        private static bool ValidateAddSelectedToFavoritesFromMenu()
        {
            foreach (var obj in Selection.objects)
            {
                if (TryBuildFavoriteItem(obj, out _))
                    return true;
            }
            return false;
        }
    }
}
