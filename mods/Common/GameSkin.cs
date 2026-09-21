using System.Collections.Generic;
using UnityEngine;

namespace ModsCommon
{
    /// <summary>
    /// Habille les fenêtres dessinées (IMGUI) avec les sprites du jeu lui-même : cadre de bois du compendium
    /// (« woodpanel_texts »), fonds de liste (« item_background », « InputFieldBackground »), boutons (« button »),
    /// barres de défilement (« Background », « UISprite »). Les sprites vivent dans des atlas non lisibles : on les
    /// recopie une fois via une RenderTexture, en y cuisant la teinte que le jeu leur applique (fond noir à 55 %,
    /// bouton éclairci au survol, orangé quand actif). En cas d'absence (autre version du jeu), rien n'est touché :
    /// le thème dessiné reste tel quel.
    /// </summary>
    internal static class GameSkin
    {
        private static Dictionary<string, Sprite> s_sprites;

        /// <summary>Sprite d'un élément du compendium du jeu (chemin sous « Texts »), sinon premier sprite de ce nom.
        /// Les noms seuls sont ambigus (« button », « Background » existent en plusieurs exemplaires) : le panneau fait foi.</summary>
        public static Sprite Find(string name)
        {
            if (s_sprites == null)
            {
                s_sprites = new Dictionary<string, Sprite>();
                try
                {
                    var texts = InventoryGui.instance != null ? InventoryGui.instance.m_textsDialog : null;
                    if (texts != null)
                    {
                        var byPath = new (string key, string path)[]
                        {
                            ("woodpanel_texts", "Texts_frame/bkg"), ("item_background", "Texts_frame/TextArea"),
                            ("InputFieldBackground", "Texts_frame/TextList/SkillList"), ("button", "Texts_frame/Closebutton"),
                            ("Background", "Texts_frame/TextList/SkillListScroll"), ("UISprite", "Texts_frame/TextList/SkillListScroll/Sliding Area/Handle"),
                        };
                        foreach (var e in byPath)
                        {
                            var img = texts.transform.Find(e.path)?.GetComponent<UnityEngine.UI.Image>();
                            if (img != null && img.sprite != null) s_sprites[e.key] = img.sprite;
                        }
                    }
                    foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                        if (s != null && !s_sprites.ContainsKey(s.name)) s_sprites[s.name] = s;
                    var sb = new System.Text.StringBuilder("[vmods] sprites du jeu pour l'habillage : ");
                    foreach (var k in new[] { "woodpanel_texts", "item_background", "InputFieldBackground", "button", "Background", "UISprite" })
                        if (s_sprites.TryGetValue(k, out var sp) && sp != null)
                            sb.Append($"{k}={sp.name} {sp.textureRect.width:0}x{sp.textureRect.height:0} bord=({sp.border.x:0},{sp.border.y:0},{sp.border.z:0},{sp.border.w:0}) packed={sp.packed} atlas={sp.texture.width}x{sp.texture.height}; ");
                        else sb.Append(k + "=ABSENT; ");
                    Debug.Log(sb.ToString());
                }
                catch (System.Exception ex) { Debug.LogWarning("[vmods] relevé des sprites : " + ex.Message); }
            }
            return s_sprites.TryGetValue(name, out var found) ? found : null;
        }

        /// <summary>Copie lisible d'un sprite, teinte cuite dans les pixels (le sprite d'atlas n'est pas lisible directement).</summary>
        public static Texture2D Bake(Sprite sprite, Color tint, out RectOffset border, Color? blend = null)
        {
            border = new RectOffset();
            if (sprite == null || sprite.texture == null) return null;
            var src = sprite.texture;
            var r = sprite.textureRect;
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                // Coordonnées de textureRect telles quelles (origine en bas à gauche, comme ReadPixels sur une RenderTexture)
                tex.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    if (blend.HasValue) { var bl = blend.Value; px[i] = new Color32((byte)Mathf.Lerp(c.r, bl.r * 255f, bl.a), (byte)Mathf.Lerp(c.g, bl.g * 255f, bl.a), (byte)Mathf.Lerp(c.b, bl.b * 255f, bl.a), c.a); } // vers la couleur de sélection, alpha du sprite conservé
                    else px[i] = new Color32((byte)(c.r * tint.r), (byte)(c.g * tint.g), (byte)(c.b * tint.b), (byte)(c.a * tint.a));
                }
                tex.SetPixels32(px);
                tex.Apply(false, false);
            }
            catch { if (tex != null) Object.Destroy(tex); tex = null; }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
            var b = sprite.border; // x = gauche, y = bas, z = droite, w = haut
            border = new RectOffset(Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.z), Mathf.RoundToInt(b.w), Mathf.RoundToInt(b.y));
            return tex;
        }

        private static bool Apply(GUIStyle style, string sprite, Color tint, bool hover = false, bool on = false)
        {
            var tex = Bake(Find(sprite), tint, out var border);
            if (tex == null) return false;
            style.normal.background = tex; style.border = border;
            if (hover)
            {
                var hov = Bake(Find(sprite), new Color(Mathf.Min(1f, tint.r * 1.18f), Mathf.Min(1f, tint.g * 1.15f), Mathf.Min(1f, tint.b * 1.05f), tint.a), out _);
                style.hover.background = hov ?? tex; style.focused.background = tex; style.active.background = hov ?? tex;
            }
            if (on)
            {
                var onTex = Bake(Find(sprite), Color.white, out _, new Color(1f, 0.643f, 0f, 0.8f)); // orangé plein de la ligne choisie du compendium (#FFA400)
                style.onNormal.background = onTex ?? tex; style.onHover.background = onTex ?? tex; style.onActive.background = onTex ?? tex; style.onFocused.background = onTex ?? tex;
            }
            return true;
        }

        /// <summary>Colours et sprites du compendium appliqués au skin ; vrai si le cadre de bois a été trouvé.</summary>
        public static bool TryApply(GUISkin skin)
        {
            if (Find("woodpanel_texts") == null) return false;
            bool ok = Apply(skin.window, "woodpanel_texts", Color.white);
            skin.window.onNormal.background = skin.window.normal.background;
            Apply(skin.box, "item_background", new Color(0f, 0f, 0f, 0.55f));
            Apply(skin.button, "button", Color.white, hover: true, on: true);
            Apply(skin.toggle, "button", Color.white, hover: true, on: true);
            Apply(skin.textField, "InputFieldBackground", new Color(0f, 0f, 0f, 0.60f));
            skin.textField.focused.background = skin.textField.normal.background; skin.textField.hover.background = skin.textField.normal.background;
            Apply(skin.verticalScrollbar, "Background", new Color(0.19f, 0.13f, 0.08f, 1f));
            Apply(skin.verticalScrollbarThumb, "UISprite", Color.white);
            Apply(skin.scrollView, "item_background", new Color(0f, 0f, 0f, 0.35f));
            return ok;
        }

        /// <summary>Couleurs du compendium : titres orangés, boutons au texte orangé, texte blanc.</summary>
        public static readonly Color Title = new Color(1f, 0.718f, 0.36f);   // #FFB75C
        public static readonly Color ButtonText = new Color(1f, 0.631f, 0.235f); // #FFA13C
    }
}
