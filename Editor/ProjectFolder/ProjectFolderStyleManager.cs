using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Project folder visuals:
    /// - row tint color (similar to Hierarchy highlight)
    /// - bottom-right badge icon on folder icon
    /// Data is persisted per-project in ProjectSettings.
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectFolderStyleManager
    {
        [Serializable] private class Entry { public string guid; public string hex; public string iconId; }
        [Serializable] private class Data  { public List<Entry> entries = new(); }

        internal readonly struct BadgeIconOption
        {
            public readonly string Id;
            public readonly string Name;
            public readonly Texture2D Icon;

            public BadgeIconOption(string id, string name, Texture2D icon)
            {
                Id   = id;
                Name = name;
                Icon = icon;
            }
        }

        private static readonly string DataPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../ProjectSettings/NoMorePainProjectFolderStyles.json"));

        private static readonly Dictionary<string, Color>  Colors       = new();
        private static readonly Dictionary<string, Color>  ColorsByPath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CustomIconId = new();

        private static readonly Dictionary<string, Texture2D> AutoBadgeCache   = new();
        private static readonly Dictionary<string, Texture2D> ResolveIconCache = new();
        private static readonly Dictionary<string, Texture2D> SolidTintIconCache = new();
        private static readonly Dictionary<string, string[]> SubFoldersCache = new();
        private static Texture2D _defaultFolderTintSourceIcon;
        private static BadgeIconOption[] _iconOptions;
        private static Dictionary<string, Type> _typeByName;
        private static GUIStyle _coloredFolderLabelStyle;
        private const float TreeIndentWidth = 14f;
        private static float _leftTreeRightXEstimate = 220f;
        private static bool _hasLeftTreeRightXEstimate;

        static ProjectFolderStyleManager()
        {
            LoadData();
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private static void OnProjectChanged()
        {
            RebuildPathColorCache();
            AutoBadgeCache.Clear();
            ResolveIconCache.Clear();
            SolidTintIconCache.Clear();
            SubFoldersCache.Clear();
            _hasLeftTreeRightXEstimate = false;
            _leftTreeRightXEstimate = 220f;
            EditorApplication.RepaintProjectWindow();
        }

        private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) return;

            // Use canonical GUID resolved from path: left tree and right pane can provide
            // different GUID strings for the same folder in some Unity layouts.
            string canonicalGuid = AssetDatabase.AssetPathToGUID(path);
            string styleGuid = !string.IsNullOrEmpty(canonicalGuid) ? canonicalGuid : guid;

            bool hasStoredColor = TryGetFolderColor(styleGuid, guid, path, out var folderColor);
            int depth = GetFolderDepth(path);
            bool isGridTileRaw = IsGridTileRect(selectionRect);
            UpdateLeftTreeRightXEstimate(selectionRect, depth, isGridTileRaw);
            bool isLeftTreePaneRow = IsProjectLeftTreeRow(selectionRect, depth, isGridTileRaw);
            bool isGridTile = !isLeftTreePaneRow && isGridTileRaw;

            // Row Colors:
            // - apply on all row/list entries
            // - exclude only clear grid tiles
            bool canDrawRowTint = hasStoredColor
                               && NMPSettings.ProjectRowColors
                               && !isGridTile;

            // Folder Colors:
            // - icon tint in both panes is controlled only by Folder Colors
            bool canDrawIconTint = hasStoredColor
                                && NMPSettings.ProjectFolderColors;

            if (Event.current.type == EventType.Repaint)
            {
                if (NMPSettings.ProjectZebra)
                    DrawZebra(selectionRect, canDrawRowTint, isLeftTreePaneRow);
                if (canDrawRowTint || canDrawIconTint)
                    DrawColorTint(folderColor, path, selectionRect, isLeftTreePaneRow, canDrawRowTint, canDrawIconTint);
                if (NMPSettings.ProjectTreeLines)
                    DrawTreeLines(path, selectionRect, hasStoredColor, folderColor, isLeftTreePaneRow);
                if (NMPSettings.ProjectBadgeIcons)
                    DrawBadge(styleGuid, path, selectionRect, isLeftTreePaneRow);
            }

            // Alt + LMB on folder opens style picker (mirrors Hierarchy UX).
            // Use MouseUp (not MouseDown) so it does not interfere with drag-and-drop,
            // especially when Alt is held to show Favorites overlay.
            var evt = Event.current;
            if (evt.type == EventType.MouseUp &&
                evt.button == 0 &&
                evt.alt &&
                DragAndDrop.objectReferences.Length == 0 &&
                selectionRect.Contains(evt.mousePosition))
            {
                ProjectFolderStylePickerWindow.Open(new[] { styleGuid }, GUIUtility.GUIToScreenPoint(evt.mousePosition));
                evt.Use();
            }
        }

        private static void DrawColorTint(
            Color color,
            string folderPath,
            Rect rect,
            bool isLeftTreePaneRow,
            bool drawRowTint,
            bool drawIconTint)
        {
            bool isListLike = isLeftTreePaneRow || rect.width > rect.height * 1.8f;

            bool shouldDrawRowTint = drawRowTint;

            // In left tree pane always tint row; in right Assets pane tint only in list mode.
            if (shouldDrawRowTint)
            {
                float rowStartX = isLeftTreePaneRow ? 0f : rect.x;
                float rowRightX = isLeftTreePaneRow && _hasLeftTreeRightXEstimate
                    ? Mathf.Min(rect.xMax, _leftTreeRightXEstimate)
                    : rect.xMax;
                var rowFillRect = new Rect(rowStartX, rect.y, Mathf.Max(0f, rowRightX - rowStartX), rect.height);
                EditorGUI.DrawRect(rowFillRect, new Color(color.r, color.g, color.b, 0.30f));
                EditorGUI.DrawRect(new Rect(rowStartX, rect.y, 3f, rect.height), new Color(color.r, color.g, color.b, 1f));
            }

            Rect folderIconRect = GetFolderIconRect(rect, isLeftTreePaneRow);
            if (drawIconTint)
            {
                var iconTex = GetTintSourceIcon(folderPath);
                if (iconTex != null)
                {
                    // Draw a solid-color icon using source alpha as mask, so color is uniform
                    // across the full folder shape without "untinted" spots.
                    var solidTintIcon = GetOrBuildSolidTintIcon(iconTex, color, useHardAlphaMask: isListLike);
                    if (solidTintIcon != null)
                    {
                        // Slight inflate so the tint fully covers Unity's default folder border.
                        float pad = Mathf.Clamp(folderIconRect.width * 0.08f, 2.8f, 4.2f);
                        float bottomExtra = Mathf.Clamp(folderIconRect.height * 0.05f, 2.2f, 3.4f);
                        var drawRect = new Rect(
                            folderIconRect.x - pad,
                            folderIconRect.y - pad,
                            folderIconRect.width + pad * 2f + 0.5f,
                            folderIconRect.height + pad * 2f + bottomExtra);
                        var prevColor = GUI.color;
                        GUI.color = Color.white;
                        GUI.DrawTexture(drawRect, solidTintIcon, ScaleMode.ScaleToFit, true);
                        GUI.color = prevColor;
                    }
                }
            }

            if (shouldDrawRowTint)
                DrawWhiteFolderLabel(folderPath, rect, folderIconRect, isLeftTreePaneRow, color);
        }

        private static Texture2D GetTintSourceIcon(string folderPath)
        {
            var fallback = _defaultFolderTintSourceIcon
                ??= EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;

            var cached = AssetDatabase.GetCachedIcon(folderPath) as Texture2D;
            if (cached == null) return fallback;

            // Some package-style folders (for example "com.*") use non-folder icons
            // with opaque backgrounds; tinting those produces a colored square.
            // For such cases, use the standard folder silhouette as tint source.
            string iconName = cached.name ?? string.Empty;
            bool isFolderIcon = iconName.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0;
            bool packageLikeName = Path.GetFileName(folderPath).StartsWith("com.", StringComparison.OrdinalIgnoreCase);
            bool packagePath = folderPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);

            if (!isFolderIcon || packageLikeName || packagePath)
                return fallback != null ? fallback : cached;

            return cached;
        }

        private static void DrawBadge(string guid, string folderPath, Rect rect, bool isLeftTreePaneRow)
        {
            Texture2D badge = ResolveBadgeIcon(guid, folderPath);
            if (badge == null) return;

            Rect folderIconRect = GetFolderIconRect(rect, isLeftTreePaneRow);
            // Scale with folder icon size (no upper cap, so slider changes are reflected).
            float badgeSize = Mathf.Max(8f, folderIconRect.width * 0.5f);
            var badgeRect = new Rect(
                folderIconRect.xMax - badgeSize + 0.5f,
                folderIconRect.yMax - badgeSize + 0.5f,
                badgeSize,
                badgeSize);

            // Stronger dark shadow/outline for readability on any folder color.
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            float o = 1.75f;
            GUI.DrawTexture(new Rect(badgeRect.x - o, badgeRect.y,     badgeRect.width, badgeRect.height), badge, ScaleMode.ScaleToFit, true);
            GUI.DrawTexture(new Rect(badgeRect.x + o, badgeRect.y,     badgeRect.width, badgeRect.height), badge, ScaleMode.ScaleToFit, true);
            GUI.DrawTexture(new Rect(badgeRect.x,     badgeRect.y - o, badgeRect.width, badgeRect.height), badge, ScaleMode.ScaleToFit, true);
            GUI.DrawTexture(new Rect(badgeRect.x,     badgeRect.y + o, badgeRect.width, badgeRect.height), badge, ScaleMode.ScaleToFit, true);

            GUI.color = Color.white;
            GUI.DrawTexture(badgeRect, badge, ScaleMode.ScaleToFit, true);
            GUI.color = prevColor;
        }

        private static void DrawTreeLines(string folderPath, Rect rowRect, bool hasCustomColor, Color folderColor, bool isLeftTreePaneRow)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!isLeftTreePaneRow) return;

            int depth = GetFolderDepth(folderPath);
            // Skip drawing connections from root parents (Assets/..., Packages/...).
            // Start tree lines only from 2+ nesting levels.
            if (depth <= 1) return;

            var chain = BuildFolderChain(folderPath);
            // Do not draw branch segments that originate from root parents
            // (Assets / Packages). Keep only deeper levels.
            if (chain.Count > 0 && GetFolderDepth(chain[0]) == 1)
                chain.RemoveAt(0);
            if (chain.Count == 0) return;

            float midY = rowRect.y + rowRect.height * 0.5f;
            var defaultLineColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.18f)
                : new Color(0f, 0f, 0f, 0.22f);
            var coloredLineColor = EditorGUIUtility.isProSkin
                ? new Color(folderColor.r, folderColor.g, folderColor.b, 0.58f)
                : new Color(folderColor.r, folderColor.g, folderColor.b, 0.50f);
            var lineColor = (NMPSettings.ProjectRowColors && hasCustomColor) ? coloredLineColor : defaultLineColor;

            int chainDepth = chain.Count;
            for (int i = 0; i < chainDepth; i++)
            {
                float lineX = rowRect.x + (i + 1 - chainDepth) * TreeIndentWidth - TreeIndentWidth * 1.5f - 1f;
                bool isLast = IsLastSibling(chain[i]);
                bool isCurrent = i == chainDepth - 1;

                if (isCurrent)
                {
                    DrawCurrentTreeNodeLines(rowRect, lineX, midY, lineColor, isLast);
                }
                else if (!isLast)
                {
                    EditorGUI.DrawRect(new Rect(lineX, rowRect.y, 2f, rowRect.height), lineColor);
                }
            }
        }

        private static Rect GetFolderIconRect(Rect rect, bool isLeftTreePaneRow)
        {
            // Detect list-like layout by aspect ratio. This remains stable while
            // the Project-window icon size slider changes.
            bool isListLike = isLeftTreePaneRow || IsListLikeRect(rect);

            if (isListLike)
            {
                // Keep in sync with Unity's list icon size slider: no hard max cap.
                float s = Mathf.Max(14f, rect.height - 2f);
                float availableW = Mathf.Max(8f, rect.xMax - rect.x - 2f);
                s = Mathf.Min(s, availableW);
                // Right Assets pane needs a slightly larger X offset in list mode;
                // otherwise the tinted icon appears shifted left versus Unity's folder icon.
                float xOffset = isLeftTreePaneRow ? 1f : 2.5f;
                float x = rect.x + xOffset;
                float maxX = rect.xMax - s - 1f;
                if (x > maxX) x = maxX;
                return new Rect(
                    x,
                    rect.y + Mathf.Floor((rect.height - s) * 0.5f),
                    s,
                    s);
            }

            // Grid mode: icon area above the label.
            // IMPORTANT: do not cap max size, otherwise overlay lags behind Unity icon
            // when the Project-window icon size slider is near maximum.
            float sGrid = Mathf.Max(16f, Mathf.Min(rect.width - 6f, rect.height - 18f));
            return new Rect(
                rect.x + Mathf.Floor((rect.width - sGrid) * 0.5f),
                rect.y + 2f,
                sGrid,
                sGrid);
        }

        private static bool IsListLikeRect(Rect rect) => rect.width > rect.height * 1.8f;
        // Grid tiles are roughly square. Rows/lists are much wider than they are tall.
        private static bool IsGridTileRect(Rect rect) => rect.width <= rect.height * 1.6f;

        private static int GetFolderDepth(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return 0;
            int slashCount = 0;
            for (int i = 0; i < folderPath.Length; i++)
                if (folderPath[i] == '/') slashCount++;
            return slashCount;
        }

        private static bool IsProjectLeftTreeRow(Rect rowRect, int depth, bool isGridTile)
        {
            // Never classify right-grid tiles as left tree rows.
            if (isGridTile) return false;
            if (!IsListLikeRect(rowRect)) return false;

            // Match the left tree by how close X is to expected indentation.
            // This avoids classifying right Assets rows as left-tree rows.
            if (depth <= 0)
                return rowRect.x <= 48f;

            float expectedTreeX = 6f + depth * 12.5f;
            bool closeToTreeIndent = Mathf.Abs(rowRect.x - expectedTreeX) <= 20f;
            return closeToTreeIndent;
        }

        private static void UpdateLeftTreeRightXEstimate(Rect rowRect, int depth, bool isGridTile)
        {
            if (isGridTile) return;
            if (!IsListLikeRect(rowRect)) return;

            bool candidate;
            if (depth <= 0)
            {
                // Root tree rows (Assets / Packages) are left-pane only and let us
                // refresh boundary immediately on splitter resize, even when collapsed.
                candidate = rowRect.x <= 48f;
            }
            else
            {
                float expectedTreeX = 6f + depth * 12.5f;
                candidate = Mathf.Abs(rowRect.x - expectedTreeX) <= 20f;
            }

            if (!candidate) return;
            _leftTreeRightXEstimate = rowRect.xMax;
            _hasLeftTreeRightXEstimate = true;
        }

        private static List<string> BuildFolderChain(string folderPath)
        {
            var chain = new List<string>();
            string current = folderPath;

            while (!string.IsNullOrEmpty(current) && GetFolderDepth(current) > 0)
            {
                chain.Add(current);
                current = GetParentFolderPath(current);
            }

            chain.Reverse();
            return chain;
        }

        private static string GetParentFolderPath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return null;
            int slash = folderPath.LastIndexOf('/');
            if (slash <= 0) return null;
            return folderPath.Substring(0, slash);
        }

        private static bool IsLastSibling(string folderPath)
        {
            string parentPath = GetParentFolderPath(folderPath);
            if (string.IsNullOrEmpty(parentPath)) return true;

            var siblings = GetSubFoldersSorted(parentPath);
            int idx = Array.IndexOf(siblings, folderPath);
            if (idx < 0) return true;
            return idx == siblings.Length - 1;
        }

        private static string[] GetSubFoldersSorted(string parentPath)
        {
            if (SubFoldersCache.TryGetValue(parentPath, out var cached))
                return cached;

            var subFolders = AssetDatabase.GetSubFolders(parentPath) ?? Array.Empty<string>();
            Array.Sort(subFolders, StringComparer.OrdinalIgnoreCase);
            SubFoldersCache[parentPath] = subFolders;
            return subFolders;
        }

        private static void DrawCurrentTreeNodeLines(Rect rowRect, float lineX, float midY, Color color, bool isLast)
        {
            float arrowCenterX = rowRect.x - TreeIndentWidth * 0.5f - 4f;
            float horizontalW = Mathf.Max(0f, arrowCenterX - lineX);

            EditorGUI.DrawRect(new Rect(lineX, rowRect.y, 2f, Mathf.Max(0f, midY - rowRect.y - 1f)), color);
            EditorGUI.DrawRect(new Rect(lineX, midY - 1f, horizontalW, 2f), color);

            if (!isLast)
                EditorGUI.DrawRect(new Rect(lineX, midY + 1f, 2f, Mathf.Max(0f, rowRect.yMax - midY - 1f)), color);
        }

        private static void DrawZebra(Rect rowRect, bool skipForColoredRow, bool isLeftTreePaneRow)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!isLeftTreePaneRow) return;
            if (skipForColoredRow) return;
            if (Mathf.RoundToInt(rowRect.y / rowRect.height) % 2 == 0) return;

            var zebraColor = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.07f)
                : new Color(0f, 0f, 0f, 0.07f);
            float rowStartX = isLeftTreePaneRow ? 0f : rowRect.x;
            float rowRightX = isLeftTreePaneRow && _hasLeftTreeRightXEstimate
                ? Mathf.Min(rowRect.xMax, _leftTreeRightXEstimate)
                : rowRect.xMax;
            EditorGUI.DrawRect(new Rect(rowStartX, rowRect.y, Mathf.Max(0f, rowRightX - rowStartX), rowRect.height), zebraColor);
        }

        private static Texture2D ResolveBadgeIcon(string folderGuid, string folderPath)
        {
            if (CustomIconId.TryGetValue(folderGuid, out var iconId) && !string.IsNullOrEmpty(iconId))
            {
                var resolved = ResolveIconById(iconId);
                if (resolved != null) return resolved;
            }

            if (AutoBadgeCache.TryGetValue(folderGuid, out var cached))
                return cached;

            Texture2D auto = BuildAutoBadge(folderPath);
            AutoBadgeCache[folderGuid] = auto;
            return auto;
        }

        private static Texture2D BuildAutoBadge(string folderPath)
        {
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (string.IsNullOrEmpty(p) || p == folderPath || AssetDatabase.IsValidFolder(p)) continue;

                var obj = AssetDatabase.LoadMainAssetAtPath(p);
                if (obj == null) continue;

                Texture2D tex = AssetPreview.GetMiniThumbnail(obj) as Texture2D;
                if (tex == null)
                    tex = EditorGUIUtility.ObjectContent(obj, obj.GetType()).image as Texture2D;
                if (tex != null) return tex;
            }
            return null;
        }

        private static Texture2D ResolveIconById(string iconId)
        {
            if (string.IsNullOrEmpty(iconId)) return null;
            if (ResolveIconCache.TryGetValue(iconId, out var cached)) return cached;

            Texture2D result = null;

            if (iconId.StartsWith("asset:", StringComparison.Ordinal))
            {
                string guid = iconId.Substring("asset:".Length);
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            else if (iconId.StartsWith("global:", StringComparison.Ordinal))
            {
                string globalId = iconId.Substring("global:".Length);
                if (GlobalObjectId.TryParse(globalId, out var gid))
                {
                    var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
                    result = obj as Texture2D;
                    if (result == null && obj != null)
                        result = AssetPreview.GetMiniThumbnail(obj) as Texture2D
                              ?? EditorGUIUtility.ObjectContent(obj, obj.GetType()).image as Texture2D;
                }
            }
            else if (iconId.StartsWith("type:", StringComparison.Ordinal))
            {
                EnsureTypeMap();
                string typeName = iconId.Substring("type:".Length);
                if (_typeByName.TryGetValue(typeName, out var t))
                {
                    result = AssetPreview.GetMiniTypeThumbnail(t) as Texture2D;
                    if (result == null)
                        result = EditorGUIUtility.ObjectContent(null, t).image as Texture2D;
                }
            }

            ResolveIconCache[iconId] = result;
            return result;
        }

        private static Texture2D GetOrBuildSolidTintIcon(Texture2D source, Color color, bool useHardAlphaMask)
        {
            if (source == null) return null;

            string key = source.GetInstanceID() + ":" + ColorUtility.ToHtmlStringRGBA(color) + ":" + (useHardAlphaMask ? "hard" : "soft");
            if (SolidTintIconCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            int w = Mathf.Max(1, source.width);
            int h = Mathf.Max(1, source.height);

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                var readable = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readable.Apply(false, false);

                var src = readable.GetPixels32();
                var dst = new Color32[src.Length];

                byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
                byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);

                for (int i = 0; i < src.Length; i++)
                {
                    byte a;
                    if (useHardAlphaMask)
                    {
                        // List/tree mode: prefer a solid fill but ignore very faint shadow pixels,
                        // otherwise some icons become tinted rectangles.
                        a = src[i].a >= 28 ? (byte)255 : (byte)0;
                    }
                    else
                    {
                        // Grid mode (large icons): preserve source alpha to avoid square artifacts.
                        a = src[i].a;
                    }
                    dst[i] = new Color32(r, g, b, a);
                }

                var solid = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
                {
                    filterMode = source.filterMode,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                solid.SetPixels32(dst);
                solid.Apply(false, true);

                UnityEngine.Object.DestroyImmediate(readable);
                SolidTintIconCache[key] = solid;
                return solid;
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void DrawWhiteFolderLabel(
            string folderPath,
            Rect rowRect,
            Rect folderIconRect,
            bool isLeftTreePaneRow,
            Color rowColor)
        {
            _coloredFolderLabelStyle ??= new GUIStyle(EditorStyles.label)
            {
                clipping = TextClipping.Clip,
                richText = false
            };
            var white = new Color(0.96f, 0.96f, 0.96f, 1f);
            _coloredFolderLabelStyle.normal.textColor  = white;
            _coloredFolderLabelStyle.hover.textColor   = white;
            _coloredFolderLabelStyle.active.textColor  = white;
            _coloredFolderLabelStyle.focused.textColor = white;

            string label = Path.GetFileName(folderPath);
            float textX = folderIconRect.xMax + (isLeftTreePaneRow ? 3f : 2f);
            float rowRightX = isLeftTreePaneRow && _hasLeftTreeRightXEstimate
                ? Mathf.Min(rowRect.xMax, _leftTreeRightXEstimate)
                : rowRect.xMax;
            var textRect = new Rect(textX, rowRect.y, Mathf.Max(0f, rowRightX - textX), rowRect.height);
            // Replace the text zone with the same visual stack as the row (base + tint),
            // then draw white text. This avoids "double text" without making the row opaque.
            var baseBg = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f);
            EditorGUI.DrawRect(textRect, baseBg);
            EditorGUI.DrawRect(textRect, new Color(rowColor.r, rowColor.g, rowColor.b, 0.30f));
            GUI.Label(textRect, label, _coloredFolderLabelStyle);
        }

        private static void EnsureTypeMap()
        {
            if (_typeByName != null) return;

            _typeByName = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var t in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (t == null || t.IsAbstract || string.IsNullOrEmpty(t.FullName)) continue;
                _typeByName[t.FullName] = t;
            }
        }

        internal static BadgeIconOption[] GetBadgeIconOptions()
        {
            if (_iconOptions != null) return _iconOptions;

            var list = new List<BadgeIconOption>();
            var seen = new HashSet<Texture2D>();

            foreach (var t in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (t.IsAbstract || t == typeof(Transform)) continue;

                Texture2D tex = AssetPreview.GetMiniTypeThumbnail(t) as Texture2D;
                if (tex == null)
                    tex = EditorGUIUtility.ObjectContent(null, t).image as Texture2D;
                if (tex == null || !seen.Add(tex)) continue;

                list.Add(new BadgeIconOption("type:" + t.FullName, t.Name, tex));
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _iconOptions = list.ToArray();
            return _iconOptions;
        }

        internal static void ApplyColor(string[] folderGuids, Color color)
        {
            foreach (var guid in folderGuids)
                Colors[guid] = color;
            RebuildPathColorCache();
            SaveData();
            EditorApplication.RepaintProjectWindow();
        }

        internal static void ClearColor(string[] folderGuids)
        {
            foreach (var guid in folderGuids)
                Colors.Remove(guid);
            RebuildPathColorCache();
            SaveData();
            EditorApplication.RepaintProjectWindow();
        }

        internal static void ApplyCustomIcon(string[] folderGuids, string iconId)
        {
            foreach (var guid in folderGuids)
                CustomIconId[guid] = iconId;

            AutoBadgeCache.Clear();
            ResolveIconCache.Clear();
            SaveData();
            EditorApplication.RepaintProjectWindow();
        }

        internal static void ClearCustomIcon(string[] folderGuids)
        {
            foreach (var guid in folderGuids)
                CustomIconId.Remove(guid);

            AutoBadgeCache.Clear();
            ResolveIconCache.Clear();
            SaveData();
            EditorApplication.RepaintProjectWindow();
        }

        internal static bool TryGetColorForFolderPath(string folderPath, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return false;

            string canonicalGuid = AssetDatabase.AssetPathToGUID(folderPath);
            return TryGetFolderColor(canonicalGuid, canonicalGuid, folderPath, out color);
        }

        internal static Texture2D GetTintedFolderIconForPath(string folderPath, Color color, bool useHardAlphaMask)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return null;

            var iconTex = GetTintSourceIcon(folderPath);
            if (iconTex == null)
                return null;

            return GetOrBuildSolidTintIcon(iconTex, color, useHardAlphaMask);
        }

        internal static Texture2D GetBadgeIconForFolderPath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return null;

            string canonicalGuid = AssetDatabase.AssetPathToGUID(folderPath);
            if (string.IsNullOrEmpty(canonicalGuid))
                return null;

            return ResolveBadgeIcon(canonicalGuid, folderPath);
        }

        private static List<string> GetSelectedFolderGuids()
        {
            var result = new List<string>();
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    result.Add(guid);
            }
            return result;
        }

        [MenuItem("Assets/No More Pain/Folder Style...", false, 2000)]
        private static void OpenPickerFromMenu()
        {
            var guids = GetSelectedFolderGuids();
            if (guids.Count == 0) return;

            Vector2 screenPos;
            var win = EditorWindow.focusedWindow;
            if (win != null)
            {
                var r = win.position;
                screenPos = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
            }
            else
            {
                screenPos = new Vector2(Screen.currentResolution.width * 0.5f, Screen.currentResolution.height * 0.5f);
            }

            ProjectFolderStylePickerWindow.Open(guids.ToArray(), screenPos);
        }

        [MenuItem("Assets/No More Pain/Folder Style...", true)]
        private static bool ValidateOpenPickerFromMenu() => GetSelectedFolderGuids().Count > 0;

        [MenuItem("Assets/No More Pain/Clear Folder Color", false, 2001)]
        private static void ClearColorFromMenu()
        {
            var guids = GetSelectedFolderGuids();
            if (guids.Count > 0) ClearColor(guids.ToArray());
        }

        [MenuItem("Assets/No More Pain/Clear Folder Color", true)]
        private static bool ValidateClearColorFromMenu() => GetSelectedFolderGuids().Count > 0;

        [MenuItem("Assets/No More Pain/Clear Folder Badge Icon", false, 2002)]
        private static void ClearBadgeFromMenu()
        {
            var guids = GetSelectedFolderGuids();
            if (guids.Count > 0) ClearCustomIcon(guids.ToArray());
        }

        [MenuItem("Assets/No More Pain/Clear Folder Badge Icon", true)]
        private static bool ValidateClearBadgeFromMenu() => GetSelectedFolderGuids().Count > 0;

        private static void SaveData()
        {
            var unionKeys = new HashSet<string>(Colors.Keys);
            foreach (var k in CustomIconId.Keys) unionKeys.Add(k);

            var data = new Data();
            foreach (var guid in unionKeys)
            {
                var entry = new Entry { guid = guid };
                if (Colors.TryGetValue(guid, out var c))
                    entry.hex = ColorUtility.ToHtmlStringRGB(c);
                if (CustomIconId.TryGetValue(guid, out var id))
                    entry.iconId = id;
                data.entries.Add(entry);
            }

            try { File.WriteAllText(DataPath, JsonUtility.ToJson(data, true)); }
            catch (Exception e) { Debug.LogWarning($"[NoMorePain] Failed to save project folder styles: {e.Message}"); }
        }

        private static void LoadData()
        {
            Colors.Clear();
            ColorsByPath.Clear();
            CustomIconId.Clear();
            if (!File.Exists(DataPath)) return;

            try
            {
                var data = JsonUtility.FromJson<Data>(File.ReadAllText(DataPath));
                if (data?.entries == null) return;

                foreach (var e in data.entries)
                {
                    if (string.IsNullOrEmpty(e.guid)) continue;

                    if (!string.IsNullOrEmpty(e.hex) &&
                        ColorUtility.TryParseHtmlString("#" + e.hex, out var c))
                        Colors[e.guid] = c;

                    if (!string.IsNullOrEmpty(e.iconId))
                        CustomIconId[e.guid] = e.iconId;
                }
                RebuildPathColorCache();
            }
            catch (Exception e) { Debug.LogWarning($"[NoMorePain] Failed to load project folder styles: {e.Message}"); }
        }

        private static bool TryGetFolderColor(string styleGuid, string rawGuid, string path, out Color color)
        {
            if (!string.IsNullOrEmpty(path) && ColorsByPath.TryGetValue(path, out color))
                return true;

            if (!string.IsNullOrEmpty(styleGuid) && Colors.TryGetValue(styleGuid, out color))
            {
                if (!string.IsNullOrEmpty(path))
                    ColorsByPath[path] = color;
                return true;
            }

            if (!string.IsNullOrEmpty(rawGuid) && Colors.TryGetValue(rawGuid, out color))
            {
                if (!string.IsNullOrEmpty(path))
                    ColorsByPath[path] = color;
                return true;
            }

            color = default;
            return false;
        }

        private static void RebuildPathColorCache()
        {
            ColorsByPath.Clear();
            foreach (var kv in Colors)
            {
                string path = AssetDatabase.GUIDToAssetPath(kv.Key);
                if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path)) continue;
                ColorsByPath[path] = kv.Value;
            }
        }
    }
}
