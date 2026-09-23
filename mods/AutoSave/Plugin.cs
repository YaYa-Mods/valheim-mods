using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace AutoSave
{
    /// <summary>
    /// Sauvegarde automatique plus fréquente. Le jeu sauvegarde de lui-même toutes les 30 minutes (Game.m_saveInterval,
    /// fixe, sans réglage) : on lui donne l'intervalle choisi, 5 minutes par défaut. C'est la sauvegarde du jeu elle-même
    /// qui tourne (personnage et monde, avec son avertissement et son icône), rien n'est réécrit à côté.
    /// </summary>
    [BepInPlugin(Guid, "Auto Save", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.autosave";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Minutes;
        private static float s_vanilla = -1f;

        private void Awake()
        {
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active la sauvegarde automatique plus fréquente (le jeu sauvegarde seul toutes les 30 minutes)."));
            Minutes = Config.Bind("General", "Minutes", 5, new ConfigDescription(L.T("Minutes entre deux sauvegardes automatiques (personnage et monde)."), new AcceptableValueRange<int>(1, 60)));
            Enabled.SettingChanged += (_, __) => Apply();
            Minutes.SettingChanged += (_, __) => Apply();
            Harmony.CreateAndPatchAll(typeof(Plugin), Guid);
            Logger.LogInfo($"Auto Save chargé (toutes les {Minutes.Value} min)");
        }

        /// <summary>Intervalle voulu, en secondes (valeur du jeu si le mod est éteint).</summary>
        internal static float IntervalSeconds() => Enabled.Value ? Minutes.Value * 60f : (s_vanilla > 0f ? s_vanilla : 1800f);

        internal static void Apply()
        {
            if (s_vanilla < 0f) s_vanilla = Game.m_saveInterval;
            Game.m_saveInterval = IntervalSeconds();
        }

        // Une partie commence : l'intervalle du jeu est remplacé (le compteur repart de zéro comme d'habitude)
        [HarmonyPatch(typeof(Game), "Awake")]
        [HarmonyPostfix]
        private static void Game_Awake() => Apply();
    }
}
