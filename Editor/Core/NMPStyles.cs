using UnityEditor;
using UnityEngine;

namespace NoMorePain.Editor
{
    internal static class NMPStyles
    {
        public const float TabHeight = 24f;

        // ── Palette ──────────────────────────────────────────────────
        // All colours are derived from a single accent so the strip
        // looks cohesive on both dark and light skin.

        private static bool IsPro => EditorGUIUtility.isProSkin;

        // Vivid accent line / active underline
        public static Color AccentColor =>
            IsPro ? new Color(0.25f, 0.55f, 1.00f)
                  : new Color(0.20f, 0.47f, 0.95f);

        // Active tab fill
        private static Color ActiveTabBgColor =>
            IsPro ? new Color(0.17f, 0.36f, 0.68f, 0.55f)
                  : new Color(0.24f, 0.49f, 0.91f, 0.25f);

        // Active tab text
        private static Color ActiveTabTextColor =>
            IsPro ? new Color(0.75f, 0.90f, 1.00f)
                  : new Color(0.05f, 0.25f, 0.65f);

        // Nav / pin button tint
        private static Color NavButtonTextColor =>
            IsPro ? new Color(0.65f, 0.82f, 1.00f)
                  : new Color(0.10f, 0.30f, 0.70f);

        private static Color StripBgColor =>
            IsPro ? new Color(0.19f, 0.19f, 0.19f, 1f)
                  : new Color(0.70f, 0.70f, 0.70f, 1f);

        // ── Tab strip background ─────────────────────────────────────
        private static GUIStyle _tabStrip;
        private static Texture2D _tabStripBg;

        public static GUIStyle TabStrip
        {
            get
            {
                if (_tabStrip == null)
                {
                    _tabStripBg = MakeTex(StripBgColor);
                    _tabStrip = new GUIStyle
                    {
                        margin  = new RectOffset(0, 0, 2, 2),
                        padding = new RectOffset(2, 2, 1, 1)
                    };
                    _tabStrip.normal.background = _tabStripBg;
                }
                return _tabStrip;
            }
        }

        // ── Active tab ───────────────────────────────────────────────
        private static GUIStyle _activeTab;
        private static Texture2D _activeTabBg;

        public static GUIStyle ActiveTab
        {
            get
            {
                if (_activeTab == null)
                {
                    _activeTabBg = MakeTex(ActiveTabBgColor);

                    _activeTab = new GUIStyle(EditorStyles.miniButtonMid)
                    {
                        fontStyle     = FontStyle.Bold,
                        imagePosition = ImagePosition.ImageLeft,
                        alignment     = TextAnchor.MiddleLeft,
                        padding       = new RectOffset(4, 6, 2, 4),
                        margin        = new RectOffset(0, 4, 0, 0),
                        fixedHeight   = TabHeight - 2
                    };
                    _activeTab.normal.background  = _activeTabBg;
                    _activeTab.hover.background   = _activeTabBg;
                    _activeTab.active.background  = _activeTabBg;
                    _activeTab.normal.textColor   = ActiveTabTextColor;
                    _activeTab.hover.textColor    = ActiveTabTextColor;
                }
                return _activeTab;
            }
        }

        // ── Inactive tab ─────────────────────────────────────────────
        private static GUIStyle _inactiveTab;

        public static GUIStyle InactiveTab
        {
            get
            {
                if (_inactiveTab == null)
                {
                    _inactiveTab = new GUIStyle(EditorStyles.miniButtonMid)
                    {
                        fontStyle     = FontStyle.Normal,
                        imagePosition = ImagePosition.ImageLeft,
                        alignment     = TextAnchor.MiddleLeft,
                        padding       = new RectOffset(4, 6, 2, 2),
                        margin        = new RectOffset(0, 4, 0, 0),
                        fixedHeight   = TabHeight - 2
                    };
                }
                return _inactiveTab;
            }
        }

        // ── Nav < > and Pin +/- buttons ──────────────────────────────
        private static GUIStyle _pinButton;

        public static GUIStyle PinButton
        {
            get
            {
                if (_pinButton == null)
                {
                    _pinButton = new GUIStyle(EditorStyles.miniButton)
                    {
                        fixedHeight = 0,
                        padding     = new RectOffset(2, 2, 0, 0),
                        fontSize    = 11,
                        fontStyle   = FontStyle.Bold,
                        alignment   = TextAnchor.MiddleCenter
                    };
                    _pinButton.normal.textColor  = NavButtonTextColor;
                    _pinButton.hover.textColor   = AccentColor;
                    _pinButton.active.textColor  = AccentColor;
                }
                return _pinButton;
            }
        }

