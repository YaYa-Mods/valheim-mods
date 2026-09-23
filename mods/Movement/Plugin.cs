using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace Movement
{
    /// <summary>
    /// Déplacement plus rapide pour le joueur local. Le jeu calcule la vitesse comme
    /// vitesse de base × facteur (GetJogSpeedFactor / GetRunSpeedFactor, qui intègrent équipement et effets) :
    /// on multiplie ces facteurs. Nage : facteur de la même façon via m_swimSpeed à l'apparition.
    /// </summary>
    [BepInPlugin(Guid, "Movement", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.movement";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> SpeedMultiplier;
        internal static ConfigEntry<float> SwimMultiplier;
        internal static ConfigEntry<bool> FreeStamina;
        internal static ConfigEntry<float> CombatSeconds;

        private void Awake()
        {
            FreeStamina = Config.Bind("Stamina", "FreeOutOfCombat", true,
                L.T("Hors combat, aucune action ne consomme d'endurance (course, saut, nage, bûcheronnage, minage...). En combat (un ennemi vous cible, ou un coup reçu ou donné à une créature il y a peu), l'endurance s'use normalement."));
            CombatSeconds = Config.Bind("Stamina", "CombatSeconds", 8f,
                new ConfigDescription(L.T("Durée (secondes) pendant laquelle on reste « en combat » après le dernier coup reçu ou donné à une créature."), new AcceptableValueRange<float>(2f, 60f)));
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le mod."));
            SpeedMultiplier = Config.Bind("General", "SpeedMultiplier", 1.3f,
                new ConfigDescription(L.T("Multiplicateur de vitesse de marche/jogging/course du joueur. 1 = vanilla."), new AcceptableValueRange<float>(1f, 3f)));
            SwimMultiplier = Config.Bind("General", "SwimMultiplier", 1.3f,
                new ConfigDescription(L.T("Multiplicateur de vitesse de nage. 1 = vanilla."), new AcceptableValueRange<float>(1f, 3f)));
            SwimMultiplier.SettingChanged += (_, __) => Patches.ApplySwim(Player.m_localPlayer);
            Enabled.SettingChanged += (_, __) => Patches.ApplySwim(Player.m_localPlayer);

            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Logger.LogInfo($"Movement chargé (vitesse ×{SpeedMultiplier.Value}, nage ×{SwimMultiplier.Value})");
        }
    }

    internal static class Patches
    {
        private static bool IsLocal(Character c) => Player.m_localPlayer != null && ReferenceEquals(c, Player.m_localPlayer);

        [HarmonyPatch(typeof(Player), "GetJogSpeedFactor")] // Player redéfinit ces méthodes : patcher Character ne suffit pas
        [HarmonyPostfix]
        private static void JogFactor(Character __instance, ref float __result)
        {
            if (Plugin.Enabled.Value && IsLocal(__instance)) __result *= Plugin.SpeedMultiplier.Value;
        }

        [HarmonyPatch(typeof(Player), "GetRunSpeedFactor")]
        [HarmonyPostfix]
        private static void RunFactor(Character __instance, ref float __result)
        {
            if (Plugin.Enabled.Value && IsLocal(__instance)) __result *= Plugin.SpeedMultiplier.Value;
        }

        private static float s_vanillaSwim = -1f;

        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        private static void OnSpawned(Player __instance) => ApplySwim(__instance);

        internal static void ApplySwim(Player player)
        {
            if (player == null) return;
            if (s_vanillaSwim < 0f) s_vanillaSwim = player.m_swimSpeed;
            player.m_swimSpeed = s_vanillaSwim * (Plugin.Enabled.Value ? Plugin.SwimMultiplier.Value : 1f);
        }

        // ---- Endurance gratuite hors combat
        // Tout ce qui use l'endurance (course, saut, nage, esquive, attaque, outils) passe par Player.UseStamina : hors
        // combat, l'appel est simplement ignoré. « Combat » = un ennemi cible le joueur (Player.IsTargeted, tenu à jour par
        // l'IA des monstres) ou un coup reçu d'une créature / donné à une créature il y a moins de CombatSeconds. Frapper
        // un arbre ou un rocher n'est pas un combat.
        private static float s_lastFight = -1000f;

        internal static bool InCombat(Player p) => p.IsTargeted() || Time.time - s_lastFight < Plugin.CombatSeconds.Value;

        [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
        [HarmonyPrefix]
        private static bool Player_UseStamina(Player __instance)
        {
            if (!Plugin.Enabled.Value || !Plugin.FreeStamina.Value || !IsLocal(__instance)) return true;
            return InCombat(__instance); // hors combat : rien n'est retiré
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        [HarmonyPostfix]
        private static void Character_Damage(Character __instance, HitData hit)
        {
            var me = Player.m_localPlayer;
            if (me == null || hit == null) return;
            var attacker = hit.GetAttacker();
            // Reçu d'une créature, ou donné par le joueur à une créature (pas à un autre joueur, pas à un animal apprivoisé)
            if (ReferenceEquals(__instance, me) && attacker != null && !attacker.IsPlayer()) s_lastFight = Time.time;
            else if (ReferenceEquals(attacker, me) && !__instance.IsPlayer() && !__instance.IsTamed()) s_lastFight = Time.time;
        }

        /// <summary>Pour les tests : marque un combat qui vient d'avoir lieu (ou l'efface).</summary>
        internal static void SetFight(bool now) => s_lastFight = now ? Time.time : -1000f;
    }
}
