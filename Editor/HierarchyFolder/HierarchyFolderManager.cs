using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Hierarchy Folders — marks plain GameObjects as folders using a persistent ID registry.
    /// No component required on the object.
    /// </summary>
    [InitializeOnLoad]
    internal static class HierarchyFolderManager
    {
        [Serializable] private class FolderData { public List<string> ids = new(); }

        private static readonly string DataPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "../ProjectSettings/NoMorePainFolders.json"));

        private static readonly HashSet<string> _folders = new();

        static HierarchyFolderManager()
        {
            LoadData();
        }

        internal static bool IsFolder(string globalId) => _folders.Contains(globalId);

        // ── Menu items ────────────────────────────────────────────────

        [MenuItem("GameObject/Create Folder", false, 0)]
        private static void CreateFolder()
        {
            var go = new GameObject("Folder");

            var parent = Selection.activeTransform;
            if (parent != null)
                go.transform.SetParent(parent, false);

            Undo.RegisterCreatedObjectUndo(go, "Create Hierarchy Folder");

            var id = GlobalObjectId.GetGlobalObjectIdSlow(go).ToString();
            _folders.Add(id);
            SaveData();

            HierarchyIconsManager.InvalidateCache();
            Selection.activeGameObject = go;
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("GameObject/Mark as Folder", false, 1)]
        private static void MarkAsFolder()
        {
            foreach (var go in Selection.gameObjects)
                _folders.Add(GlobalObjectId.GetGlobalObjectIdSlow(go).ToString());
            SaveData();
            HierarchyIconsManager.InvalidateCache();
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("GameObject/Mark as Folder", true)]
        private static bool ValidateMark()
        {
            if (Selection.gameObjects.Length == 0) return false;
            foreach (var go in Selection.gameObjects)
                if (!IsFolder(GlobalObjectId.GetGlobalObjectIdSlow(go).ToString())) return true;
            return false;
        }

        [MenuItem("GameObject/Unmark as Folder", false, 1)]
        private static void UnmarkAsFolder()
        {
            foreach (var go in Selection.gameObjects)
                _folders.Remove(GlobalObjectId.GetGlobalObjectIdSlow(go).ToString());
            SaveData();
            HierarchyIconsManager.InvalidateCache();
            EditorApplication.RepaintHierarchyWindow();
        }

        [MenuItem("GameObject/Unmark as Folder", true)]
        private static bool ValidateUnmark()
        {
            foreach (var go in Selection.gameObjects)
                if (IsFolder(GlobalObjectId.GetGlobalObjectIdSlow(go).ToString())) return true;
            return false;
        }

        // ── Persistence ───────────────────────────────────────────────
        private static void SaveData()
        {
            var data = new FolderData();
            data.ids.AddRange(_folders);
            try { File.WriteAllText(DataPath, JsonUtility.ToJson(data, true)); }
            catch (Exception e) { Debug.LogWarning($"[NoMorePain] Failed to save folders: {e.Message}"); }
        }

        private static void LoadData()
        {
            _folders.Clear();
            if (!File.Exists(DataPath)) return;
            try
            {
                var data = JsonUtility.FromJson<FolderData>(File.ReadAllText(DataPath));
                foreach (var id in data.ids)
                    _folders.Add(id);
            }
            catch (Exception e) { Debug.LogWarning($"[NoMorePain] Failed to load folders: {e.Message}"); }
        }
    }
}
