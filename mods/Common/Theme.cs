using UnityEngine;

namespace ModsCommon
{
    /// <summary>
    /// Thème IMGUI partagé par nos fenêtres (hub, finder) : panneau sombre semi-transparent, bordures discrètes,
    /// accent orangé façon Valheim, polices plus grandes. Les textures sont générées à la volée (aucune ressource
    /// externe). Utilisation : <c>var prev = GUI.skin; GUI.skin = Theme.Skin; ... GUI.skin = prev;</c>
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Accent = new Color(0.96f, 0.66f, 0.28f);
        public static readonly Color Text = new Color(0.93f, 0.90f, 0.82f);
        public static readonly Color MutedColor = new Color(0.80f, 0.78f, 0.72f);

        // ------------------------------------------------------------------ échelle et fenêtres

        /// <summary>IMGUI ignore l'échelle d'interface du jeu : on dessine en unités « 1080p » et on agrandit tout
        /// (polices, icônes, boutons) selon la hauteur d'écran (×1,33 en 1440p, ×2 en 4K).</summary>
        public static float UiScale => Mathf.Max(1f, Screen.height / 1080f);

        /// <summary>Taille de l'écran en unités 1080p.</summary>
        public static Vector2 ScreenSize => new Vector2(Screen.width / UiScale, Screen.height / UiScale);

        /// <summary>Fenêtre centrée, plafonnée à maxW × maxH (unités 1080p), avec une marge d'écran.</summary>
        public static Rect CenteredWindow(float maxW, float maxH)
        {
            var s = ScreenSize;
            float w = Mathf.Min(maxW, s.x - 60f), h = Mathf.Min(maxH, s.y - 40f);
            return new Rect((s.x - w) / 2f, (s.y - h) / 2f, w, h);
        }

        /// <summary>Applique le thème et l'échelle pour un passage OnGUI ; rendre avec <see cref="End"/>.</summary>
        public static (GUISkin skin, Matrix4x4 matrix) Begin()
        {
            var prev = (GUI.skin, GUI.matrix);
            GUI.skin = Skin;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
            return prev;
        }
        public static void End((GUISkin skin, Matrix4x4 matrix) prev) { GUI.skin = prev.skin; GUI.matrix = prev.matrix; }

        /// <summary>Titre de section (accent, gras) et texte secondaire (atténué), partagés par toutes les fenêtres.</summary>
        /// <summary>Espace entre deux lignes d'une liste : le même dans tous les mods, appelé après chaque ligne (RowSpace).</summary>
        public const float RowGap = 6f;
        /// <summary>À appeler après chaque ligne de liste (catalogue, résultats, chapitres, mods...) : aucune ne colle à la suivante.</summary>
        public static void RowSpace() => GUILayout.Space(RowGap);

        private static GUIStyle s_h1, s_h2, s_muted;
        public static GUIStyle H1 { get { if (s_h1 == null) { s_h1 = new GUIStyle(Skin.label) { fontSize = 20, fontStyle = TitleFont != null ? FontStyle.Normal : FontStyle.Bold, wordWrap = false }; if (TitleFont != null) s_h1.font = TitleFont; s_h1.normal.textColor = Accent; } return s_h1; } }
        public static GUIStyle H2 { get { if (s_h2 == null) { s_h2 = new GUIStyle(Skin.label) { fontSize = 16, fontStyle = TitleFont != null ? FontStyle.Normal : FontStyle.Bold, wordWrap = false }; if (TitleFont != null) s_h2.font = TitleFont; s_h2.normal.textColor = Accent; } return s_h2; } }
        public static GUIStyle Muted { get { if (s_muted == null) { s_muted = new GUIStyle(Skin.label) { fontSize = 13, wordWrap = true }; s_muted.normal.textColor = MutedColor; } return s_muted; } }

        // ------------------------------------------------------------------ petits dessins

        private static Texture2D s_white;
        private static Texture2D White { get { if (s_white == null) s_white = Solid(Color.white); return s_white; } }

