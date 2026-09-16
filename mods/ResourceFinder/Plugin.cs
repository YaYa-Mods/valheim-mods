using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace ResourceFinder
{
    /// <summary>
    /// Scanner de ressources façon Satisfactory : F7 ouvre une fenêtre, on choisit une ressource,
    /// le mod cherche dans le monde connu puis génère les zones inconnues par anneaux croissants jusqu'à
    /// N trouvailles. Résultats épinglés sur la carte + indicateur à l'écran vers la cible.
    /// Voir Finder.cs pour la recherche, Catalog.cs pour la liste des ressources.
    /// </summary>
    [BepInPlugin(Guid, "Resource Finder", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.resourcefinder";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<KeyCode> NextTargetKey;
        internal static ConfigEntry<KeyCode> TrackKey;
        internal static ConfigEntry<int> ResultCount;
        internal static ConfigEntry<float> MaxScanRadius;
        internal static ConfigEntry<float> ExtendedScanRadius;
        internal static ConfigEntry<float> ScanBudgetMs;
        internal static ConfigEntry<bool> DumpNames;
        internal static ConfigEntry<bool> ShowHud;
        internal static ConfigEntry<bool> AddMapPins, PinNames, MinimapMarker;
        internal static ConfigEntry<Highlight> HighlightStyle;
        internal static ConfigEntry<bool> HideUndiscovered;
        internal static ConfigEntry<int> MaxPinsPerLayer;

        internal static bool WindowOpen;
        private static Plugin s_instance;
        private readonly Pad _pad = new Pad();

        private readonly Finder _finder = new Finder();
        private Result _target;
        private ResourceEntry _currentEntry;
        private Layer _targetLayer;
        private float _nextPurge;
        private Vector2 _layerScroll;
        private bool _showHidden;
        private string _search = "";
        private Vector2 _catalogScroll, _resultScroll;
        private Rect _window = new Rect(60, 60, 1100, 800);
        private GUIStyle _hudStyle, _hudDist, _hudArrow, _small;

        // Cache d'affichage : la liste triée et les infos de cueillette ne sont recalculées que toutes les 0,5 s
        // (OnGUI est appelé plusieurs fois par image).
        private readonly List<Result> _shown = new List<Result>();
        private readonly Dictionary<Result, string> _pickInfo = new Dictionary<Result, string>();
        private float _nextUiRefresh;
        private bool _namesDumped;

        private void Awake()
        {
            Log = Logger;
            s_instance = this;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le mod (fenêtre, indicateur, épingles)."));
            Enabled.SettingChanged += (_, __) => Layers.RefreshPins();
            ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F7, L.T("Touche qui ouvre/ferme la fenêtre du scanner."));
            NextTargetKey = Config.Bind("General", "NextTargetKey", KeyCode.F6, L.T("Touche : cible suivante (par distance) dans la couche ciblée, sans ouvrir la fenêtre."));
            TrackKey = Config.Bind("General", "TrackKey", KeyCode.F4, L.T("Touche : mode Traque on/off. En traque, dès que la cible est tuée ou récoltée, la plus proche suivante est visée depuis votre position ; s'il n'en reste plus, la recherche est relancée d'ici."));
            ResultCount = Config.Bind("General", "ResultCount", 3,
                new ConfigDescription(L.T("Nombre de résultats voulus : le scan s'arrête dès qu'il les a."), new AcceptableValueRange<int>(1, 20)));
            MaxScanRadius = Config.Bind("Scan", "MaxScanRadius", 3000f,
                new ConfigDescription(L.T("Rayon (m) du premier scan des zones inconnues. Les zones générées sont enregistrées dans le monde, ") +
                                      L.T("comme si vous y étiez passé. 0 = ne jamais générer, chercher seulement dans le monde connu."),
                    new AcceptableValueRange<float>(0f, 10000f)));
            ExtendedScanRadius = Config.Bind("Scan", "ExtendedScanRadius", 10000f,
                new ConfigDescription(L.T("Rayon (m) atteint par le bouton « Chercher plus loin » quand le premier scan n'a pas assez de résultats ") +
                                      L.T("(10000 = tout le monde)."), new AcceptableValueRange<float>(0f, 10000f)));
            ScanBudgetMs = Config.Bind("Scan", "ScanBudgetMs", 8f,
                new ConfigDescription(L.T("Temps max (ms) consacré au scan à chaque image. Plus = scan plus rapide mais le jeu saccade. ") +
                                      L.T("Un PC modeste génère simplement moins de zones par image."), new AcceptableValueRange<float>(1f, 50f)));
            DumpNames = Config.Bind("Debug", "DumpNames", true,
                L.T("Au premier chargement de monde, écrit la liste des noms de prefabs et de lieux dans BepInEx/config/ResourceFinder.names.txt ") +
                L.T("(utile pour compléter le catalogue). Ne coûte rien ensuite."));
            ShowHud = Config.Bind("Display", "ShowHud", true, L.T("Indicateur à l'écran (point / flèche + distance) vers la cible."));
            HighlightStyle = Config.Bind("Display", "HighlightStyle", Highlight.Lueur, L.T("Surbrillance de la cible chargée : Lueur = la silhouette de l'objet s'éclaire (émission pulsée sur ses matériaux), Cadre = coins dessinés autour, LesDeux, Aucun."));
            MinimapMarker = Config.Bind("Display", "MinimapMarker", true, L.T("Repère de la cible au bord de la mini-carte quand elle est hors du cadre."));
            AddMapPins = Config.Bind("Display", "AddMapPins", true, L.T("Épingles sur la carte pour les résultats."));
            PinNames = Config.Bind("Display", "PinNames", false, L.T("Nom de la ressource sous chaque épingle du mod. Désactivé : icône seule, la carte reste lisible quand les épingles se touchent."));
            PinNames.SettingChanged += (_, __) => Layers.MarkDirty();
            MaxPinsPerLayer = Config.Bind("Display", "MaxPinsPerLayer", 30,
                new ConfigDescription(L.T("Nombre max de positions (donc d'épingles) conservées par couche : les plus proches. ") +
                                      L.T("Des milliers d'épingles ralentissent fortement le jeu."), new AcceptableValueRange<int>(3, 200)));
            HideUndiscovered = Config.Bind("Display", "HideUndiscovered", true,
                L.T("Immersion : ne proposer que les ressources dont vous avez déjà eu le matériau en main et les lieux dont vous avez visité le biome. ") +
                L.T("Les autres peuvent être révélés un par un dans la fenêtre."));

            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Log.LogInfo($"Resource Finder chargé (touche {ToggleKey.Value})");
        }

        // ------------------------------------------------------------------ boucle

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null) { if (WindowOpen) Close(); Layers.Unload(); _target = null; return; }

            if (!Enabled.Value) { if (WindowOpen) Close(); Layers.Tick(); return; }
            if (ZInput.GetKeyDown(ToggleKey.Value, false)) { if (WindowOpen) Close(); else Open(); }
            else if (WindowOpen && (ZInput.GetKeyDown(KeyCode.Escape, false) || _pad.Update())) Close();
            else if (ZInput.GetKeyDown(NextTargetKey.Value, false)) NextTarget(player.transform.position);
            else if (TrackKey.Value != KeyCode.None && ZInput.GetKeyDown(TrackKey.Value, false) && !Console.IsVisible() && !TextInput.IsVisible()) ToggleTrack();

            if (!_namesDumped && DumpNames.Value && ZNetScene.instance != null) { _namesDumped = true; DumpGameNames(); }

            Layers.Tick();
            if (Time.unscaledTime >= _nextPurge) { _nextPurge = Time.unscaledTime + 2f; Layers.Purge(); }

            var prev = _finder.State;
            _finder.Tick(player.transform.position);
            if (prev != Finder.Phase.Done && _finder.State == Finder.Phase.Done) OnSearchDone();

            if (_animatedTarget != _target) { Layers.Animate(_animatedTarget, false); Layers.Animate(_target, true); _animatedTarget = _target; }
            UpdateGlow();

            // Cible disparue (minée, cueillie et détruite, tuée...) → suivante de la même couche ; en Traque, on enchaîne depuis ici et,
            // s'il ne reste rien, la recherche est relancée depuis la position actuelle (chasser vingt biches sans rouvrir la fenêtre)
            if (_target != null && !_target.StillExists())
            {
                _finder.Results.Remove(_target);
                var from = player.transform.position;
                _target = Nearest(_targetLayer?.Results ?? _finder.Results, from);
                if (Tracking)
                {
                    if (_target == null && _currentEntry != null && _finder.State == Finder.Phase.Done) { _trackRelaunch = true; StartSearch(_currentEntry); }
                    else if (_target != null) player.Message(MessageHud.MessageType.TopLeft, L.F("Traque : {0} à {1:0} m", DisplayName(_target), _target.Distance(from)));
                }
            }
        }

        /// <summary>Mode Traque : la cible suivante s'enchaîne toute seule (touche TrackKey, bouton, roue).</summary>
        internal static bool Tracking;
        private bool _trackRelaunch;
        private void ToggleTrack()
        {
            Tracking = !Tracking;
            var p = Player.m_localPlayer;
            if (Tracking && _target == null && _currentEntry != null && p != null) StartSearch(_currentEntry);
            p?.Message(MessageHud.MessageType.Center, Tracking ? L.T("Traque activée") + (_currentEntry != null ? " : " + L.T(_currentEntry.Label) : "") + L.F(", {0} pour arrêter", TrackKey.Value) : L.T("Traque arrêtée"));
        }

        private bool _focusSearch;
        private Result _animatedTarget;
        private void Open() { WindowOpen = true; _pad.OnOpened(); FitWindow(); _focusSearch = !Pad.Active; }
        private void Close() { WindowOpen = false; }

        /// <summary>Efface toutes les épingles posées par le mod (couches, recherche courante, cible). Les épingles du joueur restent.</summary>
        private void ClearAllPins()
        {
            _finder.Cancel(); _finder.Results.Clear();
            _target = null; _targetLayer = null;
            Layers.RemoveAll();
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, L.T("Épingles du scanner effacées"));
        }

        // Entrées du menu radial manette, lues par réflexion par Mod Hub : « libellé|icône » → action, l'icône étant
        // un prefab d'item ou « @fichier.png » embarqué dans cette DLL (dossier Icons).
        /// <summary>Aides de touches (panneau du jeu, via Mod Hub) : contextuelles, avec une cible, cible suivante et traque ; sinon ouvrir le scanner.</summary>
        public static List<KeyValuePair<string, KeyCode>> KeyHints()
        {
            var l = new List<KeyValuePair<string, KeyCode>>();
            if (!Enabled.Value) return l;
            if (s_instance != null && s_instance._target != null)
            {
                l.Add(new KeyValuePair<string, KeyCode>(L.T("Cible suivante"), NextTargetKey.Value));
                l.Add(new KeyValuePair<string, KeyCode>(Tracking ? "Arrêter la traque" : "Traquer", TrackKey.Value));
            }
            else l.Add(new KeyValuePair<string, KeyCode>("Scanner", ToggleKey.Value));
            return l;
        }

        public static List<KeyValuePair<string, Action>> RadialEntries() => new List<KeyValuePair<string, Action>>
        {
            new KeyValuePair<string, Action>("Scanner de ressources|@scanner.png", () => { if (s_instance != null && Enabled.Value) { if (WindowOpen) s_instance.Close(); else s_instance.Open(); } }),
            new KeyValuePair<string, Action>("Cible suivante|@target.png", () => { if (s_instance != null && Enabled.Value && Player.m_localPlayer != null) s_instance.NextTarget(Player.m_localPlayer.transform.position); }),
            new KeyValuePair<string, Action>((Tracking ? "Arrêter la traque" : "Traquer") + "|@track.png", () => { if (s_instance != null && Enabled.Value) s_instance.ToggleTrack(); }),
            new KeyValuePair<string, Action>("Effacer les épingles|@clear.png", () => { if (s_instance != null && Enabled.Value) s_instance.ClearAllPins(); }),
        };

        /// <summary>Grande carte centrée sur un point, rapprochée (zoom borné par le jeu) pour voir tout de suite l'épingle et son entourage.</summary>
        private static readonly AccessTools.FieldRef<Minimap, float> s_largeZoom = AccessTools.FieldRefAccess<Minimap, float>("m_largeZoom");
        private static void ShowOnMap(Vector3 pos)
        {
            var map = Minimap.instance;
            if (map == null) return;
            s_largeZoom(map) = Mathf.Clamp(0.03f, map.m_minZoom, map.m_maxZoom);
            map.ShowPointOnMap(pos);
        }

        /// <summary>Cible suivante par distance croissante dans la couche ciblée (ou la recherche courante).</summary>
        private void NextTarget(Vector3 from)
        {
            var list = new List<Result>(_targetLayer?.Results ?? _finder.Results);
            list.RemoveAll(r => !r.StillExists());
            if (list.Count == 0) return;
            list.Sort((a, b) => a.Distance(from).CompareTo(b.Distance(from)));
            int i = _target != null ? list.IndexOf(_target) : -1;
            // Après le dernier résultat : guidage arrêté (les épingles restent) ; l'appui suivant repart du plus proche
            if (i >= 0 && i + 1 >= list.Count) { _target = null; Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, L.T("Guidage arrêté (épingles conservées)")); return; }
            _target = list[i + 1];
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, L.F("Cible {0}/{1} : {2}, {3:0} m", i + 2, list.Count, DisplayName(_target), _target.Distance(from)));
        }

        private void OnSearchDone()
        {
            var player = Player.m_localPlayer;
            if (WindowOpen) FitWindow();
            if (_currentEntry != null && _finder.Results.Count > 0)
                _targetLayer = Layers.Merge(_currentEntry, _finder.Results);
            _target = player != null ? Nearest(_finder.Results, player.transform.position) : null;
            Log.LogInfo($"[{_finder.Label}] {_finder.Status}");
            if (_trackRelaunch)
            {
                _trackRelaunch = false;
                if (_target != null) { player?.Message(MessageHud.MessageType.TopLeft, L.F("Traque : {0} à {1:0} m", DisplayName(_target), _target.Distance(player.transform.position))); return; }
                Tracking = false;
                player?.Message(MessageHud.MessageType.Center, L.T("Traque terminée : plus rien à proximité"));
                return;
            }
            // Fenêtre fermée pendant le scan : on prévient quand même du résultat
            if (!WindowOpen && player != null)
                player.Message(MessageHud.MessageType.TopLeft, _target != null
                    ? L.F("Scanner : {0} × {1}, le plus proche à {2:0} m", _finder.Results.Count, L.T(_finder.Label), _target.Distance(player.transform.position))
                    : L.F("Scanner : rien trouvé pour {0}", L.T(_finder.Label)));
        }

        private static Result Nearest(List<Result> results, Vector3 from)
        {
            Result best = null; float bd = float.MaxValue;
            foreach (var r in results)
            {
                if (!r.StillExists()) continue;
                float d = r.Distance(from);
                if (d < bd) { bd = d; best = r; }
            }
            return best;
        }

        /// <summary>Texte tapé → entrée du catalogue dont le libellé contient le texte (sans accents ni casse : « cuivre »,
        /// « or », « autel eikthyr »), le libellé le plus court gagnant ; sinon recherche libre sur les noms internes.</summary>
        private static ResourceEntry ResolveSearch(string text)
        {
            string q = Normalize(text);
            ResourceEntry best = null; int bestScore = 0;
            foreach (var e in Catalog.Entries)
            {
                string l = Normalize(e.Label);
                if (l == q) return e;
                // exact > début du libellé > début d'un mot > n'importe où ; à égalité le libellé le plus court (« or » → « Or (…) », pas « Morgen »)
                string padded = " " + l + " ";
                int score = padded.IndexOf(" " + q + " ", StringComparison.Ordinal) >= 0 ? 4 : l.StartsWith(q, StringComparison.Ordinal) ? 3 : padded.IndexOf(" " + q, StringComparison.Ordinal) >= 0 ? 2 : l.IndexOf(q, StringComparison.Ordinal) >= 0 ? 1 : 0;
                if (score == 0) continue;
                if (best == null || score > bestScore || (score == bestScore && e.Label.Length < best.Label.Length)) { best = e; bestScore = score; }
            }
            return best ?? Finder.FreeSearch(text);
        }

        private static string Normalize(string s)
        {
            var d = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(d.Length);
            foreach (var ch in d)
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                sb.Append(char.IsLetterOrDigit(ch) ? ch : ' '); // ponctuation (« : », « ( ») → espace
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), " +", " ").Trim();
        }

        private void StartSearch(ResourceEntry entry)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            _target = null;
            _currentEntry = entry;
            _targetLayer = Layers.Get(entry.Label);
            _finder.Start(entry, player.transform.position);
            // Une entrée « lieux seulement » (autels, cryptes, marchand…) est terminée dès Start : la boucle Update ne
            // voit jamais la transition vers Done, il faut conclure ici (cible, couche, épingles).
            if (_finder.State == Finder.Phase.Done) OnSearchDone();
        }

        /// <summary>
        /// API pour les autres mods (lue par réflexion, ex. le Guide) : lance la recherche d'une entrée du catalogue par
        /// libellé, sans ouvrir la fenêtre ; l'indicateur à l'écran guide ensuite vers la plus proche. Faux si inconnu.
        /// </summary>
        public static bool SearchLabel(string label)
        {
            if (s_instance == null || !Enabled.Value || Player.m_localPlayer == null) return false;
            foreach (var e in Catalog.Entries)
                if (string.Equals(e.Label, label, StringComparison.OrdinalIgnoreCase))
                {
                    s_instance.StartSearch(e);
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, L.T("Scanner : ") + e.Label);
                    return true;
                }
            Log.LogWarning($"SearchLabel : entrée « {label} » inconnue");
            return false;
        }

        /// <summary>Boutons à ajouter dans le panneau d'inventaire (Mod Hub les crée) : libellé|icône → action.</summary>
        public static List<KeyValuePair<string, Action>> InventoryEntries() => new List<KeyValuePair<string, Action>>
        {
            new KeyValuePair<string, Action>("Scanner de ressources|@scanner.png", () => { if (s_instance != null && Enabled.Value) { InventoryGui.instance?.Hide(); s_instance.Open(); } }),
        };

        // ------------------------------------------------------------------ interface

        private GUIStyle _h1, _h2, _rowLabel, _rowHidden, _panel, _badge, _tab;
        private int _category = -1;          // -1 = tout
        private readonly List<ResourceEntry> _visibleEntries = new List<ResourceEntry>();
        private float _clearArmedUntil;
        private readonly Dictionary<ResourceEntry, string> _rowText = new Dictionary<ResourceEntry, string>();
        private readonly Dictionary<ResourceEntry, string> _rowTextOn = new Dictionary<ResourceEntry, string>(); // ligne sélectionnée : textes secondaires sombres
        private readonly Dictionary<Category, string> _catHeader = new Dictionary<Category, string>(); // « Cueillette  (5) » figé à chaque rafraîchissement
        private readonly List<ResourceEntry> _hiddenEntries = new List<ResourceEntry>();
        private bool _anyRevealed;
        private float _nextCatalogRefresh;

        private void EnsureStyles()
        {
            if (_hudStyle != null) return;
            _hudStyle = new GUIStyle(Theme.Skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _hudStyle.normal.textColor = Theme.Text;
            _hudDist = new GUIStyle(_hudStyle) { fontSize = 15 }; _hudDist.normal.textColor = Theme.Accent;
            _hudArrow = new GUIStyle(_hudStyle) { fontSize = 18, alignment = TextAnchor.MiddleCenter }; _hudArrow.normal.textColor = Theme.Accent;
            _small = new GUIStyle(Theme.Skin.label) { fontSize = 13, wordWrap = true };
            _small.normal.textColor = Theme.MutedColor;
            _h1 = new GUIStyle(Theme.H1); _h2 = new GUIStyle(Theme.H2); // Norsebold, comme partout
            _rowLabel = new GUIStyle(Theme.Skin.toggle) { alignment = TextAnchor.MiddleLeft, fontSize = 14 };
            _rowLabel.padding = new RectOffset(12, 12, 0, 0); _rowLabel.margin = new RectOffset(4, 4, 4, 4);
            _rowHidden = new GUIStyle(_rowLabel); _rowHidden.normal.textColor = Theme.MutedColor; _rowHidden.hover.textColor = Theme.MutedColor;
            _panel = new GUIStyle(Theme.Skin.box) { padding = new RectOffset(14, 14, 10, 14), margin = new RectOffset(0, 0, 0, 12) };
            _h1.margin = new RectOffset(0, 0, 0, 10); _h2.margin = new RectOffset(0, 0, 14, 4);
            _small.margin = new RectOffset(4, 4, 2, 6);
            _badge = new GUIStyle(Theme.Skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _badge.normal.textColor = Theme.Accent;
            _tab = new GUIStyle(Theme.Skin.button) { fontSize = 13 };
            _tab.padding = new RectOffset(12, 12, 6, 6);
        }

        /// <summary>IMGUI ne suit pas l'échelle d'interface du jeu : on dessine en coordonnées « 1080p » et on agrandit
        /// tout (polices, icônes, boutons) avec GUI.matrix selon la hauteur d'écran (×1,33 en 1440p).</summary>
        /// <summary>Fenêtre centrée, grande (plafonnée à 1300×920 en unités 1080p), recalculée à chaque ouverture.</summary>
        /// <summary>Fenêtre à la taille du contenu : en début de partie le catalogue tient en quelques lignes, inutile d'occuper tout l'écran.
        /// Hauteur = colonne la plus haute (catalogue ou recherche + résultats + couches), bornée ; recalculée à l'ouverture et à chaque recherche.</summary>
        private void FitWindow()
        {
            _nextCatalogRefresh = 0f; RefreshCatalog();
            int categories = 0; var seen = new HashSet<Category>(); foreach (var e in _visibleEntries) if (seen.Add(e.Category)) categories++;
            float left = 140f + _visibleEntries.Count * 42f + categories * 34f + (_hiddenEntries.Count > 0 ? 56f : 0f) + (_showHidden ? _hiddenEntries.Count * 42f : 0f);
            int results = Mathf.Min(_finder.Results.Count, Mathf.Max(1, ResultCount.Value));
            float right = 140f + 80f + results * 42f + 70f + Mathf.Clamp(64f + Layers.All.Count * 38f, 96f, 240f);
            float h = Mathf.Clamp(Mathf.Max(left, right) + 120f, 520f, 920f);
            _window = Theme.CenteredWindow(1300f, h);
        }

        private void OnGUI()
        {
            if (Player.m_localPlayer == null || !Enabled.Value) return;
            var prev = Theme.Begin();
            EnsureStyles();
            if (ShowHud.Value && !WindowOpen && !Hud.IsUserHidden() && !(Minimap.instance != null && Minimap.IsOpen())) { DrawHud(); DrawMinimapMarker(); }
            if (WindowOpen && !_pad.ConsumeSkipRepaint())
                _window = GUILayout.Window(GetHashCode(), _window, DrawWindow, L.T("Scanner de ressources"));
            Theme.End(prev);
        }

        private void RefreshCatalog()
        {
            if (Time.unscaledTime < _nextCatalogRefresh) return;
            _nextCatalogRefresh = Time.unscaledTime + 0.5f;
            _visibleEntries.Clear(); _hiddenEntries.Clear();
            foreach (var e in Catalog.Entries)
            {
                if (_category >= 0 && (int)e.Category != _category) continue;
                (Discovery.IsVisible(e) ? _visibleEntries : _hiddenEntries).Add(e);
            }
            _catHeader.Clear();
            // Texte de chaque ligne figé ici : libellé + « révélé » + nombre d'épingles déjà posées pour cette ressource
            _rowText.Clear(); _rowTextOn.Clear(); _anyRevealed = false;
            foreach (var e in _visibleEntries)
            {
                bool revealedOnly = HideUndiscovered.Value && !Discovery.IsDiscovered(e);
                _anyRevealed |= revealedOnly;
                var layer = Layers.Get(e.Label);
                int pins = layer != null ? layer.Results.Count : 0;
                string biomes = Discovery.BiomeText(e);
                string revealed = revealedOnly ? "   <size=12>{0}" + L.T("révélé") + "</color></size>" : "";
                string extra = (biomes.Length > 0 ? "   <size=12>{1}" + biomes + "</color></size>" : "") + (pins > 0 ? "   <size=12>{1}" + pins + L.T(" sur la carte") + "</color></size>" : "");
                _rowText[e] = L.T(e.Label) + revealed.Replace("{0}", "<color=#f5a847>") + extra.Replace("{1}", "<color=#c9c2b4>");
                _rowTextOn[e] = L.T(e.Label) + revealed.Replace("{0}", "<color=#3a2a10>") + extra.Replace("{1}", "<color=#4a3a22>");
            }
            foreach (Category c in Enum.GetValues(typeof(Category)))
            {
                int n = 0; foreach (var e in _visibleEntries) if (e.Category == c) n++;
                _catHeader[c] = Catalog.CategoryLabel(c) + L.T("  <size=12><color=#cfcabf>(") + n + ")</color></size>";
            }
        }

        private void DrawWindow(int id)
        {
            _pad.BeginWindow();
            if (Theme.CloseButton(_window)) Close();
            var from = Player.m_localPlayer.transform.position;
            RefreshCatalog();
            RefreshUiCache(from);

            GUILayout.BeginHorizontal();
            // ------------------------------------------------ colonne gauche : catalogue
            GUILayout.BeginVertical(GUILayout.Width(_window.width * 0.44f));
            DrawCatalog();
            GUILayout.EndVertical();

            GUILayout.Space(16);

            // ------------------------------------------------ colonne droite : recherche, résultats, couches
            GUILayout.BeginVertical();
            DrawSearchPanel(from);
            bool searching = _finder.State != Finder.Phase.Idle;
            if (searching) DrawResults(from);
            DrawLayers(from, !searching);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // ------------------------------------------------ pied
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (_pad.Button(L.T("Cible : la plus proche"))) _target = Nearest(_finder.Results, from);
            if (_pad.Button(L.T("Effacer la recherche"))) { _finder.Cancel(); _finder.Results.Clear(); _target = null; Tracking = false; }
            if (Layers.All.Count > 0)
            {
                bool anyVisible = false; foreach (var l in Layers.All) if (l.Visible) { anyVisible = true; break; }
                if (_pad.Button(anyVisible ? L.T("Masquer les épingles") : L.T("Afficher les épingles"))) foreach (var l in Layers.All) Layers.SetVisible(l, !anyVisible);
            }
            // Suppression des couches (persistées) en deux temps : « Confirmer ? » pendant 3 s
            bool armed = Time.unscaledTime < _clearArmedUntil;
            if (_pad.Button(armed ? L.T("<color=#f5a847>Confirmer : tout effacer ?</color>") : L.T("Effacer toutes les épingles du mod")))
            {
                if (armed) { ClearAllPins(); _clearArmedUntil = 0f; } else _clearArmedUntil = Time.unscaledTime + 3f;
            }
            GUILayout.FlexibleSpace();
            if (_pad.Button(Pad.Active ? L.T("Fermer  (B)") : L.T("Fermer  (F7)"), GUILayout.Width(130))) Close();
            GUILayout.EndHorizontal();
            Pad.Hints(_small, L.T("<b>Échap</b> ou <b>F7</b> fermer    <b>Entrée</b> chercher    <b>clic droit</b> sur un résultat : carte    <b>F6</b> cible suivante"));
            _pad.EndWindow();
            GUI.DragWindow();
        }

        private void DrawCatalog()
        {
            GUILayout.BeginVertical(_panel, GUILayout.ExpandHeight(true));
            GUILayout.Label(L.T("Catalogue"), _h1);

            // Onglets de catégorie
            GUILayout.BeginHorizontal();
            if (_pad.Toggle(_category < 0, L.T("Tout"), _tab) && _category >= 0) { _category = -1; _nextCatalogRefresh = 0f; }
            foreach (Category c in Enum.GetValues(typeof(Category)))
            {
                bool sel = _category == (int)c;
                if (_pad.Toggle(sel, Catalog.CategoryLabel(c), _tab) && !sel) { _category = (int)c; _nextCatalogRefresh = 0f; _catalogScroll = Vector2.zero; }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // Recherche libre (clavier seulement : à la manette on choisit dans la liste)
            if (!Pad.Active)
            {
                GUILayout.BeginHorizontal();
                GUI.SetNextControlName("finder_search");
                _search = GUILayout.TextField(_search, GUILayout.Height(30));
                if (_focusSearch && Event.current.type == EventType.Repaint) { GUI.FocusControl("finder_search"); _focusSearch = false; } // au clavier : on peut taper tout de suite
                // Entrée dans le champ = chercher
                bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "finder_search";
                if (enter) Event.current.Use();
                if ((_pad.Button(L.T("Chercher"), GUILayout.Width(100), GUILayout.Height(30)) || enter) && _search.Trim().Length >= 2) StartSearch(ResolveSearch(_search));
                GUILayout.EndHorizontal();
                GUILayout.Label(L.T("Nom du catalogue (cuivre, or, autel…) ou nom interne du jeu (copper, Pickable_, Crypt)."), _small);
            }
            GUILayout.Space(8);

            _catalogScroll = _pad.BeginScrollView(_catalogScroll, GUILayout.ExpandHeight(true));
            Category? lastCat = null;
            foreach (var e in _visibleEntries)
            {
                if (_category < 0 && lastCat != e.Category) { lastCat = e.Category; GUILayout.Label(_catHeader[e.Category], _h2); }
                bool revealedOnly = HideUndiscovered.Value && !Discovery.IsDiscovered(e);
                GUILayout.BeginHorizontal();
                Icons.DrawLayout(Icons.ForEntry(e), 28f, 42f);
                // La ressource en cours de recherche reste surlignée dans le catalogue
                bool isCurrent = e == _currentEntry && _finder.State != Finder.Phase.Idle;
                if (_pad.Toggle(isCurrent, (isCurrent ? _rowTextOn : _rowText).TryGetValue(e, out var rowText) ? rowText : L.T(e.Label), _rowLabel, GUILayout.ExpandWidth(true), GUILayout.Height(34)) != isCurrent) StartSearch(e); // recliquer = relancer
                if (revealedOnly) { if (_pad.Button("×", GUILayout.Width(34), GUILayout.Height(34))) Layers.SetRevealed(e.Label, false); }
                else if (_anyRevealed) GUILayout.Space(40); // colonne du « × » réservée : les lignes gardent la même largeur
                GUILayout.EndHorizontal();
            }
            if (_visibleEntries.Count == 0) GUILayout.Label(L.T("Rien de découvert dans cette catégorie pour l'instant."), _small);

            if (_hiddenEntries.Count > 0)
            {
                GUILayout.Space(14);
                _showHidden = _pad.Toggle(_showHidden, L.F(" Non découverts ({0}), révéler brise l'immersion", _hiddenEntries.Count));
                if (_showHidden)
                {
                    foreach (var e in _hiddenEntries)
                    {
                        GUILayout.BeginHorizontal();
                        Icons.DrawLayout(Icons.ForEntry(e), 28f, 42f);
                        GUILayout.Label(L.T(e.Label), _rowHidden, GUILayout.ExpandWidth(true), GUILayout.Height(34));
                        if (_pad.Button(L.T("Révéler"), GUILayout.Width(90), GUILayout.Height(34))) Layers.SetRevealed(e.Label, true);
                        GUILayout.EndHorizontal();
                    }
                }
            }
            _pad.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawSearchPanel(Vector3 from)
        {
            GUILayout.BeginVertical(_panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("Recherche"), _h1);
            GUILayout.FlexibleSpace();
            if (_finder.State != Finder.Phase.Idle && _pad.Toggle(Tracking, Tracking ? L.F("Traque en cours  ({0} : arrêter)", TrackKey.Value) : L.F("Traquer  ({0})", TrackKey.Value), Theme.Skin.toggle, GUILayout.Width(220)) != Tracking) ToggleTrack();
            if (_finder.State == Finder.Phase.Scanning && _pad.Button(L.T("Stop"), GUILayout.Width(70))) _finder.Cancel();
            GUILayout.EndHorizontal();
            if (_finder.State == Finder.Phase.Idle)
                GUILayout.Label(L.T("Choisissez une ressource dans le catalogue. Les résultats sont épinglés sur la carte et le plus proche est pointé à l'écran."), _small);
            else
            {
                GUILayout.BeginHorizontal();
                Icons.DrawLayout(Icons.ForEntry(_currentEntry), 26f);
                GUILayout.Label($"<b>{L.T(_finder.Label)}</b>  <color=#cfcabf>-</color>  {_finder.Status}");
                GUILayout.EndHorizontal();
                // Créature introuvable : où elle vit, d'après les listes de spawn du jeu
                if (_currentEntry != null && _currentEntry.Category == Category.Creature && _finder.State == Finder.Phase.Done && _finder.Results.Count == 0)
                {
                    string where = Discovery.BiomeText(_currentEntry);
                    GUILayout.Label(where.Length > 0 ? L.F("Vit dans : <b>{0}</b>, allez-y, elle sera détectée une fois la zone chargée.", where) : "Cette créature n'apparaît que par événement ou dans certains lieux (pas de zone de spawn libre).", _small);
                }
                if (_finder.State == Finder.Phase.Scanning)
                {
                    // Barre d'avancement du scan (zones traitées), thème commun
                    var bar = GUILayoutUtility.GetRect(10f, 8f, GUILayout.ExpandWidth(true));
                    if (Event.current.type == EventType.Repaint) Theme.ProgressBar(new Rect(bar.x + 4f, bar.y + 1f, bar.width - 8f, 6f), _finder.ScanProgress);
                }
                if (_finder.CanExtend)
                {
                    _finder.EstimateExtension(out int zones, out float secs);
                    string eta = secs < 90f ? $"{secs:0} s" : $"{secs / 60f:0.#} min";
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(L.F("Seulement {0}/{1} dans {2:0} m.", _finder.Results.Count, ResultCount.Value, _finder.EffectiveRadius), _small);
                    if (_pad.Button(L.F("Chercher plus loin (jusqu'à {0:0.#} km, ~{1} zones, ≈ {2})", ExtendedScanRadius.Value / 1000f, zones, eta))) _finder.Extend();
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawResults(Vector3 from)
        {
            GUILayout.BeginVertical(_panel, GUILayout.ExpandHeight(true));
            GUILayout.Label(L.T("Résultats") + $"  <size=12><color=#cfcabf>({_shown.Count})</color></size>", _h1);
            _resultScroll = _pad.BeginScrollView(_resultScroll, GUILayout.ExpandHeight(true));
            foreach (var r in _shown)
            {
                GUILayout.BeginHorizontal();
                bool isTarget = r == _target;
                Icons.DrawLayout(r.IsLocation ? Icons.ForEntry(_currentEntry) : (Icons.ForPrefab(r.Prefab) ?? Icons.ForEntry(_currentEntry)), 28f, 42f);
                _pickInfo.TryGetValue(r, out var info);
                // Toute la ligne est le bouton « cibler » (un seul élément à parcourir à la manette) ; la ligne ciblée reste surlignée
                string sec = isTarget ? "#4a3a22" : "#c9c2b4";
                string row = (isTarget ? "<color=#3a2a10>►</color>  " : "") + $"<b>{r.Distance(from):0} m</b> <color={sec}>{Compass(from, r.Pos)}</color>   {DisplayName(r)}<color={sec}>{info}</color>";
                if (_pad.Toggle(isTarget, row, _rowLabel, GUILayout.ExpandWidth(true), GUILayout.Height(34)) && !isTarget) _target = r;
                // Clic droit sur la ligne = grande carte centrée dessus (souris)
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) { _target = r; Close(); ShowOnMap(r.Pos); Event.current.Use(); }
                // Grande carte centrée sur ce résultat
                if (_pad.Button(L.T("Carte"), GUILayout.Width(70), GUILayout.Height(34)) && Minimap.instance != null) { _target = r; Close(); ShowOnMap(r.Pos); } // (ou clic droit sur la ligne)
                GUILayout.EndHorizontal();
            }
            if (_shown.Count == 0 && _finder.State != Finder.Phase.Idle) GUILayout.Label(L.T("Aucun résultat pour l'instant."), _small);
            _pad.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>Couches connues pour ce monde : afficher/masquer sur les cartes, cibler, supprimer.</summary>
        private void DrawLayers(Vector3 from, bool fill)
        {
            if (fill) GUILayout.BeginVertical(_panel, GUILayout.ExpandHeight(true));
            else GUILayout.BeginVertical(_panel, GUILayout.Height(Mathf.Clamp(64f + Layers.All.Count * 38f, 96f, 240f)));
            GUILayout.Label(L.T("Couches sur la carte") + $"  <size=12><color=#cfcabf>({Layers.All.Count}), " + L.T("positions connues, conservées entre les sessions") + "</color></size>", _h1);
            _layerScroll = _pad.BeginScrollView(_layerScroll);
            Layer toRemove = null;
            foreach (var l in Layers.All)
            {
                GUILayout.BeginHorizontal();
                Icons.DrawLayout(l.Icon(), 26f, 38f);
                GUILayout.Label($"{L.T(l.Label)}  <color=#d6d1c6>({l.Results.Count})</color>", GUILayout.ExpandWidth(true), GUILayout.Height(30));
                bool vis = _pad.Toggle(l.Visible, L.T(l.Visible ? "Visible" : "Masquée"), GUILayout.Width(90), GUILayout.Height(30));
                if (vis != l.Visible) Layers.SetVisible(l, vis);
                if (_pad.Button(L.T("Cibler"), GUILayout.Width(80), GUILayout.Height(30))) { _targetLayer = l; _target = Nearest(l.Results, from); }
                if (_pad.Button(L.T("Supprimer"), GUILayout.Width(100), GUILayout.Height(30))) toRemove = l;
                GUILayout.EndHorizontal();
            }
            if (Layers.All.Count == 0) GUILayout.Label(L.T("Aucune couche : chaque recherche crée la sienne."), _small);
            if (toRemove != null) { if (_targetLayer == toRemove) { _targetLayer = null; _target = null; } Layers.Remove(toRemove); }
            _pad.EndScrollView();
            GUILayout.EndVertical();
        }

        private void RefreshUiCache(Vector3 from)
        {
            if (Time.unscaledTime < _nextUiRefresh) return;
            _nextUiRefresh = Time.unscaledTime + 0.5f;
            _shown.Clear();
            _shown.AddRange(_finder.Results);
            _shown.Sort((a, b) => a.Distance(from).CompareTo(b.Distance(from)));
            int keep = Mathf.Max(1, ResultCount.Value); // même borne que la recherche : les N plus proches, pas 30
            if (_shown.Count > keep) _shown.RemoveRange(keep, _shown.Count - keep);
            _pickInfo.Clear();
            foreach (var r in _shown) _pickInfo[r] = PickableInfo(r);
        }

        /// <summary>Écrit les noms de prefabs et de lieux du jeu dans un fichier texte (une fois par session).</summary>
        private static void DumpGameNames()
        {
            try
            {
                var lines = new List<string> { L.T("# Prefabs (ZNetScene)") };
                foreach (var go in ZNetScene.instance.m_prefabs) if (go != null) lines.Add(go.name);
                lines.Add(""); lines.Add(L.T("# Lieux (ZoneSystem.m_locations)"));
                if (ZoneSystem.instance != null)
                    foreach (var loc in ZoneSystem.instance.m_locations) if (loc != null) lines.Add(loc.m_prefabName + "	" + loc.m_biome);
                var path = System.IO.Path.Combine(Paths.ConfigPath, "ResourceFinder.names.txt");
                System.IO.File.WriteAllLines(path, lines);
                Log.LogInfo($"Noms du jeu exportés dans {path}");
            }
            catch (Exception ex) { Log.LogWarning($"Export des noms impossible : {ex.Message}"); }
        }

        /// <summary>Pour les cueillettes : "cueilli, repousse dans X min".</summary>
        private static string PickableInfo(Result r)
        {
            if (r.IsLocation || ZDOMan.instance == null || ZNetScene.instance == null) return "";
            var zdo = ZDOMan.instance.GetZDO(r.Id);
            if (zdo == null || !zdo.GetBool(ZDOVars.s_picked, false)) return "";
            var pickable = ZNetScene.instance.GetPrefab(r.Hash)?.GetComponent<Pickable>();
            if (pickable == null || pickable.m_respawnTimeMinutes <= 0f) return L.T(" (cueilli)");
            long picked = zdo.GetLong(ZDOVars.s_pickedTime, 0L);
            double elapsedMin = (ZNet.instance.GetTime().Ticks - picked) / (double)TimeSpan.TicksPerMinute;
            double remaining = pickable.m_respawnTimeMinutes - elapsedMin;
            return remaining <= 0 ? L.T(" (repousse bientôt)") : L.F(" (cueilli, repousse dans {0:0} min)", remaining);
        }


        // ------------------------------------------------------------------ libellés

        private static readonly Dictionary<string, string> s_displayNames = new Dictionary<string, string>();

        /// <summary>Nom lisible d'un résultat : créature (nom localisé du Character), objet (nom de l'item), lieu ou prefab.</summary>
        internal static string DisplayName(Result r)
        {
            if (s_displayNames.TryGetValue(r.Prefab, out var cached)) return cached;
            string name = null;
            try
            {
                var go = ZNetScene.instance != null && !r.IsLocation ? ZNetScene.instance.GetPrefab(r.Prefab) : null;
                var character = go != null ? go.GetComponent<Character>() : null;
                if (character != null && !string.IsNullOrEmpty(character.m_name)) name = Localization.instance.Localize(character.m_name);
                else
                {
                    var item = Icons.ItemForPrefab(r.Prefab);
                    if (item != null) name = Localization.instance.Localize(item.m_itemData.m_shared.m_name);
                }
            }
            catch { }
            // Ni créature ni butin identifiable : le libellé du catalogue plutôt qu'un nom interne « rock 4 copper »
            if (string.IsNullOrEmpty(name)) foreach (var e in Catalog.Entries) if (Array.IndexOf(e.Prefabs, r.Prefab) >= 0) { name = L.T(e.Label); break; }
            if (string.IsNullOrEmpty(name)) name = Prettify(r.Prefab);
            s_displayNames[r.Prefab] = name;
            return name;
        }

        /// <summary>« Mistlands_DvergrTownEntrance1 » → « Mistlands Dvergr Town Entrance 1 ».</summary>
        private static string Prettify(string prefab)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < prefab.Length; i++)
            {
                char c = prefab[i];
                if (c == '_') { sb.Append(' '); continue; }
                if (i > 0 && (char.IsUpper(c) || char.IsDigit(c)) && !char.IsUpper(prefab[i - 1]) && !char.IsDigit(prefab[i - 1]) && prefab[i - 1] != ' ' && prefab[i - 1] != '_') sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Direction cardinale (N, NE, E…) de la cible depuis le joueur, dans le repère du monde (z = nord).</summary>
        private static readonly string[] s_dirs = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
        internal static string Compass(Vector3 from, Vector3 to)
        {
            var d = to - from; d.y = 0f;
            if (d.sqrMagnitude < 1f) return L.T("ici");
            float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; if (ang < 0f) ang += 360f;
            return s_dirs[Mathf.RoundToInt(ang / 45f) % 8];
        }

        // ------------------------------------------------------------------ HUD
        // Repère de la cible au bord de la mini-carte quand elle est hors du cadre : on sait dans quelle direction
        // regarder sans ouvrir la grande carte. Conversion monde → carte → pixels par les méthodes privées du jeu.
        private delegate void WorldToMap(Minimap m, Vector3 p, out float mx, out float my);
        private static WorldToMap s_worldToMap;
        private static Func<Minimap, float, float, UnityEngine.UI.RawImage, Vector2> s_mapToGui;
        private static bool s_minimapTried, s_minimapOk;
        // Résolution paresseuse et tolérante (signatures explicites : MapPointToLocalGuiPos a plusieurs surcharges) : si le jeu
        // change, le repère est simplement désactivé, le mod se charge quand même.
        private static bool MinimapDelegates()
        {
            if (s_minimapTried) return s_minimapOk;
            s_minimapTried = true;
            try
            {
                s_worldToMap = AccessTools.MethodDelegate<WorldToMap>(AccessTools.Method(typeof(Minimap), "WorldToMapPoint", new[] { typeof(Vector3), typeof(float).MakeByRefType(), typeof(float).MakeByRefType() }));
                s_mapToGui = AccessTools.MethodDelegate<Func<Minimap, float, float, UnityEngine.UI.RawImage, Vector2>>(AccessTools.Method(typeof(Minimap), "MapPointToLocalGuiPos", new[] { typeof(float), typeof(float), typeof(UnityEngine.UI.RawImage) }));
                s_minimapOk = s_worldToMap != null && s_mapToGui != null;
            }
            catch (Exception ex) { Log.LogWarning("Repère mini-carte indisponible : " + ex.Message); s_minimapOk = false; }
            return s_minimapOk;
        }

        private void DrawMinimapMarker()
        {
            if (_target == null || !MinimapMarker.Value || !MinimapDelegates()) return;
            var map = Minimap.instance;
            if (map == null || map.m_mapImageSmall == null || map.m_smallRoot == null || !map.m_smallRoot.activeInHierarchy) return;
            var img = map.m_mapImageSmall; var rt = img.rectTransform;
            s_worldToMap(map, _target.Pos, out float mx, out float my);
            var local = s_mapToGui(map, mx, my, img);
            var rect = rt.rect;
            const float inset = 12f;
            float hw = rect.width / 2f - inset, hh = rect.height / 2f - inset;
            if (Mathf.Abs(local.x) <= hw && Mathf.Abs(local.y) <= hh) return; // l'épingle elle-même est visible sur la mini-carte
            // Point au bord du cadre, dans la direction de la cible
            float k = Mathf.Min(hw / Mathf.Max(Mathf.Abs(local.x), 0.001f), hh / Mathf.Max(Mathf.Abs(local.y), 0.001f));
            var edge = local * k;
            var world = rt.TransformPoint(new Vector3(edge.x + rect.center.x, edge.y + rect.center.y, 0f));
            var cam = img.canvas != null && img.canvas.renderMode != RenderMode.ScreenSpaceOverlay ? img.canvas.worldCamera : null;
            var sp = RectTransformUtility.WorldToScreenPoint(cam, world);
            float s = Theme.UiScale;
            var p = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
            const float size = 22f;
            var r = new Rect(p.x - size / 2f, p.y - size / 2f, size, size);
            Theme.Fill(new Rect(r.x - 2f, r.y - 2f, size + 4f, size + 4f), new Color(0f, 0f, 0f, 0.4f));
            if (_hudIcon != null) Icons.Draw(r, _hudIcon); else Theme.Fill(r, Theme.Accent);
        }


        private Result _hudTarget; private Layer _hudLayer; private string _hudLabel, _hudDistText; private int _hudDistKey = -1; private float _hudLabelW, _hudDistW; private Sprite _hudIcon;
        private static readonly GUIContent s_content = new GUIContent();
        private bool _hudTracking;

        // Surbrillance de la cible : un cadre à coins (couleur accent, pulsé) autour de l'objet réel quand il est chargé,
        // ------------------------------------------------------------------ lueur sur la cible (forme réelle de l'objet)
        // Les shaders du jeu (Custom/Creature, Custom/Piece, Custom/Vegetation…) exposent _EmissionColor et _Color : on
        // pousse une couleur émissive pulsée par MaterialPropertyBlock sur chaque rendu de l'objet chargé, la silhouette
        // entière s'éclaire, sans instancier ni modifier les matériaux ; retiré dès que la cible change ou disparaît.
        private readonly List<Renderer> _glowRenderers = new List<Renderer>();
        private ZDOID _glowId; private float _glowNextLookup;
        private static readonly MaterialPropertyBlock s_mpb = new MaterialPropertyBlock();
        private static readonly int s_emission = Shader.PropertyToID("_EmissionColor"), s_color = Shader.PropertyToID("_Color");

        private void UpdateGlow()
        {
            bool want = _target != null && !_target.IsLocation && Enabled.Value && (HighlightStyle.Value == Highlight.Lueur || HighlightStyle.Value == Highlight.LesDeux);
            if (!want || _glowId != _target.Id || Time.unscaledTime >= _glowNextLookup)
            {
                ClearGlow();
                if (!want) return;
                _glowId = _target.Id; _glowNextLookup = Time.unscaledTime + 1f;
                var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(_target.Id) : null;
                var nv = zdo != null && ZNetScene.instance != null ? ZNetScene.instance.FindInstance(zdo) : null;
                if (nv == null) return;
                foreach (var r in nv.GetComponentsInChildren<Renderer>())
                    if (r != null && r.GetType().Name != "ParticleSystemRenderer") _glowRenderers.Add(r);
            }
            if (_glowRenderers.Count == 0) return;
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3.5f);
            var glow = new Color(Theme.Accent.r * 1.6f * pulse, Theme.Accent.g * 1.2f * pulse, Theme.Accent.b * 0.6f * pulse, 1f);
            var tint = Color.Lerp(Color.white, Theme.Accent, 0.35f * pulse);
            for (int i = _glowRenderers.Count - 1; i >= 0; i--)
            {
                var r = _glowRenderers[i];
                if (r == null) { _glowRenderers.RemoveAt(i); continue; }
                r.GetPropertyBlock(s_mpb);
                s_mpb.SetColor(s_emission, glow);
                s_mpb.SetColor(s_color, tint);
                r.SetPropertyBlock(s_mpb);
            }
        }

        private void ClearGlow()
        {
            foreach (var r in _glowRenderers) { if (r == null) continue; try { r.SetPropertyBlock(null); } catch { } }
            _glowRenderers.Clear();
            _glowId = ZDOID.None;
        }

        internal enum Highlight { Lueur, Cadre, LesDeux, Aucun }

        // calculé sur les rendus de l'objet, lisible dans la végétation quel que soit le shader, sans toucher aux matériaux.
        private ZDOID _hlId; private GameObject _hlGo; private float _hlNextLookup;
        private void DrawTargetHighlight(Camera cam, float s)
        {
            if (_target == null || _target.IsLocation || ZNetScene.instance == null || (HighlightStyle.Value != Highlight.Cadre && HighlightStyle.Value != Highlight.LesDeux)) return;
            if (_hlId != _target.Id || _hlGo == null || Time.unscaledTime >= _hlNextLookup)
            {
                _hlId = _target.Id; _hlNextLookup = Time.unscaledTime + 1f;
                var zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(_target.Id) : null;
                var nv = zdo != null ? ZNetScene.instance.FindInstance(zdo) : null;
                _hlGo = nv != null ? nv.gameObject : null;
            }
            if (_hlGo == null) return; // zone pas chargée : rien à encadrer
            bool any = false; var b = new Bounds();
            foreach (var r in _hlGo.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled || r.GetType().Name == "ParticleSystemRenderer") continue; // pas de module particules référencé
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return;
            // 8 coins → rectangle écran (unités 1080p)
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var sp = cam.WorldToScreenPoint(c);
                if (sp.z < 0f) return; // derrière la caméra
                float x = sp.x / s, y = (Screen.height - sp.y) / s;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            float w = maxX - minX, h = maxY - minY;
            if (w < 6f || h < 6f || w > Screen.width / s || h > Screen.height / s) return;
            float pad = 4f, len = Mathf.Clamp(Mathf.Min(w, h) * 0.3f, 8f, 28f), th = 2f;
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4f);
            var col = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, pulse);
            minX -= pad; minY -= pad; maxX += pad; maxY += pad;
            // coins : haut-gauche, haut-droit, bas-gauche, bas-droit
            Theme.Fill(new Rect(minX, minY, len, th), col); Theme.Fill(new Rect(minX, minY, th, len), col);
            Theme.Fill(new Rect(maxX - len, minY, len, th), col); Theme.Fill(new Rect(maxX - th, minY, th, len), col);
            Theme.Fill(new Rect(minX, maxY - th, len, th), col); Theme.Fill(new Rect(minX, maxY - len, th, len), col);
            Theme.Fill(new Rect(maxX - len, maxY - th, len, th), col); Theme.Fill(new Rect(maxX - th, maxY - len, th, len), col);
        }

        private void DrawHud()
        {
            if (_target == null) return;
            var cam = Utils.GetMainCamera();
            if (cam == null) return;
            float s = Theme.UiScale;
            float sw = Screen.width / s, sh = Screen.height / s;
            DrawTargetHighlight(cam, s);

            var from = Player.m_localPlayer.transform.position;
            float dist = _target.Distance(from);
            string label = L.T(_targetLayer?.Label ?? _finder.Label);
            // Textes, largeurs et icône figés tant que la cible, le libellé et la distance arrondie ne changent pas
            // (OnGUI passe plusieurs fois par image : pas de formatage ni de CalcSize à chaque passage)
            int distKey = dist >= 1000f ? 1000 + Mathf.RoundToInt(dist / 100f) : Mathf.RoundToInt(dist);
            if (_hudTarget != _target || _hudLayer != _targetLayer || !ReferenceEquals(_hudLabel, label) || _hudDistKey != distKey || _hudTracking != Tracking)
            {
                _hudTarget = _target; _hudLayer = _targetLayer; _hudLabel = label; _hudDistKey = distKey; _hudTracking = Tracking;
                _hudDistText = dist >= 1000f ? $"{dist / 1000f:0.0} km" : dist < 3f ? L.T("ici") : $"{dist:0} m";
                // Pas de compteur sur la pastille : le rang n'est dit qu'au moment d'appuyer sur F6
                if (Tracking) _hudDistText += L.T("  <size=12><color=#7cc35a>traque</color></size>");
                s_content.text = label; _hudLabelW = _hudStyle.CalcSize(s_content).x;
                s_content.text = _hudDistText; _hudDistW = _hudDist.CalcSize(s_content).x;
                _hudIcon = _targetLayer?.Icon() ?? (_target.IsLocation ? Icons.ForEntry(_currentEntry) : (Icons.ForPrefab(_target.Prefab) ?? Icons.ForEntry(_currentEntry)));
            }
            string distText = _hudDistText;

            Vector3 sp = cam.WorldToScreenPoint(_target.Pos + Vector3.up * 1.5f);
            bool behind = sp.z < 0f;
            var p = new Vector2(sp.x / s, sh - sp.y / s);
            if (behind) p = new Vector2(sw - p.x, sh - p.y);

            const float margin = 60f;
            bool onScreen = !behind && p.x > margin && p.x < sw - margin && p.y > margin && p.y < sh - margin;
            string arrow = "";
            if (!onScreen)
            {
                // Vers le bord de l'écran, avec une flèche dans la direction de la cible
                var center = new Vector2(sw / 2f, sh / 2f);
                var dir = p - center;
                if (dir.sqrMagnitude < 1f) dir = Vector2.up;
                dir.Normalize();
                float sx = (sw / 2f - margin) / Mathf.Max(Mathf.Abs(dir.x), 0.0001f);
                float sy = (sh / 2f - margin) / Mathf.Max(Mathf.Abs(dir.y), 0.0001f);
                p = center + dir * Mathf.Min(sx, sy);
                arrow = Mathf.Abs(dir.x) > Mathf.Abs(dir.y) ? (dir.x > 0 ? "▶" : "◀") : (dir.y > 0 ? "▼" : "▲");
            }

            // Pastille : icône + nom + distance, fond arrondi du thème, petit point/flèche vers la cible
            var icon = _hudIcon; float labelW = _hudLabelW, distW = _hudDistW;
            float iconW = icon != null ? 30f : 0f;
            float w = 12f + iconW + (iconW > 0 ? 8f : 0f) + labelW + 10f + distW + 12f, h = 36f;
            // Cible à l'écran : la pastille est AU-DESSUS du point (jamais dessus), la flèche ▼ entre les deux ; hors écran : centrée sur le bord
            var rect = onScreen ? new Rect(p.x - w / 2f, p.y - h - 30f, w, h) : new Rect(p.x - w / 2f, p.y - h / 2f, w, h);
            if (rect.y < 4f) rect.y = 4f;
            GUI.Box(rect, GUIContent.none, Theme.Veil); // même voile sans cadre que le suivi de quête
            float x = rect.x + 12f;
            if (icon != null) { Icons.Draw(new Rect(x, rect.y + 3f, 30f, 30f), icon); x += 38f; }
            Theme.ShadowLabel(new Rect(x, rect.y, labelW + 4f, h), label, _hudStyle); x += labelW + 10f;
            Theme.ShadowLabel(new Rect(x, rect.y, distW + 4f, h), distText, _hudDist);
            if (onScreen) GUI.Label(new Rect(p.x - 10f, rect.yMax + 2f, 20f, 20f), "▼", _hudDist);
            else
            {
                // flèche collée au bord, du côté de la cible
                var a = new Rect(p.x - 12f, p.y - 12f, 24f, 24f);
                if (arrow == "▲") a.y = rect.y - 24f; else if (arrow == "▼") a.y = rect.yMax; else if (arrow == "◀") a.x = rect.x - 24f; else a.x = rect.xMax;
                GUI.Label(a, arrow, _hudArrow);
            }
        }
    }

    internal static class Patches
    {
        // Fenêtre ouverte = le jeu se comporte comme si un champ texte était actif :
        // la souris est libérée (GameCamera.UpdateMouseCapture) et le joueur ne bouge pas (PlayerController.TakeInput).
        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        [HarmonyPostfix]
        private static void TextInput_IsVisible(ref bool __result)
        {
            if (Plugin.WindowOpen) __result = true;
        }

        // Découverte par la vue : ce que le viseur désigne (rocher, buisson, gisement…) et toute créature dont la barre de
        // vie s'affiche sont marqués « vus » sur le personnage, plus besoin de l'avoir récolté ou tué pour que le catalogue le montre.
        [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
        [HarmonyPostfix]
        private static void Hud_UpdateCrosshair(Player player)
        {
            try { if (player != null) Discovery.OnHover(player.GetHoverObject()); } catch { }
        }

        [HarmonyPatch(typeof(EnemyHud), "ShowHud")]
        [HarmonyPostfix]
        private static void EnemyHud_ShowHud(Character c)
        {
            try
            {
                if (c == null || c.IsPlayer()) return;
                var nv = c.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid()) Discovery.MarkSeen(Discovery.PrefabName(nv));
            }
            catch { }
        }

        // Pendant une génération fantôme, on récupère les ZDO créés pour y chercher la ressource.
        [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.CreateNewZDO), typeof(Vector3), typeof(int))]
        [HarmonyPostfix]
        private static void ZDOMan_CreateNewZDO(ZDO __result)
        {
            if (Finder.Capturing && __result != null) Finder.Captured.Add(__result);
        }
    }
}
