using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ResourceFinder
{
    /// <summary>
    /// Donjons (chambres funéraires, cryptes des marais...) : le scanner épingle leur entrée, l'intérieur est à ~5 000 m
    /// d'altitude au-dessus d'elle. Chaque intérieur a son générateur (un objet du jeu, « DG_ForestCrypt »...) dont la
    /// boîte (m_zoneSize, 64 m pour une chambre funéraire) délimite les salles : on ne compte que ce qui est dedans, pas
    /// l'intérieur voisin (une autre chambre à 140 m, une grotte à 70 m). Le contenu est lu dans la base d'objets du monde
    /// (ZDO), complète dès que le donjon a été généré, pas seulement dans les objets chargés autour du joueur (partiels à
    /// l'approche). Ce qui reste à prendre : coffres du donjon pas vides (le jeu les construit comme des pièces : seuls ceux
    /// posés par un joueur sont écartés), objets qui donnent la ressource cherchée (cœurs de surtling, tas de ferraille).
    ///  - Plus rien : le donjon est vidé, son entrée n'est plus proposée (recherche, épingle, cible).
    ///  - Joueur à l'intérieur : la pastille guide vers le plus proche de ce qui reste, plus vers l'entrée.
    /// Donjon jamais généré (pas de générateur dans la base) : on ne conclut rien.
    /// </summary>
    internal static class Dungeons
    {
        private const float Margin = 12f;            // salles en bordure de la boîte du générateur (objets relevés jusqu'à 39 m pour 32 m)
        private const float GeneratorReach = 48f;    // générateur d'un lieu : à cette distance horizontale de son entrée au plus
        private const int MinInteriorObjects = 10;   // en dessous : intérieur pas (encore) connu, on ne conclut rien

        private sealed class Interior
        {
            public ZDOID Generator;
            public Vector3 Center;          // position du générateur
            public float Half;              // demi-côté de la boîte, marge comprise
            public string Location = "";    // lieu du jeu dont c'est l'intérieur (« Crypt3 »)
            public float At = -100f;
            public ResourceEntry Entry;
            public int Objects;
            public readonly List<ZDO> Loot = new List<ZDO>();
            public int Fixed;               // objets immobiles (salles, coffres, décor) : ni créatures ni objets au sol
            public int LastFixed = -1;      // leur nombre au relevé précédent
            public float StableSince;       // depuis quand ce nombre ne bouge plus
            public bool Contains(Vector3 p) => p.y > Finder.DungeonAltitude && Mathf.Abs(p.x - Center.x) <= Half && Mathf.Abs(p.z - Center.z) <= Half;
        }

        private static readonly Dictionary<ZDOID, Interior> s_interiors = new Dictionary<ZDOID, Interior>();
        private static readonly Dictionary<Vector3, KeyValuePair<ZDOID, float>> s_byEntrance = new Dictionary<Vector3, KeyValuePair<ZDOID, float>>();
        private static readonly Dictionary<int, DungeonGenerator> s_generatorPrefab = new Dictionary<int, DungeonGenerator>();
        private static readonly Dictionary<int, bool> s_chestPrefab = new Dictionary<int, bool>();
        private static readonly List<ZDO> s_tmp = new List<ZDO>();

        /// <summary>Le donjon de ce résultat (lieu) est-il vidé ? Faux tant qu'on ne peut pas le savoir.</summary>
        public static bool Emptied(Result r, ResourceEntry entry)
        {
            if (r == null || !r.IsLocation) return false;
            var d = ForEntrance(r.Pos);
            if (d == null) return false;
            Refresh(d, entry);
            return d.Objects >= MinInteriorObjects && d.Loot.Count == 0 && Settled(d);
        }

        /// <summary>
        /// Intérieur au complet ? Autour du joueur, le jeu fait apparaître les salles une à une (72, puis 79, puis 91 objets
        /// relevés en quelques secondes) : tant que le générateur charge des salles ou que le nombre d'objets immobiles bouge,
        /// un coffre peut encore manquer. Les squelettes et fantômes qui vont et viennent ne comptent pas. Donjon loin du
        /// joueur (générateur pas chargé) : rien n'y apparaît, le relevé est complet.
        /// </summary>
        private static bool Settled(Interior d)
        {
            var gen = ZDOMan.instance?.GetZDO(d.Generator);
            var view = gen != null ? ZNetScene.instance.FindInstance(gen) : null;
            if (view == null) return true;
            var dg = view.GetComponent<DungeonGenerator>();
            if (dg != null && s_roomsToLoad != null && (int)s_roomsToLoad.GetValue(dg) > 0) return false;
            return Time.unscaledTime - d.StableSince >= SettleSeconds;
        }
        private const float SettleSeconds = 4f;
        private static readonly FieldInfo s_roomsToLoad = AccessTools.Field(typeof(DungeonGenerator), "m_roomsToLoad");
        private static readonly Dictionary<int, bool> s_mobilePrefab = new Dictionary<int, bool>();

        /// <summary>Créature ou objet posé au sol : va et vient sans rien dire du chargement des salles.</summary>
        private static bool IsMobile(int hash)
        {
            if (s_mobilePrefab.TryGetValue(hash, out bool mobile)) return mobile;
            var prefab = ZNetScene.instance.GetPrefab(hash);
            mobile = prefab == null || prefab.GetComponent<Character>() != null || prefab.GetComponent<ItemDrop>() != null;
            if (prefab != null) s_mobilePrefab[hash] = mobile;
            return mobile;
        }

        /// <summary>Joueur dans l'intérieur d'un donjon de cette ressource : le plus proche de ce qui y reste, sinon null.</summary>
        public static ZDO NearestInside(ResourceEntry entry, Vector3 player)
        {
            if (entry == null || entry.Locations.Length == 0 || player.y < Finder.DungeonAltitude) return null;
            var d = At(player);
            if (d == null || !Matches(d, entry)) return null;
            Refresh(d, entry);
            ZDO best = null; float bd = float.MaxValue;
            foreach (var z in d.Loot)
            {
                if (z == null || !z.IsValid()) continue;
                float dist = Vector3.Distance(z.GetPosition(), player);
                if (dist < bd) { bd = dist; best = z; }
            }
            return best;
        }

        private static bool Matches(Interior d, ResourceEntry entry)
        {
            foreach (var p in entry.Locations) if (p.Length > 0 && d.Location.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // ------------------------------------------------------------------ intérieurs

        /// <summary>Intérieur du lieu dont l'entrée est ici (générateur le plus proche, à la verticale), sinon null.</summary>
        private static Interior ForEntrance(Vector3 entrance)
        {
            if (ZDOMan.instance == null || ZNetScene.instance == null) return null;
            if (s_byEntrance.TryGetValue(entrance, out var known))
            {
                if (known.Key != ZDOID.None && s_interiors.TryGetValue(known.Key, out var d)) return d;
                if (known.Key == ZDOID.None && Time.unscaledTime - known.Value < 5f) return null; // cherché il y a peu : rien
            }
            var found = NearestGenerator(entrance, GeneratorReach);
            s_byEntrance[entrance] = new KeyValuePair<ZDOID, float>(found?.Generator ?? ZDOID.None, Time.unscaledTime);
            return found;
        }

        /// <summary>Intérieur qui contient ce point (joueur dans un donjon), sinon null.</summary>
        private static Interior At(Vector3 p)
        {
            foreach (var d in s_interiors.Values) if (d.Contains(p)) return d;
            if (Time.unscaledTime < s_nextAtLookup) return null; // pas de générateur ici tout à l'heure : pas de fouille à chaque image
            s_nextAtLookup = Time.unscaledTime + 3f;
            var found = NearestGenerator(p, 100f);
            return found != null && found.Contains(p) ? found : null;
        }
        private static float s_nextAtLookup;

        private static Interior NearestGenerator(Vector3 around, float reach)
        {
            s_tmp.Clear();
            var z = ZoneSystem.GetZone(around);
            int ring = Mathf.CeilToInt(reach / 64f);
            for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                    SectorObjects(new Vector2s(z.x + dx, z.y + dy), s_tmp);
            ZDO best = null; DungeonGenerator bestGen = null; float bd = reach;
            foreach (var zdo in s_tmp)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                var p = zdo.GetPosition();
                if (p.y < Finder.DungeonAltitude) continue;
                var gen = GeneratorOf(zdo.GetPrefab());
                if (gen == null) continue;
                float d = Horizontal(p, around);
                if (d <= bd) { bd = d; best = zdo; bestGen = gen; }
            }
            if (best == null) return null;
            if (s_interiors.TryGetValue(best.m_uid, out var known)) return known;
            var c = best.GetPosition() + new Vector3(bestGen.m_zoneCenter.x, 0f, bestGen.m_zoneCenter.z);
            var interior = new Interior
            {
                Generator = best.m_uid,
                Center = c,
                Half = Mathf.Max(bestGen.m_zoneSize.x, bestGen.m_zoneSize.z) * 0.5f + Margin,
                Location = LocationNear(best.GetPosition()),
            };
            s_interiors[best.m_uid] = interior;
            return interior;
        }

        /// <summary>Nom du lieu du jeu (« Crypt3 ») à la verticale de ce générateur.</summary>
        private static string LocationNear(Vector3 p)
        {
            if (ZoneSystem.instance == null) return "";
            string best = ""; float bd = GeneratorReach;
            foreach (var kv in ZoneSystem.instance.m_locationInstances)
            {
                float d = Horizontal(kv.Value.m_position, p);
                if (d <= bd) { bd = d; best = kv.Value.m_location?.m_prefabName ?? ""; }
            }
            return best;
        }

        /// <summary>Relevé de ce qui reste dans l'intérieur : souvent près du joueur, rarement au loin (épingles de la carte).</summary>
        private static void Refresh(Interior d, ResourceEntry entry)
        {
            var player = Player.m_localPlayer;
            float every = player != null && Horizontal(player.transform.position, d.Center) < 250f ? 1f : 10f;
            if (Time.unscaledTime - d.At < every && d.Entry == entry) return;
            d.At = Time.unscaledTime; d.Entry = entry;
            d.Loot.Clear(); d.Objects = 0; d.Fixed = 0;
            var wanted = entry != null ? ItemSources.SourcesOfItem(entry.Icon) : null;
            s_tmp.Clear();
            var lo = ZoneSystem.GetZone(new Vector3(d.Center.x - d.Half, 0f, d.Center.z - d.Half));
            var hi = ZoneSystem.GetZone(new Vector3(d.Center.x + d.Half, 0f, d.Center.z + d.Half));
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                    SectorObjects(new Vector2s(x, y), s_tmp);
            foreach (var zdo in s_tmp)
            {
                if (zdo == null || !zdo.IsValid() || !d.Contains(zdo.GetPosition())) continue;
                d.Objects++;
                if (!IsMobile(zdo.GetPrefab())) d.Fixed++;
                if (IsLoot(zdo, wanted)) d.Loot.Add(zdo);
            }
            if (d.Fixed != d.LastFixed) { d.LastFixed = d.Fixed; d.StableSince = Time.unscaledTime; }
        }

        private static bool IsLoot(ZDO zdo, HashSet<string> wanted)
        {
            int hash = zdo.GetPrefab();
            if (IsChest(hash)) return zdo.GetLong(ZDOVars.s_creator, 0L) == 0L && ChestHasLoot(zdo, hash); // coffre posé par un joueur : pas du butin
            if (wanted == null || wanted.Count == 0) return false;
            var prefab = ZNetScene.instance.GetPrefab(hash);
            return prefab != null && wanted.Contains(prefab.name) && !zdo.GetBool(ZDOVars.s_picked, false);
        }

        private static bool IsChest(int hash)
        {
            if (s_chestPrefab.TryGetValue(hash, out bool chest)) return chest;
            var prefab = ZNetScene.instance.GetPrefab(hash);
            chest = prefab != null && prefab.GetComponent<Container>() != null;
            if (prefab != null) s_chestPrefab[hash] = chest;
            return chest;
        }

        private static DungeonGenerator GeneratorOf(int hash)
        {
            if (s_generatorPrefab.TryGetValue(hash, out var g)) return g;
            var prefab = ZNetScene.instance.GetPrefab(hash);
            g = prefab != null ? prefab.GetComponent<DungeonGenerator>() : null;
            if (prefab != null) s_generatorPrefab[hash] = g;
            return g;
        }

        /// <summary>Coffre : pas encore rempli (jamais ouvert) ou encore quelque chose dedans.</summary>
        private static bool ChestHasLoot(ZDO zdo, int hash)
        {
            if (!zdo.GetBool(ZDOVars.s_addedDefaultItems, false)) return true;
            // Contenu rangé en octets (Container.Save : ZDO.Set(s_items, byte[])), pas en texte
            byte[] data = zdo.GetByteArray(ZDOVars.s_items, null);
            if (data == null || data.Length == 0) return false;
            try
            {
                var c = ZNetScene.instance.GetPrefab(hash).GetComponent<Container>();
                var inv = new Inventory("", null, Mathf.Max(1, c.m_width), Mathf.Max(1, c.m_height));
                inv.Load(new ZPackage(data));
                return inv.NrOfItems() > 0;
            }
            catch { return true; } // illisible : on suppose qu'il reste quelque chose plutôt que d'effacer à tort
        }

        // ------------------------------------------------------------------ base d'objets par secteur

        // ZDOMan.FindObjects (privée) : tous les objets d'un secteur, chargés ou non. Repli : les objets chargés.
        private static MethodInfo s_findObjects = AccessTools.Method(typeof(ZDOMan), "FindObjects");
        private static object s_noVisited; // HashSet<SectorIndex> des secteurs à sauter : FindObjects y note ceux qu'il a lus, vidé avant chaque appel
        private static MethodInfo s_clearVisited;
        private static readonly FieldInfo s_instancesF = typeof(ZNetScene).GetField("m_instances", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void SectorObjects(Vector2s sector, List<ZDO> into)
        {
            if (s_findObjects != null)
            {
                try
                {
                    if (s_noVisited == null) { s_noVisited = Activator.CreateInstance(s_findObjects.GetParameters()[2].ParameterType); s_clearVisited = s_noVisited.GetType().GetMethod("Clear"); }
                    s_clearVisited.Invoke(s_noVisited, null);
                    s_findObjects.Invoke(ZDOMan.instance, new object[] { sector, into, s_noVisited });
                    return;
                }
                catch (Exception ex) { s_findObjects = null; Plugin.Log.LogWarning("Donjons : lecture de la base d'objets impossible, repli sur les objets chargés (" + ex.Message + ")"); }
            }
            if (!(s_instancesF?.GetValue(ZNetScene.instance) is System.Collections.IDictionary instances)) return;
            foreach (var key in instances.Keys)
                if (key is ZDO zdo && zdo.IsValid() && ZoneSystem.GetZone(zdo.GetPosition()).Equals(sector)) into.Add(zdo);
        }

        private static float Horizontal(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        /// <summary>Pour les tests et le diagnostic : ce qu'on sait de l'intérieur de ce lieu, et du point donné.</summary>
        internal static string Describe(Vector3 entrance, ResourceEntry entry, Vector3 player)
        {
            var d = ForEntrance(entrance);
            if (d == null) { s_tmp.Clear(); SectorObjects(ZoneSystem.GetZone(entrance), s_tmp); return $"aucun générateur près de l'entrée ({s_tmp.Count} objets dans son secteur)"; }
            Refresh(d, entry);
            return $"lieu {d.Location}, centre ({d.Center.x:0},{d.Center.z:0}), demi-côté {d.Half:0}, {d.Objects} objets dont {d.Fixed} immobiles, {d.Loot.Count} à prendre, relevé complet={Settled(d)}, joueur dedans={d.Contains(player)}, correspond={Matches(d, entry)}, au point : {(At(player) != null ? "intérieur trouvé" : "aucun")}";
        }

        /// <summary>Oublie les relevés (monde quitté, ou un test vient de vider un intérieur à la main).</summary>
        internal static void Forget() { s_interiors.Clear(); s_byEntrance.Clear(); s_nextAtLookup = 0f; }
    }
}
