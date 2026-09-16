using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using ModsCommon;

namespace InventoryMod
{
    /// <summary>
    /// Inventaire à défilement, tri et empilage.
    ///  - Plus de 9 lignes : le jeu plafonne à 9 (Player.SetInventorySize) parce que son panneau grandit avec les
    ///    lignes. On lève le plafond (jusqu'à 40) et on garde le panneau à 9 lignes visibles : la grille du joueur
    ///    reçoit un ScrollRect (comme la grille des coffres, qui en a déjà un), la molette et le stick droit défilent,
    ///    la sélection manette reste visible (ScrollRectEnsureVisible, le composant du jeu).
    ///  - Trier : lignes 1 et suivantes (la barre rapide, ligne 0, n'est jamais touchée) par famille (armes, boucliers,
    ///    munitions, armures, outils, nourriture, matériaux, trophées…) puis nom, puis pile décroissante.
    ///  - Empiler : fusionne les piles d'un même objet (même qualité), dans la limite de la taille max.
    /// Les deux boutons sont clonés du bouton « Empiler tout » du panneau coffre et posés en bas à droite du panneau.
    /// </summary>
    internal static class Grid
    {
        public const int MaxRowsSupported = 40;

        private static bool s_scrollBuilt;

        // ---- plafond des lignes : le jeu clampe à 9 dans Player.SetInventorySize ; on refait la méthode sans le plafond.
        [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
        [HarmonyPrefix]
        private static bool Player_SetInventorySize(Player __instance, int rows)
        {
            if (!Plugin.Enabled.Value || rows <= 9) return true;
            rows = Mathf.Clamp(rows, 0, MaxRowsSupported);
            __instance.GetInventory().SetHeight(rows);
            __instance.AddUniqueKeyValue("invrows", rows.ToString());
            InventoryGui.instance?.SetInventorySize(rows);
            __instance.DropInvalidItems();
            return false;
        }

        // ---- le panneau ne grandit que jusqu'aux lignes visibles ; au-delà, on défile.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetInventorySize))]
        [HarmonyPrefix]
        private static void InventoryGui_SetInventorySize(ref int rows)
        {
            if (Plugin.Enabled.Value) rows = Mathf.Min(rows, Plugin.VisibleRows.Value);
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        [HarmonyPostfix]
        private static void InventoryGui_Show(InventoryGui __instance)
        {
            // Mod éteint : la colonne d'outils doit disparaître, sinon le joueur garde un panneau dont il ne peut plus rien faire
            if (!Plugin.Enabled.Value) { ShowTools(false); return; }
            try { EnsureScroll(__instance); EnsureButtons(__instance); ShowTools(true); }
            catch (Exception ex) { Plugin.Log.LogWarning("Inventaire (défilement/boutons) : " + ex.Message); }
        }

        private static void EnsureScroll(InventoryGui gui)
        {
            var grid = gui.m_playerGrid;
            if (grid == null) return;
            if (s_scrollBuilt && grid.GetComponent<ScrollRect>() != null) return;
            s_scrollBuilt = true;
            if (grid.GetComponent<ScrollRect>() != null) return;

            // Le masque de la grille existe sur le prefab mais n'est pas actif (le jeu n'en a jamais eu besoin ici) :
            // sans lui, les lignes au-delà du panneau se dessinent par-dessus le reste de l'écran.
            var mask = grid.GetComponent<RectMask2D>() ?? grid.gameObject.AddComponent<RectMask2D>();
            mask.enabled = true;

            var scroll = grid.gameObject.AddComponent<ScrollRect>();
            scroll.content = grid.m_gridRoot;
            scroll.viewport = grid.GetComponent<RectTransform>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = grid.m_elementSpace;

            // Barre de défilement : copie de celle du panneau coffre, si elle existe
            var containerGrid = AccessTools.Field(typeof(InventoryGui), "m_containerGrid")?.GetValue(gui) as InventoryGrid;
            if (containerGrid != null && containerGrid.m_scrollbar != null)
            {
                var bar = UnityEngine.Object.Instantiate(containerGrid.m_scrollbar.gameObject, grid.transform.parent);
                bar.name = "PlayerScroll";
                var brt = bar.GetComponent<RectTransform>(); var grt = grid.GetComponent<RectTransform>();
                // Collée au bord droit du panneau, sur la hauteur de la grille (ancrage explicite : la grille est en « étirement »)
                brt.SetParent(gui.m_player, false);
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0f, 1f);
                var gridCorners = new Vector3[4]; grt.GetWorldCorners(gridCorners);
                var panelCorners = new Vector3[4]; gui.m_player.GetWorldCorners(panelCorners);
                float scale = gui.m_player.lossyScale.y > 0f ? gui.m_player.lossyScale.y : 1f;
                float top = (panelCorners[1].y - gridCorners[1].y) / scale, height = (gridCorners[1].y - gridCorners[0].y) / scale;
                brt.anchoredPosition = new Vector2(2f, -top);
                brt.sizeDelta = new Vector2(10f, height);
                var sb = bar.GetComponent<Scrollbar>();
                scroll.verticalScrollbar = sb;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                grid.m_scrollbar = sb;
            }
            grid.m_ensureVisible = grid.gameObject.AddComponent<ScrollRectEnsureVisible>();
            Plugin.Log.LogInfo($"Inventaire : grille à défilement ({Plugin.VisibleRows.Value} lignes visibles, jusqu'à {MaxRowsSupported})");
        }

        // ------------------------------------------------------------------ barre d'outils (colonne d'icônes à droite du panneau)
        //  [Empiler] [Tri : mode]  puis une icône par catégorie de la roue (Tous, Consommables, Armes et outils, Armure et
        //  accessoires, Matériaux, Trophées). Une catégorie sélectionnée est surlignée, triée en premier et les autres
        //  objets sont estompés dans la grille ; la resélectionner revient à « Tous ». Le bouton de tri fait défiler
        //  les modes (catégorie, nom, quantité, poids) et retrie aussitôt. Info-bulle du jeu au survol.
        private sealed class ToolButton { public GameObject Go; public Image Bg; public Image Icon; public UITooltip Tip; }
        private static readonly List<ToolButton> s_catButtons = new List<ToolButton>();
        private static ToolButton s_sortTool, s_stackTool;
        private static Sprite s_bgSprite, s_bgSelectedSprite;
        private static readonly Color s_bgNormal = Color.white, s_bgSelected = Color.white;
        private const float ToolSize = 46f, ToolGap = 6f;

        private static void EnsureButtons(InventoryGui gui)
        {
            if (s_stackTool != null && s_stackTool.Go != null) return;
            var model = gui.m_stackAllButton != null ? gui.m_stackAllButton.gameObject : gui.m_takeAllButton?.gameObject;
            // Cadre arrondi à bordure dorée du thème des mods (au lieu du bouton en bois du jeu) : normal sombre, sélection accent
            s_bgSprite = ModsCommon.Theme.BorderedSprite(new Color(0.13f, 0.12f, 0.10f, 0.96f), new Color(0.62f, 0.46f, 0.22f, 1f), 1, 7f);
            s_bgSelectedSprite = ModsCommon.Theme.BorderedSprite(new Color(0.58f, 0.40f, 0.14f, 1f), new Color(0.96f, 0.66f, 0.28f, 1f), 2, 7f);
            s_catButtons.Clear();

            int row = 0;
            s_stackTool = MakeTool(gui, row++, Icon("@stack.png"), L.T("Empiler : fusionner les piles d'un même objet"), () => Stack(Player.m_localPlayer));
            s_sortTool = MakeTool(gui, row++, Icon("@sort.png"), "", () =>
            {
                var modes = (SortMode[])Enum.GetValues(typeof(SortMode));
                Plugin.SortModeCfg.Value = modes[(Array.IndexOf(modes, Plugin.SortModeCfg.Value) + 1) % modes.Length];
                Sort(Player.m_localPlayer, Plugin.SortModeCfg.Value);
                RefreshTools();
            });
            MakeSeparator(gui, row++); // trait fin : au-dessus les actions, en dessous les filtres par catégorie
            for (int f = -1; f < s_familyLabels.Length; f++)
            {
                int fam = f;
                s_catButtons.Add(MakeTool(gui, row++, FamilyIcon(fam), "", () =>
                {
                    FocusFamily = FocusFamily == fam ? -1 : fam;
                    if (FocusFamily >= 0) { Plugin.SortModeCfg.Value = SortMode.Categorie; Sort(Player.m_localPlayer, SortMode.Categorie); }
                    else Changed(Player.m_localPlayer?.GetInventory());
                    // À la manette il n'y a pas d'info-bulle : le choix est confirmé à l'écran
                    Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, FocusFamily >= 0 ? L.T("Catégorie : ") + FamilyLabel(FocusFamily) : L.T("Toutes les catégories"));
                    RefreshTools();
                }));
            }
            RefreshTools();
        }

