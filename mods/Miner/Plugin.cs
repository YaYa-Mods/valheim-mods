using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace Miner
{
    /// <summary>
    /// Minage des filons plus rapide, sur les minerais uniquement (cuivre, étain, fer, argent, obsidienne, flamétal...) :
    /// les rochers de pierre ordinaires ne changent pas. Même principe que le bûcheron, la compétence compte.
    ///  - Dégâts de pioche multipliés (DamageMultiplier, plus SkillBonus au niveau 100 de Pioche, proportionnel au niveau).
    ///  - Coup en zone sur les gros filons (MineRock5, faits de dizaines de morceaux) : le jeu sait déjà appliquer un
    ///    coup à tous les morceaux dans un rayon (HitData.m_radius, qu'il n'utilise pas pour la pioche du joueur) ; on lui
    ///    donne AreaRadius mètres. Un filon de cuivre tombe en quelques coups au lieu de plusieurs minutes.
    /// Le niveau d'outil exigé par le jeu (pioche en bronze pour le fer...) reste exigé.
    /// </summary>
    [BepInPlugin(Guid, "Miner", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.miner";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> DamageMultiplier, SkillBonus, AreaRadius;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le minage rapide des filons de minerai (la pierre ordinaire ne change pas)."));
            DamageMultiplier = Config.Bind("General", "DamageMultiplier", 3f,
                new ConfigDescription(L.T("Multiplicateur des dégâts de pioche sur les minerais, quel que soit le niveau."), new AcceptableValueRange<float>(1f, 20f)));
            SkillBonus = Config.Bind("General", "SkillBonus", 3f,
                new ConfigDescription(L.T("Multiplicateur ajouté au niveau 100 de Pioche, proportionnel au niveau (3 : ×3 de base devient ×6 au niveau 100)."), new AcceptableValueRange<float>(0f, 20f)));
            AreaRadius = Config.Bind("General", "AreaRadius", 1.5f,
                new ConfigDescription(L.T("Rayon (m) du coup de pioche sur les gros filons : les morceaux voisins sont touchés aussi. 0 = un seul morceau, comme le jeu."), new AcceptableValueRange<float>(0f, 5f)));
            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Log.LogInfo($"Miner chargé (×{DamageMultiplier.Value} + ×{SkillBonus.Value} au niveau 100, rayon {AreaRadius.Value} m)");
        }

        /// <summary>Multiplicateur appliqué à la pioche du joueur local, selon son niveau de Pioche.</summary>
        internal static float Multiplier()
        {
            var p = Player.m_localPlayer;
            float level = p != null ? p.GetSkills().GetSkillLevel(Skills.SkillType.Pickaxes) : 0f;
            return DamageMultiplier.Value + SkillBonus.Value * Mathf.Clamp01(level / 100f);
        }
    }

    internal static class Patches
    {
        // Un rocher est un filon de minerai si sa table de butin contient un minerai : lu dans le jeu à chaque coup (quelques
        // entrées, rien à mettre en cache). Surtout pas de cache par nom d'objet : tous les gros filons s'appellent
        // « ___MineRock5 » une fois posés, un cache par nom classait la pierre comme le premier filon de cuivre frappé.
        internal static bool IsOreItem(string itemName) =>
            itemName.IndexOf("Ore", StringComparison.Ordinal) >= 0 || itemName.IndexOf("Scrap", StringComparison.Ordinal) >= 0 || itemName == "Obsidian";

        internal static bool IsOre(Component rock, DropTable drops)
        {
            if (drops?.m_drops == null) return false;
            foreach (var d in drops.m_drops)
                if (d.m_item != null && IsOreItem(d.m_item.name)) return true;
            return false;
        }

        /// <summary>Nom du prefab d'un objet posé (lu dans ses données réseau : le nom de l'objet ne suffit pas).</summary>
        internal static string PrefabName(Component c)
        {
            var nview = c.GetComponentInParent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            var prefab = zdo != null && ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            return prefab != null ? prefab.name : Utils.GetPrefabName(c.gameObject);
        }

        private static bool Applies(HitData hit) =>
            Plugin.Enabled.Value && hit != null && hit.m_damage.m_pickaxe > 0f && Player.m_localPlayer != null && hit.GetAttacker() == Player.m_localPlayer;

        // Gros filons : dégâts multipliés et coup en zone (le jeu lit m_radius dans MineRock5.Damage)
        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
        [HarmonyPrefix]
        private static void MineRock5_Damage(MineRock5 __instance, HitData hit)
        {
            if (!Applies(hit) || !IsOre(__instance, __instance.m_dropItems)) return;
            hit.m_damage.m_pickaxe *= Plugin.Multiplier();
            if (Plugin.AreaRadius.Value > 0f && hit.m_radius < Plugin.AreaRadius.Value) hit.m_radius = Plugin.AreaRadius.Value;
            Once(__instance);
        }

        // Une ligne par type de filon et par session : ce qui a été accéléré, pour vérifier que la pierre ordinaire ne l'est pas
        private static readonly HashSet<string> s_logged = new HashSet<string>();
        private static void Once(Component rock)
        {
            string name = PrefabName(rock);
            if (s_logged.Add(name)) Plugin.Log.LogInfo($"Filon de minerai accéléré : {name} (×{Plugin.Multiplier():0.#}, rayon {Plugin.AreaRadius.Value} m)");
        }

        // Petits rochers de minerai (étain, obsidienne...) : dégâts multipliés
        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
        [HarmonyPrefix]
        private static void MineRock_Damage(MineRock __instance, HitData hit)
        {
            if (!Applies(hit) || !IsOre(__instance, __instance.m_dropItems)) return;
            hit.m_damage.m_pickaxe *= Plugin.Multiplier();
        }

        // Filon encore intact : le jeu pose un bloc entier (Destructible) qui, touché ou cassé, laisse place au filon en
        // morceaux (MineRock5). C'est un filon de minerai si ce qu'il fait apparaître en est un.
        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
        [HarmonyPrefix]
        private static void Destructible_Damage(Destructible __instance, HitData hit)
        {
            if (!Applies(hit) || !IsOreBlock(__instance)) return;
            hit.m_damage.m_pickaxe *= Plugin.Multiplier();
        }

        internal static bool IsOreBlock(Destructible d)
        {
            if (SpawnsOre(d.m_spawnWhenDamaged) || SpawnsOre(d.m_spawnWhenDestroyed)) return true;
            var dd = d.GetComponent<DropOnDestroyed>();
            return dd?.m_dropWhenDestroyed?.m_drops != null && dd.m_dropWhenDestroyed.m_drops.Any(x => x.m_item != null && IsOreItem(x.m_item.name));
        }

        private static bool SpawnsOre(GameObject spawned)
        {
            if (spawned == null) return false;
            var mr5 = spawned.GetComponent<MineRock5>(); if (mr5 != null && IsOre(mr5, mr5.m_dropItems)) return true;
            var mr = spawned.GetComponent<MineRock>(); if (mr != null && IsOre(mr, mr.m_dropItems)) return true;
            return false;
        }
    }
}
