using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Guide : chapitres et clés de boss résolus, toutes les étapes s'évaluent sans erreur, détection d'un objet donné
    /// (5 totems → étape cochée), persistance des étapes ignorées, captures de la fenêtre et du suivi. Plus les boutons
    /// de mods dans l'inventaire (Tab) avec capture.
    /// </summary>
    internal static class GuideTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = Asm("Guide");
            var chaptersT = asm.GetType("Guide.Chapters");
            var chapterT = asm.GetType("Guide.Chapter");
            var stepT = asm.GetType("Guide.Step");
            var checksT = asm.GetType("Guide.Checks");
            var progressT = asm.GetType("Guide.Progress");
            var pluginT = asm.GetType("Guide.Plugin");
            var all = (IList)chaptersT.GetField("All").GetValue(null);

            // Chapitres et clés de boss
            try
            {
                var keys = new List<string>();
                foreach (var c in all)
                {
                    string boss = (string)chapterT.GetField("Boss").GetValue(c);
                    string key = (string)checksT.GetMethod("BossKey").Invoke(null, new object[] { boss });
                    keys.Add($"{boss}={key ?? "null"}");
                }
                h.Check("Guide.chapitres et clés de boss", all.Count == 8 && keys.All(k => !k.EndsWith("null")), string.Join(", ", keys));
                string axe = (string)checksT.GetMethod("ItemName").Invoke(null, new object[] { "AxeStone" });
                string wb = (string)checksT.GetMethod("PieceName").Invoke(null, new object[] { "piece_workbench" });
                string boar = (string)checksT.GetMethod("EnemyName").Invoke(null, new object[] { "Boar" });
                h.Check("Guide.résolution des noms", axe != null && wb != null && boar != null, $"{axe}, {wb}, {boar}");
            }
            catch (Exception ex) { h.Check("Guide.chapitres", false, ex.InnerException?.Message ?? ex.Message); }

            // Offrandes lues sur les autels du jeu (Facts) : ce sont elles qui fixent les quantités du guide
            try
            {
                var factsT = asm.GetType("Guide.Facts");
                var offM = factsT.GetMethod("OfferingOf");
                var expected = new[] { ("Eikthyrnir", "TrophyDeer", 2), ("GDKing", "AncientSeed", 3), ("Bonemass", "WitheredBone", 10), ("Dragonqueen", "DragonEgg", 3), ("GoblinKing", "GoblinTotem", 5), ("FaderLocation", "Bell", 3), ("DN_Bossroom", "HatefulBlood", 3) };
                var got = new List<string>(); bool ok = true;
                foreach (var (loc, item, count) in expected)
                {
                    var o = offM.Invoke(null, new object[] { loc });
                    var ot = o.GetType();
                    string oi = (string)ot.GetField("Item").GetValue(o); int oc = (int)ot.GetField("Count").GetValue(o); bool ov = (bool)ot.GetField("Valid").GetValue(o);
                    got.Add($"{loc}={oi}×{oc}");
                    if (!ov || oi != item || oc != count) ok = false;
                }
                h.Check("Guide.offrandes lues sur les autels", ok, string.Join(", ", got));
                string bells = (string)factsT.GetMethod("RecipeText").Invoke(null, new object[] { "Bell" });
                h.Check("Guide.recette lue dans le jeu", bells.Contains("3 "), $"Bell : {bells}");
            }
            catch (Exception ex) { h.Check("Guide.offrandes", false, ex.InnerException?.Message ?? ex.Message); }

            // Évaluation de toutes les étapes
            var tick = progressT.GetMethod("Tick");
            var get = progressT.GetMethod("Get");
            int steps = 0, done = 0;
            try
            {
                tick.Invoke(null, new object[] { player, true });
                var lines = new List<string>();
                foreach (var c in all)
                {
                    int cd = 0; var stepsList = (IList)chapterT.GetField("Steps").GetValue(c);
                    foreach (var s in stepsList)
                    {
                        var st = get.Invoke(null, new[] { c, s });
                        bool d = (bool)st.GetType().GetField("Done").GetValue(st);
                        steps++; if (d) { done++; cd++; }
                    }
                    lines.Add($"{chapterT.GetField("Id").GetValue(c)} {cd}/{stepsList.Count}");
                }
                h.Check("Guide.évaluation des étapes", steps > 60, $"{steps} étapes, {done} faites : {string.Join(", ", lines)}");
            }
            catch (Exception ex) { h.Check("Guide.évaluation", false, ex.InnerException?.Message ?? ex.Message); }

            // Détection : 5 totems fuling dans l'inventaire → étape « totems » cochée (puis retirés : reste cochée)
            try
            {
                object yag = null, totems = null;
                foreach (var c in all) if ((string)chapterT.GetField("Id").GetValue(c) == "yagluth") yag = c;
                foreach (var s in (IList)chapterT.GetField("Steps").GetValue(yag)) if ((string)stepT.GetField("Id").GetValue(s) == "totems") totems = s;
                var doneSet = (HashSet<string>)progressT.GetField("Done").GetValue(null);
                doneSet.Remove("yagluth.totems");
                tick.Invoke(null, new object[] { player, true });
                var st0 = get.Invoke(null, new[] { yag, totems });
                bool before = (bool)st0.GetType().GetField("Done").GetValue(st0);
                string prog0 = (string)st0.GetType().GetField("Progress").GetValue(st0);
                player.GetInventory().AddItem("GoblinTotem", 5, 1, 0, 0L, "", false, false);
                tick.Invoke(null, new object[] { player, true });
                var st1 = get.Invoke(null, new[] { yag, totems });
                bool after = (bool)st1.GetType().GetField("Done").GetValue(st1);
                player.GetInventory().RemoveItem("$item_goblintotem", 5, -1, false);
                tick.Invoke(null, new object[] { player, true });
                var st2 = get.Invoke(null, new[] { yag, totems });
                bool sticky = (bool)st2.GetType().GetField("Done").GetValue(st2);
                h.Check("Guide.offrande = objets sur soi (5 totems, puis retirés → à refaire)", after && !sticky, $"avant={before} ({prog0}), avec 5 totems={after}, après retrait={sticky}");
                doneSet.Remove("yagluth.totems"); // ne pas laisser une fausse étape (le test n'est pas sauvegardé de toute façon)
            }
            catch (Exception ex) { h.Check("Guide.détection", false, ex.InnerException?.Message ?? ex.Message); }

            // Persistance : ignorer une étape, recharger depuis customData
            try
            {
                object eik = null, leather = null;
                foreach (var c in all) if ((string)chapterT.GetField("Id").GetValue(c) == "eikthyr") eik = c;
                foreach (var s in (IList)chapterT.GetField("Steps").GetValue(eik)) if ((string)stepT.GetField("Id").GetValue(s) == "leather") leather = s;
                progressT.GetMethod("SetSkipped").Invoke(null, new[] { eik, leather, (object)true });
                progressT.GetMethod("Load").Invoke(null, new object[] { player });
                bool skipped = (bool)progressT.GetMethod("IsSkipped").Invoke(null, new[] { eik, leather });
                progressT.GetMethod("SetSkipped").Invoke(null, new[] { eik, leather, (object)false });
                h.Check("Guide.persistance (ignoré → rechargé)", skipped && player.m_customData.ContainsKey("vmods.guide"), player.m_customData.TryGetValue("vmods.guide", out var d) ? d.Substring(0, Math.Min(80, d.Length)) : "");
            }
            catch (Exception ex) { h.Check("Guide.persistance", false, ex.InnerException?.Message ?? ex.Message); }

            // Anti-spoiler : chapitre 1 visible, chapitres suivants verrouillés (boss précédent non vaincu), révélation à la main ; suivi masqué sous l'inventaire
            try
            {
                var unlockedM = progressT.GetMethod("IsUnlocked");
                var ch1 = all[0]; var ch2 = all[1]; var ch8 = all[7];
                bool u1 = (bool)unlockedM.Invoke(null, new[] { ch1 }), u2 = (bool)unlockedM.Invoke(null, new[] { ch2 }), u8 = (bool)unlockedM.Invoke(null, new[] { ch8 });
                bool boss1 = (bool)progressT.GetMethod("ChapterDone").Invoke(null, new[] { ch1 });
                progressT.GetMethod("Reveal").Invoke(null, new[] { ch8 });
                bool u8r = (bool)unlockedM.Invoke(null, new[] { ch8 });
                ((HashSet<string>)progressT.GetField("Revealed").GetValue(null)).Remove((string)chapterT.GetField("Id").GetValue(ch8));
                h.Check("Guide.anti-spoiler", u1 && (u2 == boss1) && !u8 && u8r, $"ch1={u1}, ch2={u2} (boss1 vaincu={boss1}), ch8={u8}, ch8 révélé={u8r}");
            }
            catch (Exception ex) { h.Check("Guide.anti-spoiler", false, ex.InnerException?.Message ?? ex.Message); }
            {
                var allowedM = pluginT.GetMethod("TrackerAllowed", BindingFlags.NonPublic | BindingFlags.Static);
                yield return new WaitForSecondsRealtime(1.5f); // l'inventaire du test précédent finit de se fermer (animation)
                bool before = (bool)allowedM.Invoke(null, null);
                InventoryGui.instance.Show(null, 1);
                yield return null;
                bool during = (bool)allowedM.Invoke(null, null);
                InventoryGui.instance.Hide();
                h.Check("Guide.suivi masqué sous l'inventaire", before && !during, $"avant={before}, inventaire ouvert={during} (HUD caché={Hud.IsUserHidden()}, inventaire={InventoryGui.IsVisible()}, menu={Menu.IsVisible()}, texte={TextInput.IsVisible()}, carte={Minimap.IsOpen()}, chargement={(Hud.instance?.m_loadingScreen != null && Hud.instance.m_loadingScreen.gameObject.activeSelf)}, mort={player.IsDead()}, cinématique={player.InCutscene()}, construction={Hud.IsPieceSelectionVisible()})");
                // Écran de chargement (téléportation) : le suivi ne doit pas rester par-dessus
                var ls = Hud.instance.m_loadingScreen;
                bool wasActive = ls.gameObject.activeSelf;
                ls.gameObject.SetActive(true);
                bool loading = (bool)allowedM.Invoke(null, null);
                ls.gameObject.SetActive(wasActive);
                h.Check("Guide.suivi masqué pendant le chargement", !loading);
            }

            // « Cibler l'autel » du guide = ResourceFinder.SearchLabel : une entrée « lieux seulement » doit donner une cible tout de suite
            {
                var rfAsm2 = Asm("ResourceFinder");
                var rfT2 = rfAsm2.GetType("ResourceFinder.Plugin");
                var rfInst2 = UnityEngine.Object.FindObjectOfType(rfT2);
                bool okCall = false; string err = null;
                try { okCall = (bool)rfT2.GetMethod("SearchLabel").Invoke(null, new object[] { "Autel : Eikthyr" }); } catch (Exception ex) { err = ex.InnerException?.Message ?? ex.Message; }
                yield return null;
                var target = rfT2.GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rfInst2);
                var layer = rfT2.GetField("_targetLayer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rfInst2);
                h.Check("Guide.Cibler l'autel donne une cible", err == null && okCall && target != null && layer != null, err ?? $"appel={okCall}, cible={(target != null)}, couche={(layer != null)}");
                rfT2.GetMethod("ClearAllPins", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst2, null);
            }

            // Épingle automatique de l'autel du chapitre courant (si le lieu est en zone explorée)
            try
            {
                var apT = asm.GetType("Guide.AltarPins");
                apT.GetField("s_next", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0f);
                ((HashSet<string>)apT.GetField("s_doneThisSession", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Clear();
                apT.GetMethod("Tick").Invoke(null, null);
                var cur = progressT.GetMethod("Current").Invoke(null, null);
                string loc = (string)chapterT.GetField("Location").GetValue(cur);
                Vector3? altar = null; bool explored = false;
                foreach (var kv in ZoneSystem.instance.m_locationInstances)
                    if ((kv.Value.m_location?.m_prefabName ?? "").IndexOf(loc, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool e = (bool)typeof(Minimap).GetMethod("IsExplored", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Vector3) }, null).Invoke(Minimap.instance, new object[] { kv.Value.m_position });
                        if (e) { altar = kv.Value.m_position; explored = true; break; }
                        if (altar == null) altar = kv.Value.m_position;
                    }
                bool near = false;
                if (altar != null)
                    foreach (var p in (List<Minimap.PinData>)typeof(Minimap).GetField("m_pins", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Minimap.instance))
                        if (Vector3.Distance(p.m_pos, altar.Value) < 40f) near = true;
                h.Check("Guide.épingle de l'autel", altar == null || !explored || near, $"lieu={loc}, trouvé={altar != null}, exploré={explored}, épingle à moins de 40 m={near}");
            }
            catch (Exception ex) { h.Check("Guide.épingle de l'autel", false, ex.InnerException?.Message ?? ex.Message); }

            // Captures : suivi à l'écran puis fenêtre
            string shotTracker = System.IO.Path.Combine(Paths.ConfigPath, "guide_tracker.png");
            string shotWindow = System.IO.Path.Combine(Paths.ConfigPath, "guide_window.png");
            // Une étape « tout juste accomplie » pour la capture : l'événement est levé à la main sur la première étape du chapitre courant
            try
            {
                var cur = progressT.GetMethod("Current").Invoke(null, null);
                var first = ((IList)chapterT.GetField("Steps").GetValue(cur))[0];
                var ev = progressT.GetField("StepCompleted", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Delegate;
                ev?.DynamicInvoke(cur, first);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("événement StepCompleted : " + ex.Message); }
            Plugin.Step("guide : événement levé");
            yield return new WaitForSecondsRealtime(0.5f);
            Plugin.Step("guide : capture du suivi");
            ScreenCapture.CaptureScreenshot(shotTracker);
            yield return new WaitForSecondsRealtime(1f);
            // Mode réduit (une ligne) : capture séparée, puis retour au mode complet
            var modeCfg = (BepInEx.Configuration.ConfigEntryBase)pluginT.GetField("Mode", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var modePrev = modeCfg.BoxedValue;
            modeCfg.BoxedValue = Enum.Parse(modeCfg.SettingType, "Reduit");
            pluginT.GetField("_expandUntil", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(UnityEngine.Object.FindObjectOfType(pluginT), 0f);
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.ConfigPath, "guide_tracker_compact.png"));
            yield return new WaitForSecondsRealtime(0.8f);
            modeCfg.BoxedValue = modePrev;
            yield return new WaitForSecondsRealtime(0.6f);

            // Étape au titre long (deux lignes) : sa ligne grandit, la suivante ne la chevauche pas
            var rectF = pluginT.GetField("LastTrackerRect", BindingFlags.NonPublic | BindingFlags.Static);
            var guide = UnityEngine.Object.FindObjectOfType(pluginT);
            var nextSteps = pluginT.GetField("_trackerNext", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(guide) as IList;
            object longStep = nextSteps != null && nextSteps.Count > 1 ? nextSteps[1] : null;
            var titleFuncF = longStep?.GetType().GetField("TitleFunc");
            var prevFunc = titleFuncF?.GetValue(longStep);
            if (longStep != null)
            {
                float before = ((Rect)rectF.GetValue(null)).height;
                titleFuncF.SetValue(longStep, (Func<string>)(() => "Ouvrir la porte, verser le sang et vaincre le roi, puis rentrer au camp avant la nuit"));
                yield return new WaitForSecondsRealtime(0.6f);
                float after = ((Rect)rectF.GetValue(null)).height;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.ConfigPath, "guide_tracker_long.png"));
                yield return new WaitForSecondsRealtime(0.8f);
                titleFuncF.SetValue(longStep, prevFunc);
                h.Check("Guide.étape sur deux lignes : sa ligne grandit", after >= before + 12f, $"hauteur du suivi {before:0} → {after:0}");
            }
            else h.Check("Guide.étape sur deux lignes : sa ligne grandit", false, "moins de deux étapes affichées");
            Plugin.Step("guide : ouverture de la fenêtre");
            pluginT.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Plugin.Step("guide : fenêtre ouverte, attente");
            yield return new WaitForSecondsRealtime(1f);
            Plugin.Step("guide : capture de la fenêtre");
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(shotWindow);
            yield return new WaitForSecondsRealtime(1.5f);
            pluginT.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            h.Check("Guide.captures", System.IO.File.Exists(shotTracker) && System.IO.File.Exists(shotWindow));

            // Boutons de mods dans l'inventaire
            string shotInv = System.IO.Path.Combine(Paths.ConfigPath, "inventory_buttons.png");
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(1f);
            // Diagnostic : structure de la grille du joueur (pour le défilement au-delà de 9 lignes)
            try
            {
                var gui = InventoryGui.instance; var grid = gui.m_playerGrid;
                string Path(Transform t) { var parts = new List<string>(); while (t != null) { parts.Insert(0, t.name); t = t.parent; } return string.Join("/", parts); }
                Plugin.Log.LogInfo($"[TEST] grille joueur : {Path(grid.transform)} composants=[{string.Join(", ", grid.GetComponents<Component>().Select(c => c.GetType().Name))}] scrollbar={(grid.m_scrollbar != null ? grid.m_scrollbar.name : "null")} ensureVisible={(grid.m_ensureVisible != null)} elementSpace={grid.m_elementSpace}");
                Plugin.Log.LogInfo($"[TEST] gridRoot : {Path(grid.m_gridRoot)} taille={grid.m_gridRoot.sizeDelta} anchors={grid.m_gridRoot.anchorMin}-{grid.m_gridRoot.anchorMax} pivot={grid.m_gridRoot.pivot}");
                for (var t = grid.m_gridRoot.parent; t != null && t != gui.transform; t = t.parent)
                    Plugin.Log.LogInfo($"[TEST] parent {t.name} : [{string.Join(", ", t.GetComponents<Component>().Select(c => c.GetType().Name))}] taille={(t as RectTransform)?.sizeDelta}");
                Plugin.Log.LogInfo($"[TEST] m_player : {Path(gui.m_player)} taille={gui.m_player.sizeDelta} playerHeight={typeof(InventoryGui).GetField("m_playerHeight", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(gui)} invGridHeight={typeof(InventoryGui).GetField("m_invGridHeight", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(gui)}");
                var cg = typeof(InventoryGui).GetField("m_containerGrid", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(gui) as InventoryGrid;
                if (cg != null) Plugin.Log.LogInfo($"[TEST] grille coffre : {Path(cg.transform)} scrollbar={(cg.m_scrollbar != null ? cg.m_scrollbar.name : "null")} parents=[{string.Join(" | ", Enumerable.Range(0, 3).Select(i => { var t = cg.m_gridRoot; for (int k = 0; k <= i && t != null; k++) t = t.parent as RectTransform; return t != null ? t.name + ":" + string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name)) : "-"; }))}]");
            }
            catch (Exception ex) { Plugin.Log.LogInfo("[TEST] diag grille : " + ex.Message); }
            int buttons = 0; var names = new List<string>();
            foreach (var t in InventoryGui.instance.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("ModButton_")) { buttons++; names.Add(t.name); }
            ScreenCapture.CaptureScreenshot(shotInv);
            yield return new WaitForSecondsRealtime(1.5f);
            InventoryGui.instance.Hide();
            h.Check("Inventaire.boutons de mods", buttons >= 3, $"{buttons} boutons : {string.Join(", ", names)}");
        }
    }
}
