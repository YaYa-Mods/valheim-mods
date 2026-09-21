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
            // Messages du jeu (haut gauche : découvertes, ramassages ; centre : événements) : un panneau ne doit pas les couvrir
            if (MessageHud.instance != null)
            {
                Add(list, "messages haut gauche", MessageHud.instance.m_messageText);
                Add(list, "message central", MessageHud.instance.m_messageCenterText);
            }
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

            // Des messages affichés pendant le relevé : leurs zones comptent (le joueur les lit en jouant)
            player.Message(MessageHud.MessageType.TopLeft, "Relevé : message en haut à gauche");
            player.Message(MessageHud.MessageType.Center, "Relevé : message central");
            yield return new WaitForSecondsRealtime(0.4f);
            var hudParts = GameHud();
            Plugin.Log.LogInfo("[TEST] HUD du jeu relevé : " + string.Join(", ", hudParts.Select(p => $"{p.Key} {p.Value.x:0},{p.Value.y:0} {p.Value.width:0}×{p.Value.height:0}")));
            h.Check("HUD.éléments du jeu repérés", hudParts.Count >= 3, $"{hudParts.Count} éléments");

            modeCfg.BoxedValue = Enum.Parse(modeCfg.SettingType, "Complet");
            var stepsCfg = (BepInEx.Configuration.ConfigEntry<int>)guideT.GetField("TrackerSteps", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            int prevSteps = stepsCfg.Value;
            // Le jeu recalcule les maximums chaque seconde d'après la nourriture : on change la base (m_baseHP, m_baseStamina) plutôt que le résultat
            var baseHp = typeof(Player).GetField("m_baseHP", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var baseSt = typeof(Player).GetField("m_baseStamina", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            float prevBaseHp = baseHp != null ? (float)baseHp.GetValue(player) : 25f, prevBaseSt = baseSt != null ? (float)baseSt.GetValue(player) : 75f;
            // Deux passes : personnage de départ, puis personnage bien nourri (barres de vie, endurance et eitr au plus grand)
            // avec le suivi au plus long (8 étapes) : le pire cas pour les positions ancrées en bas
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1)
                {
                    baseHp?.SetValue(player, 400f); baseSt?.SetValue(player, 400f);
                    // Et de la nourriture donnant de l'eitr : sa barre apparaît sous celle d'endurance
                    foreach (var food in new[] { "YggdrasilPorridge", "SerpentStew", "LoxPie" })
                    {
                        var foodPrefab = ObjectDB.instance.GetItemPrefab(food);
                        var item = foodPrefab?.GetComponent<ItemDrop>()?.m_itemData?.Clone();
                        if (item != null) item.m_dropPrefab = foodPrefab; // EatFood lit le prefab de l'objet
                        if (item != null) try { player.EatFood(item); } catch (Exception ex) { Plugin.Log.LogWarning("[TEST] EatFood " + food + " : " + ex.Message); }
                    }
                    stepsCfg.Value = 8;
                    yield return new WaitForSecondsRealtime(1.5f);
                    Plugin.Log.LogInfo($"[TEST] bien nourri : vie max={player.GetMaxHealth():0}, endurance max={player.GetMaxStamina():0}, eitr max={player.GetMaxEitr():0}");
                    hudParts = GameHud();
                    Plugin.Log.LogInfo("[TEST] HUD bien nourri : " + string.Join(", ", hudParts.Select(p => $"{p.Key} {p.Value.x:0},{p.Value.y:0} {p.Value.width:0}×{p.Value.height:0}")));
                }
                string suffix = pass == 1 ? " (bien nourri, 8 étapes)" : "";
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
                    h.Check($"Suivi.position « {name} »{suffix} n'écrase rien", hits.Count == 0 && inside,
                        $"suivi {screen.x:0},{screen.y:0} {screen.width:0}×{screen.height:0}, écran {Screen.width}×{Screen.height}, recouvre : {(hits.Count == 0 ? "rien" : string.Join(" + ", hits))}{(inside ? "" : ", DÉBORDE de l'écran")}");
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "tracker_" + name.Replace(' ', '_').ToLowerInvariant() + (pass == 1 ? "_max" : "") + ".png"));
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }
            baseHp?.SetValue(player, prevBaseHp); baseSt?.SetValue(player, prevBaseSt); stepsCfg.Value = prevSteps;
            try { player.ClearFood(); } catch { }

            xCfg.Value = prevX; yCfg.Value = prevY; modeCfg.BoxedValue = prevMode;
            yield return new WaitForSecondsRealtime(0.3f);

            // ---- colonne d'outils de l'inventaire : elle ne doit recouvrir aucun élément du panneau du jeu
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(0.8f);
            var gameParts = new List<KeyValuePair<string, Rect>>();
            var gui = InventoryGui.instance;
            Add(gameParts, "tout prendre", gui.m_takeAllButton);
            Add(gameParts, "tout empiler", gui.m_stackAllButton);
            Add(gameParts, "poids", gui.m_weight);
            Add(gameParts, "grille du joueur", gui.m_player != null ? gui.m_player.GetComponentInChildren<UnityEngine.UI.Image>() : null);
            var tools = new List<KeyValuePair<string, Rect>>();
            foreach (var t in gui.m_player.GetComponentsInChildren<RectTransform>(false))
                if (t.name.StartsWith("ModTool", StringComparison.Ordinal)) Add(tools, t.name, t);
            Plugin.Log.LogInfo($"[TEST] outils de l'inventaire : {tools.Count}, éléments du jeu relevés : {string.Join(", ", gameParts.Select(p => p.Key))}");
            var clash = new List<string>();
            foreach (var t in tools)
                foreach (var g in gameParts)
                    if (g.Key != "grille du joueur" && g.Value.Overlaps(t.Value)) clash.Add($"{t.Key} sur {g.Key}");
            h.Check("Inventaire.colonne d'outils dégagée", tools.Count >= 8 && clash.Count == 0, $"{tools.Count} outils, chevauchements : {(clash.Count == 0 ? "aucun" : string.Join(" + ", clash))}");
            bool onScreenTools = tools.TrueForAll(t => t.Value.xMax <= Screen.width && t.Value.xMin >= 0f && t.Value.yMin >= 0f && t.Value.yMax <= Screen.height);
            h.Check("Inventaire.colonne d'outils entièrement à l'écran", onScreenTools, string.Join(", ", tools.Select(t => $"{t.Key} {t.Value.x:0},{t.Value.y:0}")));
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "inventory_toolbar.png"));
            yield return new WaitForSecondsRealtime(0.6f);
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.4f);

            // ---- colonne d'outils face à un coffre ouvert : le panneau du coffre s'affiche à côté de l'inventaire
            GameObject chest = null;
            try
            {
                var prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
                if (prefab != null) chest = UnityEngine.Object.Instantiate(prefab, player.transform.position + player.transform.forward * 2f, Quaternion.identity);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TEST] coffre : " + ex.Message); }
            if (chest != null)
            {
                yield return null;
                var container = chest.GetComponent<Container>();
                if (container != null)
                {
                    InventoryGui.instance.Show(container, 1);
                    yield return new WaitForSecondsRealtime(0.8f);
                    var containerParts = new List<KeyValuePair<string, Rect>>();
                    Add(containerParts, "panneau du coffre", gui.m_container);
                    Add(containerParts, "tout prendre", gui.m_takeAllButton);
                    Add(containerParts, "tout empiler", gui.m_stackAllButton);
                    var toolsChest = new List<KeyValuePair<string, Rect>>();
                    foreach (var t in gui.m_player.GetComponentsInChildren<RectTransform>(false))
                        if (t.name.StartsWith("ModTool", StringComparison.Ordinal)) Add(toolsChest, t.name, t);
                    var clashChest = new List<string>();
                    foreach (var t in toolsChest) foreach (var g in containerParts) if (g.Value.Overlaps(t.Value)) clashChest.Add($"{t.Key} sur {g.Key}");
                    Plugin.Log.LogInfo($"[TEST] coffre ouvert : {string.Join(", ", containerParts.Select(p => $"{p.Key} {p.Value.x:0},{p.Value.y:0} {p.Value.width:0}×{p.Value.height:0}"))}");
                    h.Check("Inventaire.colonne d'outils dégagée du panneau de coffre", toolsChest.Count >= 8 && clashChest.Count == 0, $"{toolsChest.Count} outils, chevauchements : {(clashChest.Count == 0 ? "aucun" : string.Join(" + ", clashChest))}");
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "inventory_chest.png"));
                    yield return new WaitForSecondsRealtime(0.6f);
                    gui.Hide();
                    yield return new WaitForSecondsRealtime(0.4f);
                }
                ZNetScene.instance.Destroy(chest);
            }

            // ---- chaque fenêtre de mod tient dans l'écran (unités 1080p, aucun bord hors champ)
            var screen1080 = (Vector2)Asm("Guide").GetType("ModsCommon.Theme").GetProperty("ScreenSize", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            foreach (var w in new (string mod, string type, string open, string close)[]
                { ("ResourceFinder", "ResourceFinder.Plugin", "Open", "Close"), ("Guide", "Guide.Plugin", "Toggle", "Toggle"), ("ModHub", "ModHub.Plugin", "Toggle", "Toggle") })
            {
                var t = Asm(w.mod).GetType(w.type);
                var inst = UnityEngine.Object.FindObjectOfType(t);
                var openM = t.GetMethod(w.open, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                openM.Invoke(openM.IsStatic ? null : inst, null);
                yield return new WaitForSecondsRealtime(0.6f);
                var rect = (Rect)t.GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(inst);
                bool fits = rect.xMin >= 0f && rect.yMin >= 0f && rect.xMax <= screen1080.x + 0.5f && rect.yMax <= screen1080.y + 0.5f;
                h.Check($"Fenêtre.{w.mod} dans l'écran", fits, $"fenêtre {rect.x:0},{rect.y:0} {rect.width:0}×{rect.height:0}, écran {screen1080.x:0}×{screen1080.y:0}");
                var closeM = t.GetMethod(w.close, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                closeM.Invoke(closeM.IsStatic ? null : inst, null);
                yield return new WaitForSecondsRealtime(0.3f);
            }

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

            // ---- et face au vrai HUD du jeu : une pastille posée sur chaque élément relevé doit en être écartée
            var gameRects = (List<Rect>)Asm("ResourceFinder").GetType("ResourceFinder.GameHud").GetMethod("Rects", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            var reserved = (List<Rect>)Asm("ResourceFinder").GetType("ResourceFinder.Plugin").GetMethod("ReservedHudRects", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            h.Check("Pastille.HUD du jeu relevé par le scanner", gameRects.Count >= 4 && reserved.Count >= gameRects.Count, $"{gameRects.Count} zones du jeu, {reserved.Count} réservées : {string.Join(", ", gameRects.Select(r => $"{r.x:0},{r.y:0} {r.width:0}×{r.height:0}"))}");
            int hudCase = 0;
            foreach (var z in gameRects.ToList())
            {
                var ask = new Rect(z.center.x - 150f, z.center.y - 18f, 300f, 36f);
                var got = (Rect)place.Invoke(null, new object[] { ask, screen1080.x, screen1080.y, reserved });
                bool inside = got.xMin >= 0f && got.yMin >= 0f && got.xMax <= screen1080.x && got.yMax <= screen1080.y;
                bool clear = reserved.TrueForAll(r => !r.Overlaps(got));
                h.Check($"Pastille.sur le HUD du jeu {++hudCase}", inside && clear, $"zone {z.x:0},{z.y:0} {z.width:0}×{z.height:0} → placée {got.x:0},{got.y:0}, dans l'écran={inside}, dégagée={clear}");
            }
        }
    }
}
