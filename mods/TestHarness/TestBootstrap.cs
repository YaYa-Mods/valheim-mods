using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace TestHarness
{
    /// <summary>
    /// Le harnais ne doit JAMAIS toucher à la vraie partie : il joue sur un personnage et un monde qui lui sont propres,
    /// créés ici s'ils manquent (sauvegardes locales, pas le cloud Steam), et QuickStart est configuré pour les charger
    /// par leur nom (AutoLoadCharacter / AutoLoadWorld). Si la création échoue, le harnais s'interdit de tourner.
    /// </summary>
    internal static class TestBootstrap
    {
        public const string CharacterName = "ModTester";
        public const string WorldName = "ModTestWorld";
        public const string WorldSeed = "modtestseed";
        public static bool Ready { get; private set; }
        public static string Error { get; private set; }

        [HarmonyPatch(typeof(FejdStartup), "Start")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void FejdStartup_Start()
        {
            if (!Plugin.AutoRun.Value) return;
            try
            {
                EnsureCharacter();
                EnsureWorld();
                Ready = true;
                Plugin.Log.LogInfo($"[TEST] environnement de test prêt : personnage « {CharacterName} », monde « {WorldName} » (graine {WorldSeed}), sauvegardes locales");
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Plugin.Log.LogError("[TEST] environnement de test impossible à préparer, tests annulés : " + ex);
            }
        }

        private static void EnsureCharacter()
        {
            var local = FileHelpers.FileSource.Local;
            var existing = SaveSystem.GetAllPlayerProfiles();
            if (existing != null && existing.Any(p => p != null && string.Equals(p.GetName(), CharacterName, StringComparison.OrdinalIgnoreCase))) return;
            var profile = new PlayerProfile(CharacterName, local);
            profile.SetName(CharacterName);
            try { typeof(PlayerProfile).GetField("m_firstSpawn", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(profile, false); } catch { } // pas de trajet en valkyrie pour le cobaye
            if (!profile.Save()) throw new Exception("échec de la sauvegarde du personnage de test");
            Plugin.Log.LogInfo($"[TEST] personnage de test « {CharacterName} » créé (local)");
        }

        private static void EnsureWorld()
        {
            var worlds = SaveSystem.GetWorldList();
            if (worlds != null && worlds.Any(w => w != null && string.Equals(w.m_name, WorldName, StringComparison.OrdinalIgnoreCase))) return;
            var world = new World(WorldName, WorldSeed);
            world.m_fileSource = FileHelpers.FileSource.Local;
            // Écriture des métadonnées (.fwl) : nom de méthode selon la version du jeu
            world.SaveWorldFWLData(DateTime.Now); // écrit le .fwl dans worlds_local
            SaveSystem.ClearWorldListCache(true);
            Plugin.Log.LogInfo($"[TEST] monde de test « {WorldName} » créé (local, graine {WorldSeed})");
        }
    }
}
