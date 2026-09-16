using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace CraftFromChests
{
    /// <summary>
    /// Artisanat, construction et stations (feu, four, fonderie, cuisson, fermenteur) puisent dans les coffres alentour.
    ///
    /// Principe : tout ce que le jeu fait avec les ressources passe par quatre méthodes de l'inventaire du joueur :
    /// CountItems / HaveItem (vérification et affichage), RemoveItem(nom, quantité) (consommation) et GetItem
    /// (les stations y prennent l'objet à ajouter). Pendant les seules opérations concernées (« portée »), ces
    /// méthodes voient aussi le contenu des coffres proches. Aucun menu ni station n'est modifié : le jeu croit
    /// simplement que les objets sont sur le joueur.
    /// </summary>
    [BepInPlugin(Guid, "Craft From Chests", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.craftfromchests";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Range;
        internal static ConfigEntry<bool> Stations;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le mod (artisanat depuis les coffres)."));
            Range = Config.Bind("General", "Range", 30f,
                new ConfigDescription(L.T("Rayon (m) autour du joueur dans lequel les coffres sont utilisés."), new AcceptableValueRange<float>(2f, 200f)));
            Stations = Config.Bind("General", "Stations", true,
                L.T("Feu, four, fonderie, station de cuisson et fermenteur prennent aussi dans les coffres (touche E)."));

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(ScopePatches));
            harmony.PatchAll(typeof(InventoryPatches));
            harmony.PatchAll(typeof(ContainerRegistry));
            Log.LogInfo($"Craft From Chests chargé (rayon {Range.Value} m, stations={Stations.Value})");
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Coffres accessibles
    // ------------------------------------------------------------------------------------------------
    internal static class ContainerRegistry
    {
        private static readonly List<Container> s_all = new List<Container>();
        // Délégué typé plutôt que MethodInfo.Invoke : pas de boxing ni de tableau d'arguments à chaque rafraîchissement.
        private static readonly Func<Container, long, bool> s_checkAccess = AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
        private static readonly Dictionary<Container, ZNetView> s_views = new Dictionary<Container, ZNetView>();

        // Cache court : les menus appellent CountItems plusieurs fois par image.
        private static readonly List<Container> s_cache = new List<Container>();
        private static float s_cacheTime = -1f;
        private const float CacheSeconds = 0.25f;

        [HarmonyPatch(typeof(Container), "Awake")]
        [HarmonyPostfix]
        private static void Container_Awake(Container __instance) { s_all.Add(__instance); if (__instance.GetComponent<TombStone>() == null) s_views[__instance] = __instance.GetComponent<ZNetView>(); }

        /// <summary>Coffres utilisables par le joueur local, du plus proche au plus lointain.</summary>
        // Cache des comptes par (nom, qualité, niveau de monde) : les menus recomptent chaque ressource plusieurs fois
        // par image. Invalidé avec la liste des coffres, et dès qu'on retire quelque chose d'un coffre.
        private struct CountKey : IEquatable<CountKey>
        {
            public string Name; public int Quality; public bool WorldLevel;
            public bool Equals(CountKey o) => Quality == o.Quality && WorldLevel == o.WorldLevel && string.Equals(Name, o.Name);
            public override bool Equals(object o) => o is CountKey k && Equals(k);
            public override int GetHashCode() => (Name != null ? Name.GetHashCode() : 0) * 31 + Quality * 2 + (WorldLevel ? 1 : 0);
        }
        private static readonly Dictionary<CountKey, int> s_counts = new Dictionary<CountKey, int>(); // clé structurée : aucune chaîne allouée par appel

        internal static int CountInContainers(string name, int quality, bool matchWorldLevel)
        {
            var list = Nearby();
            var key = new CountKey { Name = name, Quality = quality, WorldLevel = matchWorldLevel };
            if (s_counts.TryGetValue(key, out int cached)) return cached;
            int n = 0;
            foreach (var c in list) n += c.GetInventory().CountItems(name, quality, matchWorldLevel);
            s_counts[key] = n;
            return n;
        }

        internal static void InvalidateCounts() => s_counts.Clear();

        internal static List<Container> Nearby()
        {
            if (Time.unscaledTime - s_cacheTime < CacheSeconds) return s_cache;
            s_cacheTime = Time.unscaledTime;
            s_counts.Clear();
            var result = s_cache;
            result.Clear();
            var player = Player.m_localPlayer;
            if (player == null) return result;

            if (s_all.RemoveAll(c => c == null) > 0) // objets Unity détruits
            {
                s_dead.Clear(); foreach (var kv in s_views) if (kv.Key == null) s_dead.Add(kv.Key);
                foreach (var d in s_dead) s_views.Remove(d);
            }
            long playerId = Game.instance != null ? Game.instance.GetPlayerProfile().GetPlayerID() : 0L;
            float rangeSq = Plugin.Range.Value * Plugin.Range.Value;
            var origin = player.transform.position;

            s_dist.Clear();
            foreach (var c in s_all)
            {
                if (!s_views.TryGetValue(c, out var nview) || nview == null || !nview.IsValid()) continue; // pierre tombale ou pas encore en réseau
                float d = (c.transform.position - origin).sqrMagnitude;
                if (d > rangeSq) continue;
                if (c.GetInventory() == null) continue;
                if (c.IsInUse()) continue;                                   // quelqu'un l'a ouvert
                if (!s_checkAccess(c, playerId)) continue;                   // coffre privé d'un autre joueur
                s_dist[c] = d;
                result.Add(c);
            }
            result.Sort(s_byDistance);
            return result;
        }
        private static readonly List<Container> s_dead = new List<Container>();
        private static readonly Dictionary<Container, float> s_dist = new Dictionary<Container, float>();
        private static readonly Comparison<Container> s_byDistance = (a, b) => s_dist[a].CompareTo(s_dist[b]);

        /// <summary>Modifier un coffre exige d'en être propriétaire réseau ; on le réclame si besoin.</summary>
        internal static bool Own(Container c)
        {
            if (c.IsOwner()) return true;
            var nview = c.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;
            nview.ClaimOwnership();
            return true;
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Portée : pendant quelles opérations l'inventaire du joueur « voit » les coffres
    // ------------------------------------------------------------------------------------------------
    internal static class Scope
    {
        private static int s_depth;
        internal static bool Active => s_depth > 0 && Plugin.Enabled.Value && Player.m_localPlayer != null;
        internal static void Enter() => s_depth++;
        internal static void Exit() => s_depth = Math.Max(0, s_depth - 1);

        internal static bool IsLocalPlayerInventory(Inventory inv)
        {
            var p = Player.m_localPlayer;
            return p != null && inv != null && ReferenceEquals(inv, p.GetInventory());
        }
    }

    [HarmonyPatch]
    internal static class ScopePatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var reqMode = AccessTools.Inner(typeof(Player), "RequirementMode");
            yield return AccessTools.Method(typeof(Player), "HaveRequirementItems");
            yield return AccessTools.Method(typeof(Player), "HaveRequirements", new[] { typeof(Piece), reqMode });
            yield return AccessTools.Method(typeof(Player), nameof(Player.ConsumeResources));
            yield return AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement));
            // Stations : l'objet est pris dans l'inventaire du joueur quand on appuie sur E
            yield return AccessTools.Method(typeof(Fireplace), nameof(Fireplace.Interact));
            yield return AccessTools.Method(typeof(Smelter), "OnAddOre");
            yield return AccessTools.Method(typeof(Smelter), "OnAddFuel");
            yield return AccessTools.Method(typeof(Smelter), "OnHoverAddOre");
            yield return AccessTools.Method(typeof(Smelter), "OnHoverAddFuel");
            yield return AccessTools.Method(typeof(CookingStation), "OnInteract");
            yield return AccessTools.Method(typeof(Fermenter), nameof(Fermenter.Interact));
        }

        [HarmonyPrefix]
        private static void Prefix(MethodBase __originalMethod)
        {
            if (!Plugin.Stations.Value && IsStationMethod(__originalMethod)) return;
            Scope.Enter();
        }

        // Finalizer : s'exécute même si l'original lève une exception, la portée ne reste jamais ouverte.
        [HarmonyFinalizer]
        private static void Finalizer(MethodBase __originalMethod)
        {
            if (!Plugin.Stations.Value && IsStationMethod(__originalMethod)) return;
            Scope.Exit();
        }

        private static bool IsStationMethod(MethodBase m)
        {
            var t = m.DeclaringType;
            return t == typeof(Fireplace) || t == typeof(Smelter) || t == typeof(CookingStation) || t == typeof(Fermenter);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Les quatre méthodes d'inventaire, étendues aux coffres dans la portée
    // ------------------------------------------------------------------------------------------------
    internal static class InventoryPatches
    {
        // Même filtre que Inventory.RemoveItem(string...) : nom, qualité (si >= 0), niveau de monde.
        private static int RawCount(Inventory inv, string name, int quality, bool worldLevelBased)
        {
            int n = 0;
            foreach (var item in inv.GetAllItems())
            {
                if (item.m_shared.m_name != name) continue;
                if (quality >= 0 && item.m_quality != quality) continue;
                if (worldLevelBased && item.m_worldLevel < Game.m_worldLevel) continue;
                n += item.m_stack;
            }
            return n;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
        [HarmonyPostfix]
        private static void CountItems(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            if (!Scope.Active || !Scope.IsLocalPlayerInventory(__instance)) return;
            __result += ContainerRegistry.CountInContainers(name, quality, matchWorldLevel);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), typeof(string), typeof(bool))]
        [HarmonyPostfix]
        private static void HaveItem(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
        {
            if (__result || !Scope.Active || !Scope.IsLocalPlayerInventory(__instance)) return;
            foreach (var c in ContainerRegistry.Nearby())
                if (c.GetInventory().HaveItem(name, matchWorldLevel)) { __result = true; return; }
        }

        /// <summary>
        /// Les stations prennent l'objet via GetItem puis le retirent de l'inventaire du joueur.
        /// Si le joueur ne l'a pas, on en rapatrie UN exemplaire depuis un coffre : le jeu fait ensuite le reste.
        /// </summary>
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetItem), typeof(string), typeof(int), typeof(bool))]
        [HarmonyPostfix]
        private static void GetItem(Inventory __instance, string name, int quality, bool isPrefabName, ref ItemDrop.ItemData __result)
        {
            if (__result != null || !Scope.Active || !Scope.IsLocalPlayerInventory(__instance)) return;
            foreach (var c in ContainerRegistry.Nearby())
            {
                var cInv = c.GetInventory();
                var item = cInv.GetItem(name, quality, isPrefabName);
                if (item == null || !ContainerRegistry.Own(c)) continue;

                var one = item.Clone();
                one.m_stack = 1;
                if (!__instance.AddItem(one)) return; // inventaire du joueur plein : message vanilla
                cInv.RemoveItem(item, 1);
                ContainerRegistry.InvalidateCounts();
                __result = one;
                return;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
        [HarmonyPrefix]
        private static void RemoveItem_Prefix(Inventory __instance, string name, int itemQuality, bool worldLevelBased, out int __state)
        {
            __state = Scope.Active && Scope.IsLocalPlayerInventory(__instance) ? RawCount(__instance, name, itemQuality, worldLevelBased) : -1;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
        [HarmonyPostfix]
        private static void RemoveItem_Postfix(string name, int amount, int itemQuality, bool worldLevelBased, int __state)
        {
            if (__state < 0) return;
            int remaining = amount - Math.Min(__state, amount); // ce que le joueur n'avait pas sur lui
            if (remaining <= 0) return;

            foreach (var c in ContainerRegistry.Nearby())
            {
                var cInv = c.GetInventory();
                int have = cInv.CountItems(name, itemQuality, worldLevelBased);
                int take = Math.Min(have, remaining);
                if (take <= 0 || !ContainerRegistry.Own(c)) continue;
                cInv.RemoveItem(name, take, itemQuality, worldLevelBased);
                ContainerRegistry.InvalidateCounts();
                remaining -= take;
                if (remaining <= 0) return;
            }
            if (remaining > 0) Plugin.Log.LogWarning($"Il manquait {remaining} × {name} : le jeu avait pourtant validé les ressources (coffre vidé entre-temps ?)");
        }
    }
}
