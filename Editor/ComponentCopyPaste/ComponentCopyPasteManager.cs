using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Component Copy/Paste - Adds a toolbar to the Inspector header for batch component operations.
    /// "Copy" opens a picker popup to select which components to copy.
    /// "Paste (N)" pastes all copied components to every selected GameObject.
    /// Supports adding new components and overwriting existing ones, with full Undo.
    /// </summary>
    [InitializeOnLoad]
    internal static class ComponentCopyPasteManager
    {
        internal static readonly List<ComponentClipboardEntry> Clipboard = new List<ComponentClipboardEntry>();

        static ComponentCopyPasteManager()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnInspectorHeaderGUI;
        }

        private static void OnInspectorHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.target is not GameObject go) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                // Copy button - opens component picker popup
                var copyRect = GUILayoutUtility.GetRect(
                    new GUIContent("Copy Components"),
                    EditorStyles.miniButton,
                    GUILayout.Width(118));

                if (GUI.Button(copyRect, new GUIContent("Copy Components", "Select components to copy to clipboard"), EditorStyles.miniButton))
                    PopupWindow.Show(copyRect, new ComponentPickerPopup(go));

                // Paste button - visible when clipboard has data
                if (Clipboard.Count > 0)
                {
                    var pasteLabel = $"Paste ({Clipboard.Count})";
                    var pasteTooltip = BuildPasteTooltip();

                    if (GUILayout.Button(new GUIContent(pasteLabel, pasteTooltip), EditorStyles.miniButton, GUILayout.Width(70)))
                    {
                        var targets = Selection.gameObjects;
                        if (targets.Length == 0) targets = new[] { go };
                        PasteComponents(targets);
                    }

                    var prevColor = GUI.color;
                    GUI.color = new Color(1f, 0.35f, 0.35f);
                    if (GUILayout.Button(new GUIContent("✕", "Clear component clipboard"), EditorStyles.miniButton, GUILayout.Width(20)))
                        Clipboard.Clear();
                    GUI.color = prevColor;
                }
            }
        }

        private static string BuildPasteTooltip()
        {
            var sb = new System.Text.StringBuilder("Paste to selected GameObjects:\n");
            foreach (var entry in Clipboard)
                sb.AppendLine($"  • {entry.displayName}");
            sb.Append("\nHolds existing component: overwrites values.\nMissing component: adds new.");
            return sb.ToString();
        }

        internal static void PasteComponents(GameObject[] targets)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Paste Components");

            int pastedComponents = 0;

            foreach (var target in targets)
            {
                foreach (var entry in Clipboard)
                {
                    var type = Type.GetType(entry.typeName);
                    if (type == null)
                    {
                        Debug.LogWarning($"[NoMorePain] Could not find type '{entry.typeName}'. Skipping.");
                        continue;
                    }

                    var existing = target.GetComponent(type);
                    if (existing != null)
                    {
                        // Overwrite values on existing component
                        Undo.RecordObject(existing, "Paste Component Values");
                        try
                        {
                            EditorJsonUtility.FromJsonOverwrite(entry.json, existing);
                            pastedComponents++;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[NoMorePain] Failed to paste {entry.displayName} on '{target.name}': {e.Message}");
                        }
                    }
                    else
                    {
                        // Add new component and apply values
                        var newComp = Undo.AddComponent(target, type);
                        if (newComp != null)
                        {
                            try
                            {
                                EditorJsonUtility.FromJsonOverwrite(entry.json, newComp);
                                pastedComponents++;
                            }
                            catch (Exception e)
                            {
                                Debug.LogWarning($"[NoMorePain] Failed to initialize {entry.displayName} on '{target.name}': {e.Message}");
                            }
                        }
                    }

                    EditorUtility.SetDirty(target);
                }
            }

            if (pastedComponents > 0)
                Debug.Log($"[NoMorePain] Pasted {pastedComponents} component(s) to {targets.Length} object(s). (Ctrl+Z to undo)");
        }
    }

    internal class ComponentClipboardEntry
    {
        public string typeName;
        public string displayName;
        public string json;
    }

    /// <summary>
    /// Popup that lists all components on a GameObject with checkboxes for selection.
    /// </summary>
    internal class ComponentPickerPopup : PopupWindowContent
    {
        private readonly GameObject _target;
        private readonly Component[] _components;
        private readonly bool[] _selected;
        private Vector2 _scroll;

        public ComponentPickerPopup(GameObject target)
        {
            _target = target;
            _components = target.GetComponents<Component>();
            _selected = new bool[_components.Length];

            // Pre-select everything except Transform
            for (int i = 0; i < _components.Length; i++)
                _selected[i] = _components[i] != null && _components[i] is not Transform;
        }

        public override Vector2 GetWindowSize()
        {
            float height = EditorGUIUtility.singleLineHeight * (_components.Length + 5) + 60;
            return new Vector2(260, Mathf.Clamp(height, 120, 350));
        }

        public override void OnGUI(Rect rect)
        {
            EditorGUILayout.LabelField($"Copy from: {_target.name}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Select components:", EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            for (int i = 0; i < _components.Length; i++)
            {
                if (_components[i] == null) continue;
                var name = ObjectNames.GetInspectorTitle(_components[i]);
                _selected[i] = EditorGUILayout.ToggleLeft(name, _selected[i]);
            }

            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", EditorStyles.miniButton))
                    for (int i = 0; i < _selected.Length; i++) _selected[i] = true;
                if (GUILayout.Button("None", EditorStyles.miniButton))
                    for (int i = 0; i < _selected.Length; i++) _selected[i] = false;
            }

            GUILayout.Space(2);

            int count = CountSelected();
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button($"Copy {(count > 0 ? count.ToString() : "")} Component(s)", EditorStyles.miniButton))
                {
                    CopySelected();
                    editorWindow.Close();
                }
            }
        }

        private int CountSelected()
        {
            int n = 0;
            for (int i = 0; i < _selected.Length; i++)
                if (_selected[i]) n++;
            return n;
        }

        private void CopySelected()
        {
            ComponentCopyPasteManager.Clipboard.Clear();

            for (int i = 0; i < _components.Length; i++)
            {
                if (!_selected[i] || _components[i] == null) continue;

                try
                {
                    ComponentCopyPasteManager.Clipboard.Add(new ComponentClipboardEntry
                    {
                        typeName = _components[i].GetType().AssemblyQualifiedName,
                        displayName = ObjectNames.GetInspectorTitle(_components[i]),
                        json = EditorJsonUtility.ToJson(_components[i])
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NoMorePain] Could not copy {ObjectNames.GetInspectorTitle(_components[i])}: {e.Message}");
                }
            }

            if (ComponentCopyPasteManager.Clipboard.Count > 0)
                Debug.Log($"[NoMorePain] Copied {ComponentCopyPasteManager.Clipboard.Count} component(s) from '{_target.name}'.");
        }
    }
}
