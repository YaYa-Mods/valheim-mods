using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace NoDurability
{
    /// <summary>
    /// Supprime la durabilité de tous les items (armes, outils, armures, torches...).
    /// Principe : chaque item du jeu partage un bloc <c>SharedData</c> avec son prefab dans l'ObjectDB.
    /// On passe <c>m_useDurability</c> à false sur chaque prefab : le jeu n'use plus rien,
    /// n'affiche plus de barre de durabilité et ne casse plus rien. Désactivable à la volée (valeurs vanilla conservées).
    /// </summary>
    [BepInPlugin(Guid, "No Durability", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.nodurability";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Plus aucune usure sur les items (armes, outils, armures, torches).");
            Enabled.SettingChanged += (_, __) => Patches.Reapply();

            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Log.LogInfo("No Durability chargé");
        }
    }

    [HarmonyPatch(typeof(ObjectDB))]
    internal static class Patches
    {
        // Items qui utilisaient la durabilité en vanilla (pour pouvoir la remettre).
        private static readonly HashSet<ItemDrop.ItemData.SharedData> s_vanillaUsesDurability = new HashSet<ItemDrop.ItemData.SharedData>();

        // ObjectDB.Awake : construction de la base d'items au chargement d'une scène.
        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        private static void AfterAwake(ObjectDB __instance) => Apply(__instance);

        // ObjectDB.CopyOtherDB : le jeu recopie la base quand on passe du menu au monde.
        [HarmonyPatch(nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void AfterCopyOtherDB(ObjectDB __instance) => Apply(__instance);

        private static void Apply(ObjectDB db)
        {
            int count = 0;
            foreach (var prefab in db.m_items)
            {
                var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared == null || s_vanillaUsesDurability.Contains(shared) || !shared.m_useDurability) continue;
                s_vanillaUsesDurability.Add(shared);
                if (Plugin.Enabled.Value) shared.m_useDurability = false;
                count++;
            }
            Plugin.Log.LogInfo($"Durabilité {(Plugin.Enabled.Value ? "désactivée" : "laissée (mod inactif)")} sur {count} items ({db.m_items.Count} au total)");
        }

        internal static void Reapply()
        {
            bool off = Plugin.Enabled.Value;
            foreach (var shared in s_vanillaUsesDurability) shared.m_useDurability = !off;
            Plugin.Log.LogInfo($"Durabilité {(off ? "désactivée" : "rétablie")} sur {s_vanillaUsesDurability.Count} items");
        }
    }
}
