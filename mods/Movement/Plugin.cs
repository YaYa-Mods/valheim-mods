using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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

        private void Awake()
        {
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
    }
}
