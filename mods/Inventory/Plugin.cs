using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ModsCommon;

namespace InventoryMod
{
    /// <summary>
    /// Inventaire confortable, trois réglages indépendants (BepInEx/config/vmods.inventory.cfg) :
    ///  - Poids : Player.GetMaxCarryWeight est LA source de vérité du jeu (surchargé, ramassage auto refusé quand
    ///    trop lourd, affichage). On renvoie une valeur énorme → plus jamais surchargé.
    ///  - Piles : m_maxStackSize des items empilables (pile > 1) → MaxStackSize. Les équipements (pile de 1)
    ///    ne sont pas touchés : durabilité et qualité sont par objet. Valeurs vanilla conservées → réappliqué à la volée.
    ///  - Lignes : le jeu vend des lignes d'inventaire chez les marchands (clé "invrows", max 9). On applique
    ///    le même mécanisme à l'apparition du personnage ; c'est sauvegardé sur le personnage comme un achat,
    ///    donc rien n'est perdu si le mod est retiré.
    /// </summary>
    [BepInPlugin(Guid, "Inventory", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.inventory";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> NoWeightLimit;
        internal static ConfigEntry<int> MaxStackSize;
        internal static ConfigEntry<int> InventoryRows;
        internal static ConfigEntry<int> VisibleRows;
        internal static ConfigEntry<Grid.SortMode> SortModeCfg;
        internal static ConfigEntry<Grid.FilterMode> FilterStyle;
        internal static ConfigEntry<bool> BackupEnabled;
        internal static ConfigEntry<bool> KeepSkillsOnDeath;
        internal static ConfigEntry<bool> KeepInventoryOnDeath;
        internal static ConfigEntry<bool> RecoverTombstones;
        internal static ConfigEntry<bool> FillTopFirst;
        internal static ConfigEntry<bool> EquipmentSlots;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le mod (poids, piles, lignes)."));
            Enabled.SettingChanged += (_, __) => { Patches.ReapplyStacks(); Grid.OnEnabledChanged(); Equipment.OnToggle(Player.m_localPlayer); };
            NoWeightLimit = Config.Bind("General", "NoWeightLimit", true, L.T("Plus de limite de poids (jamais surchargé)."));
            MaxStackSize = Config.Bind("General", "MaxStackSize", 9999,
                new ConfigDescription(L.T("Taille max des piles pour tous les items empilables. 0 = vanilla."), new AcceptableValueRange<int>(0, 99999)));
            InventoryRows = Config.Bind("General", "InventoryRows", 20,
                new ConfigDescription(L.T("Lignes d'inventaire appliquées à l'apparition du personnage (vanilla 4 + achats chez les marchands, plafond du jeu 9 : ") +
                                      L.T("au-delà, la grille défile). Réduit seulement si les lignes retirées sont vides."), new AcceptableValueRange<int>(4, Grid.MaxRowsSupported)));
            VisibleRows = Config.Bind("General", "VisibleRows", 9,
                new ConfigDescription(L.T("Lignes visibles à la fois dans le panneau ; le reste défile (molette, stick droit)."), new AcceptableValueRange<int>(4, 9)));
            SortModeCfg = Config.Bind("General", "SortMode", Grid.SortMode.Categorie,
                L.T("Tri de l'inventaire (bouton « Trier… » dans l'inventaire, ou menu radial). Categorie = les groupes de la roue d'action ") +
                L.T("(consommables, armes et outils, armure et accessoires, puis matériaux, trophées, divers) ; Nom ; Quantite ; Poids."));
            FilterStyle = Config.Bind("General", "FilterStyle", Grid.FilterMode.Masquer,
                L.T("Quand une catégorie est choisie dans la barre d'outils : Masquer = seuls les objets de la catégorie restent visibles (les autres cases paraissent vides, sans clic possible) ; Estomper = les autres restent visibles, atténués."));
            KeepInventoryOnDeath = Config.Bind("Death", "KeepInventoryOnDeath", true,
                L.T("À la mort, tout l'inventaire reste sur vous (pas de pierre tombale). Même effet que la clé interne DeathKeepInventory du jeu."));
            KeepSkillsOnDeath = Config.Bind("Death", "KeepSkillsOnDeath", true,
                L.T("À la mort, aucune baisse de compétences (le jeu retire 5 % de chaque compétence, hors période de grâce)."));
            FillTopFirst = Config.Bind("General", "FillTopFirst", true,
                L.T("Un objet ramassé va dans la première case libre en partant du haut (le jeu remplit par le bas, ce qui envoie tout en fin d'inventaire avec beaucoup de lignes). La barre d'action reste en dernier recours ; les armes cherchent d'abord une place dans la barre, comme dans le jeu."));
            EquipmentSlots = Config.Bind("General", "EquipmentSlots", true,
                L.T("Emplacements d'équipement (Tête, Torse, Jambes, Cape, Accessoire) à côté de la grille : ce qui est équipé y va, la grille reste libre. C'est la dernière ligne de l'inventaire, redessinée à part."));
            RecoverTombstones = Config.Bind("Death", "RecoverTombstones", true,
                L.T("À l'apparition, vos pierres tombales encore dans le monde sont vidées dans votre inventaire à distance, puis supprimées."));
            BackupEnabled = Config.Bind("Safety", "BackupEnabled", true,
                L.T("Copie de secours de l'inventaire dans les données du personnage à chaque sauvegarde ; au chargement, ce qui a été ") +
                L.T("perdu (pile tronquée, objet détruit) est restauré. Survit au retrait du mod."));

            MaxStackSize.SettingChanged += (_, __) => Patches.ReapplyStacks();
            InventoryRows.SettingChanged += (_, __) => Patches.ApplyRows(Player.m_localPlayer);
            EquipmentSlots.SettingChanged += (_, __) => Equipment.OnToggle(Player.m_localPlayer);

            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Harmony.CreateAndPatchAll(typeof(LoadGuard), Guid);
            Harmony.CreateAndPatchAll(typeof(FirstFreeSlot), Guid);
            Harmony.CreateAndPatchAll(typeof(Equipment), Guid);
            Harmony.CreateAndPatchAll(typeof(Backup), Guid);
            Harmony.CreateAndPatchAll(typeof(Death), Guid);
            Harmony.CreateAndPatchAll(typeof(Grid), Guid);
            ScrollGuard.Install(Guid, () => Enabled.Value && InventoryGui.IsVisible()); // molette : faire défiler la grille agrandie
            Log.LogInfo($"Inventory chargé (poids illimité={NoWeightLimit.Value}, piles={MaxStackSize.Value}, lignes={InventoryRows.Value})");
        }

        // Menu radial (manette) : tri avec le mode configuré, empilage.
        public static List<KeyValuePair<string, System.Action>> RadialEntries() => new List<KeyValuePair<string, System.Action>>
        {
            new KeyValuePair<string, System.Action>("Trier l'inventaire|@sort.png", () => { if (Enabled.Value) Grid.Sort(Player.m_localPlayer); }),
            new KeyValuePair<string, System.Action>("Empiler l'inventaire|@stack.png", () => { if (Enabled.Value) Grid.Stack(Player.m_localPlayer); }),
        };
    }

