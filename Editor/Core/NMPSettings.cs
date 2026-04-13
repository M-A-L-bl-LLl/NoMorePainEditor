using UnityEditor;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Persistent per-editor feature flags stored in EditorPrefs.
    /// All features are enabled by default.
    /// </summary>
    internal static class NMPSettings
    {
        private const string Prefix = "NMP.";

        // -- Hierarchy --
        public static bool HierarchyLeftIcon
        {
            get => Get("Hierarchy.LeftIcon",       true);
            set => Set("Hierarchy.LeftIcon",       value);
        }

        public static bool HierarchyRightIcons
        {
            get => Get("Hierarchy.RightIcons",     true);
            set => Set("Hierarchy.RightIcons",     value);
        }

        public static bool HierarchyTagLayerBadges
        {
            get => Get("Hierarchy.TagLayer",       true);
            set => Set("Hierarchy.TagLayer",       value);
        }

        public static bool HierarchyZebra
        {
            get => Get("Hierarchy.Zebra",          true);
            set => Set("Hierarchy.Zebra",          value);
        }

        public static bool HierarchyTreeLines
        {
            get => Get("Hierarchy.TreeLines",      true);
            set => Set("Hierarchy.TreeLines",      value);
        }

        public static bool HierarchyActiveToggle
        {
            get => Get("Hierarchy.ActiveToggle",   true);
            set => Set("Hierarchy.ActiveToggle",   value);
        }

        public static bool HierarchyColors
        {
            get => Get("Hierarchy.Colors",         true);
            set => Set("Hierarchy.Colors",         value);
        }

        public static bool HierarchyFolders
        {
            get => Get("Hierarchy.Folders",        true);
            set => Set("Hierarchy.Folders",        value);
        }

        public static bool HierarchyFolderNavbar
        {
            get => Get("Hierarchy.FolderNavbar",   true);
            set => Set("Hierarchy.FolderNavbar",   value);
        }

        // -- Project -------------------------------------------------
        public static bool ProjectFolderColors
        {
            get => Get("Project.FolderColors",     true);
            set => Set("Project.FolderColors",     value);
        }

        public static bool ProjectRowColors
        {
            get => Get("Project.RowColors",        true);
            set => Set("Project.RowColors",        value);
        }

        public static bool ProjectBadgeIcons
        {
            get => Get("Project.BadgeIcons",       true);
            set => Set("Project.BadgeIcons",       value);
        }

        public static bool ProjectTreeLines
        {
            get => Get("Project.TreeLines",        true);
            set => Set("Project.TreeLines",        value);
        }

        public static bool ProjectZebra
        {
            get => Get("Project.Zebra",            true);
            set => Set("Project.Zebra",            value);
        }

        // -- Inspector --
        public static bool InspectorTabs
        {
            get => Get("Inspector.Tabs",           true);
            set => Set("Inspector.Tabs",           value);
        }

        public static bool PlayModeSave
        {
            get => Get("Inspector.PlayModeSave",   true);
            set => Set("Inspector.PlayModeSave",   value);
        }

        public static bool ComponentCopyPaste
        {
            get => Get("Inspector.CopyPaste",      true);
            set => Set("Inspector.CopyPaste",      value);
        }

        // -- Helpers --
        private static bool Get(string key, bool defaultValue) =>
            EditorPrefs.GetBool(Prefix + key, defaultValue);

        private static void Set(string key, bool value) =>
            EditorPrefs.SetBool(Prefix + key, value);
    }
}


