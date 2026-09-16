using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace ModHub
{
    /// <summary>
    /// Hub de configuration en jeu (F9) : liste tous les plugins BepInEx qui exposent des réglages et permet de
    /// les modifier à la volée. Générique : il lit les entrées de config publiées par chaque plugin (section, clé,
    /// type, description, plage acceptable), aucun code spécifique par mod. Écrire une valeur déclenche
    /// SettingChanged côté plugin et l'enregistrement du fichier .cfg par BepInEx.
    /// </summary>
    [BepInPlugin(Guid, "Mod Hub", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.modhub";

        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<bool> WelcomeHint, ShowKeyHints;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static bool WindowOpen;
        private static Plugin s_instance;
        private readonly Pad _pad = new Pad();

        private Rect _window = new Rect(80, 60, 1200, 820);
        private Vector2 _listScroll, _entryScroll;
        private PluginInfo _selected;
        private PluginInfo _resetArmedFor; private float _resetArmedUntil;
        private ConfigEntryBase _capturingKey;                 // entrée KeyCode en attente d'une touche
        private readonly Dictionary<ConfigEntryBase, string> _textBuffers = new Dictionary<ConfigEntryBase, string>();
        private GUIStyle _title, _desc, _section, _key, _modRow, _version;
        private string _filter = "";
        // Curseurs : la valeur n'est écrite qu'au relâchement de la souris (sinon chaque pixel déclenche une sauvegarde).
        private readonly Dictionary<ConfigEntryBase, float> _pendingSliders = new Dictionary<ConfigEntryBase, float>();

        private void Awake()
        {
            s_instance = this; Log = Logger;
            ToggleKey = Config.Bind("General", "ToggleKey", KeyCode.F9, L.T("Touche qui ouvre/ferme le hub de configuration."));
            WelcomeHint = Config.Bind("General", "WelcomeHint", true, L.T("Rappel des accès aux mods (touches ou roue d'action) quelques secondes après l'arrivée dans le monde, une fois par session."));
            ShowKeyHints = Config.Bind("General", "ShowKeyHints", true, L.T("Aides de touches des mods dans le panneau d'aides du jeu (en bas à droite), quand le jeu n'en affiche pas lui-même."));
            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            Harmony.CreateAndPatchAll(typeof(Radial), Guid);
            Harmony.CreateAndPatchAll(typeof(InventoryButtons), Guid);
            ScrollGuard.Install(Guid, () => WindowOpen); // molette : faire défiler la liste, pas zoomer la caméra
            Logger.LogInfo($"Mod Hub chargé (touche {ToggleKey.Value})");
        }

        private float _welcomeAt = -1f; private bool _welcomed; private bool _hintErrorLogged;

        private void Update()
        {
            // Rappel des accès aux mods, une fois par session, quelques secondes après l'arrivée dans le monde
            if (!_welcomed && WelcomeHint.Value)
            {
                var p = Player.m_localPlayer;
                if (p == null) _welcomeAt = -1f;
                else if (_welcomeAt < 0f) _welcomeAt = Time.time + 8f;
                else if (Time.time >= _welcomeAt && !p.IsDead())
                {
                    _welcomed = true;
                    p.Message(MessageHud.MessageType.TopLeft, ZInput.IsGamepadActive()
                        ? L.T("Mods : roue d'action → Mods (scanner, guide, lit, config)")
                        : $"Mods : F7 scanner · F8 lit · {ToggleKey.Value} config · F10 guide · F11 suivi");
                }
            }
            if (_capturingKey != null) return; // la capture se fait dans OnGUI
            try { KeyHintBar.Update(); } catch (Exception ex) { if (!_hintErrorLogged) { _hintErrorLogged = true; Log.LogWarning("Aides de touches : " + ex.Message); } }
            if (ZInput.GetKeyDown(ToggleKey.Value, false)) Toggle();
            else if (WindowOpen && (ZInput.GetKeyDown(KeyCode.Escape, false) || _pad.Update())) WindowOpen = false;
        }

        internal static void Toggle()
        {
            WindowOpen = !WindowOpen;
            if (WindowOpen && s_instance != null) { s_instance._pad.OnOpened(); s_instance._window = Theme.CenteredWindow(1200f, 820f); }
        }

        // ------------------------------------------------------------------ plugins

        // ------------------------------------------------------------------ caches (OnGUI passe plusieurs fois par image : pas de LINQ à chaque passage)

        private static List<PluginInfo> s_plugins; private static float s_pluginsAt;
        private static List<PluginInfo> CachedPlugins()
        {
            if (s_plugins == null || Time.unscaledTime - s_pluginsAt > 2f) { s_plugins = Plugins(); s_pluginsAt = Time.unscaledTime; }
            return s_plugins;
        }

        private static PluginInfo s_sectionsFor; private static List<KeyValuePair<string, List<ConfigEntryBase>>> s_sections;
        private static List<KeyValuePair<string, List<ConfigEntryBase>>> CachedSections(PluginInfo p, IEnumerable<ConfigEntryBase> entries)
        {
            if (s_sectionsFor == p && s_sections != null) return s_sections;
            s_sectionsFor = p;
            s_sections = entries.GroupBy(e => e.Definition.Section).OrderBy(g => g.Key)
                .Select(g => new KeyValuePair<string, List<ConfigEntryBase>>(g.Key, g.OrderBy(e => e.Definition.Key).ToList())).ToList();
            return s_sections;
        }

        private static List<PluginInfo> Plugins()
        {
            return Chainloader.PluginInfos.Values
                .Where(p => p.Instance != null && p.Instance.Config != null && p.Instance.Config.Count > 0)
                .OrderBy(p => p.Metadata.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ------------------------------------------------------------------ interface

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = Theme.H1;
            _section = Theme.H2;
            _desc = Theme.Muted;
            _key = new GUIStyle(Theme.Skin.label) { fontSize = 14 };
            _modRow = new GUIStyle(Theme.Skin.toggle) { alignment = TextAnchor.MiddleLeft, fontSize = 14 };
            _modRow.padding = new RectOffset(12, 10, 8, 8);
            _version = new GUIStyle(Theme.Skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight }; _version.normal.textColor = Theme.MutedColor;
        }

        private void OnGUI()
        {
            if (!WindowOpen || _pad.ConsumeSkipRepaint()) return;
            var prev = Theme.Begin();
            EnsureStyles();
            HandleKeyCapture();
            CommitSliders();
            _window = GUILayout.Window(GetHashCode(), _window, DrawWindow, L.T("Configuration des mods"));
            Theme.End(prev);
        }

        private void HandleKeyCapture()
        {
            if (_capturingKey == null) return;
            var e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
            if (e.keyCode != KeyCode.Escape) _capturingKey.BoxedValue = e.keyCode;
            _capturingKey = null;
            e.Use();
        }

        private static ConfigEntryBase EnabledEntry(PluginInfo p)
        {
            foreach (var e in ((IDictionary<ConfigDefinition, ConfigEntryBase>)p.Instance.Config).Values)
                if (e.Definition.Key == "Enabled" && e.SettingType == typeof(bool)) return e;
            return null;
        }

        private void DrawWindow(int id)
        {
            _pad.BeginWindow();
            if (Theme.CloseButton(_window)) WindowOpen = false;
            var plugins = CachedPlugins();
            if (_selected == null || !plugins.Contains(_selected)) _selected = plugins.FirstOrDefault();

            GUILayout.BeginHorizontal();

            // ---- colonne gauche : les mods
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(290), GUILayout.ExpandHeight(true));
            GUILayout.Label($"Mods  <size=12><color=#cfcabf>({plugins.Count})</color></size>", _title);
            _listScroll = _pad.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            foreach (var p in plugins)
            {
                bool sel = p == _selected;
                var en = EnabledEntry(p);
                string dot = en == null ? "<color=#555>●</color>" : (bool)en.BoxedValue ? "<color=#7cc35a>●</color>" : "<color=#b04a3a>●</color>";
                if (_pad.Toggle(sel, $"{dot}  {p.Metadata.Name}  <size=12><color=#cfcabf>v{p.Metadata.Version}</color></size>", _modRow, GUILayout.Height(36)) && !sel)
                {
                    _selected = p;
                    _textBuffers.Clear();
                    _capturingKey = null;
                    _entryScroll = Vector2.zero;
                }
            }
            _pad.EndScrollView();
            GUILayout.Space(6);
            GUILayout.Label(L.T("<color=#7cc35a>●</color> activé   <color=#b04a3a>●</color> désactivé   <color=#555>●</color> toujours actif"), _desc);
            GUILayout.BeginHorizontal();
            if (_pad.Button(L.T("Tout désactiver"))) SetAllEnabled(plugins, false);
            if (_pad.Button(L.T("Tout réactiver"))) SetAllEnabled(plugins, true);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(10);

            // ---- colonne droite : les réglages du mod sélectionné
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
            if (_selected != null) DrawPlugin(_selected);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label(L.T("Les changements sont appliqués et enregistrés immédiatement. « défaut » remet un réglage à sa valeur d'origine."), _desc);
            GUILayout.FlexibleSpace();
            if (_pad.Button(Pad.Active ? L.T("Fermer  (B)") : L.T("Fermer  (Échap)"), GUILayout.Width(140))) WindowOpen = false;
            GUILayout.EndHorizontal();
            Pad.Hints(_desc);
            _pad.EndWindow();
            GUI.DragWindow();
        }

        private void DrawPlugin(PluginInfo p)
        {
            var entries = ((IDictionary<ConfigDefinition, ConfigEntryBase>)p.Instance.Config).Values;
            GUILayout.BeginHorizontal();
            GUILayout.Label(p.Metadata.Name, _title, GUILayout.ExpandWidth(true));
            var en = EnabledEntry(p);
            if (en != null)
            {
                bool v = (bool)en.BoxedValue;
                bool nv = _pad.Toggle(v, v ? L.T("Mod activé") : L.T("Mod désactivé"), GUILayout.Width(150));
                if (nv != v) en.BoxedValue = nv;
            }
            // Remise à zéro en deux temps : un premier appui arme « Confirmer ? » pendant 3 s (pas de perte par mégarde)
            bool armed = _resetArmedFor == p && Time.unscaledTime < _resetArmedUntil;
            if (_pad.Button(armed ? L.T("<color=#f5a847>Confirmer la remise à zéro ?</color>") : L.T("Tout remettre par défaut"), GUILayout.Width(230)))
            {
                if (armed) { foreach (var e in entries) e.BoxedValue = e.DefaultValue; _textBuffers.Clear(); _resetArmedFor = null; Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, p.Metadata.Name + L.T(" : réglages par défaut")); }
                else { _resetArmedFor = p; _resetArmedUntil = Time.unscaledTime + 3f; }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(ShortConfigPath(p.Instance.Config.ConfigFilePath), _desc); // chemin relatif : une capture d'écran ne montre pas l'arborescence du joueur
            if (!Pad.Active)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(L.T("Filtrer :"), GUILayout.Width(60));
                _filter = GUILayout.TextField(_filter, GUILayout.Width(280));
                if (_pad.Button("×", GUILayout.Width(28))) _filter = "";
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);

            _entryScroll = _pad.BeginScrollView(_entryScroll, GUILayout.ExpandHeight(true));
            foreach (var group in CachedSections(p, entries))
            {
                var shown = group.Value.Where(Matches).ToList();
                shown.Remove(en); // l'interrupteur du mod est déjà en tête de page
                if (shown.Count == 0) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label(group.Key, _section);
                foreach (var entry in shown) DrawEntry(entry);
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }
            _pad.EndScrollView();
        }

        /// <summary>Chemin du .cfg réduit à « BepInEx/config/… » : rien du dossier d'installation du joueur à l'écran.</summary>
        private static string ShortConfigPath(string full)
        {
            if (string.IsNullOrEmpty(full)) return "";
            string norm = full.Replace('\\', '/');
            int i = norm.IndexOf("/BepInEx/", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? norm.Substring(i + 1) : System.IO.Path.GetFileName(norm);
        }

        private bool Matches(ConfigEntryBase e)
        {
            if (string.IsNullOrWhiteSpace(_filter)) return true;
            var f = _filter.Trim();
            return e.Definition.Key.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || e.Definition.Section.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || (e.Description?.Description ?? "").IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SetAllEnabled(IEnumerable<PluginInfo> plugins, bool value)
        {
            int n = 0;
            foreach (var p in plugins)
            {
                if (p.Metadata.GUID == Guid) continue; // pas le hub lui-même
                var e = EnabledEntry(p);
                if (e != null) { e.BoxedValue = value; n++; }
            }
            var msg = value ? L.F("Mods réactivés ({0})", n) : L.F("MODS DÉSACTIVÉS ({0}), jeu vanilla (usure, poids, piles...)", n);
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, msg);
        }

        private void DrawEntry(ConfigEntryBase entry)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            bool modified = !Equals(entry.BoxedValue, entry.DefaultValue);
            GUILayout.Label((modified ? L.T("<color=#f5a847>●</color> ") : "") + entry.Definition.Key, _key, GUILayout.Width(230));
            DrawEditor(entry);
            GUILayout.FlexibleSpace();
            if (_pad.Button(L.T("défaut"), GUILayout.Width(62))) { entry.BoxedValue = entry.DefaultValue; _textBuffers.Remove(entry); } // (le glyphe ↺ n'existe pas dans la police du jeu)
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(entry.Description?.Description))
            {
                GUILayout.Space(2);
                GUILayout.Label(entry.Description.Description, _desc);
            }
            GUILayout.Space(14);
            GUILayout.EndVertical();
        }

        private void DrawEditor(ConfigEntryBase entry)
        {
            var type = entry.SettingType;
            var acceptable = entry.Description?.AcceptableValues;

            if (type == typeof(bool))
            {
                bool v = (bool)entry.BoxedValue;
                bool nv = _pad.Toggle(v, v ? L.T(" activé") : L.T(" désactivé"));
                if (nv != v) entry.BoxedValue = nv;
                return;
            }

            if (type == typeof(KeyCode))
            {
                bool capturing = _capturingKey == entry;
                if (_pad.Button(capturing ? L.T("Appuyez sur une touche… (Échap : annuler)") : entry.BoxedValue.ToString(), GUILayout.Width(260)))
                    _capturingKey = capturing ? null : entry;
                return;
            }

            if (acceptable != null && acceptable.GetType().IsGenericType && acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueList<>))
            {
                var values = (Array)Traverse.Create(acceptable).Property("AcceptableValues").GetValue();
                foreach (var candidate in values)
                {
                    bool sel = Equals(candidate, entry.BoxedValue);
                    if (_pad.Toggle(sel, candidate.ToString(), GUI.skin.button) && !sel) entry.BoxedValue = candidate;
                }
                return;
            }

            if (type.IsEnum)
            {
                var values = Enum.GetValues(type);
                int idx = Array.IndexOf(values, entry.BoxedValue);
                if (_pad.Button("◄", GUILayout.Width(28))) entry.BoxedValue = values.GetValue((idx - 1 + values.Length) % values.Length);
                GUILayout.Label(entry.BoxedValue.ToString(), GUILayout.Width(160));
                if (_pad.Button("►", GUILayout.Width(28))) entry.BoxedValue = values.GetValue((idx + 1) % values.Length);
                return;
            }

            bool isInt = type == typeof(int), isFloat = type == typeof(float) || type == typeof(double);
            if (isInt || isFloat)
            {
                bool hasRange = acceptable != null && acceptable.GetType().IsGenericType && acceptable.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueRange<>);
                float current = Convert.ToSingle(entry.BoxedValue, CultureInfo.InvariantCulture);
                if (hasRange)
                {
                    float min = Convert.ToSingle(Traverse.Create(acceptable).Property("MinValue").GetValue(), CultureInfo.InvariantCulture);
                    float max = Convert.ToSingle(Traverse.Create(acceptable).Property("MaxValue").GetValue(), CultureInfo.InvariantCulture);
                    if (_pendingSliders.TryGetValue(entry, out float pending)) current = pending;
                    float nv = _pad.HorizontalSlider(current, min, max, GUILayout.Width(220));
                    if (isInt) nv = Mathf.Round(nv);
                    if (Mathf.Abs(nv - current) > 0.0001f) { _pendingSliders[entry] = nv; _textBuffers.Remove(entry); current = nv; }
                }
                DrawNumberField(entry, current, isInt);
                return;
            }

            if (type == typeof(string))
            {
                string v = (string)entry.BoxedValue ?? "";
                string nv = GUILayout.TextField(v, GUILayout.Width(300));
                if (nv != v) entry.BoxedValue = nv;
                return;
            }

            GUILayout.Label(entry.BoxedValue?.ToString() ?? "(null)");
        }

        private void DrawNumberField(ConfigEntryBase entry, float current, bool isInt)
        {
            if (!_textBuffers.TryGetValue(entry, out var text))
                text = isInt ? ((int)current).ToString(CultureInfo.InvariantCulture) : current.ToString("0.###", CultureInfo.InvariantCulture);
            string nt = GUILayout.TextField(text, GUILayout.Width(80));
            if (nt == text) return;
            _textBuffers[entry] = nt;
            if (float.TryParse(nt.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                SetNumber(entry, parsed, isInt);
        }

        private void CommitSliders()
        {
            if (_pendingSliders.Count == 0 || Input.GetMouseButton(0)) return;
            foreach (var kv in _pendingSliders) SetNumber(kv.Key, kv.Value, kv.Key.SettingType == typeof(int));
            _pendingSliders.Clear();
        }

        private static void SetNumber(ConfigEntryBase entry, float value, bool isInt)
        {
            object boxed = isInt ? (object)Mathf.RoundToInt(value)
                : entry.SettingType == typeof(double) ? (object)(double)value : value;
            // ConfigEntry applique lui-même la plage acceptable (Clamp) à l'écriture.
            if (!Equals(boxed, entry.BoxedValue)) entry.BoxedValue = boxed;
        }
    }

    internal static class Patches
    {
        // Fenêtre ouverte = comme un champ texte actif : souris libre, joueur immobile.
        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        [HarmonyPostfix]
        private static void TextInput_IsVisible(ref bool __result)
        {
            if (Plugin.WindowOpen) __result = true;
        }
    }
}