    internal static class Patches
    {
        private const float UnlimitedWeight = 1_000_000f;

        // Les compteurs de piles sont réécrits à chaque image (UpdateGui) : chaîne mise en cache par valeur, zéro allocation.
        private static readonly Dictionary<int, string> s_stackText = new Dictionary<int, string>();
        private static string StackText(int n)
        {
            if (!s_stackText.TryGetValue(n, out var s)) { s = n.ToString(); if (s_stackText.Count < 20000) s_stackText[n] = s; }
            return s;
        }

        // ---- Poids ----
        [HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
        [HarmonyPostfix]
        private static void Player_GetMaxCarryWeight(ref float __result)
        {
            if (Plugin.Enabled.Value && Plugin.NoWeightLimit.Value) __result = UnlimitedWeight;
        }

        // Le compteur de poids "12/1000000" déborde de sa case : sans limite, on n'affiche que le poids porté.
        [HarmonyPatch(typeof(InventoryGui), "UpdateInventoryWeight")]
        [HarmonyPostfix]
        private static void InventoryGui_UpdateInventoryWeight(InventoryGui __instance, Player player)
        {
            if (!Plugin.Enabled.Value || !Plugin.NoWeightLimit.Value || player == null || __instance.m_weight == null) return;
            __instance.m_weight.text = UnityEngine.Mathf.CeilToInt(player.GetInventory().GetTotalWeight()).ToString();
        }


        // Les cases affichent "pile/max" : avec un max de 9999, c'est illisible → on n'affiche que la quantité.
        private static readonly HarmonyLib.AccessTools.FieldRef<InventoryGrid, System.Collections.Generic.List<InventoryElement>> s_elements =
            HarmonyLib.AccessTools.FieldRefAccess<InventoryGrid, System.Collections.Generic.List<InventoryElement>>("m_elements");

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        [HarmonyPostfix]
        private static void InventoryGrid_UpdateGui(InventoryGrid __instance)
        {
            if (!Plugin.Enabled.Value) return;
            var inv = __instance.GetInventory();
            var elements = s_elements(__instance);
            if (inv == null || elements == null) return;
            int width = inv.GetWidth();
            if (Plugin.MaxStackSize.Value > 0)
                for (int i = 0; i < elements.Count; i++)
                {
                    var e = elements[i];
                    if (e == null) continue;
                    if (!e.m_used || e.m_amount == null) continue;
                    var item = inv.GetItemAt(i % width, i / width);
                    if (item == null || item.m_shared.m_maxStackSize < 1000) continue;
                    string txt = StackText(item.m_stack);
                    if (e.m_amount.text != txt) e.m_amount.text = txt; // TMP ne recalcule le maillage que si le texte change
                }
            Grid.ApplyHighlight(__instance, inv, elements);
        }

        // Même chose pour la barre rapide du HUD (HotkeyBar : éléments indexés par la colonne x de la ligne 0).
        // ElementData est un type privé : accès par réflexion.
        private static readonly System.Reflection.FieldInfo s_hotbarElementsField = HarmonyLib.AccessTools.Field(typeof(HotkeyBar), "m_elements");
        private static readonly System.Reflection.FieldInfo s_hotbarAmountField =
            HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.Inner(typeof(HotkeyBar), "ElementData"), "m_amount");

