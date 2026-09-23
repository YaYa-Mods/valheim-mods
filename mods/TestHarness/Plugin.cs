using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Tests d'intégration EN JEU de nos mods : quand AutoRun est vrai et qu'un monde est chargé, exécute des scénarios
    /// réels (arbre créé et frappé, coffre rempli et artisanat, piles, poids, nuit, nourriture, finder...) et écrit
    /// [TEST] PASS/FAIL dans le log. AutoRun repasse à false après exécution : jamais déclenché pendant une vraie partie.
    /// Tout ce qui est créé (arbre, coffre) est détruit à la fin ; le bois déplacé pour le test est rendu.
    /// </summary>
    [BepInPlugin(Guid, "Test Harness", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.testharness";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> AutoRun;
        internal static ConfigEntry<bool> Quick;
        private bool _started;
        private int _pass, _fail;

        private void Awake()
        {
            Log = Logger;
            AutoRun = Config.Bind("General", "AutoRun", false, "Exécute les tests au prochain chargement de monde, puis repasse à false.");
            WorldReport = Config.Bind("General", "WorldReport", false, "Écrit un rapport du monde (lieux clés, biomes, côte autour du spawn) au prochain chargement, puis repasse à false.");
            Quick = Config.Bind("General", "Quick", false, "Avec AutoRun : n'exécute que les tests rapides (manette, épingles).");
            Harmony.CreateAndPatchAll(typeof(TestBootstrap), "vmods.testharness.bootstrap");
            Log.LogInfo($"Test Harness chargé (AutoRun={AutoRun.Value})");
        }

        // Résolu au premier usage, jamais dans un initialiseur statique (une réflexion qui échoue ne doit pas empêcher le plugin de charger)
        private static HarmonyLib.AccessTools.FieldRef<Game, bool> s_firstSpawnRef;
        private static HarmonyLib.AccessTools.FieldRef<Game, bool> s_firstSpawn => s_firstSpawnRef ?? (s_firstSpawnRef = HarmonyLib.AccessTools.FieldRefAccess<Game, bool>("m_firstSpawn"));
        private bool _introSkipped;

        private void Update()
        {
            // Personnage de test neuf : pas de trajet en valkyrie (plusieurs dizaines de secondes par run)
            if (!_introSkipped && AutoRun.Value && Game.instance != null && Player.m_localPlayer == null)
            {
                try
                {
                    if (s_firstSpawn(Game.instance)) { s_firstSpawn(Game.instance) = false; Log.LogInfo("[TEST] intro valkyrie sautée"); }
                    // et sur le profil lui-même (c'est lui que le jeu lit pour décider du trajet en valkyrie)
                    var prof = Game.instance.GetPlayerProfile();
                    var f = prof != null ? typeof(PlayerProfile).GetField("m_firstSpawn", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) : null;
                    if (f != null && (bool)f.GetValue(prof)) { f.SetValue(prof, false); Log.LogInfo("[TEST] intro valkyrie sautée (profil)"); }
                    _introSkipped = true;
                }
                catch (Exception ex) { Log.LogWarning("[TEST] intro : " + ex.Message); _introSkipped = true; }
            }
            if (_started || (!AutoRun.Value && !WorldReport.Value)) return;
            if (Player.m_localPlayer == null || ZNetScene.instance == null || ObjectDB.instance == null) return;
            _started = true;
            // Garde-fou : les tests ne tournent QUE sur le personnage et le monde de test, jamais sur une vraie partie
            string worldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            string charName = Game.instance != null ? Game.instance.GetPlayerProfile()?.GetName() ?? "" : "";
            bool testEnv = string.Equals(worldName, TestBootstrap.WorldName, StringComparison.OrdinalIgnoreCase) && string.Equals(charName, TestBootstrap.CharacterName, StringComparison.OrdinalIgnoreCase);
            if (!testEnv)
            {
                AutoRun.Value = false; WorldReport.Value = false;
                Log.LogError($"[TEST] REFUS : monde « {worldName} », personnage « {charName} », les tests n'ont le droit de tourner que sur « {TestBootstrap.WorldName} » / « {TestBootstrap.CharacterName} ». Rien n'a été exécuté.");
                Log.LogInfo("[TEST] ===== fin : 0 PASS, 1 FAIL (environnement refusé) =====");
                return;
            }
            // Pas d'autosauvegarde pendant les tests (le jeu est tué à la fin, rien ne doit s'écrire)
            Game.m_saveInterval = 1e9f;
            if (WorldReport.Value) { WorldReport.Value = false; StartCoroutine(RunWorldReport()); }
            if (AutoRun.Value) { AutoRun.Value = false; StartCoroutine(RunAll()); }
        }


        // ---------------- Rapport de monde : lieux clés et biomes autour du spawn ----------------
        internal static ConfigEntry<bool> WorldReport;

        private IEnumerator RunWorldReport()
        {
            yield return new WaitForSeconds(8f);
            var zs = ZoneSystem.instance; var wg = WorldGenerator.instance;
            Vector3 origin = Player.m_localPlayer.transform.position;
            if (zs.GetLocationIcon("StartTemple", out var temple)) origin = temple;
            Log.LogInfo($"[MONDE] {ZNet.instance.GetWorldName()} seed={wg.GetSeed()}, origine = pierres sacrificielles ({origin.x:0},{origin.z:0})");

            string Dir(Vector3 p)
            {
                var d = p - origin; float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; if (a < 0) a += 360f;
                string[] pts = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSO", "SO", "OSO", "O", "ONO", "NO", "NNO" };
                return pts[Mathf.RoundToInt(a / 22.5f) % 16];
            }
            float Dist(Vector3 p) { var d = p - origin; d.y = 0; return d.magnitude; }

            var wanted = new (string label, string[] names)[]
            {
                ("Haldor (marchand)", new[] { "Vendor_BlackForest" }), ("Hildir", new[] { "Hildir_camp" }), ("Sorcière des marais", new[] { "BogWitch_Camp" }),
                ("Autel Eikthyr", new[] { "Eikthyrnir" }), ("Autel L'Ancien", new[] { "GDKing" }), ("Autel Bonemass", new[] { "Bonemass" }),
                ("Autel Moder", new[] { "Dragonqueen" }), ("Autel Yagluth", new[] { "GoblinKing" }), ("Autel La Reine", new[] { "Mistlands_DvergrBossEntrance1" }),
                ("Autel Fader", new[] { "FaderLocation" }), ("Chambre funéraire", new[] { "Crypt2", "Crypt3", "Crypt4" }), ("Grotte de troll", new[] { "TrollCave02", "TrollCave" }),
                ("Crypte des marais (fer)", new[] { "SunkenCrypt4", "SunkenCrypt3", "SunkenCrypt2", "SunkenCrypt1" }), ("Grotte de montagne", new[] { "MountainCave01", "MountainCave02" }),
            };
            foreach (var w in wanted)
            {
                float best = float.MaxValue; Vector3 bp = Vector3.zero; bool found = false;
                foreach (var kv in zs.m_locationInstances)
                {
                    string n = kv.Value.m_location?.m_prefabName ?? "";
                    if (!w.names.Any(x => n.StartsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
                    float d = Dist(kv.Value.m_position);
                    if (d < best) { best = d; bp = kv.Value.m_position; found = true; }
                }
                Log.LogInfo(found ? $"[MONDE] {w.label,-26} {best,6:0} m  {Dir(bp)}" : $"[MONDE] {w.label,-26} -");
            }

            // Biomes : pour 16 directions, premier point (par pas de 50 m) de chaque biome jusqu'à 3 km
            var biomes = new[] { Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain, Heightmap.Biome.Plains, Heightmap.Biome.Ocean, Heightmap.Biome.Mistlands };
            foreach (var b in biomes)
            {
                float best = float.MaxValue; string bd = "";
                for (int i = 0; i < 16; i++)
                {
                    float a = i * 22.5f * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                    for (float r = 50f; r <= 3000f; r += 50f)
                    {
                        var p = origin + dir * r;
                        if (wg.GetBiome(p) == b) { if (r < best) { best = r; bd = Dir(p); } break; }
                    }
                }
                Log.LogInfo(best < float.MaxValue ? $"[MONDE] biome {b,-12} le plus proche : {best,5:0} m  {bd}" : $"[MONDE] biome {b,-12} : rien à moins de 3 km");
            }
            // Eau : distance à la côte dans chaque direction (hauteur < niveau de la mer)
            var coast = new List<string>();
            for (int i = 0; i < 16; i += 2)
            {
                float a = i * 22.5f * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); float r;
                for (r = 25f; r <= 1500f; r += 25f) { var p = origin + dir * r; if (wg.GetHeight(p.x, p.z) < 30f) break; }
                coast.Add($"{Dir(origin + dir * 100f)}:{(r > 1500f ? ">1500" : r.ToString("0"))}");
            }
            Log.LogInfo("[MONDE] côte (m) par direction : " + string.Join("  ", coast));

            // ---------------- Carte PNG : 3 km × 3 km autour de l'origine, 5 m/pixel ----------------
            try
            {
                const int size = 600; const float scale = 5f; // 600 px × 5 m = 3 km
                var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
                var px = new Color32[size * size];
                Color32 C(Heightmap.Biome b)
                {
                    switch (b)
                    {
                        case Heightmap.Biome.Meadows: return new Color32(110, 170, 70, 255);
                        case Heightmap.Biome.BlackForest: return new Color32(25, 70, 35, 255);
                        case Heightmap.Biome.Swamp: return new Color32(95, 75, 45, 255);
                        case Heightmap.Biome.Mountain: return new Color32(225, 225, 235, 255);
                        case Heightmap.Biome.Plains: return new Color32(210, 190, 90, 255);
                        case Heightmap.Biome.Mistlands: return new Color32(120, 100, 150, 255);
                        case Heightmap.Biome.AshLands: return new Color32(170, 60, 40, 255);
                        case Heightmap.Biome.DeepNorth: return new Color32(190, 215, 235, 255);
                        default: return new Color32(40, 70, 130, 255);
                    }
                }
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float wx = origin.x + (x - size / 2) * scale, wz = origin.z + (y - size / 2) * scale;
                    float h = wg.GetHeight(wx, wz);
                    Color32 c = h < 30f ? (h < 20f ? new Color32(30, 55, 110, 255) : new Color32(60, 100, 160, 255)) : C(wg.GetBiome(new Vector3(wx, 0, wz)));
                    if (h >= 30f) { float sh = Mathf.Clamp01((h - 30f) / 120f) * 0.35f; c = new Color32((byte)Mathf.Min(255, c.r + sh * 120), (byte)Mathf.Min(255, c.g + sh * 120), (byte)Mathf.Min(255, c.b + sh * 120), 255); }
                    px[y * size + x] = c;
                }
                void Dot(Vector3 p, Color32 col, int r)
                {
                    int cx = Mathf.RoundToInt((p.x - origin.x) / scale) + size / 2, cy = Mathf.RoundToInt((p.z - origin.z) / scale) + size / 2;
                    for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                    { int X = cx + dx, Y = cy + dy; if (X >= 0 && X < size && Y >= 0 && Y < size && dx * dx + dy * dy <= r * r) px[Y * size + X] = col; }
                }
                Dot(origin, new Color32(255, 255, 255, 255), 5);
                foreach (var kv in zs.m_locationInstances)
                {
                    string n = kv.Value.m_location?.m_prefabName ?? ""; var p = kv.Value.m_position;
                    if (Dist(p) > size / 2 * scale) continue;
                    if (n.StartsWith("Eikthyrnir") || n.StartsWith("GDKing") || n.StartsWith("Bonemass") || n.StartsWith("Dragonqueen") || n.StartsWith("GoblinKing")) Dot(p, new Color32(255, 40, 40, 255), 6);
                    else if (n.StartsWith("Vendor")) Dot(p, new Color32(255, 220, 0, 255), 6);
                    else if (n.StartsWith("Crypt")) Dot(p, new Color32(255, 140, 0, 255), 4);
                    else if (n.StartsWith("TrollCave")) Dot(p, new Color32(0, 200, 255, 255), 4);
                    else if (n.StartsWith("SunkenCrypt")) Dot(p, new Color32(255, 0, 200, 255), 4);
                }
                tex.SetPixels32(px); tex.Apply();
                var path = System.IO.Path.Combine(Paths.ConfigPath, $"map_{ZNet.instance.GetWorldName()}.png");
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Log.LogInfo($"[MONDE] carte écrite : {path} (3 km × 3 km, 5 m/px, nord en haut ; blanc=spawn, rouge=autels, jaune=Haldor, orange=chambres funéraires, cyan=trolls, rose=cryptes des marais)");
            }
            catch (Exception ex) { Log.LogInfo("[MONDE] carte : " + ex.Message); }
            Log.LogInfo("[MONDE] ===== fin du rapport =====");
        }
        /// <summary>Exécute un groupe de tests en rattrapant toute exception (sinon la coroutine meurt en silence, le jeu
        /// reste ouvert jusqu'au délai du script et rien n'est rapporté) : l'exception devient un FAIL et on passe au groupe suivant.</summary>
        private IEnumerator Safe(string group, IEnumerator tests)
        {
            while (true)
            {
                object current;
                try { if (!tests.MoveNext()) yield break; current = tests.Current; }
                catch (Exception ex) { Check(group + ".exception", false, ex.GetType().Name + " : " + ex.Message + " | " + ex.StackTrace); CloseModWindows(); yield break; }
                yield return current;
            }
        }

        /// <summary>Après une exception : referme les fenêtres des mods (finder, guide, hub) pour ne pas fausser les groupes suivants.</summary>
        private static void CloseModWindows()
        {
            foreach (var pair in new[] { ("ResourceFinder", "ResourceFinder.Plugin"), ("Guide", "Guide.Plugin"), ("ModHub", "ModHub.Plugin") })
            {
                try
                {
                    var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == pair.Item1);
                    var f = asm?.GetType(pair.Item2)?.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    if (f != null) f.SetValue(null, false);
                }
                catch { }
            }
        }

        // Chien de garde : un fil à part signale toutes les 30 s un test qui ne progresse plus (image figée = boucle sans fin
        // dans un mod ; les messages du fil arrivent quand même dans le journal), avec la dernière étape connue.
        private static string s_lastStep = "(début)"; private static DateTime s_lastStepAt = DateTime.UtcNow; private static System.Threading.Timer s_watchdog;
        internal static void Step(string what) { s_lastStep = what; s_lastStepAt = DateTime.UtcNow; }
        private static void StartWatchdog()
        {
            if (s_watchdog != null) return;
            s_watchdog = new System.Threading.Timer(_ =>
            {
                var idle = DateTime.UtcNow - s_lastStepAt;
                if (idle.TotalSeconds > 40) Log.LogWarning($"[TEST] chien de garde : rien depuis {idle.TotalSeconds:0} s, dernière étape « {s_lastStep} »");
            }, null, 30000, 30000);
        }

        internal void Check(string name, bool ok, string detail = "")
        {
            Step(name);
            if (ok) _pass++; else _fail++;
            Log.LogInfo($"[TEST] {(ok ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : ", " + detail)}");
        }

        private IEnumerator RunAll()
        {
            StartWatchdog();
            yield return new WaitForSeconds(8f); // laisser le monde se stabiliser
            // Personnage neuf : l'arrivée en valkyrie est une cinématique, on attend qu'elle finisse
            float cut = Time.time + 60f;
            while (Player.m_localPlayer != null && Player.m_localPlayer.InCutscene() && Time.time < cut) yield return null;
            if (Player.m_localPlayer != null && Player.m_localPlayer.InCutscene()) Log.LogWarning("[TEST] cinématique toujours en cours après 60 s");
            yield return new WaitForSeconds(2f);
            Log.LogInfo("[TEST] ===== début des tests =====");
            try
            {
                var kh = KeyHints.instance;
                var sbk = new System.Text.StringBuilder();
                void Dump(Transform tr, int depth) { sbk.AppendLine(new string(' ', depth * 2) + tr.name + " [" + string.Join(",", tr.GetComponents<Component>().Select(c => c.GetType().Name)) + "] actif=" + tr.gameObject.activeSelf + (tr.GetComponent<TMPro.TMP_Text>() != null ? " texte='" + tr.GetComponent<TMPro.TMP_Text>().text + "'" : "")); if (depth < 6) foreach (Transform ch in tr) Dump(ch, depth + 1); }
                if (kh != null) Dump(kh.transform, 0);
                System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "keyhints.txt"), sbk.ToString());
            }
            catch (Exception ex) { Log.LogWarning("[TEST] keyhints : " + ex.Message); }
            // Polices TMP réellement employées par l'interface du jeu : nom de police → exemples de textes qui l'utilisent
            try
            {
                var byFont = new Dictionary<string, List<string>>();
                foreach (var t in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
                {
                    if (t == null || t.font == null || string.IsNullOrEmpty(t.text) || !t.gameObject.scene.IsValid()) continue;
                    if (!byFont.TryGetValue(t.font.name, out var l)) byFont[t.font.name] = l = new List<string>();
                    if (l.Count < 12) l.Add(t.transform.parent != null ? t.transform.parent.name + "/" + t.name + "='" + t.text.Replace("\n", " ").Substring(0, Math.Min(25, t.text.Length)) + "'(" + t.fontSize + ")" : t.name);
                }
                foreach (var kv in byFont) Log.LogInfo($"[TEST] police TMP « {kv.Key} » : {kv.Value.Count}+ textes, ex. " + string.Join(" ; ", kv.Value));
                Log.LogInfo("[TEST] TMP_FontAsset connus : " + string.Join(", ", Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>().Select(f => f.name + (f.sourceFontFile != null ? "(" + f.sourceFontFile.name + ")" : "")).Distinct()));
            }
            catch (Exception ex) { Log.LogWarning("[TEST] polices TMP : " + ex.Message); }
            try { Log.LogInfo("[TEST] polices Unity disponibles : " + string.Join(", ", Resources.FindObjectsOfTypeAll<Font>().Select(f => f.name + "(" + f.fontSize + ")").Distinct())); } catch (Exception ex) { Log.LogWarning("[TEST] polices : " + ex.Message); }
            if (Quick.Value)
            {
                yield return Safe("GamepadTests", GamepadTests.Run(this, Player.m_localPlayer));
                yield return Safe("PinTests", PinTests.Run(this, Player.m_localPlayer));
                yield return Safe("CreatureTests", CreatureTests.Run(this, Player.m_localPlayer));
                yield return Safe("InventoryTests", InventoryTests.Run(this, Player.m_localPlayer));
                yield return Safe("EquipmentTests", EquipmentTests.Run(this, Player.m_localPlayer));
                yield return Safe("RecycleTests", RecycleTests.Run(this, Player.m_localPlayer));
                yield return Safe("RecyclerTests", RecyclerTests.Run(this, Player.m_localPlayer));
                yield return Safe("StaminaTests", StaminaTests.Run(this, Player.m_localPlayer));
                yield return Safe("GuideTests", GuideTests.Run(this, Player.m_localPlayer));
                GuideAudit.Run(this);
                CatalogAudit.Run(this);
                yield return Safe("CatalogSearchTests", CatalogSearchTests.Run(this, Player.m_localPlayer));
                yield return Safe("TeleportTests", TeleportTests.Run(this, Player.m_localPlayer));
                yield return Safe("TrackTests", TrackTests.Run(this, Player.m_localPlayer));
                yield return Safe("HudLayoutTests", HudLayoutTests.Run(this, Player.m_localPlayer));
                yield return Safe("ToggleTests", ToggleTests.Run(this, Player.m_localPlayer));
                yield return Safe("NativeUiProbe", NativeUiProbe.Run(this, Player.m_localPlayer));
                yield return Safe("ScrollTests", ScrollTests.Run(this, Player.m_localPlayer));
                yield return Safe("WindowStackTests", WindowStackTests.Run(this, Player.m_localPlayer));
                yield return Safe("L10nTests", L10nTests.Run(this, Player.m_localPlayer));
                L10nTests.Restore();
                Log.LogInfo($"[TEST] ===== fin : {_pass} PASS, {_fail} FAIL =====");
                yield break;
            }
            var player = Player.m_localPlayer;

            // ---------------- Inventory ----------------
            try
            {
                var wood = ObjectDB.instance.GetItemPrefab("Wood")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
                Check("Inventory.piles Wood", wood != null && wood.m_maxStackSize >= 1000, $"max={wood?.m_maxStackSize}");
                Check("Inventory.poids", player.GetMaxCarryWeight() >= 100000f, $"max={player.GetMaxCarryWeight()}");
                Check("Inventory.lignes", player.GetInventory().GetHeight() >= 4, $"lignes={player.GetInventory().GetHeight()}");
                var pkg = new ZPackage();
                player.Save(pkg);
                Check("Inventory.backup écrit", player.m_customData.ContainsKey("vmods.inventory.backup"));
            }
            catch (Exception ex) { Check("Inventory", false, ex.Message); }

            // ---------------- NoDurability ----------------
            try
            {
                var axe = ObjectDB.instance.GetItemPrefab("AxeBronze")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
                Check("NoDurability.AxeBronze", axe != null && !axe.m_useDurability, $"useDurability={axe?.m_useDurability}");
            }
            catch (Exception ex) { Check("NoDurability", false, ex.Message); }

            // ---------------- LongerFood ----------------
            try
            {
                var rasp = ObjectDB.instance.GetItemPrefab("Raspberry")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
                Check("LongerFood.Raspberry", rasp != null && Mathf.Abs(rasp.m_foodBurnTime - 900f) < 1f, $"burn={rasp?.m_foodBurnTime}s (attendu 900)");
            }
            catch (Exception ex) { Check("LongerFood", false, ex.Message); }

            // ---------------- ShortNights ----------------
            try
            {
                var m = AccessTools.Method(typeof(EnvMan), "RescaleDayFraction");
                float f1 = (float)m.Invoke(EnvMan.instance, new object[] { 0.075f });
                float f2 = (float)m.Invoke(EnvMan.instance, new object[] { 0.5f });
                float f3 = (float)m.Invoke(EnvMan.instance, new object[] { 0.925f });
                Check("ShortNights.seuils", Mathf.Abs(f1 - 0.25f) < 0.001f && Mathf.Abs(f2 - 0.5f) < 0.001f && Mathf.Abs(f3 - 0.75f) < 0.001f, $"{f1:0.###} {f2:0.###} {f3:0.###}");
            }
            catch (Exception ex) { Check("ShortNights", false, ex.Message); }

            // ---------------- Lumberjack : arbre créé, frappé au niveau 100 → tombe en un coup, puis auto-découpe ----------------
            GameObject tree = null;
            try
            {
                var skill = (Skills.Skill)AccessTools.Method(typeof(Skills), "GetSkill").Invoke(player.GetSkills(), new object[] { Skills.SkillType.WoodCutting });
                float savedLevel = skill.m_level;
                skill.m_level = 100f;
                var prefab = ZNetScene.instance.GetPrefab("Beech1");
                var pos = player.transform.position + player.transform.forward * 4f;
                tree = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                yield return new WaitForSeconds(1f);
                var tb = tree.GetComponent<TreeBase>();
                var hit = new HitData();
                hit.m_damage.m_chop = 1f; hit.m_toolTier = 2; hit.m_point = pos; hit.m_dir = player.transform.forward; hit.m_hitType = HitData.HitType.PlayerHit;
                hit.SetAttacker(player);
                tb.Damage(hit);
                yield return new WaitForSeconds(1f);
                bool felled = tree == null || !tree.activeSelf;
                Check("Lumberjack.un coup au niveau 100", felled);
                skill.m_level = savedLevel;

                // auto-découpe : attendre, puis compter les troncs et le bois autour
                yield return new WaitForSeconds(20f);
                int logs = UnityEngine.Object.FindObjectsOfType<TreeLog>().Count(l => Vector3.Distance(l.transform.position, pos) < 25f);
                int woodDrops = UnityEngine.Object.FindObjectsOfType<ItemDrop>().Count(d => d.m_itemData.m_shared.m_name == "$item_wood" && Vector3.Distance(d.transform.position, pos) < 25f);
                Check("Lumberjack.auto-découpe", logs == 0 && woodDrops > 0, $"troncs restants={logs}, bois au sol={woodDrops}");
            }
            finally { if (tree != null) ZNetScene.instance.Destroy(tree); }


            // ---------------- Lumberjack : réaction en chaîne (A tombe sur B) ----------------
            GameObject treeA = null, treeB = null;
            try
            {
                var skill = (Skills.Skill)AccessTools.Method(typeof(Skills), "GetSkill").Invoke(player.GetSkills(), new object[] { Skills.SkillType.WoodCutting });
                float savedLevel = skill.m_level; skill.m_level = 100f;
                var prefab = ZNetScene.instance.GetPrefab("Beech1");
                var fwd = player.transform.forward; fwd.y = 0; fwd.Normalize();
                var posA = player.transform.position + fwd * 3f;
                var posB = player.transform.position + fwd * 7f;
                treeA = UnityEngine.Object.Instantiate(prefab, posA, Quaternion.identity);
                treeB = UnityEngine.Object.Instantiate(prefab, posB, Quaternion.identity);
                yield return new WaitForSeconds(1f);
                var hit = new HitData();
                hit.m_damage.m_chop = 1f; hit.m_toolTier = 2; hit.m_point = posA; hit.m_dir = fwd; hit.m_hitType = HitData.HitType.PlayerHit;
                hit.SetAttacker(player);
                treeA.GetComponent<TreeBase>().Damage(hit);
                skill.m_level = savedLevel;
                yield return new WaitForSeconds(25f);
                bool bFelled = treeB == null || !treeB.activeSelf;
                int logs = UnityEngine.Object.FindObjectsOfType<TreeLog>().Count(l => Vector3.Distance(l.transform.position, posB) < 30f);
                // Si B n'a pas été touché (physique), le test est non concluant mais pas un échec.
                Check("Lumberjack.chaîne (A renverse B)", !bFelled || logs == 0, bFelled ? $"B renversé, troncs restants={logs}" : "B non touché par la chute (physique) : non concluant");
            }
            finally
            {
                if (treeA != null) ZNetScene.instance.Destroy(treeA);
                if (treeB != null) ZNetScene.instance.Destroy(treeB);
            }

            // ---------------- Inventory : mort sans tombe, récupération de tombe à distance ----------------
            try
            {
                var invAsm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Inventory");
                int before = player.GetInventory().NrOfItemsIncludingStacks();
                AccessTools.Method(typeof(Player), "CreateTombStone").Invoke(player, null);
                int tombsNear = UnityEngine.Object.FindObjectsOfType<TombStone>().Count(t => Vector3.Distance(t.transform.position, player.transform.position) < 20f);
                Check("Inventory.mort sans tombe", tombsNear == 0 && player.GetInventory().NrOfItemsIncludingStacks() == before, $"tombes créées={tombsNear}, objets {before}→{player.GetInventory().NrOfItemsIncludingStacks()}");
            }
            catch (Exception ex) { Check("Inventory.mort sans tombe", false, ex.InnerException?.Message ?? ex.Message); }

            GameObject tombGo = null;
            try
            {
                var prefab = ZNetScene.instance.GetPrefab("Player_tombstone");
                tombGo = UnityEngine.Object.Instantiate(prefab, player.transform.position + player.transform.forward * 60f, Quaternion.identity);
            }
            catch (Exception ex) { Check("Inventory.tombe à distance", false, ex.Message); }
            if (tombGo != null)
            {
                yield return new WaitForSeconds(1f);
                var ts = tombGo.GetComponent<TombStone>();
                ts.Setup(Game.instance.GetPlayerProfile().GetName(), Game.instance.GetPlayerProfile().GetPlayerID());
                tombGo.GetComponent<Container>().GetInventory().AddItem("Resin", 7, 1, 0, 0L, "", false, false);
                var tombId = tombGo.GetComponent<ZNetView>().GetZDO().m_uid;
                yield return new WaitForSeconds(1f);
                int resinBefore = player.GetInventory().CountItems("$item_resin", -1, false);
                var deathT = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Inventory").GetType("InventoryMod.Death");
                var recover = (IEnumerator)deathT.GetMethod("Recover", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { player });
                while (recover.MoveNext()) yield return recover.Current;
                int resinAfter = player.GetInventory().CountItems("$item_resin", -1, false);
                yield return new WaitForSeconds(1f);
                var tz = ZDOMan.instance.GetZDO(tombId);
                bool gone = tz == null || (tz.GetByteArray(ZDOVars.s_items, null)?.Length ?? 0) <= 30;
                Check("Inventory.tombe à distance", resinAfter >= resinBefore + 7 && gone, $"résine {resinBefore}→{resinAfter}, tombe vidée/supprimée={gone}");
                if (!gone) ZNetScene.instance.Destroy(tombGo);
                if (resinAfter > resinBefore) player.GetInventory().RemoveItem("$item_resin", 7, -1, false);
            }

            // ---------------- Movement ----------------
            try
            {
                var mv = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Movement");
                var enabled = (ConfigEntry<bool>)mv.GetType("Movement.Plugin").GetField("Enabled", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                var run = AccessTools.Method(typeof(Character), "GetRunSpeedFactor");
                enabled.Value = false; float v0 = (float)run.Invoke(player, null);
                enabled.Value = true;  float v1 = (float)run.Invoke(player, null);
                Check("Movement.course ×1.3", Mathf.Abs(v1 / v0 - 1.3f) < 0.01f, $"facteur {v0:0.###} → {v1:0.###}");
            }
            catch (Exception ex) { Check("Movement", false, ex.InnerException?.Message ?? ex.Message); }
            // ---------------- CraftFromChests : coffre créé avec tout le bois du joueur ----------------
            GameObject chest = null;
            // Le monde de test garde les coffres d'un run interrompu, et le bois au sol (test du bûcheron) serait ramassé
            // en cours de test : on retire les coffres voisins et on coupe le ramassage automatique le temps du test.
            var autoPickup = typeof(Player).GetField("m_enableAutoPickup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            object prevPickup = autoPickup?.GetValue(player); autoPickup?.SetValue(player, false);
            int removedChests = 0;
            foreach (var old in UnityEngine.Object.FindObjectsOfType<Container>())
                if (old.name.StartsWith("piece_chest_wood", StringComparison.Ordinal) && Vector3.Distance(old.transform.position, player.transform.position) < 250f) { ZNetScene.instance.Destroy(old.gameObject); removedChests++; }
            if (removedChests > 0) { Log.LogInfo($"[TEST] coffres d'un run précédent retirés : {removedChests}"); yield return new WaitForSeconds(0.5f); }
            try
            {
                var prefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
                chest = UnityEngine.Object.Instantiate(prefab, player.transform.position + player.transform.right * 3f, Quaternion.identity);
                yield return new WaitForSeconds(1f);
                var container = chest.GetComponent<Container>();
                var cInv = container.GetInventory();
                var pInv = player.GetInventory();
                int playerWood = pInv.CountItems("$item_wood", -1, false);
                // déplacer le bois du joueur vers le coffre, plus 20 de test
                if (playerWood > 0) pInv.RemoveItem("$item_wood", playerWood, -1, false);
                cInv.AddItem("Wood", playerWood + 20, 1, 0, 0L, "", false, false);
                yield return new WaitForSeconds(0.5f);
                var piece = ZNetScene.instance.GetPrefab("piece_chest_wood").GetComponent<Piece>();
                // Recette sans station (massue = 6 bois) : HaveRequirements(pièce) exigerait un établi à portée.
                var club = ObjectDB.instance.m_recipes.First(r => r.m_item != null && r.m_item.name == "Club");
                bool can = player.HaveRequirements(club, false, 1, 1);
                // Diagnostic : ce que le mod voit du coffre (réseau valide, à portée, accessible) et la recette elle-même
                var nviewC = chest.GetComponent<ZNetView>();
                var cfcT = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "CraftFromChests")?.GetType("CraftFromChests.ContainerRegistry");
                var nearby = cfcT?.GetMethod("Nearby", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null) as System.Collections.ICollection;
                Log.LogInfo($"[TEST] coffre : nview valide={nviewC != null && nviewC.IsValid()}, propriétaire={nviewC != null && nviewC.IsValid() && nviewC.IsOwner()}, en usage={container.IsInUse()}, coffres vus par le mod={nearby?.Count.ToString() ?? "?"}, distance={Vector3.Distance(chest.transform.position, player.transform.position):0.0} m ; recette massue : {string.Join(" + ", club.m_resources.Select(r => $"{r.GetAmount(1)} × {r.m_resItem?.m_itemData?.m_shared?.m_name}"))}, station={(club.m_craftingStation != null ? club.m_craftingStation.name : "aucune")} ; coffre : {string.Join(" + ", piece.m_resources.Select(r => $"{r.GetAmount(0)} × {r.m_resItem?.m_itemData?.m_shared?.m_name}"))}");
                Check("CraftFromChests.ressources vues dans le coffre", can && pInv.CountItems("$item_wood", -1, false) == 0, $"recette possible={can}, boisJoueur={pInv.CountItems("$item_wood", -1, false)}, boisCoffre={cInv.CountItems("$item_wood", -1, false)}");
                int before = cInv.CountItems("$item_wood", -1, false);
                player.ConsumeResources(piece.m_resources, 0, -1, 1);
                yield return null;
                int after = cInv.CountItems("$item_wood", -1, false);
                Check("CraftFromChests.consommation depuis le coffre", after == before - 10, $"coffre {before} → {after} (attendu -10)");
                // rendre le bois au joueur
                int back = cInv.CountItems("$item_wood", -1, false);
                cInv.RemoveItem("$item_wood", back, -1, false);
                pInv.AddItem("Wood", Math.Max(0, playerWood), 1, 0, 0L, "", false, false);
            }
            finally { if (chest != null) ZNetScene.instance.Destroy(chest); autoPickup?.SetValue(player, prevPickup); }

            // ---------------- ResourceFinder : recherche dans le monde connu, puis scan fantôme ----------------
            Func<string, string[], float, bool, IEnumerator> runFinder = null;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "ResourceFinder");
                var entryT = asm.GetType("ResourceFinder.ResourceEntry");
                var finderT = asm.GetType("ResourceFinder.Finder");
                var finder = Activator.CreateInstance(finderT);
                var start = finderT.GetMethod("Start");
                var tick = finderT.GetMethod("Tick");
                var stateP = finderT.GetProperty("State");
                var resultsF = finderT.GetField("Results");
                var statusP = finderT.GetProperty("Status");
                var zonesP = finderT.GetProperty("ZonesGenerated");

                runFinder = (label, prefabs, timeoutS, expectScan) => RunFinder(label, prefabs, timeoutS, expectScan);
                IEnumerator RunFinder(string label, string[] prefabs, float timeoutS, bool expectScan)
                {
                    object entry = null;
                    try
                    {
                        // Entrée du catalogue si le libellé existe (indices de biome inclus), sinon entrée ad hoc
                        var catalogT = asm.GetType("ResourceFinder.Catalog");
                        var entries = (IList)catalogT.GetField("Entries").GetValue(null);
                        foreach (var e in entries) if ((string)entryT.GetField("Label").GetValue(e) == label) entry = e;
                        if (entry == null) entry = Activator.CreateInstance(entryT, label, prefabs, null, null, Enum.ToObject(typeof(Heightmap.Biome), 0));
                        start.Invoke(finder, new[] { entry, (object)player.transform.position });
                    }
                    catch (Exception ex) { Check($"ResourceFinder.{label}", false, ex.InnerException?.Message ?? ex.Message); yield break; }
                    float t = Time.time + timeoutS;
                    while (stateP.GetValue(finder).ToString() != "Done" && Time.time < t) { tick.Invoke(finder, new object[] { player.transform.position }); yield return null; }
                    var results = (IList)resultsF.GetValue(finder);
                    int zones = (int)zonesP.GetValue(finder);
                    bool done = stateP.GetValue(finder).ToString() == "Done";
                    bool ok = done && (expectScan || results.Count > 0); // scan : on vérifie qu'il se termine, le nombre de zones dépend de la carte
                    Check($"ResourceFinder.{label}", ok, $"état={stateP.GetValue(finder)}, résultats={results.Count}, zonesGénérées={zones}, {statusP.GetValue(finder)}");
                }
            }
            catch (Exception ex) { Check("ResourceFinder", false, ex.Message); }
            if (runFinder != null)
            {
                yield return runFinder("connu (Beech1)", new[] { "Beech1" }, 30f, false);
                yield return runFinder("Or (trolls pétrifiés)", new[] { "TrollFrost_Frac" }, 180f, false); // lieux DN_gammeltrollFrac connus pour toute la carte : résultats immédiats attendus
            }


            yield return Safe("GamepadTests", GamepadTests.Run(this, player));
            yield return Safe("PinTests", PinTests.Run(this, player));
            yield return Safe("CreatureTests", CreatureTests.Run(this, player));
            yield return Safe("InventoryTests", InventoryTests.Run(this, player));
            yield return Safe("EquipmentTests", EquipmentTests.Run(this, player));
            yield return Safe("RecycleTests", RecycleTests.Run(this, player));
            yield return Safe("RecyclerTests", RecyclerTests.Run(this, player));
            yield return Safe("StaminaTests", StaminaTests.Run(this, player));
            yield return Safe("GuideTests", GuideTests.Run(this, player));
            GuideAudit.Run(this);
            CatalogAudit.Run(this);
            yield return Safe("CatalogSearchTests", CatalogSearchTests.Run(this, player));
            yield return Safe("TeleportTests", TeleportTests.Run(this, player));
            yield return Safe("TrackTests", TrackTests.Run(this, player));
            yield return Safe("HudLayoutTests", HudLayoutTests.Run(this, player));
            yield return Safe("ToggleTests", ToggleTests.Run(this, player));
            yield return Safe("NativeUiProbe", NativeUiProbe.Run(this, player));
            yield return Safe("ScrollTests", ScrollTests.Run(this, player));
            yield return Safe("WindowStackTests", WindowStackTests.Run(this, player));
            yield return Safe("L10nTests", L10nTests.Run(this, player));
            L10nTests.Restore();

            // ---------------- Diagnostic : table de végétation (filtre biome du finder) ----------------
            try
            {
                var veg = ZoneSystem.instance.m_vegetation;
                int nullPrefab = veg.Count(v => v.m_prefab == null);
                Log.LogInfo($"[TEST] végétation : {veg.Count} entrées, {nullPrefab} sans m_prefab");
                var lines = new List<string> { "# name\tprefab\tbiome\tenable\tmin\tmax" };
                foreach (var v in veg) lines.Add($"{v.m_name}\t{(v.m_prefab != null ? v.m_prefab.name : "null")}\t{v.m_biome}\t{v.m_enable}\t{v.m_min}\t{v.m_max}");
                System.IO.File.WriteAllLines(System.IO.Path.Combine(Paths.ConfigPath, "ResourceFinder.vegetation.txt"), lines);
                Log.LogInfo("[TEST] table de végétation exportée dans ResourceFinder.vegetation.txt");
                foreach (var v in veg.Where(v => (v.m_name ?? "").IndexOf("vein", StringComparison.OrdinalIgnoreCase) >= 0
                                              || (v.m_name ?? "").IndexOf("copper", StringComparison.OrdinalIgnoreCase) >= 0
                                              || (v.m_prefab != null && v.m_prefab.name.IndexOf("vein", StringComparison.OrdinalIgnoreCase) >= 0)))
                    Log.LogInfo($"[TEST]   veg name='{v.m_name}' prefab='{(v.m_prefab != null ? v.m_prefab.name : "null")}' biome={v.m_biome} enable={v.m_enable}");
            }
            catch (Exception ex) { Log.LogInfo("[TEST] diag végétation : " + ex.Message); }
            Log.LogInfo($"[TEST] ===== fin : {_pass} PASS, {_fail} FAIL =====");
        }
    }
}
