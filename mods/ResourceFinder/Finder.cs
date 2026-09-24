using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace ResourceFinder
{
    internal sealed class Result
    {
        public Vector3 Pos;
        public string Prefab;
        public int Hash;
        public ZDOID Id;          // ZDOID.None pour un lieu
        public bool IsLocation;

        /// <summary>Créature (elle bouge) : sa position est relue dans le ZDO à chaque vérification.</summary>
        public bool Dynamic => Finder.IsCreature(Hash);

        /// <summary>Distance horizontale (comme sur la carte) : un objet à 5 000 m d'altitude dans un donjon n'est pas « à 5 km ».</summary>
        public float Distance(Vector3 from) { float dx = from.x - Pos.x, dz = from.z - Pos.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        /// <summary>
        /// Un minerai miné ou un buisson détruit disparaît du monde ; une cueillette ramassée (framboise, champignon…)
        /// reste dans le monde avec le drapeau « picked » le temps de repousser : on la considère disparue aussi, une
        /// nouvelle recherche la retrouvera quand elle aura repoussé. Sur un client connecté à un serveur, les ZDO
        /// lointains ne sont pas connus : on ne conclut à la disparition que si le joueur est assez près pour le savoir.
        /// </summary>
        public bool StillExists()
        {
            if (IsLocation) return true;
            var man = ZDOMan.instance;
            if (man == null) return true;
            var zdo = man.GetZDO(Id);
            if (zdo != null)
            {
                if (Dynamic) Pos = zdo.GetPosition();
                return !zdo.GetBool(ZDOVars.s_picked, false);
            }
            var p = Player.m_localPlayer;
            return p != null && Distance(p.transform.position) > 200f;
        }
    }

    /// <summary>
    /// Recherche en deux temps :
    ///  1. dans le monde déjà généré (ZDO connus) + les lieux (connus pour toute la carte) ;
    ///  2. si pas assez de résultats, génération "fantôme" des zones inconnues par distance croissante
    ///     (exactement ce que fait le serveur quand un joueur approche), jusqu'à N trouvailles ou le rayon max.
    /// Tout est étalé sur plusieurs images via Tick().
    /// </summary>
    internal sealed class Finder
    {
        public enum Phase { Idle, SearchingKnown, Scanning, Done }

        public Phase State { get; private set; } = Phase.Idle;
        public string Label { get; private set; } = "";
        public readonly List<Result> Results = new List<Result>();
        public string Status { get; private set; } = "";

        /// <summary>Distance telle qu'on la lit en jeu : mètres jusqu'au kilomètre, puis kilomètres.</summary>
        internal static string DistanceText(float m) => m >= 1000f ? $"{m / 1000f:0.0} km" : $"{m:0} m";
        public float ScanRadius { get; private set; }
        public int ZonesGenerated { get; private set; }
        public int ZonesSkipped { get; private set; }
        public int ZonesFiltered { get; private set; }
        /// <summary>Rayon (m) jusqu'auquel les zones ont été examinées pour cette recherche.</summary>
        public float CoveredRadius { get; private set; }
        public const float DungeonAltitude = 1500f;
        /// <summary>Objets ignorés parce qu'à l'intérieur d'un donjon (altitude d'instance).</summary>
        public int InDungeons { get; private set; }
        /// <summary>Avancement du scan en cours : zones traitées / zones prévues (0..1), 0 hors scan.</summary>
        public float ScanProgress => State == Phase.Scanning && _zonesByDistance != null && _zonesByDistance.Count > 0 ? Mathf.Clamp01((float)(_zoneIdx - _pending.Count) / _zonesByDistance.Count) : 0f;
        private double _generateMs; // temps cumulé passé dans SpawnZone (pour estimer une extension)
        private Heightmap.Biome _biomeMask = Heightmap.Biome.All;

        private HashSet<int> _hashes = new HashSet<int>();
        private Dictionary<int, string> _hashToName = new Dictionary<int, string>();
        private string[] _locationPatterns = Array.Empty<string>();
        private ResourceEntry _entry;
        // Recherche d'un matériau : une créature n'est proposée que si elle est là, chargée autour du joueur. Un souvenir de
        // créature dans une zone non chargée (squelette apparu la nuit, parti au lever du jour) enverrait vers un endroit vide.
        private bool _liveCreaturesOnly;
        private Heightmap.Biome _entryBiomes = Heightmap.Biome.None;
        private bool _hasStaticPrefabs;   // faux = que des créatures : rien à générer, elles n'apparaissent pas à la génération

        private static readonly Dictionary<int, bool> s_creature = new Dictionary<int, bool>();
        /// <summary>Le prefab est une créature (composant Character).</summary>
        public static bool IsCreature(int hash)
        {
            if (hash == 0) return false;
            if (s_creature.TryGetValue(hash, out bool c)) return c;
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(hash) : null;
            c = go != null && go.GetComponent<Character>() != null;
            if (go != null) s_creature[hash] = c;
            return c;
        }

        // Phase 1
        private List<string> _prefabNamesToSearch = new List<string>();
        private int _prefabIdx;
        private int _iterIndex;
        private readonly List<ZDO> _tmp = new List<ZDO>();

        // Phase 2
        private List<Vector2s> _zonesByDistance = new List<Vector2s>();
        private int _zoneIdx;
        private readonly List<Vector2s> _pending = new List<Vector2s>();
        private readonly Dictionary<Vector2s, int> _attempts = new Dictionary<Vector2s, int>();
        private const int MaxAttempts = 600; // ~10 s à 60 fps : au-delà, la zone est abandonnée
        // Zones en attente de terrain simultanément. Le HeightmapBuilder du jeu ne garde que ~10 terrains prêts
        // et les calcule un par un dans un thread : en demander des centaines d'un coup sature ce thread pendant
        // des minutes et fait expirer les zones avant qu'on les lise.
        private const int MaxInFlight = 6;
        private Vector3 _origin;
        private float _targetRadius;

        // Accès aux méthodes privées de ZoneSystem
        // Délégué compilé une fois : évite MethodInfo.Invoke (boxing + réflexion) à chaque zone examinée.
        private static readonly Func<ZoneSystem, Vector2s, bool> s_isZoneGenerated =
            AccessTools.MethodDelegate<Func<ZoneSystem, Vector2s, bool>>(AccessTools.Method(typeof(ZoneSystem), "IsZoneGenerated"));
        private static readonly MethodInfo s_spawnZone = AccessTools.Method(typeof(ZoneSystem), "SpawnZone");
        private static readonly object s_ghostMode = Enum.ToObject(AccessTools.Inner(typeof(ZoneSystem), "SpawnMode"), 2); // Ghost

        // Capture des ZDO créés pendant une génération fantôme (patch sur ZDOMan.CreateNewZDO)
        internal static bool Capturing;
        internal static readonly List<ZDO> Captured = new List<ZDO>();

        // ------------------------------------------------------------------ résolution des noms

        /// <summary>Tous les prefabs du jeu (nom → hash), construit une fois par session de jeu.</summary>
        private static Dictionary<string, int> s_allPrefabs;

        private static Dictionary<string, int> AllPrefabs()
        {
            if (s_allPrefabs != null && s_allPrefabs.Count > 0) return s_allPrefabs;
            s_allPrefabs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var scene = ZNetScene.instance;
            if (scene == null) return s_allPrefabs;
            foreach (var go in scene.m_prefabs)
                if (go != null && !s_allPrefabs.ContainsKey(go.name))
                    s_allPrefabs[go.name] = go.name.GetStableHashCode();
            return s_allPrefabs;
        }

        /// <summary>Nombre de prefabs du jeu reconnus pour une entrée du catalogue (0 = nom(s) à corriger).</summary>
        public static int KnownPrefabCount(ResourceEntry e)
        {
            var all = AllPrefabs();
            return e.Prefabs.Count(all.ContainsKey);
        }

        /// <summary>Noms de prefabs et de lieux contenant le texte tapé (recherche libre).</summary>
        public static ResourceEntry FreeSearch(string text)
        {
            text = text.Trim();
            var prefabs = AllPrefabs().Keys.Where(n => n.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            return new ResourceEntry(text, prefabs, new[] { text });
        }

        // ------------------------------------------------------------------ pilotage

        public void Start(ResourceEntry entry, Vector3 origin)
        {
            Cancel();
            Label = entry.Label;
            _entry = entry;
            _origin = origin;
            Results.Clear();
            ZonesGenerated = 0;
            ZonesSkipped = 0;
            InDungeons = 0;
            ScanRadius = 0f;
            CoveredRadius = 0f;
            _generateMs = 0;

            var all = AllPrefabs();
            _hashes = new HashSet<int>();
            _hashToName = new Dictionary<int, string>();
            _prefabNamesToSearch = new List<string>();
            foreach (var name in entry.Prefabs)
            {
                if (!all.TryGetValue(name, out int hash)) continue;
                _hashes.Add(hash);
                _hashToName[hash] = name;
                _prefabNamesToSearch.Add(name);
            }
            _locationPatterns = entry.Locations;
            _liveCreaturesOnly = entry.Category == Category.Materiau;
            _entryBiomes = entry.Biomes;
            _hasStaticPrefabs = _hashes.Any(h => !IsCreature(h));

            SearchLocations();

            _prefabIdx = 0;
            _iterIndex = 0;
            State = _prefabNamesToSearch.Count > 0 ? Phase.SearchingKnown : Phase.Done;
            Status = State == Phase.Done ? "" : L.T("Recherche dans le monde connu…");
            if (State == Phase.Done) Finish();
        }

        public void Cancel()
        {
            Capturing = false;
            Captured.Clear();
            _pending.Clear();
            if (State == Phase.Scanning) { State = Phase.Done; CoveredRadius = ScanRadius; Status = L.F("Scan arrêté à {0}.", DistanceText(ScanRadius)); SortResults(); }
        }

        /// <summary>À appeler chaque image. Fait un peu de travail et rend la main.</summary>
        public void Tick(Vector3 playerPos)
        {
            switch (State)
            {
                case Phase.SearchingKnown: TickKnown(); break;
                case Phase.Scanning: TickScan(); break;
            }
        }

        // ------------------------------------------------------------------ phase 0 : lieux

        private void SearchLocations()
        {
            if (_locationPatterns.Length == 0 || ZoneSystem.instance == null) return;
            foreach (var kv in ZoneSystem.instance.m_locationInstances)
            {
                var loc = kv.Value;
                string name = loc.m_location?.m_prefabName ?? "";
                if (!_locationPatterns.Any(p => p.Length > 0 && name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                Results.Add(new Result { Pos = loc.m_position, Prefab = name, Hash = 0, Id = ZDOID.None, IsLocation = true });
            }
        }

        // ------------------------------------------------------------------ phase 1 : ZDO connus

        private void TickKnown()
        {
            var man = ZDOMan.instance;
            if (man == null) { State = Phase.Done; return; }

            // Un "paquet" d'itération par image ; GetAllZDOsWithPrefabIterative renvoie true quand il a fini.
            _tmp.Clear();
            bool done = man.GetAllZDOsWithPrefabIterative(_prefabNamesToSearch[_prefabIdx], _tmp, ref _iterIndex);
            foreach (var zdo in _tmp) AddZdo(zdo);
            if (Results.Count > 200) SortResults(); // borne la liste pendant l'itération

            if (!done) return;
            _prefabIdx++;
            _iterIndex = 0;
            if (_prefabIdx < _prefabNamesToSearch.Count) return;

            SortResults();
            if (Enough(Plugin.MaxScanRadius.Value) || !CanScan())
            {
                Finish();
                return;
            }
            BeginScan(0f, Plugin.MaxScanRadius.Value);
        }

        private void AddZdo(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid()) return;
            int hash = zdo.GetPrefab();
            if (!_hashes.Contains(hash)) return;
            var pos = zdo.GetPosition();
            // Intérieurs de donjon (cryptes, grottes…) : le jeu les place à ~5 000 m d'altitude ; on ne peut pas y aller « tout droit »
            if (pos.y > DungeonAltitude) { InDungeons++; return; }
            if (_liveCreaturesOnly && IsCreature(hash) && (ZNetScene.instance == null || ZNetScene.instance.FindInstance(zdo) == null)) return;
            // Cueillette déjà ramassée : elle reste dans le monde le temps de repousser, mais il n'y a rien à prendre
            if (zdo.GetBool(ZDOVars.s_picked, false)) return;
            var id = zdo.m_uid;
            if (Results.Any(r => r.Id == id)) return;
            Results.Add(new Result { Pos = pos, Prefab = _hashToName[hash], Hash = hash, Id = id, IsLocation = false });
            // Liste bornée en continu : on ne garde que les N plus proches, même pendant le scan (l'affichage suit)
            if (Results.Count > Mathf.Max(1, Plugin.ResultCount.Value) * 3) SortResults();
        }

        // ------------------------------------------------------------------ phase 2 : scan fantôme

        private bool CanScan()
        {
            // Seul le serveur (donc le joueur en solo / l'hôte) génère des zones ; inutile pour des créatures seules.
            return _hasStaticPrefabs && ZNet.instance != null && ZNet.instance.IsServer() && ZoneSystem.instance != null
                   && Plugin.MaxScanRadius.Value > 0f;
        }

        private void BeginScan(float fromR, float toR)
        {
            float maxR = toR;
            float zoneSize = ZoneSystem.instance.m_zoneSize;
            var center = ZoneSystem.GetZone(_origin);
            int r = Mathf.CeilToInt(maxR / zoneSize) + 1;

            var list = new List<KeyValuePair<float, Vector2s>>();
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                var z = new Vector2s(center.x + dx, center.y + dy);
                var pos = ZoneSystem.GetZonePos(z);
                float d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(_origin.x, _origin.z));
                if (d <= fromR || d > maxR || pos.magnitude > 10500f) continue; // déjà couvert / hors rayon / hors monde
                list.Add(new KeyValuePair<float, Vector2s>(d, z));
            }
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            _zonesByDistance = list.Select(kv => kv.Value).ToList();
            _zoneIdx = 0;
            _pending.Clear();
            _attempts.Clear();
            _biomeMask = ComputeBiomeMask();
            _targetRadius = toR;
            State = Phase.Scanning;
            Status = L.T("Scan des zones inconnues…");
        }

        /// <summary>
        /// Biomes où la ressource peut apparaître, d'après la table de végétation du jeu (ZoneSystem.m_vegetation).
        /// Si aucun prefab cherché n'y figure (objet placé par un lieu, nom inconnu...), on ne filtre que l'océan.
        /// </summary>
        private Heightmap.Biome ComputeBiomeMask()
        {
            Heightmap.Biome mask = Heightmap.Biome.None;
            foreach (var veg in ZoneSystem.instance.m_vegetation)
            {
                if (veg?.m_prefab == null || !veg.m_enable) continue;
                if (_hashes.Contains(veg.m_prefab.name.GetStableHashCode())) mask |= veg.m_biome;
            }
            if (mask == Heightmap.Biome.None) mask = _entryBiomes;                       // indice du catalogue (objets placés par des lieux)
            if (mask == Heightmap.Biome.None) mask = Heightmap.Biome.All & ~Heightmap.Biome.Ocean;
            return mask;
        }

        /// <summary>Vrai si au moins un des 9 points d'échantillon de la zone est dans un biome compatible.</summary>
        private bool ZoneMayContainResource(Vector2s zone)
        {
            var wg = WorldGenerator.instance;
            if (wg == null) return true;
            var c = ZoneSystem.GetZonePos(zone);
            float h = ZoneSystem.instance.m_zoneSize * 0.5f - 1f;
            for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                var biome = wg.GetBiome(new Vector3(c.x + ix * h, 0f, c.z + iz * h));
                if ((biome & _biomeMask) != 0) return true;
            }
            return false;
        }

        private void TickScan()
        {
            // Budget en temps par image : un PC lent génère simplement moins de zones par image.
            double budgetMs = Mathf.Clamp(Plugin.ScanBudgetMs.Value, 1f, 50f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool InBudget() => sw.Elapsed.TotalMilliseconds < budgetMs;

            // 1. Zones dont le terrain n'était pas prêt : on réessaie (le terrain se calcule en arrière-plan).
            for (int i = _pending.Count - 1; i >= 0 && InBudget(); i--)
            {
                var z = _pending[i];
                if (TryGenerate(z)) { _pending.RemoveAt(i); _attempts.Remove(z); continue; }
                _attempts.TryGetValue(z, out int n);
                _attempts[z] = ++n;
                if (n >= MaxAttempts)
                {
                    Plugin.Log.LogWarning($"Zone {z.x},{z.y} jamais prête après {n} essais, abandonnée.");
                    _pending.RemoveAt(i);
                    _attempts.Remove(z);
                }
            }

            // 2. Zones suivantes par distance croissante, sans dépasser MaxInFlight zones en attente de terrain.
            while (InBudget() && _pending.Count < MaxInFlight && _zoneIdx < _zonesByDistance.Count)
            {
                var z = _zonesByDistance[_zoneIdx++];
                var pos = ZoneSystem.GetZonePos(z);
                ScanRadius = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(_origin.x, _origin.z));

                if (s_isZoneGenerated(ZoneSystem.instance, z)) { ZonesSkipped++; continue; }
                if (!ZoneMayContainResource(z)) { ZonesFiltered++; continue; }
                if (!TryGenerate(z)) _pending.Add(z);
            }

            // Lisible pour un joueur : distance atteinte et nombre de trouvailles ; le détail technique reste en retrait
            Status = L.F("Scan… {0}, {1} trouvé(s)", DistanceText(ScanRadius), Results.Count)
                   + L.F("  <size=12><color=#9a9488>({0} zones explorées)</color></size>", ZonesGenerated);

            // Arrêt : assez de résultats et plus rien en attente, ou fin du rayon.
            bool enough = Enough(ScanRadius);
            bool exhausted = _zoneIdx >= _zonesByDistance.Count && _pending.Count == 0;
            if ((enough && _pending.Count == 0) || exhausted)
            {
                CoveredRadius = exhausted ? _targetRadius : ScanRadius;
                SortResults();
                Finish();
            }
        }

        /// <summary>Génère une zone en mode fantôme. false = terrain pas encore prêt, réessayer plus tard.</summary>
        private bool TryGenerate(Vector2s zone)
        {
            var args = new object[] { zone, s_ghostMode, null };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Capturing = true;
            Captured.Clear();
            bool ok;
            try
            {
                ok = (bool)s_spawnZone.Invoke(ZoneSystem.instance, args);
            }
            catch (Exception ex)
            {
                Capturing = false;
                Plugin.Log.LogError($"Génération fantôme de la zone {zone.x},{zone.y} échouée : {ex}");
                State = Phase.Done;
                Status = L.T("Erreur pendant le scan (voir LogOutput.log).");
                return true;
            }
            Capturing = false;
            _generateMs += sw.Elapsed.TotalMilliseconds;
            if (args[2] is GameObject root) UnityEngine.Object.Destroy(root); // sécurité : le jeu le détruit déjà en mode fantôme
            if (!ok) return false;

            ZonesGenerated++;
            foreach (var zdo in Captured) AddZdo(zdo);
            Captured.Clear();
            return true;
        }

        // ------------------------------------------------------------------ extension du scan

        /// <summary>Vrai quand le scan est fini, qu'il manque des résultats et qu'on peut aller plus loin.</summary>
        /// <summary>Rayon réellement couvert : le scan effectué, ou, sans scan, le rayon standard, puisque le monde connu a suffi.</summary>
        public float EffectiveRadius => Mathf.Max(CoveredRadius, Plugin.MaxScanRadius.Value);

        public bool CanExtend =>
            State == Phase.Done && !Enough(EffectiveRadius) && CanScan()
            && CoveredRadius < Plugin.ExtendedScanRadius.Value && _prefabNamesToSearch.Count > 0;

        /// <summary>
        /// Estimation pour l'extension jusqu'au rayon max : zones à générer et durée, d'après ce qui a été
        /// mesuré pendant le scan (part des zones réellement générées après filtre biome, temps moyen par zone).
        /// </summary>
        public void EstimateExtension(out int zonesToGenerate, out float seconds)
        {
            float zoneSize = ZoneSystem.instance != null ? ZoneSystem.instance.m_zoneSize : 64f;
            float toR = Plugin.ExtendedScanRadius.Value;
            float ringArea = Mathf.PI * (toR * toR - CoveredRadius * CoveredRadius);
            int ringZones = Mathf.Max(0, Mathf.RoundToInt(ringArea / (zoneSize * zoneSize)));

            int examined = ZonesGenerated + ZonesFiltered + ZonesSkipped;
            float generatedShare = examined > 0 ? (float)ZonesGenerated / examined : 0.3f;
            zonesToGenerate = Mathf.RoundToInt(ringZones * generatedShare);

            double msPerZone = ZonesGenerated > 0 ? _generateMs / ZonesGenerated : 40.0;
            // Le budget par image limite le débit : au mieux ScanBudgetMs par image, ~60 images/s.
            double perFrame = Mathf.Clamp(Plugin.ScanBudgetMs.Value, 1f, 50f);
            double zonesPerSecond = Math.Max(1.0, 60.0 * perFrame / Math.Max(msPerZone, 1.0));
            seconds = (float)(zonesToGenerate / zonesPerSecond);
        }

        /// <summary>Reprend le scan là où il s'est arrêté, jusqu'au rayon max.</summary>
        public void Extend()
        {
            if (!CanExtend) return;
            BeginScan(CoveredRadius, Plugin.ExtendedScanRadius.Value);
        }

        // ------------------------------------------------------------------ fin

        /// <summary>
        /// « Assez de résultats » au sens des anneaux : les N plus proches sont connus ET tous à moins de
        /// <paramref name="radius"/> m, au-delà, une zone non générée plus proche pourrait encore en cacher un.
        /// </summary>
        private bool Enough(float radius)
        {
            int n = Plugin.ResultCount.Value;
            return Results.Count >= n && Results[n - 1].Distance(_origin) <= radius;
        }

        private void SortResults()
        {
            Results.RemoveAll(r => !r.StillExists());
            // Un lieu et un objet réel trouvé à l'intérieur (zone chargée) : on garde l'objet, plus précis, pas les deux
            Results.RemoveAll(r => r.IsLocation && Results.Any(o => !o.IsLocation && Vector3.Distance(o.Pos, r.Pos) < 60f));
            Results.Sort((a, b) => a.Distance(_origin).CompareTo(b.Distance(_origin)));
            // On ne garde que les N plus proches : une ressource courante (sapins, pierres...) existe par milliers
            // dans le monde connu, et chaque résultat devient une épingle sur la carte. Un donjon déjà vidé n'en fait pas
            // partie : examinés du plus proche au plus loin, jusqu'à en avoir N.
            int keep = Mathf.Max(1, Plugin.ResultCount.Value);
            var kept = new List<Result>(keep);
            foreach (var r in Results)
            {
                if (kept.Count >= keep) break;
                if (r.IsLocation && Dungeons.Emptied(r, _entry)) continue;
                kept.Add(r);
            }
            Results.Clear();
            Results.AddRange(kept);
        }

        private void Finish()
        {
            State = Phase.Done;
            SortResults();
            Status = Results.Count == 0
                ? (CoveredRadius > 0f ? L.F("Rien trouvé dans un rayon de {0}.", DistanceText(CoveredRadius)) : L.T("Rien trouvé dans le monde connu."))
                : L.F("{0} trouvé(s)", Results.Count) + (CoveredRadius > 0f ? L.F(" (jusqu'à {0:0} m, {1} zones générées)", CoveredRadius, ZonesGenerated) : "") + ".";
            if (InDungeons > 0) Status += L.F(" {0} dans des donjons (ignorés : passez par l'entrée).", InDungeons);
            if (!_hasStaticPrefabs && _hashes.Count > 0) Status += Results.Count == 0 ? L.T(" Créature : rien de chargé autour de vous, seules celles présentes dans le monde sont détectées.") : L.T(" Créatures : seules celles présentes dans le monde sont détectées.");
        }
    }
}