        [HarmonyPatch(typeof(HotkeyBar), "UpdateIcons")]
        [HarmonyPostfix]
        private static void HotkeyBar_UpdateIcons(HotkeyBar __instance, Player player)
        {
            if (!Plugin.Enabled.Value || Plugin.MaxStackSize.Value <= 0 || player == null) return;
            var inv = player.GetInventory();
            var elements = s_hotbarElementsField?.GetValue(__instance) as System.Collections.IList;
            if (inv == null || elements == null || s_hotbarAmountField == null) return;
            foreach (var item in inv.GetAllItems())
            {
                if (item.m_gridPos.y != 0 || item.m_shared.m_maxStackSize < 1000) continue;
                int x = item.m_gridPos.x;
                if (x < 0 || x >= elements.Count || elements[x] == null) continue;
                if (s_hotbarAmountField.GetValue(elements[x]) is TMPro.TMP_Text amount) { string txt = StackText(item.m_stack); if (amount.text != txt) amount.text = txt; }
            }
        }
        // ---- Piles ----
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, int> s_original = new Dictionary<ItemDrop.ItemData.SharedData, int>();

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void ObjectDB_Awake(ObjectDB __instance) => ApplyStacks(__instance);

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void ObjectDB_CopyOtherDB(ObjectDB __instance) => ApplyStacks(__instance);

        private static int Target(int vanilla) => (!Plugin.Enabled.Value || Plugin.MaxStackSize.Value <= 0) ? vanilla : Plugin.MaxStackSize.Value;

        private static void ApplyStacks(ObjectDB db)
        {
            int count = 0;
            foreach (var prefab in db.m_items)
            {
                var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared == null || shared.m_maxStackSize <= 1 || s_original.ContainsKey(shared)) continue;
                s_original[shared] = shared.m_maxStackSize;
                shared.m_maxStackSize = Target(shared.m_maxStackSize);
                count++;
            }
            if (count > 0) Plugin.Log.LogInfo($"Piles portées à {Plugin.MaxStackSize.Value} sur {count} items empilables");
        }

        internal static void ReapplyStacks()
        {
            foreach (var kv in s_original) kv.Key.m_maxStackSize = Target(kv.Value);
            if (s_original.Count > 0) Plugin.Log.LogInfo($"Piles réappliquées ({Plugin.MaxStackSize.Value}) sur {s_original.Count} items");
        }

        // ---- Lignes ----
        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        private static void Player_OnSpawned(Player __instance) => ApplyRows(__instance);

