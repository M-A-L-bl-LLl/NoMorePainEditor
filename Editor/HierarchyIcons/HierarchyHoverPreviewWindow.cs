using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    /// <summary>
    /// Shows an on-hover mesh preview for hierarchy rows while Alt is pressed.
    /// </summary>
    internal sealed class HierarchyHoverPreviewWindow : EditorWindow
    {
        private const float WindowW = 320f;
        private const float WindowH = 220f;
        private const float HoverHideDelay = 0.12f;
        private const float PreviewPadding = 1.18f;

        private static HierarchyHoverPreviewWindow _window;
        private static double _lastHoverRequestTime;
        private static EditorWindow _ownerWindow;

        private GameObject _source;
        private PreviewRenderUtility _preview;
        private Mesh _mesh;
        private Material[] _materials;
        private readonly List<Material> _tempMaterials = new();
        private Bounds _meshBounds;
        private bool _hasPreview;

        static HierarchyHoverPreviewWindow()
        {
            EditorApplication.update += Tick;
        }

        internal static void HandleHierarchyRow(GameObject go, Rect rowRect)
        {
            var evt = Event.current;
            if (evt == null) return;
            if (evt.type != EventType.Repaint &&
                evt.type != EventType.Layout &&
                evt.type != EventType.MouseMove)
                return;

            // Alt released while hovering a row -> close immediately.
            if (!evt.alt)
            {
                if (rowRect.Contains(evt.mousePosition))
                    HidePreview();
                return;
            }

            if (!rowRect.Contains(evt.mousePosition)) return;
            if (!TryGetSelfMeshData(go, out _, out _))
            {
                HidePreview();
                return;
            }

            var mw = EditorWindow.mouseOverWindow;
            if (mw != null && mw.GetType().Name == "SceneHierarchyWindow")
                _ownerWindow = mw;

            var screenPos = GUIUtility.GUIToScreenPoint(evt.mousePosition);
            ShowFor(go, screenPos);
        }

        internal static void HideIfShown() => HidePreview();

        private static void ShowFor(GameObject source, Vector2 mouseScreenPos)
        {
            if (source == null) return;

            _lastHoverRequestTime = EditorApplication.timeSinceStartup;

            if (_window == null)
            {
                _window = CreateInstance<HierarchyHoverPreviewWindow>();
                _window.titleContent = GUIContent.none;
                if (!TryShowAsTooltip(_window))
                    _window.ShowPopup();
            }

            _window.minSize = new Vector2(WindowW, WindowH);
            _window.maxSize = new Vector2(WindowW, WindowH);
            _window.position = ComputeWindowRect(mouseScreenPos);
            _window.SetTarget(source);
            _window.Repaint();

            if (_ownerWindow != null)
            {
                try { _ownerWindow.Focus(); }
                catch { /* owner may be recreated */ }
            }
        }

        private static Rect ComputeWindowRect(Vector2 screenPos)
        {
            float x = screenPos.x + 18f;
            float y = screenPos.y + 18f;
            float maxX = Mathf.Max(0f, Screen.currentResolution.width - WindowW);
            float maxY = Mathf.Max(0f, Screen.currentResolution.height - WindowH);
            x = Mathf.Clamp(x, 0f, maxX);
            y = Mathf.Clamp(y, 0f, maxY);
            return new Rect(x, y, WindowW, WindowH);
        }

        private static bool TryShowAsTooltip(EditorWindow window)
        {
            if (window == null) return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            try
            {
                var showTooltip = typeof(EditorWindow).GetMethod("ShowTooltip", flags, null, Type.EmptyTypes, null);
                if (showTooltip != null)
                {
                    showTooltip.Invoke(window, null);
                    return true;
                }

                var popupWithMode = typeof(EditorWindow).GetMethod("ShowPopupWithMode", flags);
                if (popupWithMode == null) return false;

                var p = popupWithMode.GetParameters();
                if (p.Length == 0 || !p[0].ParameterType.IsEnum) return false;

                var args = new object[p.Length];
                args[0] = Enum.ToObject(p[0].ParameterType, 6); // Tooltip mode
                for (int i = 1; i < p.Length; i++)
                {
                    if (p[i].ParameterType == typeof(bool)) args[i] = false;
                    else if (p[i].ParameterType.IsValueType) args[i] = Activator.CreateInstance(p[i].ParameterType);
                    else args[i] = null;
                }

                popupWithMode.Invoke(window, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Tick()
        {
            if (_window == null) return;

            // Keep hierarchy GUI active so hover state updates even without clicks.
            EditorApplication.RepaintHierarchyWindow();

            if (EditorApplication.timeSinceStartup - _lastHoverRequestTime > HoverHideDelay)
                HidePreview();
        }

        private static void HidePreview()
        {
            if (_window == null) return;
            _window.Close();
            _window = null;
        }

        private void SetTarget(GameObject source)
        {
            if (_source == source) return;
            _source = source;
            RebuildPreviewData();
        }

        private void RebuildPreviewData()
        {
            CleanupPreviewData();
            if (_source == null) return;

            if (!TryGetSelfMeshData(_source, out var mesh, out var sourceMaterials))
            {
                _hasPreview = false;
                return;
            }

            _mesh = mesh;
            _meshBounds = _mesh.bounds;
            if (_meshBounds.size.sqrMagnitude <= 0.0001f)
                _meshBounds = new Bounds(Vector3.zero, Vector3.one);

            _preview = new PreviewRenderUtility();
            _preview.camera.fieldOfView = 30f;
            _preview.camera.clearFlags = CameraClearFlags.Color;
            _preview.camera.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.17f, 0.17f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f);
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 500f;
            _preview.lights[0].intensity = 1.0f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(35f, 35f, 0f);
            _preview.lights[1].intensity = 1.0f;
            _preview.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
            _preview.ambientColor = new Color(0.35f, 0.35f, 0.35f, 1f);

            _materials = BuildPreviewMaterials(sourceMaterials, _mesh.subMeshCount);
            _hasPreview = _materials != null && _materials.Length > 0;
        }

        private static bool TryGetSelfMeshData(GameObject go, out Mesh mesh, out Material[] materials)
        {
            mesh = null;
            materials = null;
            if (go == null) return false;

            var skinned = go.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null)
            {
                mesh = skinned.sharedMesh;
                materials = skinned.sharedMaterials;
                return true;
            }

            var meshFilter = go.GetComponent<MeshFilter>();
            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshFilter == null || meshRenderer == null || meshFilter.sharedMesh == null)
                return false;

            mesh = meshFilter.sharedMesh;
            materials = meshRenderer.sharedMaterials;
            return true;
        }

        private Material[] BuildPreviewMaterials(Material[] source, int subMeshCount)
        {
            int count = Mathf.Max(1, subMeshCount);
            var result = new Material[count];

            for (int i = 0; i < count; i++)
            {
                var src = (source != null && i < source.Length) ? source[i] : null;
                var mat = BuildPreviewMaterial(src);
                if (mat == null)
                    mat = BuildPreviewMaterial(null);
                result[i] = mat;
            }

            return result;
        }

        private Material BuildPreviewMaterial(Material src)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Texture");
            if (shader == null) return null;

            var mat = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            if (src != null)
            {
                Texture tex = null;
                if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
                if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");

                Color col = Color.white;
                if (src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");
                else if (src.HasProperty("_Color")) col = src.GetColor("_Color");

                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            }

            _tempMaterials.Add(mat);
            return mat;
        }

        private void OnGUI()
        {
            var rect = new Rect(0f, 0f, position.width, position.height);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.20f, 0.20f, 0.20f, 1f)
                : new Color(0.78f, 0.78f, 0.78f, 1f));

            if (!_hasPreview || _source == null || _preview == null || _mesh == null)
            {
                EditorGUI.LabelField(new Rect(10f, 10f, rect.width - 20f, 20f), "No mesh preview");
                return;
            }

            var previewRect = new Rect(0f, 0f, rect.width, rect.height - 22f);
            DrawPreview(previewRect);

            var footerRect = new Rect(0f, rect.height - 22f, rect.width, 22f);
            EditorGUI.DrawRect(footerRect, new Color(0f, 0f, 0f, 0.22f));

            var labelRect = new Rect(8f, rect.height - 20f, rect.width - 16f, 18f);
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            style.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
            GUI.Label(labelRect, _source.name, style);
        }

        private void DrawPreview(Rect rect)
        {
            _preview.BeginPreview(rect, GUIStyle.none);

            var cam = _preview.camera;
            float radius = Mathf.Max(0.1f, _meshBounds.extents.magnitude);
            Vector3 pivot = _meshBounds.center;
            Vector3 dir = Quaternion.Euler(20f, -140f, 0f) * Vector3.forward;
            float aspect = Mathf.Max(0.1f, rect.width / Mathf.Max(1f, rect.height));
            float vHalfFov = Mathf.Max(0.01f, cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float hHalfFov = Mathf.Atan(Mathf.Tan(vHalfFov) * aspect);
            float limitingHalfFov = Mathf.Max(0.01f, Mathf.Min(vHalfFov, hHalfFov));
            float distance = Mathf.Max(1f, (radius / Mathf.Sin(limitingHalfFov)) * PreviewPadding);

            cam.transform.position = pivot - dir * distance;
            cam.transform.rotation = Quaternion.LookRotation((pivot - cam.transform.position).normalized, Vector3.up);
            cam.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2.8f);
            cam.farClipPlane = distance + radius * 3.8f;

            int subMeshes = Mathf.Max(1, _mesh.subMeshCount);
            for (int i = 0; i < subMeshes; i++)
            {
                var mat = (i < _materials.Length) ? _materials[i] : _materials[0];
                if (mat == null) continue;
                _preview.DrawMesh(_mesh, Matrix4x4.identity, mat, i);
            }

            _preview.Render();

            var tex = _preview.EndPreview();
            GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);
        }

        private void OnDisable()
        {
            CleanupPreviewData();
            if (_window == this)
                _window = null;
        }

        private void CleanupPreviewData()
        {
            for (int i = 0; i < _tempMaterials.Count; i++)
            {
                if (_tempMaterials[i] != null)
                    DestroyImmediate(_tempMaterials[i]);
            }
            _tempMaterials.Clear();

            if (_preview != null)
            {
                _preview.Cleanup();
                _preview = null;
            }

            _mesh = null;
            _materials = null;
            _hasPreview = false;
        }
    }
}