        /// <summary>Bouton « × » en haut à droite d'une fenêtre (cible confortable à la souris).</summary>
        private static GUIStyle s_close;
        public static bool CloseButton(Rect window)
        {
            if (s_close == null) { s_close = new GUIStyle(Skin.button) { fontSize = 18, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 2) }; }
            return GUI.Button(new Rect(window.width - 36f, 3f, 30f, 26f), "×", s_close);
        }

        /// <summary>Rectangle plein (couleur multipliée par GUI.color).</summary>
        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = new Color(c.r * prev.r, c.g * prev.g, c.b * prev.b, c.a * prev.a); // respecte un fondu global (GUI.color)
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        /// <summary>Dessine un sprite (éventuellement issu d'un atlas) dans un rectangle IMGUI.</summary>
        public static void DrawSprite(Rect rect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;
            Rect uv;
            try { var tr = sprite.textureRect; var tex = sprite.texture; uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height); }
            catch { uv = new Rect(0, 0, 1, 1); }
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
        }

        /// <summary>Réserve une case carrée dans le layout courant et y dessine le sprite.</summary>
        /// <summary>Icône dans une mise en page ; rowHeight : hauteur de la ligne où la centrer verticalement (0 = sa propre taille).</summary>
        public static void SpriteLayout(Sprite sprite, float size, float rowHeight = 0f)
        {
            float h = Mathf.Max(size, rowHeight);
            var r = GUILayoutUtility.GetRect(size, h, GUILayout.Width(size), GUILayout.Height(h));
            if (Event.current.type == EventType.Repaint) DrawSprite(new Rect(r.x, r.y + (h - size) * 0.5f, size, size), sprite);
        }

        /// <summary>Barre de progression fine : piste sombre, remplissage accent.</summary>
        public static void ProgressBar(Rect r, float fraction, Color? fill = null)
        {
            fraction = Mathf.Clamp01(fraction);
            Fill(r, new Color(0f, 0f, 0f, 0.45f));
            if (fraction > 0f) Fill(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * fraction, r.height - 2f), fill ?? Accent);
        }

        // ------------------------------------------------------------------ glyphes dessinés (pas de dépendance à la police)

        private static Texture2D s_check, s_diamond, s_diamondHollow;
        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
            return Vector2.Distance(p, a + ab * t);
        }
        private static Texture2D Glyph(int size, System.Func<Vector2, float> sdf)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float d = sdf(new Vector2((x + 0.5f) / size, 1f - (y + 0.5f) / size)); // distance signée normalisée, y vers le bas comme à l'écran
                float a = Mathf.Clamp01(0.5f - d * size);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }
        /// <summary>Coche ✓ (trait épais) dans le rectangle, teintée.</summary>
        public static void DrawCheck(Rect r, Color c)
        {
            if (s_check == null) s_check = Glyph(32, p => Mathf.Min(SegDist(p, new Vector2(0.18f, 0.48f), new Vector2(0.42f, 0.74f)), SegDist(p, new Vector2(0.42f, 0.74f), new Vector2(0.85f, 0.24f))) - 0.09f);
            var prev = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * prev.a); GUI.DrawTexture(r, s_check, ScaleMode.ScaleToFit, true); GUI.color = prev;
        }
        /// <summary>Losange plein ou creux (marqueur d'étape).</summary>
        public static void DrawDiamond(Rect r, Color c, bool filled)
        {
            if (s_diamond == null) s_diamond = Glyph(32, p => Mathf.Abs(p.x - 0.5f) + Mathf.Abs(p.y - 0.5f) - 0.38f);
            if (s_diamondHollow == null) s_diamondHollow = Glyph(32, p => Mathf.Abs(Mathf.Abs(p.x - 0.5f) + Mathf.Abs(p.y - 0.5f) - 0.32f) - 0.06f);
            var prev = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * prev.a); GUI.DrawTexture(r, filled ? s_diamond : s_diamondHollow, ScaleMode.ScaleToFit, true); GUI.color = prev;
        }
        /// <summary>Style de panneau « journal » : dégradé vertical (plus clair en haut), bordure dorée, coins arrondis.</summary>
        private static GUIStyle s_journal;
        public static GUIStyle Journal
        {
            get
            {
                if (s_journal == null)
                {
                    const int W = 24, H = 96;
                    var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    var top = new Color(0.17f, 0.15f, 0.12f, 0.96f); var bottom = new Color(0.08f, 0.07f, 0.06f, 0.97f); var border = new Color(0.62f, 0.46f, 0.22f, 1f);
                    float radius = 7f;
                    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                    {
                        // distance signée au rectangle arrondi W×H
                        float px = Mathf.Abs(x + 0.5f - W / 2f) - (W / 2f - radius), py = Mathf.Abs(y + 0.5f - H / 2f) - (H / 2f - radius);
                        float d = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                        float coverage = Mathf.Clamp01(0.5f - d);
                        var fill = Color.Lerp(bottom, top, (float)y / (H - 1)); // y=0 est le bas de la texture
                        var c = d > -1.2f ? border : fill;
                        t.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * coverage));
                    }
                    t.Apply();
                    s_journal = new GUIStyle(Skin.box) { border = new RectOffset(9, 9, 9, 9), padding = new RectOffset(12, 12, 10, 10) };
                    s_journal.normal.background = t;
                }
                return s_journal;
            }
        }

        // ------------------------------------------------------------------ style « moderne » : voile, textes ombrés, filets fondus

        private static GUIStyle s_veil;
        /// <summary>Panneau sans cadre : voile sombre plus dense en haut, qui s'estompe vers le bas ; coins arrondis.</summary>
        public static GUIStyle Veil
        {
            get
            {
                if (s_veil == null)
                {
                    const int W = 24, H = 96; float radius = 8f;
                    var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                    {
                        float px = Mathf.Abs(x + 0.5f - W / 2f) - (W / 2f - radius), py = Mathf.Abs(y + 0.5f - H / 2f) - (H / 2f - radius);
                        float d = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                        float coverage = Mathf.Clamp01(0.5f - d);
                        float v = (float)y / (H - 1);                       // 0 = bas, 1 = haut
                        float alpha = Mathf.Lerp(0.55f, 0.88f, v * v);      // dense en haut, plus léger en bas (lisible sur la pierre claire)
                        t.SetPixel(x, y, new Color(0.05f, 0.045f, 0.04f, alpha * coverage));
                    }
                    t.Apply();
                    s_veil = new GUIStyle(Skin.box) { border = new RectOffset(10, 10, 10, 10), padding = new RectOffset(14, 12, 10, 12) };
                    s_veil.normal.background = t;
                }
                return s_veil;
            }
        }

        private static Texture2D s_fade;
        /// <summary>Trait horizontal qui s'efface vers la droite (filet sous un titre, surlignage).</summary>
        public static void FadeLine(Rect r, Color c)
        {
            if (s_fade == null)
            {
                s_fade = new Texture2D(64, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int x = 0; x < 64; x++) s_fade.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - Mathf.Pow(x / 63f, 1.5f)));
                s_fade.Apply();
            }
            var prev = GUI.color; GUI.color = new Color(c.r * prev.r, c.g * prev.g, c.b * prev.b, c.a * prev.a);
            GUI.DrawTexture(r, s_fade, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        private static readonly GUIContent s_shadowContent = new GUIContent();
        /// <summary>Étiquette ombrée à position fixe (HUD).</summary>
        public static void ShadowLabel(Rect r, string text, GUIStyle style)
        {
            if (Event.current.type != EventType.Repaint) return;
            s_shadowContent.text = text;
            var prevGui = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, prevGui.a);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), s_shadowContent, style);
            GUI.color = prevGui;
            GUI.Label(r, s_shadowContent, style);
        }
        /// <summary>Étiquette avec une ombre portée d'un pixel : lisible sur un ciel clair comme sur la neige.</summary>
        public static void ShadowLabel(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            s_shadowContent.text = text;
            var r = GUILayoutUtility.GetRect(s_shadowContent, style, options);
            if (Event.current.type != EventType.Repaint) return;
            var prevColor = style.normal.textColor; var prevGui = GUI.color;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f * prevGui.a);
            // le texte de l'ombre ne doit pas porter les balises de couleur du texte : richText garde les balises, on assombrit via GUI.color
            GUI.color = new Color(0f, 0f, 0f, prevGui.a);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), s_shadowContent, style);
            GUI.color = prevGui; style.normal.textColor = prevColor;
            GUI.Label(r, s_shadowContent, style);
        }

        // ------------------------------------------------------------------ texte net (HUD)

        // GUI.matrix agrandit l'image du texte déjà rendu (police à 16 px étirée à 21 px en 1440p) : texte flou. Pour le HUD,
        // lu en jouant, on rend le texte à la taille réelle de l'écran : police à fontSize × échelle, matrice neutre le temps
        // du dessin. Rectangles en unités 1080p, positions absolues (pas dans un GUILayout.BeginArea ni une fenêtre).
        private static readonly System.Collections.Generic.Dictionary<GUIStyle, GUIStyle> s_sharp = new System.Collections.Generic.Dictionary<GUIStyle, GUIStyle>();
        private static readonly System.Collections.Generic.Dictionary<string, string> s_sharpText = new System.Collections.Generic.Dictionary<string, string>();
        private static readonly System.Text.RegularExpressions.Regex s_sizeTag = new System.Text.RegularExpressions.Regex(@"<size=(\d+)>");
        private static float s_sharpScale;

        /// <summary>Échelle réelle du dessin en cours (celle de GUI.matrix posée par <see cref="Begin"/>).</summary>
        private static float CurrentScale => Mathf.Max(1f, GUI.matrix.m00);

        /// <summary>Copie du style à la taille réelle de l'écran (police, marges intérieures), gardée tant que l'échelle ne change pas.</summary>
        private static GUIStyle SharpStyle(GUIStyle style, float s)
        {
            if (!Mathf.Approximately(s, s_sharpScale)) { s_sharp.Clear(); s_sharpText.Clear(); s_sharpScale = s; }
            if (s_sharp.TryGetValue(style, out var st)) return st;
            st = new GUIStyle(style);
            int size = style.fontSize > 0 ? style.fontSize : (style.font != null && style.font.fontSize > 0 ? style.font.fontSize : 14);
            st.fontSize = Mathf.RoundToInt(size * s);
            st.padding = new RectOffset(Mathf.RoundToInt(style.padding.left * s), Mathf.RoundToInt(style.padding.right * s), Mathf.RoundToInt(style.padding.top * s), Mathf.RoundToInt(style.padding.bottom * s));
            s_sharp[style] = st;
            return st;
        }

        /// <summary>Balises &lt;size=N&gt; en pixels : mises à l'échelle elles aussi (les tailles en % suivent la police).</summary>
        private static string SharpText(string text, float s)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("<size=", System.StringComparison.Ordinal) < 0) return text;
            if (s_sharpText.TryGetValue(text, out var t)) return t;
            t = s_sizeTag.Replace(text, m => "<size=" + Mathf.RoundToInt(int.Parse(m.Groups[1].Value) * s) + ">");
            if (s_sharpText.Count > 512) s_sharpText.Clear();
            s_sharpText[text] = t;
            return t;
        }

        /// <summary>Étiquette ombrée nette à position fixe (HUD) : même rendu que <see cref="ShadowLabel(Rect,string,GUIStyle)"/>, sans flou.</summary>
        public static void SharpLabel(Rect r, string text, GUIStyle style, bool shadow = true)
        {
            if (Event.current.type != EventType.Repaint) return;
            float s = CurrentScale;
            if (s <= 1.001f) { if (shadow) ShadowLabel(r, text, style); else GUI.Label(r, text, style); return; }
            var prevM = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            var st = SharpStyle(style, s);
            s_shadowContent.text = SharpText(text, s);
            var rr = new Rect(Mathf.Round(r.x * s), Mathf.Round(r.y * s), Mathf.Round(r.width * s), Mathf.Round(r.height * s));
            if (shadow)
            {
                var prevGui = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, prevGui.a);
                float o = Mathf.Max(1f, Mathf.Round(s));
                GUI.Label(new Rect(rr.x + o, rr.y + o, rr.width, rr.height), s_shadowContent, st);
                GUI.color = prevGui;
            }
            GUI.Label(rr, s_shadowContent, st);
            GUI.matrix = prevM;
        }

        /// <summary>Taille (unités 1080p) du texte tel que <see cref="SharpLabel"/> le rend.</summary>
        public static Vector2 SharpSize(string text, GUIStyle style)
        {
            float s = CurrentScale;
            s_shadowContent.text = SharpText(text, s);
            return SharpStyle(style, s).CalcSize(s_shadowContent) / s;
        }

        /// <summary>Hauteur (unités 1080p) du texte renvoyé à la ligne dans cette largeur, tel que <see cref="SharpLabel"/> le rend.</summary>
        public static float SharpHeight(string text, GUIStyle style, float width)
        {
            float s = CurrentScale;
            s_shadowContent.text = SharpText(text, s);
            return SharpStyle(style, s).CalcHeight(s_shadowContent, width * s) / s;
        }

        private static GUISkin s_skin;
        private static GUIStyle s_focus;

        /// <summary>Cadre orangé (2 px, fond transparent) dessiné autour du contrôle focalisé à la manette.</summary>
        public static GUIStyle Focus
        {
            get
            {
                if (s_focus == null)
                {
                    s_focus = new GUIStyle { border = new RectOffset(7, 7, 7, 7) };
                    s_focus.normal.background = Bordered(new Color(0f, 0f, 0f, 0f), Accent, 2);
                }
                return s_focus;
            }
        }
        public static GUISkin Skin
        {
            get
            {
                if (s_skin == null) Build();
                return s_skin;
            }
        }

        // Texture 24×24 : rectangle à coins arrondis (rayon `radius`), bordure de `thick` px, anti-crénelé, utilisée en
        // 9-slice via style.border (≥ radius + 1). Le rendu reste net à toute taille : seuls les coins sont dans la texture.
        private static Texture2D Bordered(Color fill, Color border, int thick = 1, float radius = 5f)
        {
            const int S = 24;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            float half = S / 2f;
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // distance signée au rectangle arrondi (négatif = dedans)
                float px = Mathf.Abs(x + 0.5f - half) - (half - radius), py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float d = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                float coverage = Mathf.Clamp01(0.5f - d);
                var c = d > -thick ? border : fill;
                t.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * coverage));
            }
            t.Apply();
            return t;
        }

        /// <summary>Le même cadre arrondi que nos fenêtres, en Sprite 9-slice pour l'interface uGUI du jeu (boutons de la barre d'inventaire).</summary>
        public static Sprite BorderedSprite(Color fill, Color border, int thick = 1, float radius = 6f)
        {
            var t = Bordered(fill, border, thick, radius);
            t.filterMode = FilterMode.Bilinear;
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(8, 8, 8, 8));
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // Polices du jeu (assets Unity encore présents à côté des polices TMP), relevées sur son interface : Norsebold pour les
        // titres, AveriaSansLibre pour tout le texte courant (aides de touches, réglages, barre d'action) ; Averia Serif en secours.
        private static Font s_title, s_body, s_fallback;
        private static bool s_fontsLooked;
        public static Font TitleFont { get { LookupFonts(); return s_title; } }
        public static Font BodyFont { get { LookupFonts(); return s_body; } }
        private static void LookupFonts()
        {
            if (s_fontsLooked) return;
            s_fontsLooked = true;
            try
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                {
                    if (f == null) continue;
                    if (f.name == "Norsebold") s_title = f;
                    else if (f.name == "AveriaSansLibre-Regular") s_body = f;
                    else if (f.name == "AveriaSerifLibre-Regular") s_fallback = f;
                }
                if (s_body != null && !(s_body.HasCharacter('é') && s_body.HasCharacter('É') && s_body.HasCharacter('ç'))) s_body = null;
                if (s_body == null) s_body = s_fallback;
            }
            catch { }
        }

        private static void Build()
        {
            var skin = Object.Instantiate(GUI.skin);
            skin.hideFlags = HideFlags.HideAndDontSave;
            LookupFonts();
            if (s_body != null) skin.font = s_body; // toutes les étiquettes, boutons, champs : la police de l'interface du jeu
            var b = new RectOffset(7, 7, 7, 7); // ≥ rayon + bordure : les coins ne sont jamais étirés

            var panel = Bordered(new Color(0.09f, 0.08f, 0.07f, 0.97f), new Color(0.55f, 0.40f, 0.20f, 1f), 1, 8f);
            var box = Bordered(new Color(0.15f, 0.14f, 0.12f, 0.95f), new Color(0.28f, 0.25f, 0.20f, 1f));
            var btn = Bordered(new Color(0.24f, 0.22f, 0.18f, 1f), new Color(0.40f, 0.34f, 0.24f, 1f));
            var btnHover = Bordered(new Color(0.34f, 0.30f, 0.22f, 1f), Accent);
            var btnActive = Bordered(new Color(0.50f, 0.36f, 0.16f, 1f), Accent);
            var btnOn = Bordered(new Color(0.58f, 0.40f, 0.14f, 1f), Accent);
            var field = Bordered(new Color(0.05f, 0.05f, 0.04f, 1f), new Color(0.40f, 0.34f, 0.24f, 1f));
            var track = Bordered(new Color(0.06f, 0.06f, 0.05f, 1f), new Color(0.30f, 0.26f, 0.20f, 1f));
            var thumb = Bordered(Accent, new Color(0.70f, 0.45f, 0.15f, 1f));

            // Fenêtre
            skin.window.normal.background = panel; skin.window.onNormal.background = panel;
            skin.window.border = new RectOffset(10, 10, 10, 10);
            // IMGUI dessine le titre à la même hauteur que le début du contenu : la marge haute ouvre la bande du titre
            // et contentOffset y remonte le texte, sinon titre et premier panneau se touchent.
            skin.window.padding = new RectOffset(18, 18, 56, 18);
            skin.window.contentOffset = new Vector2(0f, -40f);
            skin.window.normal.textColor = Accent; skin.window.onNormal.textColor = Accent;
            skin.window.fontSize = s_title != null ? 20 : 15; skin.window.fontStyle = s_title != null ? FontStyle.Normal : FontStyle.Bold;
            if (s_title != null) skin.window.font = s_title;
            skin.window.alignment = TextAnchor.UpperCenter;

            // Boîtes et libellés
            skin.box.normal.background = box; skin.box.border = b; skin.box.padding = new RectOffset(12, 12, 10, 10);
            skin.box.margin = new RectOffset(0, 0, 0, 8); // deux boîtes empilées ne se touchent jamais
            skin.box.normal.textColor = Text;
            skin.label.normal.textColor = Text; skin.label.fontSize = 13; skin.label.richText = true;
            skin.label.padding = new RectOffset(2, 2, 3, 3); skin.label.margin = new RectOffset(4, 4, 4, 4);

            // Boutons
            skin.button.normal.background = btn; skin.button.hover.background = btnHover;
            skin.button.active.background = btnActive; skin.button.focused.background = btn;
            skin.button.onNormal.background = btnOn; skin.button.onHover.background = btnOn;
            skin.button.onActive.background = btnActive; skin.button.onFocused.background = btnOn;
            skin.button.border = b; skin.button.padding = new RectOffset(10, 10, 6, 6); skin.button.margin = new RectOffset(4, 4, 4, 4);
            skin.button.fontSize = 13;
            foreach (var s in new[] { skin.button.normal, skin.button.hover, skin.button.active, skin.button.focused,
                                      skin.button.onNormal, skin.button.onHover, skin.button.onActive, skin.button.onFocused })
                s.textColor = Text;

            // Interrupteurs : rendus comme des boutons, orangés quand actifs
            skin.toggle.normal.background = btn; skin.toggle.hover.background = btnHover; skin.toggle.active.background = btnActive;
            skin.toggle.focused.background = btn;
            skin.toggle.onNormal.background = btnOn; skin.toggle.onHover.background = btnOn; skin.toggle.onActive.background = btnActive;
            skin.toggle.onFocused.background = btnOn;
            skin.toggle.border = b; skin.toggle.padding = new RectOffset(10, 10, 6, 6); skin.toggle.margin = new RectOffset(4, 4, 4, 4);
            skin.toggle.overflow = new RectOffset(); skin.toggle.fontSize = 13; skin.toggle.alignment = TextAnchor.MiddleCenter;
            foreach (var s in new[] { skin.toggle.normal, skin.toggle.hover, skin.toggle.active, skin.toggle.focused,
                                      skin.toggle.onNormal, skin.toggle.onHover, skin.toggle.onActive, skin.toggle.onFocused })
                s.textColor = Text;

            // Champs texte
            skin.textField.normal.background = field; skin.textField.hover.background = field;
            skin.textField.focused.background = Bordered(new Color(0.05f, 0.05f, 0.04f, 1f), Accent); skin.textField.active.background = field;
            skin.textField.border = b; skin.textField.padding = new RectOffset(6, 6, 5, 5); skin.textField.fontSize = 13;
            skin.textField.normal.textColor = Color.white; skin.textField.focused.textColor = Color.white; skin.textField.hover.textColor = Color.white;

            // Curseurs
            skin.horizontalSlider.normal.background = track; skin.horizontalSlider.border = b;
            skin.horizontalSlider.fixedHeight = 8; skin.horizontalSlider.margin = new RectOffset(4, 4, 10, 4);
            skin.horizontalSliderThumb.normal.background = thumb; skin.horizontalSliderThumb.hover.background = thumb;
            skin.horizontalSliderThumb.active.background = thumb; skin.horizontalSliderThumb.border = b;
            skin.horizontalSliderThumb.fixedWidth = 14; skin.horizontalSliderThumb.fixedHeight = 16;

            // Barres de défilement
            skin.verticalScrollbar.normal.background = track; skin.verticalScrollbar.fixedWidth = 10;
            skin.verticalScrollbar.margin = new RectOffset(8, 0, 0, 0); // le contenu ne colle pas à la barre
            skin.verticalScrollbarThumb.normal.background = Bordered(new Color(0.40f, 0.34f, 0.24f, 1f), new Color(0.30f, 0.26f, 0.20f, 1f));
            skin.verticalScrollbarThumb.fixedWidth = 10;
            skin.scrollView.normal.background = Solid(new Color(0, 0, 0, 0.25f)); skin.scrollView.padding = new RectOffset(8, 8, 8, 8);

            skin.name = SkinName; // reconnu par le rendu net du texte (toutes nos fenêtres, quel que soit le mod)
            s_skin = skin;
            CrispText.Install();
        }
        internal const string SkinName = "vmods.theme";

    }

    /// <summary>
    /// Texte net dans nos fenêtres. Elles sont mises en page en unités 1080p puis agrandies par GUI.matrix (×1,33 en
    /// 1440p) : le jeu rend la police à sa taille 1080p et l'image est étirée, d'où un texte flou. Tout le texte IMGUI passe
    /// par GUIStyle.Draw(Rect, GUIContent, int, bool, bool, bool, bool) : pour nos fenêtres (skin « vmods.theme »), on y
    /// dessine le fond tel quel et on note le texte (position et zone visible à l'écran, style, état). Le texte noté est
    /// dessiné juste après, par un composant passé en dernier, avec une police à la taille réelle de l'écran et sans
    /// agrandissement. Il ne peut pas l'être sur place : le moteur découpe le contenu d'une fenêtre en coordonnées locales,
    /// avant l'agrandissement, et un texte rendu 1,33 fois plus grand y dépasse (relevé en jeu : phrase coupée au bord de la
    /// fenêtre). La mise en page, les clics et les autres interfaces ne changent pas. Un seul patch pour tous nos mods
    /// (chacun embarque sa copie de ce fichier : le premier installe, les autres voient qu'un patch « vmods.sharptext » existe).
    /// </summary>
    internal static class CrispText
    {
        private const string HarmonyPrefix = "vmods.sharptext";
        private static bool s_tried, s_busy;
        internal static bool Off; // tests : rendu d'origine pour comparer
        private static System.Action<GUIStyle, Rect, GUIContent, int, bool, bool, bool, bool> s_draw; // surcharge à 7 paramètres, pas publique : délégué direct
        private static System.Func<Vector2, Vector2> s_unclip;
        private static System.Func<Rect, Rect> s_unclipRect;
        private static System.Func<Rect> s_visibleRect;
        private static readonly GUIContent s_noText = new GUIContent(), s_text = new GUIContent();
        private static readonly System.Collections.Generic.Dictionary<GUIStyle, GUIStyle> s_styles = new System.Collections.Generic.Dictionary<GUIStyle, GUIStyle>();
        private static readonly System.Collections.Generic.Dictionary<string, string> s_sized = new System.Collections.Generic.Dictionary<string, string>();
        private static readonly System.Text.RegularExpressions.Regex s_sizeTag = new System.Text.RegularExpressions.Regex(@"<size=(\d+)>");
        private static float s_scale;

        /// <summary>Un texte à dessiner : rectangle et zone visible en pixels d'écran, style à l'échelle, état, couleurs.</summary>
        private struct Pending { public GUIStyle Style; public Rect Pixel, Clip; public string Text; public bool Hover, Active, On, Focus; public Color Color, ContentColor; }
        private static readonly System.Collections.Generic.List<Pending> s_pending = new System.Collections.Generic.List<Pending>();

        internal static void Install()
        {
            if (s_tried) return;
            s_tried = true;
            try
            {
                var draw = HarmonyLib.AccessTools.Method(typeof(GUIStyle), "Draw", new[] { typeof(Rect), typeof(GUIContent), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
                var clipT = HarmonyLib.AccessTools.TypeByName("UnityEngine.GUIClip");
                var unclip = HarmonyLib.AccessTools.Method(clipT, "Unclip", new[] { typeof(Vector2) });
                var unclipRect = HarmonyLib.AccessTools.Method(clipT, "Unclip", new[] { typeof(Rect) });
                var visible = HarmonyLib.AccessTools.PropertyGetter(clipT, "visibleRect");
                if (draw == null || unclip == null || unclipRect == null || visible == null) return;
                var info = HarmonyLib.Harmony.GetPatchInfo(draw);
                if (info != null) foreach (var owner in info.Owners) if (owner.StartsWith(HarmonyPrefix, System.StringComparison.Ordinal)) return; // un autre de nos mods l'a posé
                s_draw = HarmonyLib.AccessTools.MethodDelegate<System.Action<GUIStyle, Rect, GUIContent, int, bool, bool, bool, bool>>(draw);
                s_unclip = (System.Func<Vector2, Vector2>)System.Delegate.CreateDelegate(typeof(System.Func<Vector2, Vector2>), unclip);
                s_unclipRect = (System.Func<Rect, Rect>)System.Delegate.CreateDelegate(typeof(System.Func<Rect, Rect>), unclipRect);
                s_visibleRect = (System.Func<Rect>)System.Delegate.CreateDelegate(typeof(System.Func<Rect>), visible);
                var go = new GameObject("vmods.crisptext") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(go);
                go.AddComponent<CrispTextPass>();
                new HarmonyLib.Harmony(HarmonyPrefix + "." + typeof(CrispText).Assembly.GetName().Name)
                    .Patch(draw, prefix: new HarmonyLib.HarmonyMethod(typeof(CrispText), nameof(DrawPrefix)));
            }
            catch (System.Exception ex) { Debug.LogWarning("[vmods] texte net indisponible : " + ex.Message); }
        }

        private static bool DrawPrefix(GUIStyle __instance, Rect position, GUIContent content, int controlId, bool isHover, bool isActive, bool on, bool hasKeyboardFocus)
        {
            if (Off || s_busy || content == null || content.image != null || string.IsNullOrEmpty(content.text)) return true;
            var ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return true;
            var skin = GUI.skin;
            if (skin == null || skin.name != Theme.SkinName) return true;
            var m = GUI.matrix;
            float s = m.m00;
            // Agrandissement pur (celui de Theme.Begin) seulement ; texte déjà rendu net (HUD) ou matrice inhabituelle : tel quel
            if (s <= 1.001f || !Mathf.Approximately(m.m11, s) || m.m01 != 0f || m.m10 != 0f || m.m03 != 0f || m.m13 != 0f) return true;
            if (s_pending.Count > 4000) s_pending.Clear(); // passe finale absente : on ne s'accumule pas
            s_busy = true;
            try
            {
                // 1. fond, bordure, état (survol, appui) : le rendu du jeu, sans le texte
                s_noText.text = ""; s_noText.image = null; s_noText.tooltip = content.tooltip;
                s_draw(__instance, position, s_noText, controlId, isHover, isActive, on, hasKeyboardFocus);
                // 2. texte noté en pixels d'écran : coin et zone visible en coordonnées absolues NON agrandies, lues sous une
                // matrice neutre (sous la matrice agrandie, le moteur mélange décalage de fenêtre brut et contenu agrandi)
                GUI.matrix = Matrix4x4.identity;
                Vector2 a = s_unclip(position.position);
                Rect vis = s_unclipRect(s_visibleRect());
                GUI.matrix = m;
                s_pending.Add(new Pending
                {
                    Style = Scaled(__instance, s), Text = Sized(content.text, s),
                    Pixel = new Rect(a.x * s, a.y * s, position.width * s, position.height * s),
                    Clip = new Rect(vis.x * s, vis.y * s, vis.width * s, vis.height * s),
                    Hover = isHover, Active = isActive, On = on, Focus = hasKeyboardFocus, Color = GUI.color, ContentColor = GUI.contentColor,
                });
            }
            catch { GUI.matrix = m; s_busy = false; return true; } // au moindre souci : rendu d'origine
            s_busy = false;
            return false;
        }

        /// <summary>Dessine les textes notés pendant ce passage de rendu, à la taille réelle de l'écran, chacun dans sa zone visible.</summary>
        internal static void Flush()
        {
            if (s_pending.Count == 0) return;
            var prevM = GUI.matrix; var prevC = GUI.color; var prevCC = GUI.contentColor;
            GUI.matrix = Matrix4x4.identity;
            s_busy = true;
            try
            {
                foreach (var p in s_pending)
                {
                    GUI.BeginClip(p.Clip);
                    GUI.color = p.Color; GUI.contentColor = p.ContentColor;
                    s_text.text = p.Text;
                    s_draw(p.Style, new Rect(p.Pixel.x - p.Clip.x, p.Pixel.y - p.Clip.y, p.Pixel.width, p.Pixel.height), s_text, -1, p.Hover, p.Active, p.On, p.Focus);
                    GUI.EndClip();
                }
            }
            finally
            {
                s_busy = false;
                s_pending.Clear();
                GUI.matrix = prevM; GUI.color = prevC; GUI.contentColor = prevCC;
            }
        }

        /// <summary>Copie du style pour le texte seul : police et marges intérieures à l'échelle, aucun fond.</summary>
        private static GUIStyle Scaled(GUIStyle style, float s)
        {
            if (!Mathf.Approximately(s, s_scale)) { s_styles.Clear(); s_sized.Clear(); s_scale = s; }
            if (s_styles.TryGetValue(style, out var st)) return st;
            st = new GUIStyle(style);
            // Police héritée du thème (style sans police propre) : fixée ici, le dessin différé se fait hors de notre thème
            if (st.font == null) st.font = GUI.skin.font;
            int size = style.fontSize > 0 ? style.fontSize : (style.font != null && style.font.fontSize > 0 ? style.font.fontSize : (GUI.skin.font != null && GUI.skin.font.fontSize > 0 ? GUI.skin.font.fontSize : 13));
            // Arrondi vers le bas : le texte ne prend jamais plus de place que ce que la mise en page 1080p a prévu
            st.fontSize = Mathf.FloorToInt(size * s);
            st.padding = new RectOffset(Mathf.RoundToInt(style.padding.left * s), Mathf.RoundToInt(style.padding.right * s), Mathf.RoundToInt(style.padding.top * s), Mathf.RoundToInt(style.padding.bottom * s));
            st.contentOffset = style.contentOffset * s;
            st.border = new RectOffset(); st.overflow = new RectOffset();
            // Texte sur plusieurs lignes : la hauteur de ligne grandit un peu plus vite que la police (13 → 17 px) ; on le
            // laisse dépasser de ces quelques pixels plutôt que de perdre une ligne
            if (style.wordWrap) st.clipping = TextClipping.Overflow;
            foreach (var state in new[] { st.normal, st.hover, st.active, st.focused, st.onNormal, st.onHover, st.onActive, st.onFocused })
                state.background = null;
            s_styles[style] = st;
            return st;
        }

        /// <summary>Balises &lt;size=N&gt; en pixels : à l'échelle elles aussi.</summary>
        private static string Sized(string text, float s)
        {
            if (text.IndexOf("<size=", System.StringComparison.Ordinal) < 0) return text;
            if (s_sized.TryGetValue(text, out var t)) return t;
            t = s_sizeTag.Replace(text, x => "<size=" + Mathf.RoundToInt(int.Parse(x.Groups[1].Value) * s) + ">");
            if (s_sized.Count > 512) s_sized.Clear();
            s_sized[text] = t;
            return t;
        }
    }

    /// <summary>Passe finale du texte net : son OnGUI vient après celui des mods (profondeur la plus basse = dessiné en dernier).</summary>
    internal sealed class CrispTextPass : MonoBehaviour
    {
        private void OnGUI()
        {
            GUI.depth = -1000;
            if (Event.current.type == EventType.Repaint) CrispText.Flush();
        }
    }
}
