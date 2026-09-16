using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace ShortNights
{
    /// <summary>
    /// Raccourcit la nuit sans toucher à la durée du cycle (m_dayLengthSec, 1800 s par défaut).
    ///
    /// Le jeu découpe le cycle brut [0..1] en trois morceaux via EnvMan.RescaleDayFraction :
    ///   [0 .. 0.15]    → nuit (fin)      → fraction "affichée" [0 .. 0.25]
    ///   [0.15 .. 0.85] → jour            → [0.25 .. 0.75]
    ///   [0.85 .. 1]    → nuit (début)    → [0.75 .. 1]
    /// Tout le reste (soleil, éclairage, IsNight, spawns nocturnes, sommeil) lit la fraction affichée.
    /// En vanilla la nuit occupe donc 30 % du cycle (9 min). On remplace les seuils 0.15 / 0.85 par
    /// NightFraction/2 et 1 - NightFraction/2, et on aligne l'heure de réveil au lit sur le nouveau lever du jour.
    /// </summary>
    [BepInPlugin(Guid, "Short Nights", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.shortnights";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> NightFraction;

        private void Awake()
        {
            Enabled = Config.Bind("General", "Enabled", true, "Active le mod (nuit raccourcie).");
            NightFraction = Config.Bind("General", "NightFraction", 0.15f,
                new ConfigDescription(
                    "Part du cycle jour/nuit occupée par la nuit. Vanilla : 0.30 (9 min sur 30). " +
                    "0.15 = nuit deux fois plus courte (4,5 min), le jour récupère le temps gagné.",
                    new AcceptableValueRange<float>(0.02f, 0.30f)));

            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Logger.LogInfo($"Short Nights chargé (NightFraction={NightFraction.Value})");
        }
    }

    internal static class Patches
    {
        // Seuils bruts de fin de nuit (matin) et de début de nuit (soir).
        private static float NightEnd => Plugin.NightFraction.Value * 0.5f;
        private static float NightStart => 1f - Plugin.NightFraction.Value * 0.5f;

        // Même mapping que le jeu, seuils configurables.
        [HarmonyPatch(typeof(EnvMan), "RescaleDayFraction")]
        [HarmonyPrefix]
        private static bool RescaleDayFraction(float fraction, ref float __result)
        {
            if (!Plugin.Enabled.Value) return true;
            float a = NightEnd, b = NightStart;
            if (fraction >= a && fraction <= b)
                __result = 0.25f + (fraction - a) / (b - a) * 0.5f;
            else if (fraction < 0.5f)
                __result = fraction / a * 0.25f;
            else
                __result = 0.75f + (fraction - b) / (1f - b) * 0.25f;
            return false;
        }

        // Le lit réveille le joueur au "matin" : vanilla = jour × longueur + longueur × 0.15. On aligne sur NightEnd.
        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.GetMorningStartSec))]
        [HarmonyPrefix]
        private static bool GetMorningStartSec(EnvMan __instance, int day, ref double __result)
        {
            if (!Plugin.Enabled.Value) return true;
            long len = __instance.m_dayLengthSec;
            __result = (double)((float)(day * len) + (float)len * NightEnd);
            return false;
        }
    }
}
