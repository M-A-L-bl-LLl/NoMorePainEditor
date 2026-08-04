using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Adds a Scene List selector immediately before Unity's Play Mode controls.
    /// A scene can be locked for an explicit Play Mode launch while regular Play uses the active scene.
    /// </summary>
    [InitializeOnLoad]
    internal static class ScenePlayToolbar
    {
        private const string ToolbarElementName = "NMP.ScenePlayToolbar";
        private const float ToolbarWidth = 228f;
        private const float ToolbarHeight = 22f;
        private const float LockedPlayButtonWidth = 34f;
        private const float ToolbarControlGap = 2f;

        private static readonly Type ToolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
        private static readonly FieldInfo ToolbarRootField = FindField(ToolbarType, "m_Root");

        private static IMGUIContainer _toolbarElement;
        private static string _selectedGuid;
        private static HashSet<string> _favoriteGuids;
        private static SceneAsset _selectedSceneAsset;
        private static EditorWindow _sceneListPopupWindow;
        private static bool _disabledStateApplied;

        [Serializable]
        private sealed class FavoriteData
        {
            public List<string> guids = new();
        }

        private readonly struct SceneEntry
        {
            public SceneEntry(string guid, string path, bool enabled, bool favorite)
            {
                Guid = guid;
                Path = path;
                Name = System.IO.Path.GetFileNameWithoutExtension(path);
                Enabled = enabled;
                Favorite = favorite;
            }

            public string Guid { get; }
            public string Path { get; }
            public string Name { get; }
            public bool Enabled { get; }
            public bool Favorite { get; }
        }

        static ScenePlayToolbar()
        {
            _selectedGuid = EditorPrefs.GetString(SelectedSceneKey, string.Empty);
            _favoriteGuids = LoadFavorites();

            EditorApplication.update += OnEditorUpdate;
            EditorBuildSettings.sceneListChanged += OnSceneListChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += Initialize;
        }

        private static string PreferencePrefix =>
            "NMP.ScenePlayToolbar." + Hash128.Compute(Application.dataPath);

        private static string SelectedSceneKey => PreferencePrefix + ".SelectedScene";
        private static string FavoritesKey => PreferencePrefix + ".Favorites";
        private static string LaunchingLockedSceneKey => PreferencePrefix + ".LaunchingLockedScene";

        [MenuItem("Assets/Add To Scene List", false, 2000)]
        private static void AddSelectedScenesToSceneList()
        {
            if (!NMPSettings.ScenePlayToolbar)
                return;

            List<string> selectedScenePaths = GetSelectedScenePaths();
            var scenes = EditorBuildSettings.scenes.ToList();
            var existingPaths = new HashSet<string>(
                scenes.Select(scene => scene.path),
                StringComparer.OrdinalIgnoreCase);

            int addedCount = 0;
            foreach (string path in selectedScenePaths)
            {
                if (!existingPaths.Add(path))
                    continue;

                scenes.Add(new EditorBuildSettingsScene(path, true));
                addedCount++;
            }

            if (addedCount == 0)
                return;

            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[NoMorePain] Added {addedCount} scene(s) to the active Scene List.");
        }

        [MenuItem("Assets/Add To Scene List", true)]
        private static bool CanAddSelectedScenesToSceneList()
        {
            if (!NMPSettings.ScenePlayToolbar)
                return false;

            var existingPaths = new HashSet<string>(
                EditorBuildSettings.scenes.Select(scene => scene.path),
                StringComparer.OrdinalIgnoreCase);

            return GetSelectedScenePaths().Any(path => !existingPaths.Contains(path));
        }

        private static List<string> GetSelectedScenePaths()
        {
            return Selection.objects
                .OfType<SceneAsset>()
                .Select(AssetDatabase.GetAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void Initialize()
        {
            if (!NMPSettings.ScenePlayToolbar)
            {
                DisableToolbar();
                return;
            }

            _disabledStateApplied = false;
            RefreshSelection();
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                ClearPlayModeStartScene();
        }

        private static void OnEditorUpdate()
        {
            EnsureToolbar();
        }

        private static void OnSceneListChanged()
        {
            if (!NMPSettings.ScenePlayToolbar)
                return;

            PruneFavorites();
            RefreshSelection();
            RepaintToolbar();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!NMPSettings.ScenePlayToolbar)
            {
                ClearPlayModeStartScene();
                return;
            }

            if (state == PlayModeStateChange.ExitingEditMode)
            {
                if (SessionState.GetBool(LaunchingLockedSceneKey, false))
                    TryApplyLockedPlayModeStartScene();
                else
                    EditorSceneManager.playModeStartScene = null;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                ClearPlayModeStartScene();
            }

            RepaintToolbar();
        }

        private static void EnsureToolbar()
        {
            if (!NMPSettings.ScenePlayToolbar)
            {
                if (!_disabledStateApplied)
                    DisableToolbar();
                return;
            }

            _disabledStateApplied = false;
            if (_toolbarElement != null && _toolbarElement.parent != null)
                return;

            if (ToolbarType == null || ToolbarRootField == null)
                return;

            UnityEngine.Object[] toolbars = Resources.FindObjectsOfTypeAll(ToolbarType);
            if (toolbars == null || toolbars.Length == 0)
                return;

            var root = ToolbarRootField.GetValue(toolbars[0]) as VisualElement;
            if (root == null)
                return;

            VisualElement playModeZone = root.Q<VisualElement>("ToolbarZonePlayMode");
            if (playModeZone == null)
                return;

            VisualElement staleElement = root.Q<VisualElement>(ToolbarElementName);
            if (staleElement != null)
                staleElement.RemoveFromHierarchy();

            _toolbarElement = new IMGUIContainer(DrawToolbar)
            {
                name = ToolbarElementName,
                pickingMode = PickingMode.Position
            };
            _toolbarElement.style.width = ToolbarWidth;
            _toolbarElement.style.minWidth = ToolbarWidth;
            _toolbarElement.style.maxWidth = ToolbarWidth;
            _toolbarElement.style.height = ToolbarHeight;
            _toolbarElement.style.marginRight = 4f;
            _toolbarElement.style.flexShrink = 0f;
            _toolbarElement.style.alignSelf = Align.Center;

            playModeZone.Insert(0, _toolbarElement);
        }

        internal static void RefreshEnabledState()
        {
            _disabledStateApplied = false;
            if (NMPSettings.ScenePlayToolbar)
                EnsureToolbar();
            else
                DisableToolbar();

            RepaintToolbar();
        }

        private static void DisableToolbar()
        {
            if (_sceneListPopupWindow != null)
            {
                EditorWindow popup = _sceneListPopupWindow;
                _sceneListPopupWindow = null;
                popup.Close();
            }

            if (_toolbarElement != null)
            {
                _toolbarElement.RemoveFromHierarchy();
                _toolbarElement = null;
            }

            if (ToolbarType != null && ToolbarRootField != null)
            {
                UnityEngine.Object[] toolbars = Resources.FindObjectsOfTypeAll(ToolbarType);
                foreach (UnityEngine.Object toolbar in toolbars)
                {
                    var root = ToolbarRootField.GetValue(toolbar) as VisualElement;
                    VisualElement staleElement = root?.Q<VisualElement>(ToolbarElementName);
                    staleElement?.RemoveFromHierarchy();
                }
            }

            ClearPlayModeStartScene();
            _disabledStateApplied = true;
        }

        private static void DrawToolbar()
        {
            if (!NMPSettings.ScenePlayToolbar)
                return;

            Rect toolbarRect = GUILayoutUtility.GetRect(
                ToolbarWidth,
                ToolbarHeight,
                GUILayout.Width(ToolbarWidth),
                GUILayout.Height(ToolbarHeight));
            float selectorWidth = toolbarRect.width - LockedPlayButtonWidth - ToolbarControlGap;
            var selectorRect = new Rect(toolbarRect.x, toolbarRect.y, selectorWidth, toolbarRect.height);
            var lockedPlayRect = new Rect(
                selectorRect.xMax + ToolbarControlGap,
                toolbarRect.y + (toolbarRect.height - 20f) * 0.5f,
                LockedPlayButtonWidth,
                20f);

            List<SceneEntry> scenes = GetSceneEntries();
            SceneEntry? lockedScene = FindSelected(scenes);
            string label = lockedScene.HasValue
                ? "Locked: " + lockedScene.Value.Name
                : (scenes.Count == 0 ? "Scene List is empty" : "No locked scene");
            string selectorTooltip = lockedScene.HasValue
                ? $"Locked Play Mode scene: {lockedScene.Value.Path}\nClick to open the Scene List."
                : "Open the Scene List and lock a scene for the dedicated Play button.";

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUI.Button(selectorRect, new GUIContent(label, selectorTooltip), EditorStyles.toolbarDropDown))
                    UnityEditor.PopupWindow.Show(selectorRect, new SceneListPopup());
            }

            string playTooltip = lockedScene.HasValue
                ? $"Play from locked scene: {lockedScene.Value.Path}"
                : "Lock a scene in the Scene List first.";
            bool lockedPlayDisabled =
                EditorApplication.isPlayingOrWillChangePlaymode || !lockedScene.HasValue;
            GUIStyle lockedPlayButtonStyle =
                GUI.skin.FindStyle("AppCommand")
                ?? EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle("AppCommand")
                ?? EditorStyles.toolbarButton;
            bool launchClicked;
            using (new EditorGUI.DisabledScope(lockedPlayDisabled))
            {
                launchClicked = GUI.Button(
                    lockedPlayRect,
                    new GUIContent(string.Empty, playTooltip),
                    lockedPlayButtonStyle);
            }

            if (Event.current.type == EventType.Repaint)
            {
                Texture lockedSceneIcon = EditorGUIUtility.IconContent("SceneAsset Icon").image;
                Texture playBadgeIcon = EditorGUIUtility.IconContent("PlayButton").image;
                Color previousColor = GUI.color;

                if (lockedSceneIcon != null)
                {
                    var sceneIconRect = new Rect(
                        lockedPlayRect.x + 5f,
                        lockedPlayRect.y + 2f,
                        16f,
                        16f);
                    GUI.color = lockedPlayDisabled
                        ? new Color(1f, 1f, 1f, 0.35f)
                        : Color.white;
                    GUI.DrawTexture(sceneIconRect, lockedSceneIcon, ScaleMode.ScaleToFit, true);
                }

                if (playBadgeIcon != null)
                {
                    var playBadgeRect = new Rect(
                        lockedPlayRect.x + 19f,
                        lockedPlayRect.y + 9f,
                        9f,
                        9f);
                    GUI.color = lockedPlayDisabled
                        ? new Color(0.45f, 0.75f, 0.50f, 0.30f)
                        : new Color(0.45f, 1f, 0.55f, 1f);
                    GUI.DrawTexture(playBadgeRect, playBadgeIcon, ScaleMode.ScaleToFit, true);
                }

                GUI.color = previousColor;
            }

            if (launchClicked)
            {
                LaunchLockedScene();
                GUIUtility.ExitGUI();
            }
        }
        private static List<SceneEntry> GetSceneEntries()
        {
            var entries = new List<SceneEntry>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (string.IsNullOrEmpty(scene.path) || !seenPaths.Add(scene.path))
                    continue;

                string guid = AssetDatabase.AssetPathToGUID(scene.path);
                if (string.IsNullOrEmpty(guid))
                    continue;

                entries.Add(new SceneEntry(guid, scene.path, scene.enabled, _favoriteGuids.Contains(guid)));
            }

            return entries
                .OrderBy(GetSceneGroup)
                .ThenBy(scene => GetSceneListIndex(scene.Path))
                .ToList();
        }

        private static int GetSceneGroup(SceneEntry scene)
        {
            if (scene.Guid == _selectedGuid)
                return 0;
            return scene.Favorite ? 1 : 2;
        }

        private static int GetSceneListIndex(string path)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (string.Equals(scenes[i].path, path, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return int.MaxValue;
        }

        private static SceneEntry? FindSelected(IReadOnlyList<SceneEntry> scenes)
        {
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].Guid == _selectedGuid)
                    return scenes[i];
            }

            return null;
        }

        private static void RefreshSelection()
        {
            List<SceneEntry> scenes = GetSceneEntries();
            if (FindSelected(scenes).HasValue)
            {
                LoadSelectedSceneAsset(scenes);
                RepaintToolbar();
                return;
            }

            _selectedGuid = string.Empty;
            _selectedSceneAsset = null;
            SaveSelectedGuid();
            RepaintToolbar();
        }
        private static void SelectAndOpenScene(SceneEntry scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            TryOpenScene(scene.Path);
        }

        private static void ToggleLockedScene(SceneEntry scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            bool unlock = scene.Guid == _selectedGuid;
            _selectedGuid = unlock ? string.Empty : scene.Guid;
            _selectedSceneAsset = unlock
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.Path);
            SaveSelectedGuid();
            ClearPlayModeStartScene();
            RepaintToolbar();
        }

        private static void LaunchLockedScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            SessionState.SetBool(LaunchingLockedSceneKey, true);
            if (!TryApplyLockedPlayModeStartScene())
            {
                SessionState.SetBool(LaunchingLockedSceneKey, false);
                return;
            }

            EditorApplication.isPlaying = true;
        }

        private static bool TryOpenScene(string path)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (string.Equals(activeScene.path, path, StringComparison.OrdinalIgnoreCase))
                return true;

            Scene loadedScene = SceneManager.GetSceneByPath(path);
            if (loadedScene.IsValid() && loadedScene.isLoaded)
                return SceneManager.SetActiveScene(loadedScene);

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            Scene openedScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return openedScene.IsValid() && openedScene.isLoaded;
        }

        private static void ToggleFavorite(string guid)
        {
            if (!_favoriteGuids.Add(guid))
                _favoriteGuids.Remove(guid);

            SaveFavorites();
            RepaintToolbar();
        }

        private static bool ReorderSceneWithinGroup(string guid, int insertIndex)
        {
            List<SceneEntry> entries = GetSceneEntries();
            int sourceEntryIndex = entries.FindIndex(scene => scene.Guid == guid);
            if (sourceEntryIndex < 0)
                return false;

            SceneEntry source = entries[sourceEntryIndex];
            List<SceneEntry> group = entries
                .Where(scene => GetSceneGroup(scene) == GetSceneGroup(source))
                .ToList();
            int sourceGroupIndex = group.FindIndex(scene => scene.Guid == guid);
            if (sourceGroupIndex < 0)
                return false;

            int targetIndex = Mathf.Clamp(insertIndex, 0, group.Count);
            if (sourceGroupIndex < targetIndex)
                targetIndex--;
            if (targetIndex == sourceGroupIndex)
                return false;

            group.RemoveAt(sourceGroupIndex);
            group.Insert(targetIndex, source);

            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            var groupPaths = new HashSet<string>(
                group.Select(scene => scene.Path),
                StringComparer.OrdinalIgnoreCase);
            var enabledByPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (EditorBuildSettingsScene buildScene in buildScenes)
            {
                if (!enabledByPath.ContainsKey(buildScene.path))
                    enabledByPath.Add(buildScene.path, buildScene.enabled);
            }

            int groupIndex = 0;
            for (int i = 0; i < buildScenes.Length; i++)
            {
                if (!groupPaths.Contains(buildScenes[i].path))
                    continue;

                SceneEntry reorderedScene = group[groupIndex++];
                buildScenes[i] = new EditorBuildSettingsScene(
                    reorderedScene.Path,
                    enabledByPath[reorderedScene.Path]);
            }

            EditorBuildSettings.scenes = buildScenes;
            return true;
        }

        private static void LoadSelectedSceneAsset(IReadOnlyList<SceneEntry> scenes)
        {
            SceneEntry? selected = FindSelected(scenes);
            _selectedSceneAsset = selected.HasValue
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(selected.Value.Path)
                : null;
        }

        private static bool TryApplyLockedPlayModeStartScene()
        {
            if (_selectedSceneAsset == null && !string.IsNullOrEmpty(_selectedGuid))
                LoadSelectedSceneAsset(GetSceneEntries());

            if (_selectedSceneAsset == null)
                return false;

            EditorSceneManager.playModeStartScene = _selectedSceneAsset;
            return true;
        }

        private static void ClearPlayModeStartScene()
        {
            SessionState.SetBool(LaunchingLockedSceneKey, false);
            if (EditorSceneManager.playModeStartScene != null)
                EditorSceneManager.playModeStartScene = null;
        }

        private static void SaveSelectedGuid()
        {
            EditorPrefs.SetString(SelectedSceneKey, _selectedGuid ?? string.Empty);
        }

        private static HashSet<string> LoadFavorites()
        {
            string json = EditorPrefs.GetString(FavoritesKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new HashSet<string>(StringComparer.Ordinal);

            try
            {
                FavoriteData data = JsonUtility.FromJson<FavoriteData>(json);
                return new HashSet<string>(
                    data?.guids?.Where(guid => !string.IsNullOrEmpty(guid)) ?? Enumerable.Empty<string>(),
                    StringComparer.Ordinal);
            }
            catch
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        private static void SaveFavorites()
        {
            var data = new FavoriteData { guids = _favoriteGuids.OrderBy(guid => guid).ToList() };
            EditorPrefs.SetString(FavoritesKey, JsonUtility.ToJson(data));
        }

        private static void PruneFavorites()
        {
            var sceneGuids = new HashSet<string>(
                EditorBuildSettings.scenes
                    .Select(scene => AssetDatabase.AssetPathToGUID(scene.path))
                    .Where(guid => !string.IsNullOrEmpty(guid)),
                StringComparer.Ordinal);

            if (_favoriteGuids.RemoveWhere(guid => !sceneGuids.Contains(guid)) > 0)
                SaveFavorites();
        }

        private static void RepaintToolbar()
        {
            _toolbarElement?.MarkDirtyRepaint();
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;

                type = type.BaseType;
            }

            return null;
        }

        private sealed class SceneListPopup : PopupWindowContent
        {
            private const float PopupWidth = 370f;
            private const float HeaderHeight = 23f;
            private const float SectionHeight = 19f;
            private const float RowHeight = 38f;
            private const float MaximumHeight = 430f;

            private const int RowDragControlHint = 0x4E4D5053;
            private const float DragStartDistance = 4f;

            private readonly List<SceneRowLayout> _rowLayouts = new();
            private Vector2 _scrollPosition;
            private Vector2 _dragStartMousePosition;
            private string _pressedSceneGuid;
            private bool _isDraggingScene;
            private int _dragInsertIndex = -1;
            private int _rowDragControlId;

            private readonly struct SceneRowLayout
            {
                public SceneRowLayout(SceneEntry scene, Rect rowRect, Rect sceneRect)
                {
                    Scene = scene;
                    RowRect = rowRect;
                    SceneRect = sceneRect;
                }

                public SceneEntry Scene { get; }
                public Rect RowRect { get; }
                public Rect SceneRect { get; }
            }

            public override void OnOpen()
            {
                if (!NMPSettings.ScenePlayToolbar)
                {
                    editorWindow?.Close();
                    return;
                }

                _sceneListPopupWindow = editorWindow;
                if (editorWindow != null)
                    editorWindow.wantsMouseMove = true;
            }

            public override void OnClose()
            {
                if (_sceneListPopupWindow == editorWindow)
                    _sceneListPopupWindow = null;

                if (_rowDragControlId != 0 && GUIUtility.hotControl == _rowDragControlId)
                    GUIUtility.hotControl = 0;

                ClearDragState();
            }

            public override Vector2 GetWindowSize()
            {
                List<SceneEntry> scenes = GetSceneEntries();
                bool hasLocked = scenes.Any(scene => scene.Guid == _selectedGuid);
                bool hasFavorites = scenes.Any(scene => scene.Guid != _selectedGuid && scene.Favorite);
                bool hasRegular = scenes.Any(scene => scene.Guid != _selectedGuid && !scene.Favorite);
                int sectionCount = (hasLocked ? 1 : 0) + (hasFavorites ? 1 : 0) + (hasRegular ? 1 : 0);
                float contentHeight = HeaderHeight + sectionCount * SectionHeight + scenes.Count * RowHeight + 8f;
                return new Vector2(PopupWidth, Mathf.Clamp(contentHeight, 76f, MaximumHeight));
            }

            public override void OnGUI(Rect rect)
            {
                if (Event.current.type == EventType.MouseMove)
                    editorWindow?.Repaint();

                _rowLayouts.Clear();
                int rowDragControlId = GUIUtility.GetControlID(RowDragControlHint, FocusType.Passive);
                DrawHeader();

                List<SceneEntry> scenes = GetSceneEntries();
                if (scenes.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "The active Scene List is empty. Add scenes in File > Build Profiles.",
                        MessageType.Info);
                    return;
                }

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                List<SceneEntry> locked = scenes
                    .Where(scene => scene.Guid == _selectedGuid)
                    .ToList();
                List<SceneEntry> favorites = scenes
                    .Where(scene => scene.Guid != _selectedGuid && scene.Favorite)
                    .ToList();
                List<SceneEntry> others = scenes
                    .Where(scene => scene.Guid != _selectedGuid && !scene.Favorite)
                    .ToList();

                DrawSection("Locked", locked);
                DrawSection("Favorites", favorites);
                DrawSection("Scenes", others);

                HandleSceneDrag(rowDragControlId, scenes);
                EditorGUILayout.EndScrollView();
            }

            private static void DrawHeader()
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label("Play Mode Scene", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Scene List", EditorStyles.miniLabel);
                }
            }

            private void DrawSection(string title, IReadOnlyList<SceneEntry> scenes)
            {
                if (scenes.Count == 0)
                    return;

                Rect sectionRect = GUILayoutUtility.GetRect(1f, SectionHeight, GUILayout.ExpandWidth(true));
                sectionRect.xMin += 7f;
                Texture sectionIcon = title == "Favorites"
                    ? EditorGUIUtility.IconContent("Favorite").image
                    : null;
                GUI.Label(sectionRect, new GUIContent(title, sectionIcon), EditorStyles.miniBoldLabel);

                for (int i = 0; i < scenes.Count; i++)
                    DrawSceneRow(scenes[i]);
            }

            private void DrawSceneRow(SceneEntry scene)
            {
                Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
                rowRect.xMin += 4f;
                rowRect.xMax -= 4f;

                Scene activeScene = SceneManager.GetActiveScene();
                bool active = string.Equals(scene.Path, activeScene.path, StringComparison.OrdinalIgnoreCase);
                bool hovered = rowRect.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint)
                {
                    if (active)
                        EditorGUI.DrawRect(rowRect, GetSelectedColor());
                    else if (hovered)
                        EditorGUI.DrawRect(rowRect, GetHoverColor());

                    if (_isDraggingScene && scene.Guid == _pressedSceneGuid)
                        EditorGUI.DrawRect(rowRect, new Color(0.18f, 0.48f, 0.82f, 0.20f));
                }

                Rect favoriteRect = new Rect(rowRect.xMax - 36f, rowRect.y + 2f, 32f, 34f);
                Rect lockRect = new Rect(favoriteRect.xMin - 36f, rowRect.y + 2f, 32f, 34f);
                Rect sceneRect = rowRect;
                sceneRect.xMax = lockRect.xMin - 2f;
                bool favoriteHovered = favoriteRect.Contains(Event.current.mousePosition);
                bool lockHovered = lockRect.Contains(Event.current.mousePosition);
                bool controlsDisabled = EditorApplication.isPlayingOrWillChangePlaymode;
                bool isLocked = scene.Guid == _selectedGuid;

                _rowLayouts.Add(new SceneRowLayout(scene, rowRect, sceneRect));

                using (new EditorGUI.DisabledScope(controlsDisabled))
                {
                    string lockTooltip = isLocked
                        ? "Unlock Play Mode scene"
                        : "Lock this scene for the dedicated Play button";
                    if (GUI.Button(
                            lockRect,
                            new GUIContent(string.Empty, lockTooltip),
                            GUIStyle.none))
                    {
                        ToggleLockedScene(scene);
                        editorWindow?.Repaint();
                        GUIUtility.ExitGUI();
                    }

                    string favoriteTooltip = scene.Favorite ? "Remove from favorites" : "Add to favorites";
                    if (GUI.Button(favoriteRect, new GUIContent(string.Empty, favoriteTooltip), GUIStyle.none))
                    {
                        ToggleFavorite(scene.Guid);
                        editorWindow?.Repaint();
                        GUIUtility.ExitGUI();
                    }
                }

                if (Event.current.type == EventType.Repaint)
                    DrawLockIcon(lockRect, isLocked, lockHovered, controlsDisabled);

                Texture favoriteIcon = EditorGUIUtility.IconContent(
                    scene.Favorite ? "Favorite On Icon" : "Favorite Icon").image;
                if (Event.current.type == EventType.Repaint && favoriteIcon != null)
                {
                    float favoriteIconSize = favoriteHovered ? 28f : 26f;
                    var favoriteIconRect = new Rect(
                        favoriteRect.center.x - favoriteIconSize * 0.5f,
                        favoriteRect.center.y - favoriteIconSize * 0.5f,
                        favoriteIconSize,
                        favoriteIconSize);

                    Color previousColor = GUI.color;
                    Color favoriteColor;
                    if (scene.Favorite)
                    {
                        favoriteColor = favoriteHovered
                            ? new Color(1f, 0.82f, 0.30f, 1f)
                            : new Color(1f, 0.68f, 0.10f, 1f);
                    }
                    else if (EditorGUIUtility.isProSkin)
                    {
                        favoriteColor = favoriteHovered
                            ? new Color(1f, 1f, 1f, 0.95f)
                            : new Color(1f, 1f, 1f, 0.55f);
                    }
                    else
                    {
                        favoriteColor = favoriteHovered
                            ? new Color(0.20f, 0.20f, 0.20f, 0.95f)
                            : new Color(0.20f, 0.20f, 0.20f, 0.55f);
                    }

                    if (controlsDisabled)
                        favoriteColor.a *= 0.45f;

                    GUI.color = favoriteColor;
                    GUI.DrawTexture(favoriteIconRect, favoriteIcon, ScaleMode.ScaleToFit, true);
                    GUI.color = previousColor;
                }

                Texture sceneIcon = EditorGUIUtility.IconContent("SceneAsset Icon").image;
                Rect iconRect = new Rect(sceneRect.x + 6f, sceneRect.y + 10f, 18f, 18f);
                if (sceneIcon != null)
                    GUI.DrawTexture(iconRect, sceneIcon, ScaleMode.ScaleToFit, true);

                Rect nameRect = new Rect(iconRect.xMax + 5f, sceneRect.y + 3f, sceneRect.width - 35f, 18f);
                Rect pathRect = new Rect(nameRect.x, sceneRect.y + 20f, nameRect.width, 15f);
                GUI.Label(nameRect, scene.Name, EditorStyles.label);

                string pathLabel = scene.Enabled ? scene.Path : scene.Path + "  (disabled)";
                GUIStyle pathStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    clipping = TextClipping.Clip
                };
                if (!scene.Enabled)
                    pathStyle.normal.textColor = Color.gray;
                GUI.Label(pathRect, new GUIContent(pathLabel, scene.Path), pathStyle);
            }

            private static void DrawLockIcon(
                Rect hitRect,
                bool isLocked,
                bool hovered,
                bool disabled)
            {
                float iconSize = hovered ? 28f : 26f;
                float scale = iconSize / 28f;
                Vector2 center = hitRect.center;
                var bodyRect = new Rect(
                    center.x - 8.5f * scale,
                    center.y - 0.5f * scale,
                    17f * scale,
                    11.5f * scale);

                Color lockColor;
                if (EditorGUIUtility.isProSkin)
                {
                    lockColor = isLocked
                        ? new Color(1f, 1f, 1f, hovered ? 1f : 0.92f)
                        : new Color(1f, 1f, 1f, hovered ? 0.74f : 0.46f);
                }
                else
                {
                    lockColor = isLocked
                        ? new Color(0.12f, 0.12f, 0.12f, hovered ? 1f : 0.90f)
                        : new Color(0.12f, 0.12f, 0.12f, hovered ? 0.72f : 0.44f);
                }

                if (disabled)
                    lockColor.a *= 0.45f;

                float leftPostX = center.x - 5.5f * scale;
                float rightPostX = center.x + 5.5f * scale;
                Vector3[] shacklePoints = isLocked
                    ? new[]
                    {
                        new Vector3(leftPostX, bodyRect.y + 1.5f * scale),
                        new Vector3(leftPostX, center.y - 5.5f * scale),
                        new Vector3(center.x - 4.2f * scale, center.y - 8.5f * scale),
                        new Vector3(center.x, center.y - 10f * scale),
                        new Vector3(center.x + 4.2f * scale, center.y - 8.5f * scale),
                        new Vector3(rightPostX, center.y - 5.5f * scale),
                        new Vector3(rightPostX, bodyRect.y + 1.5f * scale)
                    }
                    : new[]
                    {
                        new Vector3(leftPostX, bodyRect.y + 1.5f * scale),
                        new Vector3(leftPostX, center.y - 5.5f * scale),
                        new Vector3(center.x - 4.2f * scale, center.y - 8.5f * scale),
                        new Vector3(center.x, center.y - 10f * scale),
                        new Vector3(center.x + 4.8f * scale, center.y - 8.2f * scale),
                        new Vector3(center.x + 7f * scale, center.y - 5.2f * scale),
                        new Vector3(center.x + 7f * scale, bodyRect.y - 4f * scale)
                    };
                float corner = 2f * scale;
                var bodyPoints = new[]
                {
                    new Vector3(bodyRect.x + corner, bodyRect.y),
                    new Vector3(bodyRect.xMax - corner, bodyRect.y),
                    new Vector3(bodyRect.xMax, bodyRect.y + corner),
                    new Vector3(bodyRect.xMax, bodyRect.yMax - corner),
                    new Vector3(bodyRect.xMax - corner, bodyRect.yMax),
                    new Vector3(bodyRect.x + corner, bodyRect.yMax),
                    new Vector3(bodyRect.x, bodyRect.yMax - corner),
                    new Vector3(bodyRect.x, bodyRect.y + corner)
                };

                Color previousHandlesColor = Handles.color;
                Handles.BeginGUI();
                Handles.color = lockColor;
                Handles.DrawAAPolyLine(2.6f * scale, shacklePoints);
                Handles.DrawAAConvexPolygon(bodyPoints);

                if (isLocked)
                {
                    Color keyholeColor = EditorGUIUtility.isProSkin
                        ? new Color(0.14f, 0.14f, 0.14f, lockColor.a)
                        : new Color(0.94f, 0.94f, 0.94f, lockColor.a);
                    Handles.color = keyholeColor;
                    Handles.DrawSolidDisc(
                        new Vector3(center.x, bodyRect.y + 4f * scale),
                        Vector3.forward,
                        1.45f * scale);
                    Handles.DrawAAPolyLine(
                        1.8f * scale,
                        new Vector3(center.x, bodyRect.y + 4.5f * scale),
                        new Vector3(center.x, bodyRect.y + 8f * scale));
                }

                Handles.color = previousHandlesColor;
                Handles.EndGUI();
            }
            private void HandleSceneDrag(int controlId, IReadOnlyList<SceneEntry> scenes)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                MouseCursor cursor = _isDraggingScene ? MouseCursor.MoveArrow : MouseCursor.Pan;
                for (int i = 0; i < _rowLayouts.Count; i++)
                    EditorGUIUtility.AddCursorRect(_rowLayouts[i].SceneRect, cursor);

                Event evt = Event.current;
                if (evt.type == EventType.Repaint && _isDraggingScene && _dragInsertIndex >= 0)
                    DrawDragInsertionMarker();

                EventType controlEvent = evt.GetTypeForControl(controlId);
                if (controlEvent == EventType.MouseDown && evt.button == 0)
                {
                    SceneRowLayout? hitRow = FindSceneRow(evt.mousePosition);
                    if (!hitRow.HasValue)
                        return;

                    _rowDragControlId = controlId;
                    GUIUtility.hotControl = controlId;
                    _pressedSceneGuid = hitRow.Value.Scene.Guid;
                    _dragStartMousePosition = evt.mousePosition;
                    _isDraggingScene = false;
                    _dragInsertIndex = -1;
                    evt.Use();
                    return;
                }

                if (GUIUtility.hotControl != controlId || _pressedSceneGuid == null)
                    return;

                if (controlEvent == EventType.MouseDrag)
                {
                    if (!_isDraggingScene &&
                        (evt.mousePosition - _dragStartMousePosition).sqrMagnitude >=
                        DragStartDistance * DragStartDistance)
                    {
                        _isDraggingScene = true;
                    }

                    if (_isDraggingScene)
                        _dragInsertIndex = CalculateDragInsertIndex(evt.mousePosition.y);

                    editorWindow?.Repaint();
                    evt.Use();
                    return;
                }

                if (controlEvent != EventType.MouseUp || evt.button != 0)
                    return;

                bool wasDragging = _isDraggingScene;
                string pressedGuid = _pressedSceneGuid;
                int insertIndex = _dragInsertIndex;
                SceneRowLayout? releasedRow = FindSceneRow(evt.mousePosition);

                GUIUtility.hotControl = 0;
                _rowDragControlId = 0;
                ClearDragState();
                evt.Use();

                if (wasDragging)
                {
                    if (insertIndex >= 0)
                        ReorderSceneWithinGroup(pressedGuid, insertIndex);

                    editorWindow?.Repaint();
                    GUIUtility.ExitGUI();
                    return;
                }

                if (!releasedRow.HasValue || releasedRow.Value.Scene.Guid != pressedGuid)
                    return;

                int sceneIndex = scenes.ToList().FindIndex(scene => scene.Guid == pressedGuid);
                if (sceneIndex < 0)
                    return;

                SelectAndOpenScene(scenes[sceneIndex]);
                editorWindow?.Close();
                GUIUtility.ExitGUI();
            }

            private SceneRowLayout? FindSceneRow(Vector2 mousePosition)
            {
                for (int i = 0; i < _rowLayouts.Count; i++)
                {
                    if (_rowLayouts[i].SceneRect.Contains(mousePosition))
                        return _rowLayouts[i];
                }

                return null;
            }

            private int CalculateDragInsertIndex(float mouseY)
            {
                SceneRowLayout? sourceRow = null;
                for (int i = 0; i < _rowLayouts.Count; i++)
                {
                    if (_rowLayouts[i].Scene.Guid == _pressedSceneGuid)
                    {
                        sourceRow = _rowLayouts[i];
                        break;
                    }
                }

                if (!sourceRow.HasValue)
                    return -1;

                List<SceneRowLayout> groupRows = _rowLayouts
                    .Where(row => GetSceneGroup(row.Scene) == GetSceneGroup(sourceRow.Value.Scene))
                    .ToList();
                for (int i = 0; i < groupRows.Count; i++)
                {
                    if (mouseY < groupRows[i].RowRect.center.y)
                        return i;
                }

                return groupRows.Count;
            }

            private void DrawDragInsertionMarker()
            {
                SceneRowLayout? sourceRow = null;
                for (int i = 0; i < _rowLayouts.Count; i++)
                {
                    if (_rowLayouts[i].Scene.Guid == _pressedSceneGuid)
                    {
                        sourceRow = _rowLayouts[i];
                        break;
                    }
                }

                if (!sourceRow.HasValue)
                    return;

                List<SceneRowLayout> groupRows = _rowLayouts
                    .Where(row => GetSceneGroup(row.Scene) == GetSceneGroup(sourceRow.Value.Scene))
                    .ToList();
                if (groupRows.Count == 0)
                    return;

                int insertIndex = Mathf.Clamp(_dragInsertIndex, 0, groupRows.Count);
                float lineY = insertIndex < groupRows.Count
                    ? groupRows[insertIndex].RowRect.yMin
                    : groupRows[groupRows.Count - 1].RowRect.yMax;
                Rect firstRect = groupRows[0].RowRect;
                var lineRect = new Rect(firstRect.xMin + 2f, lineY - 1f, firstRect.width - 4f, 2f);
                EditorGUI.DrawRect(lineRect, new Color(0.20f, 0.55f, 1f, 1f));
            }

            private void ClearDragState()
            {
                _pressedSceneGuid = null;
                _isDraggingScene = false;
                _dragInsertIndex = -1;
            }

            private static Color GetSelectedColor()
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.20f, 0.42f, 0.66f, 0.75f)
                    : new Color(0.24f, 0.50f, 0.83f, 0.45f);
            }

            private static Color GetHoverColor()
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.07f)
                    : new Color(0f, 0f, 0f, 0.06f);
            }
        }
    }
}
