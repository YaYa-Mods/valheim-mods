using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;
using ModsCommon;

namespace ResourceFinder
{
    /// <summary>Une couche = une ressource et toutes les positions connues pour elle, affichable/masquable sur la carte.</summary>
    internal sealed class Layer
    {
        public string Label;
        public string IconPrefab;              // item explicite (lieux) ; sinon icône du premier résultat
        public bool Visible = true;
        public readonly List<Result> Results = new List<Result>();
        public readonly Dictionary<Result, Minimap.PinData> Pins = new Dictionary<Result, Minimap.PinData>();

        public Sprite Icon()
        {
            var s = Icons.ForPrefab(IconPrefab);
            if (s != null) return s;
            foreach (var r in Results) { s = Icons.ForPrefab(r.Prefab); if (s != null) return s; }
            return null;
        }
    }

    /// <summary>
    /// Couches persistantes par monde : chaque recherche alimente la couche de sa ressource (fusion, pas remplacement).
    /// Épingles sur la grande carte et la mini-carte avec l'icône de la ressource. Sauvegarde texte dans
    /// BepInEx/config/ResourceFinder.layers.&lt;uid du monde&gt;.txt, rechargée au chargement du monde.
    /// </summary>
    internal static class Layers
    {
        public static readonly List<Layer> All = new List<Layer>();
        private static long s_worldUid;
        private static bool s_pinsDirty;
        private static float s_nextDynamic;

        private static string FilePath(long uid) => Path.Combine(Paths.ConfigPath, $"ResourceFinder.layers.{uid}.txt");

        // ------------------------------------------------------------------ cycle de vie

        /// <summary>À appeler chaque image : charge les couches du monde courant, pose les épingles quand la carte existe.</summary>
        public static void Tick()
        {
            if (ZNet.instance == null || Player.m_localPlayer == null) return;
            long uid = ZNet.instance.GetWorldUID();
            if (uid != s_worldUid)
            {
                ClearPins();
                All.Clear();
                Discovery.Revealed.Clear();
                s_worldUid = uid;
                Load();
                s_pinsDirty = true;
            }
            if (s_pinsDirty && Minimap.instance != null) { RefreshPins(); s_pinsDirty = false; }
            if (Time.unscaledTime >= s_nextDynamic) { s_nextDynamic = Time.unscaledTime + 0.5f; UpdateDynamicPins(); }
        }

        /// <summary>Les créatures bougent : leurs épingles suivent la position lue dans le ZDO.</summary>
        private static void UpdateDynamicPins()
        {
            foreach (var l in All)
                foreach (var kv in l.Pins)
                {
                    var r = kv.Key;
                    if (!r.Dynamic || !r.StillExists()) continue; // StillExists relit la position
                    kv.Value.m_pos = r.Pos;
                }
        }

        /// <summary>L'épingle de la cible pulse sur la carte (m_animate, comme un ping) : on la repère d'un coup d'œil.</summary>
        private static Result s_animated;
        public static void Animate(Result r, bool on)
        {
            if (on) s_animated = r; else if (s_animated == r) s_animated = null;
            if (r == null) return;
            foreach (var l in All) if (l.Pins.TryGetValue(r, out var pin) && pin != null) pin.m_animate = on;
        }

        public static void Unload()
        {
            ClearPins();
            All.Clear();
            Discovery.Revealed.Clear();
            s_worldUid = 0;
        }

        // ------------------------------------------------------------------ contenu

