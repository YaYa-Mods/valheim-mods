using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace Guide
{
    /// <summary>
    /// Guide de progression : un objectif principal par boss, des étapes obligatoires / conseillées / optionnelles
    /// cochées automatiquement d'après ce que le jeu enregistre (voir Model.cs), un suivi à l'écran (sous la
    /// mini-carte) et une fenêtre complète (F10, menu radial, bouton dans l'inventaire). « Cibler » lance le scanner
    /// de ressources sur l'entrée correspondante (par réflexion : aucune dépendance entre DLL).
    /// </summary>
    [BepInPlugin(Guid, "Guide", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.guide";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<bool> ShowTracker;
        internal static ConfigEntry<TrackerMode> Mode;
        internal static ConfigEntry<KeyCode> TrackerKey;
        internal enum TrackerMode { Complet, Reduit, Masque }
        internal static ConfigEntry<int> TrackerSteps;
        internal static ConfigEntry<float> TrackerX, TrackerY;
        internal static ConfigEntry<bool> Notify;
        internal static ConfigEntry<bool> HideFuture;
        internal static ConfigEntry<bool> AltarPin;
        internal static bool WindowOpen;
        private static Plugin s_instance;

        private readonly Pad _pad = new Pad();
        private Rect _window = new Rect(60, 60, 1100, 800);
        private Vector2 _chapterScroll, _stepScroll;
        private Chapter _selected;
        private Chapter _trackerChapter; private List<Step> _trackerNext = new List<Step>(); private int _trackerDone; private float _trackerNextRefresh;
        private GUIStyle _nowrap, _chapterRow, _trackerCompact, _trackerDoneLine;
        private GUIStyle _h1, _h2, _small, _step, _stepDone, _tag, _trackerTitle, _trackerLine, _trackerMuted, _trackerCount;
        private static MethodInfo s_searchLabel;
        private static bool s_searchLooked;

        private void Awake()
        {
            Log = Logger;
            s_instance = this;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le guide (suivi à l'écran et fenêtre)."));
            ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F10, L.T("Touche qui ouvre/ferme le guide complet."));
            Notify = Config.Bind("General", "Notify", true, L.T("Message à l'écran quand une étape est accomplie."));
            HideFuture = Config.Bind("General", "HideFuture", true, L.T("Anti-spoiler : les chapitres non atteints sont masqués (titre, étapes, récompense) jusqu'à la victoire sur le boss précédent. Chaque chapitre peut être révélé à la main."));
            AltarPin = Config.Bind("General", "AltarPin", true, L.T("Épingle automatiquement l'autel du chapitre courant sur la carte dès qu'il se trouve en zone explorée (rien n'est dévoilé : seulement ce que vous avez déjà vu)."));
            ShowTracker = Config.Bind("Tracker", "ShowTracker", true, L.T("Afficher l'objectif courant et ses prochaines étapes à l'écran."));
            Mode = Config.Bind("Tracker", "Mode", TrackerMode.Complet, L.T("Suivi à l'écran : Complet (objectif, barre, prochaines étapes), Reduit (une ligne : objectif et prochaine étape), Masque. Touche TrackerKey ou roue d'action → Mods pour passer de l'un à l'autre ; il se déploie quelques secondes quand une étape est accomplie."));
            TrackerKey = Config.Bind("Tracker", "TrackerKey", KeyCode.F11, L.T("Touche qui fait tourner le suivi : complet → réduit → masqué."));
            TrackerSteps = Config.Bind("Tracker", "Steps", 4, new ConfigDescription(L.T("Nombre d'étapes affichées sous l'objectif."), new AcceptableValueRange<int>(1, 8)));
            TrackerX = Config.Bind("Tracker", "X", -350f, new ConfigDescription(L.T("Position X du suivi (unités 1080p ; négatif = depuis le bord droit)."), new AcceptableValueRange<float>(-1920f, 1920f)));
            TrackerY = Config.Bind("Tracker", "Y", 290f, new ConfigDescription(L.T("Position Y du suivi (unités 1080p, depuis le haut)."), new AcceptableValueRange<float>(0f, 1080f)));
            if (Mathf.Approximately(TrackerX.Value, -330f)) TrackerX.Value = -350f; // ancien défaut (suivi de 310 px) : suit l'élargissement à 330 px
            Progress.StepCompleted += OnStepCompleted;
            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Log.LogInfo($"Guide chargé ({Chapters.All.Count} chapitres, touche {ToggleKey.Value})");
        }

        private Step _recentStep; private Chapter _recentChapter; private float _recentTime;

        private void OnStepCompleted(Chapter c, Step s)
        {
            _recentStep = s; _recentChapter = c; _recentTime = Time.unscaledTime; _trackerNextRefresh = 0f;
            _expandUntil = Time.unscaledTime + 6f;
            if (!Notify.Value || Player.m_localPlayer == null) return;
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, $"Guide : ✓ {s.DisplayTitle}");
            if (s.Id == "kill")
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, L.F("Chapitre terminé : {0}", c.DisplayTitle));
                // Le chapitre suivant devient l'objectif : on l'annonce et le suivi se déploie dessus
                var next = Progress.Current();
                if (next != null && next != c && !Progress.ChapterDone(next)) { Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, L.T("Nouvel objectif : ") + next.DisplayTitle); _expandUntil = Time.unscaledTime + 10f; _trackerNextRefresh = 0f; }
            }
        }

        private void Update()
        {
            var player = Player.m_localPlayer;
            if (player == null) { WindowOpen = false; _loginPlayer = null; return; }
            if (!Enabled.Value) { WindowOpen = false; return; }
            // À l'arrivée dans le monde : le suivi (même réduit) se déploie 12 s pour rappeler où on en était
            if (_loginPlayer != player) { _loginPlayer = player; _expandUntil = Time.unscaledTime + 12f; }
            if (ZInput.GetKeyDown(ToggleKey.Value, false) && !Console.IsVisible()) { if (WindowOpen) Close(); else Open(); }
            else if (TrackerKey.Value != KeyCode.None && ZInput.GetKeyDown(TrackerKey.Value, false) && !Console.IsVisible() && !TextInput.IsVisible()) CycleTracker();
            else if (WindowOpen && (ZInput.GetKeyDown(KeyCode.Escape, false) || _pad.Update())) Close();
            Progress.Tick(player);
            AltarPins.Tick();
        }

        private void Open() { WindowOpen = true; _pad.OnOpened(); FitWindow(); _selected = Progress.Current(); Progress.Tick(Player.m_localPlayer, true); }
        private void Close() { WindowOpen = false; }
        internal static void Toggle() { if (s_instance == null) return; if (WindowOpen) s_instance.Close(); else s_instance.Open(); }

        /// <summary>Aides de touches (panneau du jeu, via Mod Hub).</summary>
        public static List<KeyValuePair<string, KeyCode>> KeyHints() => !Enabled.Value ? new List<KeyValuePair<string, KeyCode>>() : new List<KeyValuePair<string, KeyCode>>
        {
            new KeyValuePair<string, KeyCode>("Guide", ToggleKey.Value),
        };

        public static List<KeyValuePair<string, Action>> RadialEntries() => new List<KeyValuePair<string, Action>>
        {
            new KeyValuePair<string, Action>("Guide de progression|@guide.png", () => { if (Enabled.Value) Toggle(); }),
            // Le suivi HUD se masque/affiche d'un geste (libellé selon l'état au moment où la roue se construit)
            new KeyValuePair<string, Action>(L.T("Suivi : ") + ModeLabel(NextMode(Mode.Value)) + "|@tracker.png", CycleTracker),
        };
        public static List<KeyValuePair<string, Action>> InventoryEntries() => new List<KeyValuePair<string, Action>>
        {
            new KeyValuePair<string, Action>("Guide de progression|@guide.png", () => { if (Enabled.Value) { InventoryGui.instance?.Hide(); Toggle(); } }),
        };

        private static TrackerMode NextMode(TrackerMode m) => m == TrackerMode.Complet ? TrackerMode.Reduit : m == TrackerMode.Reduit ? TrackerMode.Masque : TrackerMode.Complet;
        private static string ModeLabel(TrackerMode m) => m == TrackerMode.Complet ? L.T("complet") : m == TrackerMode.Reduit ? L.T("réduit") : L.T("masqué");
        /// <summary>Complet → réduit → masqué (touche ou roue d'action), avec confirmation à l'écran.</summary>
        internal static void CycleTracker()
        {
            Mode.Value = NextMode(Mode.Value);
            if (s_instance != null) s_instance._expandUntil = 0f;
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, L.T("Suivi du guide : ") + ModeLabel(Mode.Value));
        }
        private bool _showLocked; // liste complète des chapitres verrouillés dépliée
        private bool _trackerWasShown, _trackerWasCompact; private float _trackerShownAt;
        private float _expandUntil; private Player _loginPlayer; // le suivi réduit se déploie quelques secondes quand une étape vient d'être accomplie

        // ------------------------------------------------------------------ scanner (réflexion)

        private static bool FinderAvailable()
        {
            if (!s_searchLooked)
            {
                s_searchLooked = true;
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        if (asm.GetName().Name == "ResourceFinder") s_searchLabel = asm.GetType("ResourceFinder.Plugin")?.GetMethod("SearchLabel", BindingFlags.Public | BindingFlags.Static);
                }
                catch { }
            }
            return s_searchLabel != null;
        }

        private static void Target(string label)
        {
            if (!FinderAvailable()) return;
            try
            {
                bool ok = (bool)s_searchLabel.Invoke(null, new object[] { label });
                // La fenêtre se ferme : le joueur voit tout de suite la pastille HUD qui pointe la cible
                if (ok && s_instance != null) { s_instance.Close(); Player.m_localPlayer?.Message(MessageHud.MessageType.Center, L.T("Cible : ") + label); }
            }
            catch (Exception ex) { Log.LogWarning("Cibler : " + ex.Message); }
        }

        // ------------------------------------------------------------------ interface

        private void FitWindow() => _window = Theme.CenteredWindow(1200f, 860f);

        private void EnsureStyles()
        {
            if (_h1 != null) return;
            _h1 = new GUIStyle(Theme.H1); _h2 = new GUIStyle(Theme.H2);
            _nowrap = new GUIStyle(Theme.Muted) { wordWrap = false, alignment = TextAnchor.MiddleRight };
            _chapterRow = new GUIStyle(Theme.Skin.button) { alignment = TextAnchor.MiddleLeft, wordWrap = false }; _chapterRow.padding.left = 12; _chapterRow.padding.bottom = 8;
            _small = new GUIStyle(Theme.Skin.label) { fontSize = 13, wordWrap = true }; _small.normal.textColor = Theme.MutedColor;
            _step = new GUIStyle(Theme.Skin.label) { fontSize = 14, wordWrap = true };
            _stepDone = new GUIStyle(_step); _stepDone.normal.textColor = new Color(0.55f, 0.75f, 0.45f);
            _tag = new GUIStyle(Theme.Skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight }; _tag.normal.textColor = Theme.MutedColor;
            _trackerTitle = new GUIStyle(Theme.H2) { fontSize = 18 }; // Norsebold, comme les titres du jeu
            _trackerLine = new GUIStyle(Theme.Skin.label) { fontSize = 13, wordWrap = true }; _trackerLine.padding = new RectOffset(2, 2, 1, 1);
            _trackerCompact = new GUIStyle(Theme.H2) { fontSize = 15, wordWrap = false, clipping = TextClipping.Clip }; _trackerCompact.padding = new RectOffset(2, 2, 0, 0);
            _trackerMuted = new GUIStyle(_trackerLine); _trackerMuted.normal.textColor = Theme.MutedColor;
            _trackerDoneLine = new GUIStyle(_trackerLine); _trackerDoneLine.normal.textColor = new Color(0.60f, 0.82f, 0.48f);
            _trackerCount = new GUIStyle(_trackerMuted) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            _trackerTitle.wordWrap = false;
        }

        private void OnGUI()
        {
            if (Player.m_localPlayer == null || !Enabled.Value) return;
            var prev = Theme.Begin();
            EnsureStyles();
            bool showTracker = ShowTracker.Value && Mode.Value != TrackerMode.Masque && !WindowOpen && TrackerAllowed();
            if (showTracker)
            {
                // Apparition en fondu (0,35 s) quand le suivi revient après une interface, un chargement, un changement de mode
                bool compact = Mode.Value == TrackerMode.Reduit && Time.unscaledTime >= _expandUntil;
                if (!_trackerWasShown || compact != _trackerWasCompact) _trackerShownAt = Time.unscaledTime;
                _trackerWasCompact = compact;
                float alpha = Mathf.Clamp01((Time.unscaledTime - _trackerShownAt) / 0.35f);
                var prevColor = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha);
                DrawTracker(compact);
                GUI.color = prevColor;
            }
            _trackerWasShown = showTracker;
            if (WindowOpen && !_pad.ConsumeSkipRepaint())
                _window = GUILayout.Window(GetHashCode(), _window, DrawWindow, L.T("Guide de progression"));
            Theme.End(prev);
        }

        /// <summary>Le suivi fait partie du HUD, comme la mini-carte : il disparaît dès qu'une interface passe devant
        /// (inventaire, carte, menu, marchand, dialogue, console, saisie de texte, construction, nos fenêtres…).</summary>
        private static bool TrackerAllowed()
        {
            var p = Player.m_localPlayer;
            if (p == null || p.IsDead() || p.InCutscene() || p.IsTeleporting()) return false;
            // Écran de chargement (téléportation, arrivée dans le monde) : rien ne doit rester par-dessus
            if (Hud.instance != null && Hud.instance.m_loadingScreen != null && Hud.instance.m_loadingScreen.gameObject.activeSelf) return false;
            if (Hud.IsUserHidden() || Hud.IsPieceSelectionVisible()) return false;
            if (InventoryGui.IsVisible() || Menu.IsVisible() || StoreGui.IsVisible() || Console.IsVisible() || TextInput.IsVisible()) return false;
            if (Minimap.instance != null && Minimap.IsOpen()) return false;
            if (Game.IsPaused()) return false;
            return true;
        }

        private static string NeedTag(Need n) => n == Need.Required ? "" : n == Need.Advised ? L.T("conseillé") : "optionnel";

        private void DrawTracker(bool compact)
        {
            var chapter = Progress.Current();
            float x = TrackerX.Value < 0 ? Theme.ScreenSize.x + TrackerX.Value : TrackerX.Value;
            // La liste des prochaines étapes et le compte ne changent qu'à l'évaluation (1 s) : recalcul toutes les 0,5 s, pas à chaque passage OnGUI
            if (_trackerChapter != chapter || Time.unscaledTime >= _trackerNextRefresh)
            {
                _trackerChapter = chapter; _trackerNextRefresh = Time.unscaledTime + 0.5f;
                _trackerNext = Progress.Next(chapter, TrackerSteps.Value, essentials: true);
                _trackerDone = 0; foreach (var s in chapter.Steps) if (Progress.Get(chapter, s).Done) _trackerDone++;
            }
            var next = _trackerNext; int done = _trackerDone;
            float frac = chapter.Steps.Count > 0 ? (float)done / chapter.Steps.Count : 0f;
            bool repaint = Event.current.type == EventType.Repaint;

            // Style « moderne » : pas de cadre, un voile sombre qui s'estompe vers le bas, textes ombrés, accents fins.
            GUILayout.BeginArea(new Rect(x, TrackerY.Value, 340f, 400f));
            GUILayout.BeginVertical(Theme.Veil);

            // ---- en-tête : trophée, titre en Norse, compteur ; filet accent qui s'efface vers la droite
            GUILayout.BeginHorizontal();
            var chIcon = Facts.ChapterIcon(chapter);
            float iconSize = compact ? 22f : 30f;
            if (chIcon != null) { Theme.SpriteLayout(chIcon, iconSize); GUILayout.Space(8f); }
            Theme.ShadowLabel(chapter.DisplayTitle, compact ? _trackerCompact : _trackerTitle, GUILayout.ExpandWidth(true));
            Theme.ShadowLabel($"{done}<size=11>/{chapter.Steps.Count}</size>", _trackerCount, GUILayout.Width(48f));
            GUILayout.EndHorizontal();
            var rule = GUILayoutUtility.GetRect(10f, compact ? 3f : 4f, GUILayout.ExpandWidth(true));
            if (repaint)
            {
                Theme.FadeLine(new Rect(rule.x + 2f, rule.y, rule.width - 4f, 1f), new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.7f));
                // barre d'avancement : piste discrète, remplissage accent avec un point lumineux au bout
                var pr = new Rect(rule.x + 2f, rule.y + (compact ? 2f : 3f), rule.width - 4f, compact ? 1f : 2f);
                Theme.Fill(pr, new Color(1f, 1f, 1f, 0.10f));
                if (frac > 0f) { Theme.Fill(new Rect(pr.x, pr.y, pr.width * frac, pr.height), Theme.Accent); Theme.Fill(new Rect(pr.x + pr.width * frac - 2f, pr.y - 1f, 4f, pr.height + 2f), new Color(1f, 0.9f, 0.7f, 0.9f)); }
            }
            GUILayout.Space(compact ? 3f : 6f);

            if (compact)
            {
                // Mode réduit : la prochaine étape seulement
                if (next.Count > 0) StepLine(chapter, next[0], true, false);
                else Theme.ShadowLabel(Progress.ChapterDone(chapter) ? L.T("Chapitre terminé") : L.T("Tout est prêt : au combat !"), _trackerLine);
                GUILayout.EndVertical();
                GUILayout.EndArea();
                return;
            }

            // Étape tout juste accomplie : reste affichée cochée en vert quelques secondes avant de laisser la place
            if (_recentStep != null && _recentChapter == chapter && Time.unscaledTime - _recentTime < 5f) StepLine(chapter, _recentStep, false, true);
            else _recentStep = null;
            for (int i = 0; i < next.Count; i++) StepLine(chapter, next[i], i == 0, false);
            if (next.Count == 0) Theme.ShadowLabel(Progress.ChapterDone(chapter) ? L.T("Chapitre terminé") : L.T("Tout est prêt : au combat !"), _trackerLine);
            GUILayout.Space(2f);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>Une ligne d'étape du suivi : marqueur dessiné, icône, titre ombré ; l'étape courante a un liseré et un texte plus clair.</summary>
        private void StepLine(Chapter chapter, Step s, bool current, bool done)
        {
            var st = Progress.Get(chapter, s);
            string prog = st.Progress.Length > 0 && !done ? $"  <color=#f5a847>{st.Progress}</color>" : "";
            GUILayout.BeginHorizontal();
            Marker(s.Need == Need.Required, done);
            Theme.SpriteLayout(Facts.StepIcon(s), 20f); // case réservée même sans icône : lignes alignées
            GUILayout.Space(2f);
            var style = done ? _trackerDoneLine : current ? _trackerLine : _trackerMuted;
            Theme.ShadowLabel(s.DisplayTitle + prog, style);
            GUILayout.EndHorizontal();
            if (current && !done && Event.current.type == EventType.Repaint)
            {
                var r = GUILayoutUtility.GetLastRect();
                // liseré accent à gauche de la ligne courante + léger surlignage qui s'estompe vers la droite
                Theme.FadeLine(new Rect(r.x - 6f, r.y + 1f, r.width * 0.9f, r.height - 2f), new Color(1f, 0.75f, 0.35f, 0.10f));
                Theme.Fill(new Rect(r.x - 8f, r.y + 2f, 2f, r.height - 4f), Theme.Accent);
            }
        }

        /// <summary>Marqueur d'étape dessiné (pas un caractère de police) : losange plein accent = obligatoire, creux = conseillé, coche verte = fait.</summary>
        private static void Marker(bool required, bool done)
        {
            var r = GUILayoutUtility.GetRect(16f, 20f, GUILayout.Width(16f), GUILayout.Height(20f));
            if (Event.current.type != EventType.Repaint) return;
            var g = new Rect(r.x + 2f, r.y + 4f, 12f, 12f);
            if (done) Theme.DrawCheck(new Rect(r.x, r.y + 2f, 16f, 16f), new Color(0.49f, 0.76f, 0.35f));
            else Theme.DrawDiamond(g, required ? Theme.Accent : new Color(0.62f, 0.58f, 0.50f), required);
        }

        private static readonly Dictionary<Chapter, List<Step>> s_ordered = new Dictionary<Chapter, List<Step>>();
        /// <summary>Étapes triées par nécessité (tri stable), calculé une fois par chapitre plutôt qu'à chaque passage OnGUI.</summary>
        private static List<Step> OrderedSteps(Chapter ch)
        {
            if (!s_ordered.TryGetValue(ch, out var l)) { l = ch.Steps.OrderBy(s => (int)s.Need).ToList(); s_ordered[ch] = l; }
            return l;
        }

        /// <summary>Positions prêtes à l'emploi du suivi (unités 1080p ; X négatif = depuis le bord droit).</summary>
        private static readonly KeyValuePair<string, Vector2>[] s_presets =
        {
            new KeyValuePair<string, Vector2>("Sous la carte", new Vector2(-350f, 290f)),
            new KeyValuePair<string, Vector2>("Haut gauche", new Vector2(20f, 130f)),
            new KeyValuePair<string, Vector2>("Bas gauche", new Vector2(20f, 700f)),
            new KeyValuePair<string, Vector2>("Bas droite", new Vector2(-350f, 700f)),
        };

        private void DrawWindow(int id)
        {
            _pad.BeginWindow();
            if (Theme.CloseButton(_window)) Close();
            if (_selected == null) _selected = Progress.Current();
            var current = Progress.Current();

            GUILayout.BeginHorizontal();
            // ---- chapitres
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(300f), GUILayout.ExpandHeight(true));
            GUILayout.Label(L.T("Chapitres"), _h1);
            _chapterScroll = _pad.BeginScrollView(_chapterScroll, GUILayout.ExpandHeight(true));
            int n = 1; int lockedShown = 0;
            foreach (var c in Chapters.All)
            {
                bool done = Progress.ChapterDone(c);
                bool unlocked = Progress.IsUnlocked(c);
                // Les chapitres verrouillés au-delà du prochain sont repliés en une ligne : on sait qu'il en reste, sans liste de « ??? »
                if (!unlocked && c != _selected && ++lockedShown > 1 && !_showLocked)
                {
                    if (lockedShown == 2)
                    {
                        int rest = 0; for (int k = Chapters.All.IndexOf(c); k < Chapters.All.Count; k++) if (!Progress.IsUnlocked(Chapters.All[k]) && Chapters.All[k] != _selected) rest++;
                        GUILayout.BeginHorizontal(); Theme.SpriteLayout(null, 30f);
                        if (_pad.Button(L.F("… {0} chapitre(s) à venir", rest), _chapterRow, GUILayout.Height(30))) _showLocked = true;
                        GUILayout.EndHorizontal();
                    }
                    n++; continue;
                }
                string mark = done ? "✓ " : c == current ? "► " : "    ";
                string title = unlocked ? c.DisplayTitle : "???";
                bool sel = c == _selected;
                int cd = 0; if (unlocked) foreach (var s in c.Steps) if (Progress.Get(c, s).Done) cd++;
                string count = unlocked ? $"  <size=12><color=#cfcabf>{cd}/{c.Steps.Count}</color></size>" : "";
                GUILayout.BeginHorizontal();
                Theme.SpriteLayout(unlocked ? Facts.ChapterIcon(c) : null, 30f); // trophée du boss (rien tant que le chapitre est verrouillé : pas de spoiler)
                if (_pad.Toggle(sel, $"{mark}{n++}. {title}{count}", _chapterRow, GUILayout.Height(36)) && !sel) { _selected = c; _stepScroll = Vector2.zero; }
                GUILayout.EndHorizontal();
                // Fine barre d'avancement au bas de chaque chapitre atteint
                if (unlocked && Event.current.type == EventType.Repaint)
                {
                    var rr = GUILayoutUtility.GetLastRect();
                    Theme.ProgressBar(new Rect(rr.x + 10f, rr.yMax - 6f, rr.width - 20f, 3f), c.Steps.Count > 0 ? (float)cd / c.Steps.Count : 0f, done ? new Color(0.55f, 0.75f, 0.45f) : sel ? new Color(1f, 1f, 1f, 0.9f) : (Color?)null);
                }
            }
            _pad.EndScrollView();
            GUILayout.Space(4);
            if (Progress.PinnedChapter == null) GUILayout.Label(L.T("Suivi automatique : le premier boss non vaincu."), _small);
            else if (_pad.Button(L.T("Revenir au suivi automatique"))) Progress.Pin(null);
            // Réglages du suivi HUD à portée de main : mode et position (préréglages)
            GUILayout.Space(6);
            GUILayout.Label(L.T("Suivi à l'écran"), _h2);
            GUILayout.BeginHorizontal();
            foreach (TrackerMode m in Enum.GetValues(typeof(TrackerMode)))
                if (_pad.Toggle(Mode.Value == m, ModeLabel(m), Theme.Skin.toggle, GUILayout.Width(86f)) && Mode.Value != m) { Mode.Value = m; _expandUntil = 0f; }
            GUILayout.EndHorizontal();
            for (int i = 0; i < s_presets.Length; i++)
            {
                if (i % 2 == 0) GUILayout.BeginHorizontal();
                var p = s_presets[i];
                bool sel = Mathf.Approximately(TrackerX.Value, p.Value.x) && Mathf.Approximately(TrackerY.Value, p.Value.y);
                if (_pad.Toggle(sel, L.T(p.Key), Theme.Skin.toggle, GUILayout.Width(132f)) && !sel) { TrackerX.Value = p.Value.x; TrackerY.Value = p.Value.y; }
                if (i % 2 == 1) GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(10);

            // ---- chapitre sélectionné
            var ch = _selected;
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
            if (!Progress.IsUnlocked(ch))
            {
                // Anti-spoiler : un chapitre non atteint ne montre ni titre, ni étapes, ni récompense.
                int idx = Chapters.All.IndexOf(ch);
                GUILayout.Label(L.F("Chapitre {0}, verrouillé", idx + 1), _h1);
                GUILayout.Label(L.T("Vous le découvrirez en terminant le chapitre précédent : c'est une progression, le guide ne dévoile rien d'avance."), _small);
                GUILayout.Space(10);
                GUILayout.Label(L.T("Vous pouvez quand même le révéler, cela dévoile le boss, ses préparatifs et ce qu'il débloque."), _small);
                if (_pad.Button(L.T("Révéler ce chapitre (spoiler)"), GUILayout.Width(280))) Progress.Reveal(ch);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (_pad.Button(Pad.Active ? L.T("Fermer  (B)") : L.F("Fermer  ({0})", ToggleKey.Value), GUILayout.Width(140))) Close();
                GUILayout.EndHorizontal();
                Pad.Hints(_small);
                _pad.EndWindow();
                GUI.DragWindow();
                return;
            }
            GUILayout.BeginHorizontal();
            var chSprite = Facts.ChapterIcon(ch);
            if (chSprite != null) { Theme.SpriteLayout(chSprite, 34f); GUILayout.Space(6f); }
            GUILayout.Label(ch.DisplayTitle, _h1, GUILayout.ExpandWidth(true));
            GUILayout.FlexibleSpace();
            if (ch != current && _pad.Button(L.T("Suivre ce chapitre"), GUILayout.Width(170))) Progress.Pin(ch.Id);
            if (!string.IsNullOrEmpty(ch.Finder) && FinderAvailable() && _pad.Button(L.T("Cibler l'autel"), GUILayout.Width(130))) Target(ch.Finder);
            GUILayout.EndHorizontal();
            GUILayout.Label(ch.DisplayIntro, _small);
            // Barre d'avancement du chapitre, comme sur le suivi HUD
            int chDone = 0; foreach (var s in ch.Steps) if (Progress.Get(ch, s).Done) chDone++;
            GUILayout.BeginHorizontal();
            GUILayout.Label(GUIContent.none, GUILayout.ExpandWidth(true), GUILayout.Height(18f));
            if (Event.current.type == EventType.Repaint) { var br = GUILayoutUtility.GetLastRect(); Theme.ProgressBar(new Rect(br.x, br.y + 5f, br.width, 8f), ch.Steps.Count > 0 ? (float)chDone / ch.Steps.Count : 0f); }
            GUILayout.Label(chDone + "/" + ch.Steps.Count + L.T(" étapes"), _nowrap, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            var nextSteps = Progress.Next(ch, 1);
            var nextStep = nextSteps.Count > 0 ? nextSteps[0] : null;
            _stepScroll = _pad.BeginScrollView(_stepScroll, GUILayout.ExpandHeight(true));
            Need? lastNeed = null;
            foreach (var s in OrderedSteps(ch)) // tri stable : obligatoires, puis conseillées, puis optionnelles
            {
                if (lastNeed != s.Need) { lastNeed = s.Need; GUILayout.Label(L.T(s.Need == Need.Required ? "Obligatoire" : s.Need == Need.Advised ? "Conseillé" : "Optionnel"), _h2); }
                var st = Progress.Get(ch, s);
                bool skipped = Progress.IsSkipped(ch, s);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                // Icône de l'objet ou de la construction concernés (lue dans ObjectDB / ZNetScene, en cache)
                var stepIcon = Facts.StepIcon(s);
                Theme.SpriteLayout(stepIcon, 26f); GUILayout.Space(4f); // case réservée même sans icône : les titres restent alignés
                // Marqueur dessiné (coche verte / losange plein ou creux / rond gris si ignoré), même langage que le suivi HUD
                if (skipped) { var mr = GUILayoutUtility.GetRect(16f, 20f, GUILayout.Width(16f), GUILayout.Height(20f)); if (Event.current.type == EventType.Repaint) Theme.DrawDiamond(new Rect(mr.x + 2f, mr.y + 4f, 12f, 12f), new Color(0.45f, 0.43f, 0.40f), false); }
                else Marker(s.Need == Need.Required, st.Done);
                string prog = st.Progress.Length > 0 && !st.Done ? $"  <color=#f5a847>{st.Progress}</color>" : "";
                GUILayout.Label(s.DisplayTitle + prog + (s.Volatile ? "  <size=12><color=#cfcabf>" + L.T("état du moment") + "</color></size>" : ""), st.Done ? _stepDone : _step, GUILayout.ExpandWidth(true));

                if (!st.Done && !string.IsNullOrEmpty(s.Finder) && FinderAvailable() && _pad.Button(L.T("Cibler"), GUILayout.Width(70))) Target(s.Finder);
                if (!st.Done && s.Need != Need.Required && _pad.Button(L.T(skipped ? "Rétablir" : "Ignorer"), GUILayout.Width(80))) Progress.SetSkipped(ch, s, !skipped);
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(s.DisplayDetail)) GUILayout.Label(s.DisplayDetail, _small);
                GUILayout.EndVertical();
                // La prochaine étape à faire porte un liseré accentué à gauche (même repère que le suivi HUD)
                if (s == nextStep && Event.current.type == EventType.Repaint) { var r = GUILayoutUtility.GetLastRect(); Theme.Fill(new Rect(r.x, r.y + 3f, 3f, r.height - 6f), Theme.Accent); }
            }
            _pad.EndScrollView();
            GUILayout.Label(L.T("<b>Récompense :</b> ") + (Progress.ChapterDone(ch) || !HideFuture.Value ? ch.DisplayReward : L.T("à découvrir en vainquant le boss.")), _small);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("Losange plein : obligatoire · creux : conseillé/optionnel · coche : fait, les étapes se cochent toutes seules d'après ce que le jeu enregistre."), _small);
            GUILayout.FlexibleSpace();
            if (_pad.Button(Pad.Active ? L.T("Fermer  (B)") : L.F("Fermer  ({0})", ToggleKey.Value), GUILayout.Width(140))) Close();
            GUILayout.EndHorizontal();
            Pad.Hints(_small, L.F("<b>Échap</b> ou <b>{0}</b> fermer    <b>{1}</b> mode du suivi    <b>Cibler</b> lance le scanner et ferme le guide", ToggleKey.Value, TrackerKey.Value));
            _pad.EndWindow();
            GUI.DragWindow();
        }
    }

    internal static class Patches
    {
        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        [HarmonyPostfix]
        private static void TextInput_IsVisible(ref bool __result)
        {
            if (Plugin.WindowOpen) __result = true;
        }
    }
}
