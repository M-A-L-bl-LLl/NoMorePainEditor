using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Floating window that renders the full inspector for a single component.
    /// Opened by clicking a component icon in the Hierarchy.
    /// </summary>
    internal class ComponentQuickEditWindow : EditorWindow
    {
        private int                  _componentId;
        private UnityEditor.Editor   _editor;
        private Vector2              _scroll;

        // ── Open ─────────────────────────────────────────────────────

        internal static void Open(Component component, Vector2 screenPos)
        {
            // Reuse the single existing window, just swap the component
            var all = Resources.FindObjectsOfTypeAll<ComponentQuickEditWindow>();
            if (all.Length > 0)
            {
                var win = all[0];
                // Close any extras that shouldn't exist
                for (int i = 1; i < all.Length; i++) all[i].Close();

                win.SwitchTo(component);
                win.Focus();
                return;
            }

            var newWin = CreateInstance<ComponentQuickEditWindow>();
            newWin.minSize  = new Vector2(300f, 160f);
            newWin.position = new Rect(screenPos.x + 12f, screenPos.y - 40f, 340f, 420f);
            newWin.SwitchTo(component);
            newWin.ShowUtility();
        }

        // ── Lifecycle ─────────────────────────────────────────────────

        private void OnDestroy()
        {
            DestroyEditorIfNeeded();
        }

        private void OnGUI()
        {
            var component = EditorUtility.InstanceIDToObject(_componentId) as Component;

            if (component == null)
            {
                EditorGUILayout.HelpBox("Component no longer exists.", MessageType.Warning);
                if (GUILayout.Button("Close")) Close();
                return;
            }

            // Recreate editor if it got wiped (e.g. after domain reload)
            if (_editor == null) SwitchTo(component);

            DrawHeader(component);

            // _editor can become null if Close() was called inside DrawHeader
            if (_editor == null) return;

            EditorGUILayout.Space(2);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _editor.OnInspectorGUI();
            EditorGUILayout.EndScrollView();
        }

        // ── Drawing ───────────────────────────────────────────────────

        private void DrawHeader(Component component)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var icon   = EditorGUIUtility.ObjectContent(component, component.GetType()).image;
                var title  = ObjectNames.GetInspectorTitle(component);
                var goName = component.gameObject.name;

                // Enable/disable toggle — for any component that exposes a writable 'enabled'
                var enabledProp = component.GetType().GetProperty("enabled",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (enabledProp != null && enabledProp.CanWrite)
                {
                    bool current = (bool)enabledProp.GetValue(component);
                    EditorGUI.BeginChangeCheck();
                    bool next = GUILayout.Toggle(current, GUIContent.none,
                        EditorStyles.toggle, GUILayout.Width(16));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(component, next ? "Enable Component" : "Disable Component");
                        enabledProp.SetValue(component, next);
                        EditorUtility.SetDirty(component);
                    }
                }

                if (icon != null)
                    GUILayout.Label(new GUIContent(icon), GUILayout.Width(18), GUILayout.Height(18));

                GUILayout.Label(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"on: {goName}", EditorStyles.miniLabel);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────

        internal void SwitchTo(Component component)
        {
            DestroyEditorIfNeeded();
            _componentId        = component.GetInstanceID();
            _scroll             = Vector2.zero;
            _editor             = UnityEditor.Editor.CreateEditor(component);
            titleContent        = new GUIContent(
                ObjectNames.GetInspectorTitle(component),
                EditorGUIUtility.ObjectContent(component, component.GetType()).image);
            Repaint();
        }

        private void DestroyEditorIfNeeded()
        {
            if (_editor != null)
            {
                DestroyImmediate(_editor);
                _editor = null;
            }
        }
    }
}
