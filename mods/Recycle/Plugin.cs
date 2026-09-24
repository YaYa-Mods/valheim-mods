using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace Recycle
{
    /// <summary>
    /// Recyclage : le jeu ne reprend jamais un objet fabriqué (démonter une construction rend tout, l'incinérateur ne
    /// rend que du charbon). Ici, une fenêtre (F3, roue d'action, bouton dans l'inventaire) liste l'équipement porté
    /// dans le sac (armes, outils, armures, boucliers, munitions...) avec ce que son recyclage rendrait : une part
    /// (un tiers par défaut) du coût de fabrication lu dans la recette du jeu, améliorations comprises, arrondie à
    /// l'unité inférieure. Deux clics pour recycler (le second confirme), les matériaux vont dans l'inventaire, au sol
    /// s'il est plein. Ce qui est équipé n'est jamais proposé.
    /// </summary>
    [BepInPlugin(Guid, "Recycle", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.recycle";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<float> Ratio;
        internal static ConfigEntry<bool> IncludeMaterials;
        internal static ConfigEntry<bool> IncludeAmmo;
        internal static ConfigEntry<int> PieceWood, PieceStone;
        internal static bool WindowOpen;
        private static Plugin s_instance;

        private readonly Pad _pad = new Pad();
        private Rect _window;
        private Vector2 _scroll;
        private ItemDrop.ItemData _armed; private float _armedUntil;
        private readonly List<Entry> _entries = new List<Entry>();
        private float _nextRefresh;
        private GUIStyle _small, _row, _rowName, _yield;

        /// <summary>Un objet recyclable et ce qu'il rend.</summary>
        internal sealed class Entry
        {
            public ItemDrop.ItemData Item; public Recipe Recipe;
            public List<KeyValuePair<ItemDrop, int>> Yield = new List<KeyValuePair<ItemDrop, int>>();
            public string YieldText;
        }

        private void Awake()
        {
            Log = Logger; s_instance = this;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le recyclage."));
            ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F3, L.T("Touche qui ouvre/ferme la fenêtre de recyclage. Aussi dans la roue d'action (Mods) et dans l'inventaire."));
            Ratio = Config.Bind("General", "Ratio", 0.34f, new ConfigDescription(L.T("Part du coût de fabrication rendue (0,34 = un tiers), arrondie à l'unité inférieure par matériau."), new AcceptableValueRange<float>(0.05f, 1f)));
            IncludeAmmo = Config.Bind("General", "IncludeAmmo", true, L.T("Proposer aussi les munitions (flèches, carreaux...) : la pile entière est recyclée d'un coup."));
            IncludeMaterials = Config.Bind("General", "IncludeMaterials", false, L.T("Proposer aussi les matériaux fabriqués (bronze, clous, cuir tanné...). Éteint par défaut : on recycle de l'équipement, pas des lingots."));
            PieceWood = Config.Bind("Recycler", "Wood", 10, new ConfigDescription(L.T("Bois demandé pour poser le Recycleur (meuble, établi à portée). Lu au lancement du jeu."), new AcceptableValueRange<int>(0, 200)));
            PieceStone = Config.Bind("Recycler", "Stone", 10, new ConfigDescription(L.T("Pierre demandée pour poser le Recycleur. Lu au lancement du jeu."), new AcceptableValueRange<int>(0, 200)));
            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Harmony.CreateAndPatchAll(typeof(Recycler), Guid);
            ScrollGuard.Install(Guid, () => WindowOpen);
            Log.LogInfo($"Recycle chargé (touche {ToggleKey.Value}, part rendue {Ratio.Value:P0})");
        }

        private void Update()
        {
            try { Recycler.Update(); } catch (Exception ex) { Log.LogWarning("Recycleur : " + ex.Message); }
            if (Player.m_localPlayer == null || !Enabled.Value) { WindowOpen = false; return; }
            if (WindowOpen && ModWindows.GameTookScreen()) Close();
            if (ZInput.GetKeyDown(ToggleKey.Value, false) && !Console.IsVisible()) { if (WindowOpen) Close(); else if (!ModWindows.GameBusy()) Open(); }
            else if (WindowOpen && (ZInput.GetKeyDown(KeyCode.Escape, false) || _pad.Update())) Close();
        }

        private void Open()
        {
            ModWindows.TakeScreen();
            WindowOpen = true; _armed = null; _nextRefresh = 0f;
            _pad.OnOpened();
            RefreshEntries();
            _window = Theme.CenteredWindow(900f, Mathf.Clamp(230f + _entries.Count * 66f, 340f, 720f)); // à la taille de la liste
        }

        private void Close() { WindowOpen = false; _armed = null; }
        internal static void Toggle() { if (s_instance == null) return; if (WindowOpen) s_instance.Close(); else s_instance.Open(); }
        /// <summary>Fermeture demandée par un autre mod qui ouvre sa propre fenêtre (convention ModWindows).</summary>
        public static void CloseWindow() { if (s_instance != null && WindowOpen) s_instance.Close(); }

        public static List<KeyValuePair<string, KeyCode>> KeyHints() => !Enabled.Value ? new List<KeyValuePair<string, KeyCode>>() : new List<KeyValuePair<string, KeyCode>>
        {
            new KeyValuePair<string, KeyCode>(L.T("Recycler"), ToggleKey.Value),
        };

        public static List<KeyValuePair<string, Action>> RadialEntries() => new List<KeyValuePair<string, Action>>
        {
            new KeyValuePair<string, Action>("Recycler|@recycle.png", () => { if (Enabled.Value) Toggle(); }),
        };

        public static List<KeyValuePair<string, Action>> InventoryEntries() => new List<KeyValuePair<string, Action>>
        {
            new KeyValuePair<string, Action>("Recycler|@recycle.png", () => { if (Enabled.Value) { InventoryGui.instance?.Hide(); Toggle(); } }),
        };

        // ------------------------------------------------------------------ ce qui se recycle, et pour combien

        private static readonly HashSet<ItemDrop.ItemData.ItemType> s_gear = new HashSet<ItemDrop.ItemData.ItemType>
        {
            ItemDrop.ItemData.ItemType.OneHandedWeapon, ItemDrop.ItemData.ItemType.TwoHandedWeapon, ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft,
            ItemDrop.ItemData.ItemType.Bow, ItemDrop.ItemData.ItemType.Shield, ItemDrop.ItemData.ItemType.Tool, ItemDrop.ItemData.ItemType.Torch,
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility, ItemDrop.ItemData.ItemType.Attach_Atgeir,
        };

        /// <summary>Recette de fabrication de l'objet dans la base du jeu, ou null s'il n'est pas recyclable.</summary>
        internal static Recipe RecipeOf(ItemDrop.ItemData item)
        {
            if (item == null || item.m_equipped || ObjectDB.instance == null) return null;
            var type = item.m_shared.m_itemType;
            bool ok = s_gear.Contains(type)
                || (type == ItemDrop.ItemData.ItemType.Ammo && IncludeAmmo.Value)
                || (type == ItemDrop.ItemData.ItemType.Material && IncludeMaterials.Value);
            if (!ok) return null;
            var recipe = ObjectDB.instance.GetRecipe(item);
            if (recipe == null || recipe.m_resources == null || recipe.m_resources.Length == 0 || recipe.m_requireOnlyOneIngredient) return null;
            return recipe;
        }

        /// <summary>Matériaux rendus : part du coût (niveau 1 plus chaque amélioration), par pile, arrondie vers le bas.</summary>
        internal static List<KeyValuePair<ItemDrop, int>> YieldOf(ItemDrop.ItemData item, Recipe recipe)
        {
            var result = new List<KeyValuePair<ItemDrop, int>>();
            int per = Mathf.Max(1, recipe.m_amount); // une fabrication donne parfois plusieurs objets (flèches) : le coût se répartit
            foreach (var r in recipe.m_resources)
            {
                if (r == null || r.m_resItem == null || r.m_upgraderResource) continue; // l'« améliorateur » exigé par la recette n'est pas un matériau consommé
                float cost = r.GetAmount(1) * (float)item.m_stack / per;
                for (int q = 2; q <= item.m_quality; q++) cost += r.GetAmount(q);
                int got = Mathf.FloorToInt(cost * Ratio.Value + 0.0001f);
                if (got > 0) result.Add(new KeyValuePair<ItemDrop, int>(r.m_resItem, got));
            }
            return result;
        }

        private static string Name(ItemDrop.ItemData item) => Localization.instance.Localize(item.m_shared.m_name);
        private static string Name(ItemDrop drop) => Localization.instance.Localize(drop.m_itemData.m_shared.m_name);

        private void RefreshEntries()
        {
            _entries.Clear();
            var inv = Player.m_localPlayer?.GetInventory();
            if (inv == null) return;
            foreach (var item in inv.GetAllItems().OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x))
            {
                var recipe = RecipeOf(item);
                if (recipe == null) continue;
                var e = new Entry { Item = item, Recipe = recipe, Yield = YieldOf(item, recipe) };
                e.YieldText = e.Yield.Count == 0 ? L.T("rien (coût trop faible)") : string.Join(", ", e.Yield.Select(y => $"{y.Value} × {Name(y.Key)}"));
                _entries.Add(e);
            }
        }

        /// <summary>Recycle l'objet : il disparaît, les matériaux entrent dans l'inventaire (ou tombent au sol s'il est plein).</summary>
        internal static bool Recycle(ItemDrop.ItemData item)
        {
            var player = Player.m_localPlayer;
            var inv = player?.GetInventory();
            if (inv == null || item == null || !inv.ContainsItem(item)) return false;
            var recipe = RecipeOf(item);
            if (recipe == null) return false;
            var yield = YieldOf(item, recipe);
            string what = Name(item) + (item.m_quality > 1 ? L.F(" niv. {0}", item.m_quality) : "") + (item.m_stack > 1 ? $" ×{item.m_stack}" : "");
            inv.RemoveItem(item);
            var got = new List<string>();
            foreach (var y in yield)
            {
                int left = y.Value;
                var added = inv.AddItem(y.Key.name, left, 1, 0, 0L, "", false, false);
                if (added == null)
                {
                    // Inventaire plein : au sol devant le joueur, comme le jeu le fait quand on lâche un objet
                    ItemDrop.DropItem(y.Key.m_itemData, left, player.transform.position + player.transform.forward * 1.2f + Vector3.up * 0.8f, Quaternion.identity);
                }
                got.Add($"{left} × {Name(y.Key)}");
            }
            player.Message(MessageHud.MessageType.TopLeft, got.Count == 0 ? L.F("Recyclé : {0}, rien récupéré", what) : L.F("Recyclé : {0} → {1}", what, string.Join(", ", got)));
            Log.LogInfo($"Recyclé {what} → {(got.Count == 0 ? "rien" : string.Join(", ", got))}");
            return true;
        }

        // ------------------------------------------------------------------ fenêtre

        private void EnsureStyles()
        {
            if (_small != null) return;
            _small = new GUIStyle(Theme.Skin.label) { fontSize = 13, wordWrap = true }; _small.normal.textColor = Theme.MutedColor;
            _row = new GUIStyle(Theme.Skin.box) { padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(0, 0, 0, (int)Theme.RowGap) };
            _rowName = new GUIStyle(Theme.Skin.label) { fontSize = 15, wordWrap = false, alignment = TextAnchor.MiddleLeft };
            _yield = new GUIStyle(Theme.Skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.MiddleLeft }; _yield.normal.textColor = Theme.Accent;
        }

        private void OnGUI()
        {
            if (!WindowOpen || Player.m_localPlayer == null || !Enabled.Value) return;
            if (_pad.ConsumeSkipRepaint()) return;
            var prev = Theme.Begin();
            EnsureStyles();
            if (Time.unscaledTime >= _nextRefresh) { RefreshEntries(); _nextRefresh = Time.unscaledTime + 0.5f; }
            _window = GUILayout.Window(GetHashCode(), _window, DrawWindow, L.T("Recyclage"));
            Theme.End(prev);
        }

        private void DrawWindow(int id)
        {
            // Croix de fermeture dans le coin de la fenêtre (coordonnées de la fenêtre, comme les autres mods)
            if (Theme.CloseButton(_window)) Close();
            _pad.BeginWindow();
            GUILayout.Label(L.F("Chaque objet rend <color=#f5a847>{0:P0}</color> de son coût de fabrication (améliorations comprises), arrondi à l'unité inférieure. Ce qui est équipé n'apparaît pas : retirez-le d'abord.", Ratio.Value), _small);
            Theme.RowSpace();
            if (_entries.Count == 0)
            {
                GUILayout.Label(L.T("Rien à recycler dans l'inventaire : seuls les objets fabriqués (armes, outils, armures, boucliers, munitions) sont proposés."), Theme.Muted);
                GUILayout.FlexibleSpace();
            }
            else
            {
                _scroll = _pad.BeginScrollView(_scroll);
                foreach (var e in _entries.ToList())
                {
                    GUILayout.BeginHorizontal(_row);
                    var icon = e.Item.GetIcon();
                    if (icon != null) { Theme.SpriteLayout(icon, 32f, 40f); GUILayout.Space(8f); }
                    GUILayout.BeginVertical();
                    string quality = e.Item.m_quality > 1 ? "  <color=#f5a847>" + L.F("niv. {0}", e.Item.m_quality) + "</color>" : "";
                    string stack = e.Item.m_stack > 1 ? $"  <color=#9a9488>×{e.Item.m_stack}</color>" : "";
                    GUILayout.Label(Name(e.Item) + quality + stack, _rowName);
                    GUILayout.Label("→ " + e.YieldText, _yield);
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    bool armed = _armed == e.Item && Time.unscaledTime < _armedUntil;
                    string label = armed ? L.T("Confirmer ?") : L.T("Recycler");
                    if (_pad.Button(label, GUILayout.Width(150f), GUILayout.Height(34f)))
                    {
                        if (armed) { _armed = null; Recycle(e.Item); _nextRefresh = 0f; }
                        else { _armed = e.Item; _armedUntil = Time.unscaledTime + 4f; }
                    }
                    GUILayout.EndHorizontal();
                }
                _pad.EndScrollView();
            }
            Theme.RowSpace();
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("Un premier clic arme le bouton, le second recycle. Les matériaux tombent au sol si l'inventaire est plein."), _small);
            GUILayout.FlexibleSpace();
            if (_pad.Button(Pad.Active ? L.T("Fermer  (B)") : L.F("Fermer  ({0})", ToggleKey.Value), GUILayout.Width(140))) Close();
            GUILayout.EndHorizontal();
            Pad.Hints(_small);
            _pad.EndWindow();
            GUI.DragWindow();
        }
    }

    internal static class Patches
    {
        // Fenêtre ouverte = comme un champ texte actif : souris libre, joueur immobile.
        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        [HarmonyPostfix]
        private static void TextInput_IsVisible(ref bool __result)
        {
            if (Plugin.WindowOpen) __result = true;
        }
    }
}
