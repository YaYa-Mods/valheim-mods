using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Placement du suivi de quête : chaque position proposée dans le guide doit laisser le HUD du jeu lisible (barres
    /// de vie et d'endurance, barre d'action, effets, mini-carte, aides de touches, messages). On relève l'encombrement
    /// réel du suivi et celui des éléments du jeu à l'écran, et on vérifie qu'ils ne se recouvrent pas ; une capture
    /// par position permet de le voir. Sans ça, une position « prête à l'emploi » peut tomber pile sur la barre d'action.
    /// </summary>
    internal static class HudLayoutTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        /// <summary>Rectangle écran (origine en haut à gauche) d'un élément d'interface, ou vide s'il est absent ou caché.</summary>
        private static Rect ScreenRectOf(Component c)
        {
            var rt = c != null ? c.transform as RectTransform : null;
            if (rt == null || !c.gameObject.activeInHierarchy) return Rect.zero;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float minX = Mathf.Min(corners[0].x, corners[2].x), maxX = Mathf.Max(corners[0].x, corners[2].x);
            float minY = Mathf.Min(corners[0].y, corners[2].y), maxY = Mathf.Max(corners[0].y, corners[2].y);
            if (maxX - minX < 4f || maxY - minY < 4f) return Rect.zero;
            // Conteneur plein écran (EnemyHud, racines du HUD...) : il ne « recouvre » rien au sens visuel, on l'ignore
            if ((maxX - minX) * (maxY - minY) > 0.5f * Screen.width * Screen.height) return Rect.zero;
            return new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY); // y compté depuis le haut, comme l'IMGUI
        }

        private static void Add(List<KeyValuePair<string, Rect>> list, string name, Component c)
        {
            var r = ScreenRectOf(c);
            if (r.width > 0f) list.Add(new KeyValuePair<string, Rect>(name, r));
        }

        /// <summary>Éléments du HUD du jeu qu'un panneau de mod ne doit pas recouvrir.</summary>
        private static List<KeyValuePair<string, Rect>> GameHud()
        {
            var list = new List<KeyValuePair<string, Rect>>();
            var hud = Hud.instance;
            if (hud == null) return list;
            // Recherche par nom dans la hiérarchie du HUD : les noms sont stables d'une version à l'autre, et ce qui manque est simplement ignoré
            var wanted = new (string label, string path)[]
            {
                ("barres vie/endurance", "healthpanel"), ("barre d'action", "HotKeyBar"), ("effets", "StatusEffects"),
                ("mini-carte", "minimap_small"), ("aides de touches", "KeyHints"), ("messages", "TopLeftMsgs"),
                ("cible/ennemi", "EnemyHud"), ("boussole", "Compass"),
            };
            foreach (var w in wanted)
            {
                var t = FindDeep(hud.transform.root, w.path);
                if (t != null) Add(list, w.label, t);
            }
            Add(list, "mini-carte", Minimap.instance != null ? Minimap.instance.m_smallRoot?.transform as RectTransform : null);
            return list;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(false))
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var guideT = Asm("Guide").GetType("Guide.Plugin");
            var rectF = guideT.GetField("LastTrackerRect", BindingFlags.NonPublic | BindingFlags.Static);
            var xCfg = (BepInEx.Configuration.ConfigEntry<float>)guideT.GetField("TrackerX", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var yCfg = (BepInEx.Configuration.ConfigEntry<float>)guideT.GetField("TrackerY", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var modeCfg = (BepInEx.Configuration.ConfigEntryBase)guideT.GetField("Mode", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var presets = (Array)guideT.GetField("s_presets", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            float prevX = xCfg.Value, prevY = yCfg.Value; object prevMode = modeCfg.BoxedValue;
            var scale = (float)Asm("Guide").GetType("ModsCommon.Theme").GetProperty("UiScale", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);

            var hudParts = GameHud();
            Plugin.Log.LogInfo("[TEST] HUD du jeu relevé : " + string.Join(", ", hudParts.Select(p => $"{p.Key} {p.Value.x:0},{p.Value.y:0} {p.Value.width:0}×{p.Value.height:0}")));
            h.Check("HUD.éléments du jeu repérés", hudParts.Count >= 3, $"{hudParts.Count} éléments");

            modeCfg.BoxedValue = Enum.Parse(modeCfg.SettingType, "Complet");
            foreach (var kv in presets)
            {
                string name = (string)kv.GetType().GetProperty("Key").GetValue(kv, null);
                var pos = (Vector2)kv.GetType().GetProperty("Value").GetValue(kv, null);
                xCfg.Value = pos.x; yCfg.Value = pos.y;
                yield return new WaitForSecondsRealtime(0.4f); // le temps que le suivi soit redessiné à sa nouvelle place
                var t = (Rect)rectF.GetValue(null);
                var screen = new Rect(t.x * scale, t.y * scale, t.width * scale, t.height * scale);
                var hits = hudParts.Where(p => p.Value.Overlaps(screen)).Select(p => p.Key).Distinct().ToList();
                bool inside = screen.xMin >= -1f && screen.yMin >= -1f && screen.xMax <= Screen.width + 1f && screen.yMax <= Screen.height + 1f;
                h.Check($"Suivi.position « {name} » n'écrase rien", hits.Count == 0 && inside,
                    $"suivi {screen.x:0},{screen.y:0} {screen.width:0}×{screen.height:0}, écran {Screen.width}×{Screen.height}, recouvre : {(hits.Count == 0 ? "rien" : string.Join(" + ", hits))}{(inside ? "" : ", DÉBORDE de l'écran")}");
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "tracker_" + name.Replace(' ', '_').ToLowerInvariant() + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
            }

            xCfg.Value = prevX; yCfg.Value = prevY; modeCfg.BoxedValue = prevMode;
            yield return new WaitForSecondsRealtime(0.3f);

            // ---- pastille du scanner : tous les cas de bord, avec et sans panneau à éviter
            var place = Asm("ResourceFinder").GetType("ResourceFinder.Plugin").GetMethod("PlacePill", BindingFlags.NonPublic | BindingFlags.Static);
            if (place == null) { h.Check("Pastille.fonction de placement", false, "PlacePill introuvable"); yield break; }
            const float sw = 1920f, sh = 1080f;
            var none = new List<Rect>();
            var tracker = new List<Rect> { new Rect(1570f, 290f, 340f, 200f) }; // suivi de quête « sous la carte »
            var cases = new (string nom, Rect demande, List<Rect> zones)[]
            {
                ("bord gauche", new Rect(-180f, 500f, 300f, 36f), none),
                ("bord droit", new Rect(sw - 120f, 500f, 300f, 36f), none),
                ("bord haut", new Rect(800f, -40f, 300f, 36f), none),
                ("bord bas", new Rect(800f, sh - 10f, 300f, 36f), none),
                ("coin haut droit", new Rect(sw - 100f, -30f, 300f, 36f), none),
                ("sur le suivi de quête", new Rect(1650f, 330f, 300f, 36f), tracker),
                ("suivi + bord droit", new Rect(sw - 60f, 300f, 300f, 36f), tracker),
                ("pastille très large", new Rect(-400f, 520f, 900f, 36f), tracker),
            };
            foreach (var c in cases)
            {
                var got = (Rect)place.Invoke(null, new object[] { c.demande, sw, sh, c.zones });
                bool inside = got.xMin >= 0f && got.yMin >= 0f && got.xMax <= sw && got.yMax <= sh;
                bool clear = c.zones.TrueForAll(z => !z.Overlaps(got));
                h.Check($"Pastille.{c.nom}", inside && clear, $"placée {got.x:0},{got.y:0} {got.width:0}×{got.height:0}, dans l'écran={inside}, dégagée={clear}");
            }
        }
    }
}
