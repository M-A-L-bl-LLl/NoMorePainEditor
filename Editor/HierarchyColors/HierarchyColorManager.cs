using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Hierarchy Color Highlight — right-click any GameObject → "Highlight Color..." to pick a color.
    /// Draws a semi-transparent colored background on the full row.
    /// Data is stored per-project in ProjectSettings.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyColorManager
    {
        // ── Persistence ──────────────────────────────────────────────
        [Serializable] private class Entry     { public string id; public string hex; }
        [Serializable] private class ColorData { public List<Entry> entries = new(); }

        private static readonly string DataPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../ProjectSettings/NoMorePainColors.json"));

        private static readonly Dictionary<string, Color> _colors = new();

        // ── Init ─────────────────────────────────────────────────────
        static HierarchyColorManager()
        {
            LoadData();
        }

        internal static bool TryGetColor(string globalId, out Color color) =>
            _colors.TryGetValue(globalId, out color);

        // ── Called by HierarchyIconsManager (drawn first, behind icons) ──
        internal static void DrawForItem(string globalId, Rect rowRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!_colors.TryGetValue(globalId, out var color)) return;

            // Subtle tint across the full row
            var fullRect = new Rect(0, rowRect.y, Screen.width, rowRect.height);
            EditorGUI.DrawRect(fullRect, new Color(color.r, color.g, color.b, 0.30f));

            // Solid accent stripe on the left for clear color identification
            EditorGUI.DrawRect(new Rect(0, rowRect.y, 4f, rowRect.height),
                new Color(color.r, color.g, color.b, 1f));
        }

        // ── Public API for HierarchyColorPickerWindow ─────────────────
        internal static void ApplyColor(GameObject[] targets, Color color)
        {
            foreach (var go in targets)
                _colors[GetGlobalId(go)] = color;
            SaveData();
            EditorApplication.RepaintHierarchyWindow();
        }

        internal static void ClearColor(GameObject[] targets)
        {
            foreach (var go in targets)
                _colors.Remove(GetGlobalId(go));
            SaveData();
            EditorApplication.RepaintHierarchyWindow();
        }

        private static string GetGlobalId(GameObject go) =>
            GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();

        // ── Menu items ────────────────────────────────────────────────

        [MenuItem("GameObject/Highlight Color...", false, 49)]
        static void OpenPicker()
        {
            var targets = Selection.gameObjects;
            if (targets.Length == 0) return;

            Vector2 screenPos;
            var win = EditorWindow.focusedWindow;
            if (win != null)
            {
                var r = win.position;
                screenPos = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
            }
            else
            {
                screenPos = new Vector2(Screen.currentResolution.width  * 0.5f,
                                        Screen.currentResolution.height * 0.5f);
            }

            HierarchyColorPickerWindow.Open(targets, screenPos);
        }

        [MenuItem("GameObject/Highlight Color...", true)]
        static bool ValidatePicker() => Selection.gameObjects.Length > 0;

        [MenuItem("GameObject/Clear Highlight Color", false, 50)]
        static void ClearHighlight()
        {
            var targets = Selection.gameObjects;
            if (targets.Length > 0) ClearColor(targets);
        }

        [MenuItem("GameObject/Clear Highlight Color", true)]
        static bool ValidateClear() => Selection.gameObjects.Length > 0;

        // ── Persistence ───────────────────────────────────────────────
        private static void SaveData()
        {
            var data = new ColorData();
            foreach (var kv in _colors)
                data.entries.Add(new Entry { id = kv.Key, hex = ColorUtility.ToHtmlStringRGB(kv.Value) });
            try { File.WriteAllText(DataPath, JsonUtility.ToJson(data, true)); }
            catch (Exception e) { Debug.LogWarning($"[NoMorePain] Failed to save colors: {e.Message}"); }
        }

        private static void LoadData()
        {
            _colors.Clear();
            if (!File.Exists(DataPath)) return;
            try
            {
                var data = JsonUtility.FromJson<ColorData>(File.ReadAllText(DataPath));
                foreach (var entry in data.entries)
                    if (ColorUtility.TryParseHtmlString("#" + entry.hex, out var color))
                        _colors[entry.id] = color;
            }
            catch (Exception e) { Debug.LogWarning($"[NoMorePain] Failed to load colors: {e.Message}"); }
        }
    }
}
