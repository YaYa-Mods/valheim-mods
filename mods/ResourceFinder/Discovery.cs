using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace ResourceFinder
{
    /// <summary>
    /// Immersion : une entrée du catalogue n'est proposée que si le joueur l'a « découverte ».
    ///  - Ressource : le joueur a déjà eu en main l'item qu'elle donne (Player.IsMaterialKnown, c'est ce qui
    ///    débloque les recettes en vanilla). Sans item résoluble : un biome où elle pousse a été visité.
    ///  - Lieu : le joueur a déjà visité le biome d'un des lieux correspondants (m_knownBiome).
    ///  - Créature : le joueur en a déjà tué une (statistiques du profil, m_enemyStats, clé = Character.m_name)
    ///    ou a déjà eu son trophée en main.
    /// Le joueur peut « révéler » une entrée non découverte ; c'est mémorisé par monde (fichier des couches).
    /// </summary>
    internal static class Discovery
    {
        private static readonly AccessTools.FieldRef<Player, HashSet<string>> s_knownBiome =
            AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownBiome");

        public static readonly HashSet<string> Revealed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---- « vu » : objets/créatures que le joueur a regardés (viseur, barre de vie ennemie), mémorisés sur le personnage
        private const string SeenKey = "vmods.finder.seen";
        private static readonly HashSet<string> s_seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Player s_seenFor;
        private static GameObject s_lastHover;

        private static void LoadSeen(Player p)
        {
            if (s_seenFor == p) return;
            s_seenFor = p; s_seen.Clear();
            if (p != null && p.m_customData != null && p.m_customData.TryGetValue(SeenKey, out var csv))
                foreach (var n in csv.Split(';')) if (n.Length > 0) s_seen.Add(n);
        }

        /// <summary>Le joueur a vu ce prefab (viseur ou barre de vie). Renvoie vrai si c'est nouveau.</summary>
        public static bool MarkSeen(string prefab)
        {
            var p = Player.m_localPlayer;
            if (p == null || string.IsNullOrEmpty(prefab)) return false;
            LoadSeen(p);
            if (!s_seen.Add(prefab)) return false;
            p.m_customData[SeenKey] = string.Join(";", s_seen);
            // Une entrée du catalogue vient d'être découverte par la vue : on le dit
            foreach (var e in Catalog.Entries)
                if (Array.IndexOf(e.Prefabs, prefab) >= 0 && !WasDiscoveredBefore(e, prefab))
                {
                    s_cache.Remove(e);
                    p.Message(MessageHud.MessageType.TopLeft, L.T("Scanner : ") + L.T(e.Label) + L.T(" (découvert)"));
                    break;
                }
            return true;
        }
        private static bool WasDiscoveredBefore(ResourceEntry e, string justSeen)
        {
            foreach (var pr in e.Prefabs) if (pr != justSeen && s_seen.Contains(pr)) return true;
            return Plugin.HideUndiscovered.Value ? Revealed.Contains(e.Label) || ComputeWithoutSeen(e) : true;
        }
        public static bool Seen(ResourceEntry e)
        {
            LoadSeen(Player.m_localPlayer);
            foreach (var pr in e.Prefabs) if (s_seen.Contains(pr)) return true;
            return false;
        }
        /// <summary>Nom du prefab d'un objet réseau (par le hachage du ZDO, comme le fait ZNetScene).</summary>
        public static string PrefabName(ZNetView nv)
        {
            var zdo = nv != null ? nv.GetZDO() : null;
            if (zdo == null || ZNetScene.instance == null) return null;
            var go = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            return go != null ? go.name : null;
        }

        /// <summary>Objet sous le viseur (appelé à chaque image par le HUD) : un nouvel objet regardé est marqué vu.</summary>
        public static void OnHover(GameObject hover)
        {
            if (hover == s_lastHover) return;
            s_lastHover = hover;
            if (hover == null) return;
            var nv = hover.GetComponentInParent<ZNetView>();
            if (nv == null || !nv.IsValid()) return;
            MarkSeen(PrefabName(nv));
        }

        public static bool IsVisible(ResourceEntry e)
        {
            if (!Plugin.HideUndiscovered.Value) return true;
            return Revealed.Contains(e.Label) || IsDiscovered(e);
        }

        private static readonly Dictionary<ResourceEntry, KeyValuePair<float, bool>> s_cache = new Dictionary<ResourceEntry, KeyValuePair<float, bool>>();

        public static bool IsDiscovered(ResourceEntry e)
        {
            if (s_cache.TryGetValue(e, out var c) && UnityEngine.Time.unscaledTime - c.Key < 1f) return c.Value;
            bool d = Compute(e);
            s_cache[e] = new KeyValuePair<float, bool>(UnityEngine.Time.unscaledTime, d);
            return d;
        }

        private static bool Compute(ResourceEntry e) => Seen(e) || ComputeWithoutSeen(e);

        private static bool ComputeWithoutSeen(ResourceEntry e)
        {
            var player = Player.m_localPlayer;
            if (player == null) return true;

            // Créatures : déjà tuée
            if (e.Category == Category.Creature)
                foreach (var p in e.Prefabs)
                {
                    var name = CreatureName(p);
                    if (name != null && Kills(name) > 0f) return true;
                }

            // Ressources : matériau connu (pour une créature : son trophée)
            bool anyItem = false;
            foreach (var p in e.Prefabs)
            {
                var item = Icons.ItemForPrefab(p);
                if (item == null) continue;
                anyItem = true;
                if (player.IsMaterialKnown(item.m_itemData.m_shared.m_name)) return true;
            }
            if (e.Prefabs.Length > 0 && !anyItem && BiomeKnown(player, VegetationBiomes(e) | e.Biomes)) return true;

            // Lieux : biome visité
            if (e.Locations.Length > 0 && BiomeKnown(player, LocationBiomes(e))) return true;

            return false;
        }

        /// <summary>Nombre de créatures de ce nom tuées par le personnage (toutes difficultés, sans triche).</summary>
        public static float Kills(string characterName)
        {
            try
            {
                var stats = Game.instance?.GetPlayerProfile()?.m_playerStats;
                if (stats == null) return 0f;
                float total = 0f;
                foreach (var s in stats)
                    if (s?.m_enemyStats != null)
                        foreach (var dict in s.m_enemyStats)
                            if (dict != null && dict.TryGetValue(characterName, out float n)) total += n;
                return total;
            }
            catch { return 0f; }
        }

        // La table de végétation ne change pas pendant une session : masque calculé une fois par entrée.
        private static readonly Dictionary<ResourceEntry, Heightmap.Biome> s_vegBiomes = new Dictionary<ResourceEntry, Heightmap.Biome>();

        private static Heightmap.Biome VegetationBiomes(ResourceEntry e)
        {
            if (s_vegBiomes.TryGetValue(e, out var cached)) return cached;
            var mask = Heightmap.Biome.None;
            if (ZoneSystem.instance == null) return mask;
            foreach (var veg in ZoneSystem.instance.m_vegetation)
                if (veg?.m_prefab != null && e.Prefabs.Any(p => string.Equals(p, veg.m_prefab.name, StringComparison.OrdinalIgnoreCase)))
                    mask |= veg.m_biome;
            s_vegBiomes[e] = mask;
            return mask;
        }

        // « Forêt-Noire, Marais » : biomes où la ressource pousse (table de végétation), où se trouvent ses lieux, ou déclarés
        // dans le catalogue. Calculé une fois par entrée ; vide pour les créatures (biomes de spawn non lus).
        private static readonly Dictionary<ResourceEntry, string> s_biomeText = new Dictionary<ResourceEntry, string>();
        private static string s_biomeLang; // langue des textes en cache : un changement de langue du jeu les invalide
        private static readonly Heightmap.Biome[] s_biomeOrder = { Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Ocean, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth };

        public static string BiomeText(ResourceEntry e)
        {
            string lang = Localization.instance != null ? Localization.instance.GetSelectedLanguage() : null;
            if (lang != s_biomeLang) { s_biomeText.Clear(); s_biomeLang = lang; }
            if (s_biomeText.TryGetValue(e, out var t)) return t;
            if (ZoneSystem.instance == null) return "";
            var mask = e.Biomes | (e.Category == Category.Creature ? SpawnBiomes(e) : VegetationBiomes(e)) | LocationBiomes(e);
            var parts = new List<string>();
            foreach (var b in s_biomeOrder)
                if ((mask & b) != 0) parts.Add(Localization.instance.Localize("$biome_" + b.ToString().ToLowerInvariant()));
            t = string.Join(", ", parts);
            if (e.Category != Category.Creature || s_spawnBiomes != null) s_biomeText[e] = t;
            return t;
        }

        // Biomes de spawn d'une créature (listes du SpawnSystem, identiques sur toutes les zones) : « vit dans … »
        private static Dictionary<string, Heightmap.Biome> s_spawnBiomes;
        private static Heightmap.Biome SpawnBiomes(ResourceEntry e)
        {
            if (s_spawnBiomes == null)
            {
                var ss = UnityEngine.Object.FindObjectOfType<SpawnSystem>();
                if (ss == null) return Heightmap.Biome.None; // pas encore de zone chargée : on réessaiera
                s_spawnBiomes = new Dictionary<string, Heightmap.Biome>(StringComparer.OrdinalIgnoreCase);
                foreach (var list in ss.m_spawnLists) foreach (var sd in list.m_spawners)
                    if (sd?.m_prefab != null && sd.m_enabled) s_spawnBiomes[sd.m_prefab.name] = (s_spawnBiomes.TryGetValue(sd.m_prefab.name, out var m) ? m : Heightmap.Biome.None) | sd.m_biome;
            }
            var mask = Heightmap.Biome.None;
            foreach (var p in e.Prefabs) if (s_spawnBiomes.TryGetValue(p, out var b)) mask |= b;
            return mask;
        }

        // Nom de créature (Character.m_name) par prefab, résolu une fois.
        private static readonly Dictionary<string, string> s_creatureName = new Dictionary<string, string>();

        private static string CreatureName(string prefab)
        {
            if (s_creatureName.TryGetValue(prefab, out var n)) return n;
            var character = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab)?.GetComponent<Character>() : null;
            n = character != null ? character.m_name : null;
            if (ZNetScene.instance != null) s_creatureName[prefab] = n;
            return n;
        }

        private static Heightmap.Biome LocationBiomes(ResourceEntry e)
        {
            var mask = Heightmap.Biome.None;
            if (ZoneSystem.instance == null) return mask;
            foreach (var loc in ZoneSystem.instance.m_locations)
                if (loc != null && e.Locations.Any(l => l.Length > 0 && (loc.m_prefabName ?? "").IndexOf(l, StringComparison.OrdinalIgnoreCase) >= 0))
                    mask |= loc.m_biome;
            return mask;
        }

        // m_knownBiome contient des noms de secteurs de biome ("Meadows", "BlackForest", ...).
        private static bool BiomeKnown(Player player, Heightmap.Biome mask)
        {
            if (mask == Heightmap.Biome.None) return false;
            var known = s_knownBiome(player);
            if (known == null) return false;
            foreach (Heightmap.Biome b in Enum.GetValues(typeof(Heightmap.Biome)))
            {
                if (b == Heightmap.Biome.None || b == Heightmap.Biome.All || (mask & b) == 0) continue;
                string name = b.ToString();
                foreach (var k in known)
                    if (k.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
