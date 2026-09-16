using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace LongerFood
{
    /// <summary>
    /// Allonge la durée des aliments à effets positifs.
    /// Chaque aliment porte <c>m_foodBurnTime</c> (secondes) dans le SharedData de son prefab ; Player.EatFood
    /// l'utilise comme durée initiale et Player.UpdateFood pour la courbe de décroissance des stats. En multipliant
    /// cette valeur sur les prefabs, l'aliment donne les mêmes stats, plus longtemps, et le tooltip suit.
    /// Seuls les items dont toutes les valeurs nourriture sont ≥ 0 (et au moins une > 0) sont touchés :
    /// les effets de statut des potions/hydromels (positifs ou négatifs) ont leur propre durée, non modifiée.
    /// La valeur d'origine de chaque aliment est conservée : un changement de config se réapplique à la volée.
    /// </summary>
    [BepInPlugin(Guid, "Longer Food", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.longerfood";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> FoodDurationMultiplier;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Active le mod (durée des aliments allongée).");
            Enabled.SettingChanged += (_, __) => Patches.Reapply();
            FoodDurationMultiplier = Config.Bind("General", "FoodDurationMultiplier", 1.5f,
                new ConfigDescription(
                    "Multiplicateur de la durée des aliments à effets positifs. 1.0 = vanilla, 1.5 = +50%, 1.67 ≈ 3 min → 5 min.",
                    new AcceptableValueRange<float>(1f, 3f)));
            FoodDurationMultiplier.SettingChanged += (_, __) => Patches.Reapply();

            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Log.LogInfo($"Longer Food chargé (FoodDurationMultiplier={FoodDurationMultiplier.Value})");
        }
    }

    [HarmonyPatch(typeof(ObjectDB))]
    internal static class Patches
    {
        // SharedData → durée vanilla. Les SharedData sont partagés par tous les exemplaires d'un item.
        private static readonly Dictionary<ItemDrop.ItemData.SharedData, float> s_original = new Dictionary<ItemDrop.ItemData.SharedData, float>();

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        private static void AfterAwake(ObjectDB __instance) => Apply(__instance);

        [HarmonyPatch(nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void AfterCopyOtherDB(ObjectDB __instance) => Apply(__instance);

        private static bool IsPositiveFood(ItemDrop.ItemData.SharedData s)
        {
            if (s.m_food < 0f || s.m_foodStamina < 0f || s.m_foodEitr < 0f || s.m_foodRegen < 0f) return false;
            return s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f;
        }

        private static void Apply(ObjectDB db)
        {
            float mult = Plugin.Enabled.Value ? Plugin.FoodDurationMultiplier.Value : 1f;
            int count = 0;
            string example = null;

            foreach (var prefab in db.m_items)
            {
                var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared == null || !IsPositiveFood(shared) || s_original.ContainsKey(shared)) continue;

                float before = shared.m_foodBurnTime;
                s_original[shared] = before;
                shared.m_foodBurnTime = before * mult;
                count++;

                if (prefab.name == "Raspberry" || example == null)
                    example = $"{prefab.name} : {before / 60f:0.#} min → {shared.m_foodBurnTime / 60f:0.#} min";
            }

            if (count > 0)
                Plugin.Log.LogInfo($"Durée × {mult} appliquée à {count} aliments (ex. {example})");
        }

        /// <summary>Changement de config en jeu : on repart des valeurs vanilla conservées.</summary>
        internal static void Reapply()
        {
            float mult = Plugin.Enabled.Value ? Plugin.FoodDurationMultiplier.Value : 1f;
            foreach (var kv in s_original) kv.Key.m_foodBurnTime = kv.Value * mult;
            if (s_original.Count > 0) Plugin.Log.LogInfo($"Durée × {mult} réappliquée à {s_original.Count} aliments");
        }
    }
}