        internal static void ApplyRows(Player player)
        {
            if (player == null || !Plugin.Enabled.Value) return;
            int rows = Plugin.InventoryRows.Value + (Plugin.EquipmentSlots.Value ? 1 : 0); // une ligne de plus : les cases d'équipement
            var inv = player.GetInventory();
            if (inv == null || inv.GetHeight() == rows) return;

            // Réduction : seulement si les lignes à retirer sont vides, sinon le jeu jetterait leurs objets au sol.
            if (rows < inv.GetHeight())
            {
                int blocking = 0;
                foreach (var item in inv.GetAllItems()) if (item.m_gridPos.y >= rows) blocking++;
                if (blocking > 0)
                {
                    string msg = $"Inventaire : videz d'abord les lignes {rows + 1}-{inv.GetHeight()} ({blocking} objet(s)) pour passer à {rows} lignes";
                    player.Message(MessageHud.MessageType.Center, msg);
                    Plugin.Log.LogWarning(msg);
                    return;
                }
            }
            player.SetInventorySize(rows);
            Plugin.Log.LogInfo($"Inventaire passé à {rows} lignes");
        }
    }

    /// <summary>
    /// Où va un objet ramassé. Le jeu remplit l'inventaire par le BAS pour tout ce qui n'est pas une arme (TopFirst) :
    /// avec 4 lignes on ne le remarque pas, avec 20 lignes chaque ramassage part tout en bas et il faut trier sans
    /// arrêt. Ici : première case libre en partant du haut, la barre d'action (ligne 1) restant en dernier recours,
    /// comme dans le jeu. Les armes gardent le comportement du jeu (elles cherchent d'abord une place dans la barre).
    /// </summary>
    [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
    internal static class FirstFreeSlot
    {
        [HarmonyPrefix]
        private static bool Prefix(Inventory __instance, bool topFirst, ref Vector2i __result)
        {
            if (!Plugin.Enabled.Value) return true;
            var p = Player.m_localPlayer;
            if (p == null || !ReferenceEquals(__instance, p.GetInventory())) return true;
            int reserved = Equipment.ReservedRow(__instance);
            if (reserved < 0 && (topFirst || !Plugin.FillTopFirst.Value)) return true; // comportement du jeu, rien à réserver
            if (topFirst || Plugin.FillTopFirst.Value) { __result = Equipment.FirstFree(__instance, topFirst); return false; }
            // Remplissage par le bas (réglage du jeu conservé) mais sans la ligne réservée
            int w = __instance.GetWidth();
            for (int y = reserved - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                    if (__instance.GetItemAt(x, y) == null) { __result = new Vector2i(x, y); return false; }
            __result = new Vector2i(-1, -1);
            return false;
        }
    }

    /// <summary>
    /// Garde-fou, toujours actif : au chargement d'un inventaire (personnage, coffre), le jeu fait
    /// pile = min(pile sauvegardée, m_maxStackSize actuel). Si la taille max a baissé entre-temps (mod désactivé,
    /// curseur baissé, mod retiré...), l'excédent est détruit sans prévenir. On relève temporairement la taille max
    /// du prefab le temps de l'appel pour que rien ne soit jamais tronqué.
    /// </summary>
    [HarmonyPatch]
    internal static class LoadGuard
    {
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(Inventory), "AddItem", new[]
        {
            typeof(int), typeof(int), typeof(float), typeof(Vector2i), typeof(bool), typeof(int), typeof(int), typeof(long),
            typeof(string), typeof(Dictionary<string, string>), typeof(int), typeof(bool), typeof(bool), typeof(bool)
        });

        [HarmonyPrefix]
        private static void Prefix(int prefabHash, int stack, out KeyValuePair<ItemDrop.ItemData.SharedData, int> __state)
        {
            __state = default;
            var db = ObjectDB.instance;
            var shared = db != null ? db.GetItemPrefab(prefabHash)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            // Uniquement les objets empilables : les tables de butin des coffres produisent parfois « 2 torches » ou
            // « 2 haches » que le jeu ramène silencieusement à 1, ce n'est pas une perte à protéger.
            if (shared == null || shared.m_maxStackSize <= 1 || stack <= shared.m_maxStackSize) return;
            __state = new KeyValuePair<ItemDrop.ItemData.SharedData, int>(shared, shared.m_maxStackSize);
            shared.m_maxStackSize = stack;
            Plugin.Log.LogWarning($"Pile de {stack} × {shared.m_name} plus grande que le max actuel ({__state.Value}) : conservée intacte");
        }

        [HarmonyFinalizer]
        private static void Finalizer(KeyValuePair<ItemDrop.ItemData.SharedData, int> __state)
        {
            if (__state.Key != null) __state.Key.m_maxStackSize = __state.Value;
        }
    }
}