        // ── Save button ──────────────────────────────────────────────
        private static GUIStyle _saveButton;

        public static GUIStyle SaveButton
        {
            get
            {
                if (_saveButton == null)
                {
                    _saveButton = new GUIStyle(EditorStyles.miniButton)
                    {
                        fontStyle     = FontStyle.Bold,
                        imagePosition = ImagePosition.ImageLeft,
                        alignment     = TextAnchor.MiddleLeft,
                        padding       = new RectOffset(6, 8, 2, 2),
                        fixedHeight   = 0
                    };
                }
                return _saveButton;
            }
        }

        // ── Accent button (Copy / primary action) ────────────────────
        private static GUIStyle _accentButton;
        private static Texture2D _accentButtonBg;
        private static Texture2D _accentButtonHoverBg;

        public static GUIStyle AccentButton
        {
            get
            {
                if (_accentButton == null)
                {
                    var c  = AccentColor;
                    _accentButtonBg      = MakeTex(IsPro ? new Color(c.r * 0.28f, c.g * 0.28f, c.b * 0.28f, 1f)
                                                         : new Color(c.r, c.g, c.b, 0.18f));
                    _accentButtonHoverBg = MakeTex(IsPro ? new Color(c.r * 0.42f, c.g * 0.42f, c.b * 0.42f, 1f)
                                                         : new Color(c.r, c.g, c.b, 0.32f));

                    _accentButton = new GUIStyle(EditorStyles.miniButton)
                    {
                        fontStyle     = FontStyle.Bold,
                        imagePosition = ImagePosition.ImageLeft,
                        alignment     = TextAnchor.MiddleLeft,
                        padding       = new RectOffset(8, 8, 2, 2),
                        fixedHeight   = 22
                    };
                    _accentButton.normal.background = _accentButtonBg;
                    _accentButton.hover.background  = _accentButtonHoverBg;
                    _accentButton.active.background = _accentButtonHoverBg;
                    _accentButton.normal.textColor  = IsPro ? new Color(0.72f, 0.88f, 1.00f) : c;
                    _accentButton.hover.textColor   = IsPro ? Color.white                     : c;
                    _accentButton.active.textColor  = IsPro ? Color.white                     : c;
                }
                return _accentButton;
            }
        }

        // ── Toolbar button (miniButton without fixedHeight so GUILayout.Height works) ──
        private static GUIStyle _toolbarButton;

        public static GUIStyle ToolbarButton
        {
            get
            {
                if (_toolbarButton == null)
                {
                    _toolbarButton = new GUIStyle(EditorStyles.miniButton)
                    {
                        fixedHeight = 0,
                    };
                }
                return _toolbarButton;
            }
        }

        // ── Toolbar button content-only (no background, same layout as ToolbarButton) ──
        private static GUIStyle _toolbarButtonLabel;

        public static GUIStyle ToolbarButtonLabel
        {
            get
            {
                if (_toolbarButtonLabel == null)
                {
                    // Base on GUIStyle.none so there are zero background textures —
                    // miniButton base keeps scaled backgrounds that cover the color rect.
                    var src = EditorStyles.miniButton;
                    _toolbarButtonLabel = new GUIStyle(GUIStyle.none)
                    {
                        padding       = src.padding,
                        margin        = src.margin,
                        alignment     = TextAnchor.MiddleLeft,
                        imagePosition = ImagePosition.ImageLeft,
                        fixedHeight   = 0,
                        clipping      = TextClipping.Clip,
                        fontSize      = src.fontSize,
                        fontStyle     = src.fontStyle,
                    };
                    _toolbarButtonLabel.normal.textColor = Color.white;
                    _toolbarButtonLabel.hover.textColor  = Color.white;
                    _toolbarButtonLabel.active.textColor = Color.white;
                }
                return _toolbarButtonLabel;
            }
        }

        // ── Icon-only button (no background, just the image) ──────────
        private static GUIStyle _iconButton;

        public static GUIStyle IconButton
        {
            get
            {
                if (_iconButton == null)
                {
                    _iconButton = new GUIStyle(GUIStyle.none)
                    {
                        fixedHeight  = 0,
                        fixedWidth   = 0,
                        padding      = new RectOffset(0, 0, 0, 0),
                        margin       = new RectOffset(0, 0, 0, 0),
                        imagePosition = ImagePosition.ImageOnly,
                        alignment    = TextAnchor.MiddleCenter,
                    };
                    _iconButton.hover.textColor  = AccentColor;
                    _iconButton.active.textColor = AccentColor;
                }
                return _iconButton;
            }
        }

        // ─────────────────────────────────────────────────────────────
        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
