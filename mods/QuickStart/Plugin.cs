using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace QuickStart
{
    /// <summary>
    /// Démarrage rapide.
    ///  - SkipIntro : la cinématique d'intro (CinematicsManager.m_introOnStartup) est désactivée avant que le menu
    ///    ne la lance → on arrive directement sur le menu principal. (Le logo Unity, lui, précède tout code de mod.)
    ///  - AutoLoadLastWorld : au premier passage par le menu, le mod « clique » lui-même Démarrer → dernier personnage
    ///    → dernier monde (ceux que le jeu présélectionne déjà via ses préférences). Une seule fois par lancement :
    ///    revenir au menu depuis une partie ne relance rien, on peut alors choisir une autre partie.
    /// </summary>
    [BepInPlugin(Guid, "Quick Start", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.quickstart";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SkipIntro;
        internal static ConfigEntry<bool> AutoLoadLastWorld;
        internal static ConfigEntry<float> AutoLoadDelay;
        internal static ConfigEntry<string> AutoLoadCharacter, AutoLoadWorld;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le mod."));
            SkipIntro = Config.Bind("General", "SkipIntro", true, L.T("Saute la cinématique d'intro : arrivée directe sur le menu principal."));
            AutoLoadLastWorld = Config.Bind("General", "AutoLoadLastWorld", false,
                L.T("Au lancement du jeu, charge automatiquement le dernier personnage dans le dernier monde joué. ") +
                L.T("Une seule fois par lancement : revenir au menu depuis une partie laisse le choix libre."));
            AutoLoadDelay = Config.Bind("General", "AutoLoadDelay", 0.15f,
                new ConfigDescription(L.T("Secondes d'attente entre chaque étape automatique (menu → personnage → monde)."), new AcceptableValueRange<float>(0.1f, 5f)));
            AutoLoadCharacter = Config.Bind("General", "AutoLoadCharacter", "", L.T("Nom du personnage à charger automatiquement (vide = dernier joué). Utilisé par le harnais de test pour ne JAMAIS toucher la vraie partie."));
            AutoLoadWorld = Config.Bind("General", "AutoLoadWorld", "", L.T("Nom du monde à charger automatiquement (vide = dernier joué). Si le personnage ou le monde demandé n'existe pas, rien n'est chargé."));

            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Log.LogInfo($"Quick Start chargé (SkipIntro={SkipIntro.Value}, AutoLoadLastWorld={AutoLoadLastWorld.Value})");
        }
    }

    internal static class Patches
    {
        private static bool s_autoStartDone; // une fois par lancement du jeu

        private static readonly AccessTools.FieldRef<FejdStartup, List<PlayerProfile>> s_profiles = AccessTools.FieldRefAccess<FejdStartup, List<PlayerProfile>>("m_profiles");
        private static readonly AccessTools.FieldRef<FejdStartup, int> s_profileIndex = AccessTools.FieldRefAccess<FejdStartup, int>("m_profileIndex");
        private static readonly AccessTools.FieldRef<FejdStartup, World> s_world = AccessTools.FieldRefAccess<FejdStartup, World>("m_world");
        private static readonly AccessTools.FieldRef<FejdStartup, List<World>> s_worlds = AccessTools.FieldRefAccess<FejdStartup, List<World>>("m_worlds");
        // Résolus au premier usage (jamais dans un initialiseur statique : une signature qui change ne doit pas empêcher le mod de charger)
        private static Action<FejdStartup, string> s_setProfile;
        private static Action<FejdStartup, int, bool> s_setWorld;
        private static bool ResolveSelectors()
        {
            if (s_setProfile != null && s_setWorld != null) return true;
            try
            {
                s_setProfile = AccessTools.MethodDelegate<Action<FejdStartup, string>>(AccessTools.Method(typeof(FejdStartup), "SetSelectedProfile", new[] { typeof(string) }));
                s_setWorld = AccessTools.MethodDelegate<Action<FejdStartup, int, bool>>(AccessTools.Method(typeof(FejdStartup), "SetSelectedWorld", new[] { typeof(int), typeof(bool) }));
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Sélection par nom indisponible : " + ex.Message); }
            return s_setProfile != null && s_setWorld != null;
        }

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        [HarmonyPrefix]
        private static void Start_Prefix()
        {
            if (!Plugin.Enabled.Value || !Plugin.SkipIntro.Value) return;
            var cm = CinematicsManager.s_instance;
            if (cm != null && cm.m_introOnStartup)
            {
                cm.m_introOnStartup = false;
                Plugin.Log.LogInfo("Intro sautée");
            }
        }

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        [HarmonyPostfix]
        private static void Start_Postfix(FejdStartup __instance)
        {
            if (!Plugin.Enabled.Value || !Plugin.AutoLoadLastWorld.Value || s_autoStartDone) return;
            s_autoStartDone = true;
            __instance.StartCoroutine(AutoStart(__instance));
        }

        private static IEnumerator AutoStart(FejdStartup fs)
        {
            // Attendre que le menu principal soit affiché (fin de l'intro ou du fondu)
            float timeout = Time.unscaledTime + 30f;
            while ((fs.m_mainMenu == null || !fs.m_mainMenu.activeInHierarchy) && Time.unscaledTime < timeout)
                yield return null;
            if (fs.m_mainMenu == null || !fs.m_mainMenu.activeInHierarchy) { Plugin.Log.LogWarning("Auto-démarrage : menu principal jamais affiché"); yield break; }
            yield return new WaitForSecondsRealtime(Plugin.AutoLoadDelay.Value);

            // 1. « Démarrer » → sélection du personnage
            fs.OnStartGame();
            yield return new WaitForSecondsRealtime(Plugin.AutoLoadDelay.Value);

            var profiles = s_profiles(fs);
            int idx = s_profileIndex(fs);
            // Personnage imposé par la configuration : on le sélectionne par son nom, sinon on n'ouvre rien
            // Sécurité : un personnage/monde imposé n'est honoré que si le harnais de test est chargé. Sans lui (partie normale),
            // des clés laissées par un run interrompu sont ignorées : le joueur retrouve toujours sa propre partie.
            bool harnessLoaded = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("vmods.testharness");
            if (!harnessLoaded && (!string.IsNullOrEmpty(Plugin.AutoLoadCharacter.Value) || !string.IsNullOrEmpty(Plugin.AutoLoadWorld.Value)))
            {
                Plugin.Log.LogWarning("Auto-démarrage : AutoLoadCharacter/AutoLoadWorld ignorés (harnais de test absent), et effacés");
                Plugin.AutoLoadCharacter.Value = ""; Plugin.AutoLoadWorld.Value = "";
            }
            if (!string.IsNullOrEmpty(Plugin.AutoLoadCharacter.Value) || !string.IsNullOrEmpty(Plugin.AutoLoadWorld.Value))
                if (!ResolveSelectors()) { Plugin.Log.LogWarning("Auto-démarrage : sélection par nom impossible, rien n'est chargé"); yield break; }
            if (!string.IsNullOrEmpty(Plugin.AutoLoadCharacter.Value))
            {
                idx = profiles != null ? profiles.FindIndex(p => string.Equals(p.GetName(), Plugin.AutoLoadCharacter.Value, StringComparison.OrdinalIgnoreCase)) : -1;
                if (idx < 0)
                {
                    // Personnage créé après l'ouverture du menu (harnais) : on relit la liste des sauvegardes
                    try { AccessTools.Method(typeof(FejdStartup), "UpdateCharacterList")?.Invoke(fs, null); profiles = s_profiles(fs); } catch (Exception ex) { Plugin.Log.LogWarning("UpdateCharacterList : " + ex.Message); }
                    idx = profiles != null ? profiles.FindIndex(p => string.Equals(p.GetName(), Plugin.AutoLoadCharacter.Value, StringComparison.OrdinalIgnoreCase)) : -1;
                }
                if (idx < 0) { Plugin.Log.LogWarning($"Auto-démarrage : personnage « {Plugin.AutoLoadCharacter.Value} » introuvable, rien n'est chargé"); yield break; }
                s_setProfile(fs, profiles[idx].GetFilename()); // le jeu sélectionne un profil par son nom de fichier
                yield return new WaitForSecondsRealtime(Plugin.AutoLoadDelay.Value);
            }
            if (profiles == null || profiles.Count == 0 || idx < 0 || idx >= profiles.Count)
            {
                Plugin.Log.LogWarning("Auto-démarrage : aucun personnage sélectionnable, on reste sur le menu");
                yield break;
            }
            Plugin.Log.LogInfo($"Auto-démarrage : personnage « {profiles[idx].GetName()} »");

            // 2. Personnage → liste des mondes (le dernier monde joué est présélectionné par le jeu)
            fs.OnCharacterStart();
            yield return new WaitForSecondsRealtime(Plugin.AutoLoadDelay.Value);

            // Monde imposé par la configuration
            if (!string.IsNullOrEmpty(Plugin.AutoLoadWorld.Value))
            {
                var worlds = s_worlds(fs);
                int wi = worlds != null ? worlds.FindIndex(w => string.Equals(w.m_name, Plugin.AutoLoadWorld.Value, StringComparison.OrdinalIgnoreCase)) : -1;
                if (wi < 0) { Plugin.Log.LogWarning($"Auto-démarrage : monde « {Plugin.AutoLoadWorld.Value} » introuvable, rien n'est chargé"); yield break; }
                s_setWorld(fs, wi, true);
                yield return new WaitForSecondsRealtime(Plugin.AutoLoadDelay.Value);
            }
            var world = s_world(fs);
            if (!string.IsNullOrEmpty(Plugin.AutoLoadWorld.Value) && (world == null || !string.Equals(world.m_name, Plugin.AutoLoadWorld.Value, StringComparison.OrdinalIgnoreCase)))
            {
                Plugin.Log.LogWarning($"Auto-démarrage : le monde sélectionné n'est pas « {Plugin.AutoLoadWorld.Value} », rien n'est chargé");
                yield break;
            }
            if (world == null)
            {
                Plugin.Log.LogWarning("Auto-démarrage : aucun monde présélectionné, on reste sur la liste des mondes");
                yield break;
            }
            Plugin.Log.LogInfo($"Auto-démarrage : monde « {world.m_name} »");

            // 3. « Démarrer » le monde
            fs.OnWorldStart();
        }
    }
}
