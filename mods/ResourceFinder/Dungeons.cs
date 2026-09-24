using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ResourceFinder
{
    /// <summary>
    /// Donjons (chambres funéraires, cryptes des marais...) : le scanner épingle leur entrée, l'intérieur est à ~5 000 m
    /// d'altitude au-dessus d'elle. Quand la zone est chargée (joueur à l'entrée ou dedans), on relève ce qui reste à
    /// l'intérieur : coffres pas encore vidés, et objets qui donnent la ressource cherchée (cœurs de surtling, tas de
    /// ferraille), lus dans les tables de butin du jeu.
    ///  - Plus rien : le donjon est vidé, son repère disparaît (épingle et cible).
    ///  - Joueur à l'intérieur : la pastille guide vers le plus proche de ce qui reste, plus vers l'entrée.
    /// Sans intérieur généré (donjon jamais visité), on ne conclut rien.
    /// </summary>
    internal static class Dungeons
    {
        private const float Radius = 90f;          // étendue horizontale d'un intérieur autour de l'entrée
        private const int MinInteriorObjects = 10; // en dessous : intérieur pas encore généré, on ne conclut rien

        private static readonly FieldInfo s_instancesF = typeof(ZNetScene).GetField("m_instances", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Dictionary<int, int> s_kind = new Dictionary<int, int>(); // hash de prefab → 0 rien, 1 coffre, 2 source
        private static readonly Dictionary<Vector3, Snapshot> s_cache = new Dictionary<Vector3, Snapshot>();

        private sealed class Snapshot { public float At; public bool Generated; public readonly List<ZDO> Loot = new List<ZDO>(); }

        /// <summary>Le donjon de ce résultat (lieu) est-il vidé ? Faux tant qu'on ne peut pas le savoir.</summary>
        public static bool Emptied(Result r, ResourceEntry entry)
        {
            if (r == null || !r.IsLocation) return false;
            var s = Look(r.Pos, entry);
            return s != null && s.Generated && s.Loot.Count == 0;
        }

        /// <summary>Joueur dans l'intérieur de ce donjon : le plus proche de ce qui reste, sinon null.</summary>
        public static ZDO NearestInside(Result r, ResourceEntry entry, Vector3 player)
        {
            if (r == null || !r.IsLocation || player.y < Finder.DungeonAltitude) return null;
            if (Horizontal(player, r.Pos) > Radius) return null;
            var s = Look(r.Pos, entry);
            if (s == null || s.Loot.Count == 0) return null;
            ZDO best = null; float bd = float.MaxValue;
            foreach (var z in s.Loot)
            {
                if (z == null || !z.IsValid()) continue;
                float d = Vector3.Distance(z.GetPosition(), player);
                if (d < bd) { bd = d; best = z; }
            }
            return best;
        }

        private static float Horizontal(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        /// <summary>Relevé de l'intérieur (objets chargés), refait au plus une fois par seconde et par donjon.</summary>
        private static Snapshot Look(Vector3 entrance, ResourceEntry entry)
        {
            if (ZNetScene.instance == null || ZoneSystem.instance == null || !ZoneSystem.instance.IsZoneLoaded(entrance)) return null;
            if (s_cache.TryGetValue(entrance, out var snap) && Time.unscaledTime - snap.At < 1f) return snap;
            if (snap == null) s_cache[entrance] = snap = new Snapshot();
            snap.At = Time.unscaledTime; snap.Loot.Clear(); snap.Generated = false;
            var instances = s_instancesF?.GetValue(ZNetScene.instance) as System.Collections.IDictionary;
            if (instances == null) return snap;
            var wanted = entry != null ? ItemSources.SourcesOfItem(entry.Icon) : null;
            int interior = 0;
            foreach (var key in instances.Keys)
            {
                var zdo = key as ZDO;
                if (zdo == null || !zdo.IsValid()) continue;
                var pos = zdo.GetPosition();
                if (pos.y < Finder.DungeonAltitude || Horizontal(pos, entrance) > Radius) continue;
                interior++;
                if (IsLoot(zdo, wanted)) snap.Loot.Add(zdo);
            }
            snap.Generated = interior >= MinInteriorObjects;
            return snap;
        }

        private static bool IsLoot(ZDO zdo, HashSet<string> wanted)
        {
            int hash = zdo.GetPrefab();
            if (!s_kind.TryGetValue(hash, out int kind))
            {
                var prefab = ZNetScene.instance.GetPrefab(hash);
                kind = prefab == null ? 0 : prefab.GetComponent<Container>() != null && prefab.GetComponent<Piece>() == null ? 1 : wanted != null && wanted.Contains(prefab.name) ? 2 : 0;
                if (kind != 2) s_kind[hash] = kind; // « source » dépend de la ressource cherchée : pas mis en cache
            }
            if (kind == 1) return ChestHasLoot(zdo, hash);
            if (kind == 2) return !zdo.GetBool(ZDOVars.s_picked, false);
            return false;
        }

        /// <summary>Coffre : pas encore rempli (jamais ouvert) ou encore quelque chose dedans.</summary>
        private static bool ChestHasLoot(ZDO zdo, int hash)
        {
            if (!zdo.GetBool(ZDOVars.s_addedDefaultItems, false)) return true;
            string data = zdo.GetString(ZDOVars.s_items, "");
            if (string.IsNullOrEmpty(data)) return false;
            try
            {
                var c = ZNetScene.instance.GetPrefab(hash).GetComponent<Container>();
                var inv = new Inventory("", null, Mathf.Max(1, c.m_width), Mathf.Max(1, c.m_height));
                inv.Load(new ZPackage(data));
                return inv.NrOfItems() > 0;
            }
            catch { return true; } // illisible : on suppose qu'il reste quelque chose plutôt que d'effacer à tort
        }

        /// <summary>Pour les tests : oublie les relevés (un objet vient d'être ramassé à la main).</summary>
        internal static void Forget() { s_cache.Clear(); }
    }
}