        public static Layer Get(string label)
        {
            foreach (var l in All) if (string.Equals(l.Label, label, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        /// <summary>Ajoute les résultats d'une recherche à la couche de cette ressource (créée si besoin).</summary>
        public static Layer Merge(ResourceEntry entry, IEnumerable<Result> results)
        {
            var layer = Get(entry.Label);
            if (layer == null) { layer = new Layer { Label = entry.Label, IconPrefab = entry.Icon }; All.Add(layer); }
            else if (string.IsNullOrEmpty(layer.IconPrefab)) layer.IconPrefab = entry.Icon;

            int added = 0;
            foreach (var r in results)
            {
                if (Contains(layer, r)) continue;
                layer.Results.Add(r);
                added++;
            }
            if (Cap(layer)) added++;
            if (added > 0) { Save(); s_pinsDirty = true; }
            return layer;
        }

        /// <summary>Plafonne une couche aux N positions les plus proches du joueur (chaque position = une épingle).</summary>
        private static bool Cap(Layer layer)
        {
            int max = Plugin.MaxPinsPerLayer.Value;
            if (layer.Results.Count <= max) return false;
            var p = Player.m_localPlayer;
            if (p != null)
            {
                var from = p.transform.position;
                layer.Results.Sort((a, b) => a.Distance(from).CompareTo(b.Distance(from)));
            }
            layer.Results.RemoveRange(max, layer.Results.Count - max);
            return true;
        }

        private static bool Contains(Layer layer, Result r)
        {
            foreach (var x in layer.Results)
            {
                if (r.IsLocation ? (x.IsLocation && Vector3.Distance(x.Pos, r.Pos) < 1f) : (!x.IsLocation && x.Id == r.Id)) return true;
            }
            return false;
        }

        public static void Remove(Layer layer)
        {
            RemovePins(layer);
            All.Remove(layer);
            Save();
        }

        /// <summary>Supprime toutes les couches (donc toutes les épingles posées par le mod ; celles du joueur, ajoutées
        /// par lui sur la carte, ne sont pas touchées : nos épingles sont créées avec save=false et suivies par couche).</summary>
        public static void RemoveAll()
        {
            ClearPins();
            All.Clear();
            Save();
        }

        public static void SetVisible(Layer layer, bool visible)
        {
            layer.Visible = visible;
            s_pinsDirty = true;
            Save();
        }

        /// <summary>Retire les résultats disparus (veine minée, buisson détruit). Appelé périodiquement.</summary>
        private static readonly List<ZDO> s_sectorZdos = new List<ZDO>();
        private static ResourceEntry s_zdoCacheEntry;

        /// <summary>Résultat « lieu » d'une ressource de surface (goudron, œufs, trolls pétrifiés…) : une fois la zone chargée,
        /// s'il n'y a plus aucun objet de la ressource à moins de 80 m, la ressource a été récoltée → l'épingle part.</summary>
        private static bool LocationEmptied(ResourceEntry entry, Result r)
        {
            if (entry == null || !entry.SurfacePrefabs || entry.Prefabs.Length == 0 || ZoneSystem.instance == null || ZDOMan.instance == null) return false;
            if (!ZoneSystem.instance.IsZoneLoaded(r.Pos)) return false;
            // Peu d'objets de ce type dans un monde (goudron, œufs, trolls pétrifiés…) : liste complète, une fois par purge et par couche
            if (s_zdoCacheEntry != entry) { s_zdoCacheEntry = entry; s_sectorZdos.Clear(); foreach (var p in entry.Prefabs) { int idx = 0; while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(p, s_sectorZdos, ref idx)) { } } }
            foreach (var z in s_sectorZdos)
            {
                if (z == null || !z.IsValid() || z.GetBool(ZDOVars.s_picked, false)) continue;
                if (Vector3.Distance(z.GetPosition(), r.Pos) < 80f) return false;
            }
            return true;
        }

        public static void Purge()
        {
            bool changed = false;
            s_zdoCacheEntry = null;
            foreach (var l in All)
            {
                ResourceEntry entry = null;
                foreach (var e in Catalog.Entries) if (string.Equals(e.Label, l.Label, StringComparison.OrdinalIgnoreCase)) { entry = e; break; }
                int n = l.Results.RemoveAll(r => !r.StillExists() || (r.IsLocation && (LocationEmptied(entry, r) || Dungeons.Emptied(r, entry)))); // donjon vidé : son entrée n'a plus rien à offrir
                if (n > 0) changed = true;
            }
            if (changed) { Save(); s_pinsDirty = true; }
        }

        public static void SetRevealed(string label, bool revealed)
        {
            if (revealed) Discovery.Revealed.Add(label); else Discovery.Revealed.Remove(label);
            Save();
        }

        // ------------------------------------------------------------------ épingles

        public static void MarkDirty() => s_pinsDirty = true;

        public static void RefreshPins()
        {
            ClearPins();
            var map = Minimap.instance;
            if (map == null || !Plugin.AddMapPins.Value || !Plugin.Enabled.Value) return;
            foreach (var l in All)
            {
                if (!l.Visible) continue;
                var icon = l.Icon();
                foreach (var r in l.Results)
                {
                    var pin = map.AddPin(r.Pos, Minimap.PinType.Icon2, Plugin.PinNames.Value ? L.T(l.Label) : "", false, false, 0L, default(Splatform.PlatformUserID));
                    if (pin == null) continue;
                    if (icon != null) pin.m_icon = icon; // grande carte et mini-carte lisent m_icon
                    pin.m_doubleSize = true; // à la taille normale l'icône fait ~7 px sur la grande carte : illisible, toutes les épingles se ressemblent
                    l.Pins[r] = pin;
                    if (r == s_animated) pin.m_animate = true; // la cible pulse même après une reconstruction des épingles
                }
            }
        }

        private static void RemovePins(Layer l)
        {
            var map = Minimap.instance;
            if (map != null) foreach (var p in l.Pins.Values) map.RemovePin(p);
            l.Pins.Clear();
        }

        private static void ClearPins()
        {
            foreach (var l in All) RemovePins(l);
        }

        // ------------------------------------------------------------------ persistance

        private static void Save()
        {
            if (s_worldUid == 0) return;
            try
            {
                var lines = new List<string> { L.T("# ResourceFinder layers, '#revealed' lines, then one '#layer' line per layer followed by its results") };
                foreach (var r in Discovery.Revealed) lines.Add("#revealed	" + r);
                foreach (var l in All)
                {
                    lines.Add($"#layer\t{l.Label}\t{l.IconPrefab}\t{(l.Visible ? 1 : 0)}");
                    foreach (var r in l.Results)
                        lines.Add(string.Join("\t", "r", r.Prefab, r.IsLocation ? 1 : 0,
                            r.Id.UserID.ToString(CultureInfo.InvariantCulture), r.Id.ID.ToString(CultureInfo.InvariantCulture),
                            F(r.Pos.x), F(r.Pos.y), F(r.Pos.z)));
                }
                File.WriteAllLines(FilePath(s_worldUid), lines);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Sauvegarde des couches impossible : {ex.Message}"); }
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static void Load()
        {
            var path = FilePath(s_worldUid);
            if (!File.Exists(path)) return;
            try
            {
                Layer current = null;
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split('\t');
                    if (parts[0] == "#revealed" && parts.Length >= 2) { Discovery.Revealed.Add(parts[1]); continue; }
                    if (parts[0] == "#layer" && parts.Length >= 4)
                    {
                        current = new Layer { Label = parts[1], IconPrefab = string.IsNullOrEmpty(parts[2]) ? null : parts[2], Visible = parts[3] == "1" };
                        All.Add(current);
                    }
                    else if (parts[0] == "r" && current != null && parts.Length >= 8)
                    {
                        var r = new Result
                        {
                            Prefab = parts[1],
                            IsLocation = parts[2] == "1",
                            Id = new ZDOID(long.Parse(parts[3], CultureInfo.InvariantCulture), uint.Parse(parts[4], CultureInfo.InvariantCulture)),
                            Pos = new Vector3(P(parts[5]), P(parts[6]), P(parts[7])),
                        };
                        r.Hash = r.Prefab.GetStableHashCode();
                        current.Results.Add(r);
                    }
                }
                foreach (var l in All) Cap(l);
                Plugin.Log.LogInfo($"{All.Count} couche(s) chargée(s) pour ce monde");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Lecture des couches impossible : {ex.Message}"); }
        }
    }
}