        /// <summary>Trait de séparation dans la colonne d'outils, à la place d'une simple case vide.</summary>
        private static void MakeSeparator(InventoryGui gui, int row)
        {
            var go = new GameObject("ModToolSeparator", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(gui.m_player, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(26f + ToolSize * 0.15f, -8f - row * (ToolSize + ToolGap) - ToolSize * 0.5f);
            rt.sizeDelta = new Vector2(ToolSize * 0.7f, 2f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.62f, 0.46f, 0.22f, 0.75f);
            img.raycastTarget = false;
        }

        private static ToolButton MakeTool(InventoryGui gui, int row, Sprite icon, string tip, Action action)
        {
            var go = new GameObject("ModTool_" + row, typeof(RectTransform), typeof(Image), typeof(Button), typeof(UITooltip));
            go.transform.SetParent(gui.m_player, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(26f, -8f - row * (ToolSize + ToolGap)); // écartée du cadre de l'inventaire
            rt.sizeDelta = new Vector2(ToolSize, ToolSize);
            var bg = go.GetComponent<Image>();
            bg.sprite = s_bgSprite; bg.type = Image.Type.Sliced; bg.color = s_bgNormal;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors; colors.highlightedColor = new Color(1.25f, 1.15f, 1f); colors.pressedColor = new Color(1f, 0.8f, 0.55f); btn.colors = colors;
            btn.onClick.AddListener(() => { try { action(); } catch (Exception ex) { Plugin.Log.LogWarning("barre d'outils : " + ex.Message); } });

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.15f, 0.15f); irt.anchorMax = new Vector2(0.85f, 0.85f); irt.offsetMin = irt.offsetMax = Vector2.zero;
            var img = iconGo.GetComponent<Image>();
            img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
            iconGo.SetActive(icon != null);

            var tooltip = go.GetComponent<UITooltip>();
            tooltip.m_text = tip; tooltip.m_topic = "";
            try { var modelTip = gui.m_stackAllButton?.GetComponentInChildren<UITooltip>(true); if (modelTip != null) tooltip.m_tooltipPrefab = modelTip.m_tooltipPrefab; } catch { }
            return new ToolButton { Go = go, Bg = bg, Icon = img, Tip = tooltip };
        }

        /// <summary>Icône embarquée dans cette DLL (« @fichier.png »), chargée une fois.</summary>
        private static readonly Dictionary<string, Sprite> s_icons = new Dictionary<string, Sprite>();
        private static Sprite Icon(string name)
        {
            if (s_icons.TryGetValue(name, out var s)) return s;
            try
            {
                using (var stream = typeof(Grid).Assembly.GetManifestResourceStream(name.TrimStart('@')))
                {
                    if (stream != null)
                    {
                        var bytes = new byte[stream.Length]; stream.Read(bytes, 0, bytes.Length);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                        if (ImageConversion.LoadImage(tex, bytes)) s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    }
                }
            }
            catch { s = null; }
            s_icons[name] = s;
            return s;
        }

        private static Sprite ItemIcon(string prefab)
        {
            var item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            var icons = item?.m_itemData?.m_shared?.m_icons;
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }

        /// <summary>Icône d'une catégorie : celle de la roue d'action quand elle est chargée, sinon un objet typique.</summary>
        private static Sprite FamilyIcon(int f)
        {
            if (f < 0) return Icon("@all.png");
            try
            {
                var so = Valheim.UI.RadialData.SO;
                if (f < 3 && so != null && so.ItemGroupMappings != null)
                {
                    var m = so.ItemGroupMappings.GetMapping(new[] { "consumables", "weaponstools", "armor_utility" }[f]);
                    if (m.Sprite != null) return m.Sprite;
                }
            }
            catch { }
            switch (f)
            {
                case 0: return ItemIcon("CookedMeat");
                case 1: return ItemIcon("AxeBronze");
                case 2: return ItemIcon("HelmetBronze");
                // Même langage graphique que les icônes de groupe du jeu (trait clair) plutôt que des vignettes d'objets
                case 3: return Icon("@cat_materials.png");
                case 4: return Icon("@cat_trophies.png");
                default: return Icon("@cat_misc.png");
            }
        }

        /// <summary>Le mod vient d'être allumé ou éteint : la colonne d'outils suit tout de suite, même inventaire ouvert.</summary>
        internal static void OnEnabledChanged() => ShowTools(Plugin.Enabled.Value);

        /// <summary>Affiche ou cache toute la colonne d'outils (séparateur compris) : le mod éteint doit rendre l'inventaire d'origine.</summary>
        private static void ShowTools(bool show)
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null) return;
            foreach (var t in gui.m_player.GetComponentsInChildren<RectTransform>(true))
                if (t.name.StartsWith("ModTool", StringComparison.Ordinal) && t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
        }

        private static void RefreshTools()
        {
            if (s_sortTool?.Go != null)
            {
                s_sortTool.Tip.m_text = L.T("Tri : ") + ModeLabel(Plugin.SortModeCfg.Value) + L.T("  (cliquer pour changer)");
                s_sortTool.Icon.sprite = Icon(SortIconName(Plugin.SortModeCfg.Value)); // l'icône dit le mode courant
            }
            for (int i = 0; i < s_catButtons.Count; i++)
            {
                var t = s_catButtons[i]; if (t?.Go == null) continue;
                int fam = i - 1;
                bool selected = fam == FocusFamily && (fam < 0 || Plugin.SortModeCfg.Value == SortMode.Categorie);
                t.Bg.sprite = selected ? s_bgSelectedSprite : s_bgSprite; t.Bg.color = Color.white;
                t.Tip.m_text = fam < 0 ? L.T("Toutes les catégories") : FamilyLabel(fam) + (selected ? L.T("  (sélectionnée : trié en premier, le reste estompé)") : L.T("  (trier en premier, estomper le reste)"));
                if (t.Icon.sprite == null) { t.Icon.sprite = FamilyIcon(fam); t.Icon.gameObject.SetActive(t.Icon.sprite != null); }
            }
            if (Player.m_localPlayer != null) Changed(Player.m_localPlayer.GetInventory()); // rafraîchit l'estompage
        }

        // ---- tri
        internal enum SortMode { Categorie, Nom, Quantite, Poids }
        internal enum FilterMode { Masquer, Estomper }
        private static string SortIconName(SortMode m) => m == SortMode.Nom ? "@sort_name.png" : m == SortMode.Quantite ? "@sort_qty.png" : m == SortMode.Poids ? "@sort_weight.png" : "@sort_cat.png";

        private static readonly ItemDrop.ItemData.ItemType[] s_consumables = { ItemDrop.ItemData.ItemType.Consumable };
        private static readonly ItemDrop.ItemData.ItemType[] s_weaponsTools =
        {
            ItemDrop.ItemData.ItemType.OneHandedWeapon, ItemDrop.ItemData.ItemType.TwoHandedWeapon, ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft,
            ItemDrop.ItemData.ItemType.Bow, ItemDrop.ItemData.ItemType.Shield, ItemDrop.ItemData.ItemType.Tool, ItemDrop.ItemData.ItemType.Torch,
            ItemDrop.ItemData.ItemType.Ammo, ItemDrop.ItemData.ItemType.AmmoNonEquipable,
        };
        private static readonly ItemDrop.ItemData.ItemType[] s_armorUtility =
        {
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs, ItemDrop.ItemData.ItemType.Shoulder,
            ItemDrop.ItemData.ItemType.Hands, ItemDrop.ItemData.ItemType.Utility, ItemDrop.ItemData.ItemType.Trinket,
        };

        /// <summary>
        /// Les catégories de la roue d'action du jeu, dans son ordre : Consommables, Armes et outils, Armure et accessoires,
        /// puis le reste (matériaux, trophées, divers). Lues dans les données du radial quand elles sont chargées
        /// (RadialData.SO existe après une première ouverture de la roue), sinon la même table en dur.
        /// </summary>
        private static List<KeyValuePair<string, ItemDrop.ItemData.ItemType[]>> s_groups;
        private static List<KeyValuePair<string, ItemDrop.ItemData.ItemType[]>> Groups()
        {
            if (s_groups != null) return s_groups;
            var groups = new List<KeyValuePair<string, ItemDrop.ItemData.ItemType[]>>();
            try
            {
                var so = Valheim.UI.RadialData.SO;
                if (so != null && so.ItemGroupMappings != null)
                    foreach (var name in new[] { "consumables", "weaponstools", "armor_utility" })
                    {
                        var m = so.ItemGroupMappings.GetMapping(name);
                        if (m.ItemTypes != null && m.ItemTypes.Length > 0) groups.Add(new KeyValuePair<string, ItemDrop.ItemData.ItemType[]>(name, m.ItemTypes));
                    }
            }
            catch { groups.Clear(); }
            if (groups.Count < 3)
            {
                groups.Clear();
                groups.Add(new KeyValuePair<string, ItemDrop.ItemData.ItemType[]>("consumables", s_consumables));
                groups.Add(new KeyValuePair<string, ItemDrop.ItemData.ItemType[]>("weaponstools", s_weaponsTools));
                groups.Add(new KeyValuePair<string, ItemDrop.ItemData.ItemType[]>("armor_utility", s_armorUtility));
                return groups; // pas de cache : on réessaiera avec les vraies données du radial
            }
            s_groups = groups;
            return groups;
        }

        /// <summary>Rang de catégorie : 0..2 = groupes de la roue, 3 = matériaux, 4 = trophées, 5 = divers.</summary>
        private static int Family(ItemDrop.ItemData it)
        {
            var groups = Groups();
            for (int i = 0; i < groups.Count; i++) if (Array.IndexOf(groups[i].Value, it.m_shared.m_itemType) >= 0) return i;
            switch (it.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Material: return 3;
                case ItemDrop.ItemData.ItemType.Trophy: return 4;
                default: return 5;
            }
        }

        private static string Name(ItemDrop.ItemData it) => Localization.instance.Localize(it.m_shared.m_name);

        /// <summary>Rang de tri par catégorie : la catégorie mise en avant passe devant toutes les autres.</summary>
        private static int Rank(ItemDrop.ItemData it) { int f = Family(it); return FocusFamily >= 0 && f == FocusFamily ? -1 : f; }

        public static void Sort(Player player) => Sort(player, Plugin.SortModeCfg.Value);

        public static void Sort(Player player, SortMode mode)
        {
            var inv = player?.GetInventory();
            if (inv == null) return;
            int width = inv.GetWidth(), height = inv.GetHeight();
            var items = inv.GetAllItems().Where(i => i.m_gridPos.y > 0).ToList();
            Comparison<ItemDrop.ItemData> byName = (a, b) => string.Compare(Name(a), Name(b), StringComparison.OrdinalIgnoreCase);
            Comparison<ItemDrop.ItemData> tail = (a, b) => { int q = b.m_quality.CompareTo(a.m_quality); return q != 0 ? q : b.m_stack.CompareTo(a.m_stack); };
            items.Sort((a, b) =>
            {
                int r;
                switch (mode)
                {
                    case SortMode.Nom: r = byName(a, b); break;
                    case SortMode.Quantite: r = b.m_stack.CompareTo(a.m_stack); if (r == 0) r = byName(a, b); break;
                    case SortMode.Poids: r = b.GetWeight().CompareTo(a.GetWeight()); if (r == 0) r = byName(a, b); break;
                    default: r = Rank(a).CompareTo(Rank(b)); if (r == 0) r = byName(a, b); break;
                }
                return r != 0 ? r : tail(a, b);
            });
            int slot = 0;
            foreach (var it in items)
            {
                it.m_gridPos = new Vector2i(slot % width, 1 + slot / width);
                slot++;
                if (1 + slot / width >= height && slot < items.Count) { Plugin.Log.LogWarning("Tri : plus de place que d'objets ?"); break; }
            }
            Changed(inv);
            player.Message(MessageHud.MessageType.TopLeft, L.T("Inventaire trié : ") + ModeLabel(mode));
        }

        internal static string ModeLabel(SortMode m)
        {
            switch (m)
            {
                case SortMode.Nom: return "nom";
                case SortMode.Quantite: return L.T("quantité");
                case SortMode.Poids: return "poids";
                default: return L.T("catégorie (roue)");
            }
        }

        // ---- catégorie mise en avant : triée en premier et seule en pleine couleur dans la grille (les autres sont estompées)
        /// <summary>-1 = toutes ; sinon rang de Family().</summary>
        internal static int FocusFamily = -1;
        private static readonly string[] s_familyLabels = { "Consommables", "Armes et outils", "Armure et accessoires", "Matériaux", "Trophées", "Divers" };

        internal static string FamilyLabel(int f)
        {
            if (f < 0) return L.T("Toutes les catégories");
            try
            {
                var so = Valheim.UI.RadialData.SO;
                if (f < 3 && so != null && so.ItemGroupMappings != null)
                {
                    var m = so.ItemGroupMappings.GetMapping(new[] { "consumables", "weaponstools", "armor_utility" }[f]);
                    if (!string.IsNullOrEmpty(m.LocaString)) return Localization.instance.Localize(m.LocaString.StartsWith("$") ? m.LocaString : "$" + m.LocaString);
                }
            }
            catch { }
            return L.T(f < s_familyLabels.Length ? s_familyLabels[f] : "Divers");
        }

        /// <summary>Estompe les objets hors de la catégorie mise en avant (grille du joueur seulement).</summary>
        private static bool s_dimApplied;                       // des cases sont atténuées : il faudra les rétablir même sans filtre
        private static readonly Dictionary<int, int> s_familyAt = new Dictionary<int, int>(); // case -> famille, une passe sur les objets

        /// <summary>Atténue les cases hors de la catégorie choisie. Appelé à chaque UpdateGui (chaque image, inventaire
        /// ouvert) : sans filtre et rien d'atténué, on sort tout de suite ; sinon une seule passe sur les objets, et
        /// la couleur n'est réécrite que si elle change (chaque écriture invalide le Canvas).</summary>
        internal static void ApplyHighlight(InventoryGrid grid, Inventory inv, List<InventoryElement> elements)
        {
            if (InventoryGui.instance == null || grid != InventoryGui.instance.m_playerGrid) return;
            if (FocusFamily < 0 && !s_dimApplied) return;
            int width = inv.GetWidth();
            s_familyAt.Clear();
            if (FocusFamily >= 0) foreach (var it in inv.GetAllItems()) s_familyAt[it.m_gridPos.y * width + it.m_gridPos.x] = Family(it);
            bool any = false;
            for (int i = 0; i < elements.Count; i++)
            {
                var e = elements[i];
                if (e == null || e.m_icon == null || !e.m_used) continue;
                bool dim = FocusFamily >= 0 && s_familyAt.TryGetValue(i, out int fam) && fam != FocusFamily;
                any |= dim;
                bool hide = dim && Plugin.FilterStyle.Value == FilterMode.Masquer;
                // Masquer : la case paraît vide et n'est plus cliquable ni survolable (l'objet reste dans l'inventaire, à sa place)
                float ia = hide ? 0f : dim ? 0.3f : 1f;
                var c = e.m_icon.color; if (c.a != ia) { c.a = ia; e.m_icon.color = c; }
                if (e.m_amount != null) { float aa = hide ? 0f : dim ? 0.4f : 1f; var a = e.m_amount.color; if (a.a != aa) { a.a = aa; e.m_amount.color = a; } }
                if (hide)
                {
                    if (e.m_equiped != null && e.m_equiped.enabled) e.m_equiped.enabled = false;
                    if (e.m_queued != null && e.m_queued.enabled) e.m_queued.enabled = false;
                    if (e.m_noteleport != null && e.m_noteleport.enabled) e.m_noteleport.enabled = false;
                    if (e.m_food != null && e.m_food.enabled) e.m_food.enabled = false;
                    if (e.m_durability != null && e.m_durability.gameObject.activeSelf) e.m_durability.gameObject.SetActive(false);
                    if (e.m_quality != null && e.m_quality.gameObject.activeSelf) e.m_quality.gameObject.SetActive(false);
                }
                var input = e.GetComponent<UIInputHandler>();
                if (input != null && input.enabled == hide) input.enabled = !hide;
                if (e.m_tooltip != null && e.m_tooltip.enabled == hide) e.m_tooltip.enabled = !hide;
            }
            s_dimApplied = any;
        }

        // ---- empilage
        public static void Stack(Player player)
        {
            var inv = player?.GetInventory();
            if (inv == null) return;
            int merged = 0;
            var groups = inv.GetAllItems().Where(i => i.m_shared.m_maxStackSize > 1 && !i.m_equipped)
                .GroupBy(i => i.m_shared.m_name + "|" + i.m_quality + "|" + i.m_variant).Where(g => g.Count() > 1);
            var toRemove = new List<ItemDrop.ItemData>();
            foreach (var g in groups)
            {
                var list = g.OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).ToList();
                int total = list.Sum(i => i.m_stack), max = list[0].m_shared.m_maxStackSize;
                foreach (var it in list)
                {
                    int take = Mathf.Min(max, total);
                    if (it.m_stack != take) merged++;
                    it.m_stack = take; total -= take;
                    if (take == 0) toRemove.Add(it);
                }
            }
            foreach (var it in toRemove) inv.RemoveItem(it);
            Changed(inv);
            player.Message(MessageHud.MessageType.TopLeft, merged > 0 ? L.F("Piles fusionnées ({0} case(s) libérée(s))", toRemove.Count) : L.T("Rien à empiler"));
        }

        private static readonly System.Reflection.MethodInfo s_changed = AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
        private static void Changed(Inventory inv) { try { s_changed?.Invoke(inv, new object[] { true, false }); } catch { } }
    }
}
