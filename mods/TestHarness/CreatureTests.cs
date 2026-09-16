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
    /// Créatures dans le finder : un sanglier créé devant le joueur est trouvé par la recherche « Sanglier » sans
    /// génération de zones ; sa position suit ses déplacements ; sa disparition retire le résultat. Découverte par
    /// statistiques de kills. Plus une capture d'écran de la nouvelle fenêtre (config/finder_window.png).
    /// </summary>
    internal static class CreatureTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        public static IEnumerator Run(Plugin h, Player player)
        {
            var rfAsm = Asm("ResourceFinder");
            var finderT = rfAsm.GetType("ResourceFinder.Finder");
            var entryT = rfAsm.GetType("ResourceFinder.ResourceEntry");
            var catalogT = rfAsm.GetType("ResourceFinder.Catalog");
            var discoveryT = rfAsm.GetType("ResourceFinder.Discovery");
            GameObject boar = null;
            try
            {
                var entries = (IList)catalogT.GetField("Entries").GetValue(null);
                object entry = null;
                foreach (var e in entries) if ((string)entryT.GetField("Label").GetValue(e) == "Sanglier") entry = e;
                if (entry == null) throw new Exception("entrée Sanglier absente du catalogue");
                int creatures = 0; foreach (var e in entries) if (entryT.GetField("Category").GetValue(e).ToString() == "Creature") creatures++;
                h.Check("Créatures.catalogue", creatures >= 40, $"{creatures} entrées créatures");
                var missingIcons = new List<string>();
                foreach (var e in entries) { var ic = (string)entryT.GetField("Icon").GetValue(e); if (!string.IsNullOrEmpty(ic) && ObjectDB.instance.GetItemPrefab(ic) == null) missingIcons.Add(ic); }
                var iconsT = rfAsm.GetType("ResourceFinder.Icons");
                var copperItem = iconsT.GetMethod("ItemForPrefab").Invoke(null, new object[] { "rock4_copper" }) as ItemDrop;
                string copperName = copperItem?.m_itemData?.m_shared?.m_name;
                h.Check("Catalogue.rocher de cuivre → minerai de cuivre", copperName == "$item_copperore", copperName ?? "null");
                // Recherche tapée : libellés du catalogue sans accents, « or » → l'entrée Or et pas « Morgen »/« Forteresse »
                var resolve = rfAsm.GetType("ResourceFinder.Plugin").GetMethod("ResolveSearch", BindingFlags.NonPublic | BindingFlags.Static);
                string R(string q) => (string)entryT.GetField("Label").GetValue(resolve.Invoke(null, new object[] { q }));
                h.Check("Recherche tapée → catalogue", R("cuivre") == "Cuivre" && R("or").StartsWith("Or ") && R("AUTEL EIKTHYR") == "Autel : Eikthyr" && R("mures") == "Mûres arctiques" && R("greydwarf").StartsWith("Greydwarf"), $"cuivre→{R("cuivre")}, or→{R("or")}, autel eikthyr→{R("AUTEL EIKTHYR")}, mures→{R("mures")}, greydwarf→{R("greydwarf")}");
                // Intérieur de donjon (altitude ~5 000 m) : ignoré et compté, et distance horizontale
                {
                    var bp = ZNetScene.instance.GetPrefab("BonePileSpawner");
                    var high = UnityEngine.Object.Instantiate(bp, new Vector3(player.transform.position.x, 5000f, player.transform.position.z), Quaternion.identity);
                    yield return null;
                    object skelEntry = null; foreach (var e2 in entries) if ((string)entryT.GetField("Label").GetValue(e2) == "Squelette (et ossuaires)") skelEntry = e2;
                    var maxScan = (BepInEx.Configuration.ConfigEntry<float>)rfAsm.GetType("ResourceFinder.Plugin").GetField("MaxScanRadius", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                    float prevMax = maxScan.Value; maxScan.Value = 0f;
                    var f2 = Activator.CreateInstance(finderT);
                    finderT.GetMethod("Start").Invoke(f2, new[] { skelEntry, (object)player.transform.position });
                    var st2 = finderT.GetProperty("State"); var tk2 = finderT.GetMethod("Tick"); float t2 = Time.time + 8f;
                    while (st2.GetValue(f2).ToString() != "Done" && Time.time < t2) { tk2.Invoke(f2, new object[] { player.transform.position }); yield return null; }
                    maxScan.Value = prevMax;
                    var highId = high.GetComponent<ZNetView>().GetZDO().m_uid;
                    bool listed = false; foreach (var r in (IList)finderT.GetField("Results").GetValue(f2)) if ((ZDOID)r.GetType().GetField("Id").GetValue(r) == highId) listed = true;
                    int inDungeons = (int)finderT.GetProperty("InDungeons").GetValue(f2);
                    ZNetScene.instance.Destroy(high);
                    h.Check("Finder.intérieur de donjon ignoré", !listed && inDungeons >= 1, $"listé={listed}, ignorés={inDungeons}");
                    var resT = rfAsm.GetType("ResourceFinder.Result");
                    var rr = Activator.CreateInstance(resT); resT.GetField("Pos").SetValue(rr, new Vector3(10f, 5000f, 0f));
                    float dist = (float)resT.GetMethod("Distance").Invoke(rr, new object[] { new Vector3(0f, 0f, 0f) });
                    h.Check("Finder.distance horizontale", Mathf.Abs(dist - 10f) < 0.01f, $"{dist:0.##} m (attendu 10)");
                }
                // Créature : biomes de spawn lus dans le jeu (« vit dans … »)
                {
                    object seal = null; foreach (var e2 in entries) if ((string)entryT.GetField("Label").GetValue(e2) == "Phoque") seal = e2;
                    string bt = (string)rfAsm.GetType("ResourceFinder.Discovery").GetMethod("BiomeText").Invoke(null, new[] { seal });
                    h.Check("Finder.biome de spawn des créatures", !string.IsNullOrEmpty(bt), "Phoque → " + bt);
                }
                h.Check("Catalogue.icônes explicites résolues", missingIcons.Count == 0, missingIcons.Count == 0 ? "toutes" : string.Join(", ", missingIcons));

                var prefab = ZNetScene.instance.GetPrefab("Boar");
                boar = UnityEngine.Object.Instantiate(prefab, player.transform.position + player.transform.forward * 6f, Quaternion.identity);
                var zdo = boar.GetComponent<ZNetView>().GetZDO();

                var finder = Activator.CreateInstance(finderT);
                finderT.GetMethod("Start").Invoke(finder, new[] { entry, (object)player.transform.position });
                var stateP = finderT.GetProperty("State"); var tick = finderT.GetMethod("Tick");
                float t = Time.time + 20f;
                while (stateP.GetValue(finder).ToString() != "Done" && Time.time < t) { tick.Invoke(finder, new object[] { player.transform.position }); yield return null; }
                var results = (IList)finderT.GetField("Results").GetValue(finder);
                int zones = (int)finderT.GetProperty("ZonesGenerated").GetValue(finder);
                object mine = null;
                foreach (var r in results) if ((ZDOID)r.GetType().GetField("Id").GetValue(r) == zdo.m_uid) mine = r;
                h.Check("Créatures.sanglier trouvé sans scan", stateP.GetValue(finder).ToString() == "Done" && mine != null && zones == 0,
                    $"état={stateP.GetValue(finder)}, résultats={results.Count}, zones={zones}, {finderT.GetProperty("Status").GetValue(finder)}");

                if (mine != null)
                {
                    var posF = mine.GetType().GetField("Pos");
                    bool dynamic = (bool)mine.GetType().GetProperty("Dynamic").GetValue(mine);
                    var before = (Vector3)posF.GetValue(mine);
                    boar.transform.position = before + new Vector3(15f, 0f, 0f);
                    yield return null; yield return null; // le ZDO reprend la position du transform (ZNetView/ZSyncTransform)
                    zdo.SetPosition(boar.transform.position);
                    bool exists = (bool)mine.GetType().GetMethod("StillExists").Invoke(mine, null);
                    var after = (Vector3)posF.GetValue(mine);
                    h.Check("Créatures.position suivie", dynamic && exists && Vector3.Distance(after, before) > 10f, $"dynamique={dynamic}, déplacement={Vector3.Distance(after, before):0.#} m");

                    ZNetScene.instance.Destroy(boar); boar = null;
                    yield return null;
                    bool gone = !(bool)mine.GetType().GetMethod("StillExists").Invoke(mine, null);
                    h.Check("Créatures.disparition détectée", gone);
                }

                // Découverte : cohérente avec les stats de kills / le trophée
                float kills = (float)discoveryT.GetMethod("Kills").Invoke(null, new object[] { "$enemy_boar" });
                bool discovered = (bool)discoveryT.GetMethod("IsDiscovered").Invoke(null, new[] { entry });
                bool seenBoar = (bool)discoveryT.GetMethod("Seen").Invoke(null, new[] { entry });
                bool expected = kills > 0f || player.IsMaterialKnown("$item_trophy_boar") || seenBoar;
                h.Check("Créatures.découverte = vu, tué ou trophée", discovered == expected, $"kills={kills}, trophée={player.IsMaterialKnown("$item_trophy_boar")}, vu={seenBoar}, découvert={discovered}");

                // Découverte par la vue : regarder un rocher de cuivre (viseur) ou voir une créature (barre de vie) suffit
                {
                    object copperE = null, deerE = null;
                    foreach (var e2 in entries) { string l2 = (string)entryT.GetField("Label").GetValue(e2); if (l2 == "Cuivre") copperE = e2; if (l2 == "Cerf") deerE = e2; }
                    var rock = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("rock4_copper"), player.transform.position + player.transform.forward * 12f + Vector3.up * 3f, Quaternion.identity);
                    yield return null;
                    discoveryT.GetMethod("OnHover").Invoke(null, new object[] { rock });
                    bool copperSeen = (bool)discoveryT.GetMethod("IsDiscovered").Invoke(null, new[] { copperE });
                    ZNetScene.instance.Destroy(rock);
                    discoveryT.GetMethod("MarkSeen").Invoke(null, new object[] { "Deer" });
                    bool deerSeen = (bool)discoveryT.GetMethod("IsDiscovered").Invoke(null, new[] { deerE });
                    h.Check("Découverte par la vue (viseur sur un rocher de cuivre, cerf aperçu)", copperSeen && deerSeen, $"cuivre={copperSeen}, cerf={deerSeen}");
                }
            }
            finally { if (boar != null) ZNetScene.instance.Destroy(boar); }

            // Capture de la fenêtre
            var rfT = rfAsm.GetType("ResourceFinder.Plugin");
            var rfInst = UnityEngine.Object.FindObjectOfType(rfT);
            var openF = rfT.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Static);
            if (!(bool)openF.GetValue(null)) rfT.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
            // Une recherche « Sanglier » avec deux sangliers à portée pour que résultats et couche soient remplis
            var boars = new List<GameObject>();
            for (int i = 0; i < 2; i++) boars.Add(UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Boar"), player.transform.position + player.transform.forward * (4.5f + 5f * i) + player.transform.right * 3f * i, Quaternion.identity));
            object boarEntry = null;
            foreach (var e in (IList)catalogT.GetField("Entries").GetValue(null)) if ((string)entryT.GetField("Label").GetValue(e) == "Sanglier") boarEntry = e;
            rfT.GetMethod("StartSearch", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, new[] { boarEntry });
            yield return new WaitForSecondsRealtime(2.5f);
            string shot = System.IO.Path.Combine(Paths.ConfigPath, "finder_window.png");
            ScreenCapture.CaptureScreenshot(shot);
            yield return new WaitForSecondsRealtime(1.5f);
            // Capture pendant un scan de zones (barre d'avancement) : minerai de cuivre, puis arrêt
            object copper = null;
            foreach (var e in (IList)catalogT.GetField("Entries").GetValue(null)) if ((string)entryT.GetField("Label").GetValue(e) == "Goudron") copper = e;
            if (copper != null)
            {
                rfT.GetMethod("StartSearch", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, new[] { copper });
                yield return new WaitForSecondsRealtime(1.5f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.ConfigPath, "finder_scan.png"));
                yield return new WaitForSecondsRealtime(0.5f);
                var finderField = rfT.GetField("_finder", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rfInst);
                finderT.GetMethod("Cancel").Invoke(finderField, null);
                rfT.GetMethod("StartSearch", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, new[] { boarEntry });
                yield return new WaitForSecondsRealtime(1f);
            }
            rfT.GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
            yield return new WaitForSecondsRealtime(0.6f); // fenêtre fermée : l'indicateur HUD vers la cible est visible
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.ConfigPath, "finder_hud.png"));
            yield return new WaitForSecondsRealtime(1.2f);
            // Cible lointaine (autel d'Eikthyr) : pastille en bord d'écran + repère au bord de la mini-carte
            rfT.GetMethod("SearchLabel").Invoke(null, new object[] { "Autel : Eikthyr" });
            yield return new WaitForSecondsRealtime(0.8f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.ConfigPath, "finder_hud_far.png"));
            yield return new WaitForSecondsRealtime(1f);
            rfT.GetMethod("StartSearch", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, new[] { boarEntry });
            yield return new WaitForSecondsRealtime(1f);
            // Une recherche réelle doit produire des épingles portant l'icône de la ressource (sinon toutes se ressemblent sur la carte)
            try
            {
                var layersT = rfAsm.GetType("ResourceFinder.Layers");
                var layerT = rfAsm.GetType("ResourceFinder.Layer");
                var allLayers = (IList)layersT.GetField("All").GetValue(null);
                object boarLayer = null;
                foreach (var l in allLayers) if ((string)layerT.GetField("Label").GetValue(l) == "Sanglier") boarLayer = l;
                var ic = boarLayer != null ? layerT.GetMethod("Icon").Invoke(boarLayer, null) as Sprite : null;
                string iconPrefab = boarLayer != null ? (string)layerT.GetField("IconPrefab").GetValue(boarLayer) : "(pas de couche)";
                h.Check("Créatures.icône des épingles de la carte", ic != null, $"couche « Sanglier », IconPrefab={iconPrefab}, icône={(ic != null ? ic.name : "null")}");
            }
            catch (Exception ex) { h.Check("Créatures.icône des épingles de la carte", false, ex.InnerException?.Message ?? ex.Message); }
            // Grande carte centrée sur la cible (ce que fait le bouton « Carte ») : les épingles du mod y sont visibles
            var tgt = rfT.GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rfInst);
            if (tgt != null && Minimap.instance != null)
            {
                rfT.GetMethod("ShowOnMap", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { (Vector3)tgt.GetType().GetField("Pos").GetValue(tgt) });
                yield return new WaitForSecondsRealtime(1f);
                // État exact des épingles au moment de la capture (c'est ce que l'œil voit sur la grande carte)
                try
                {
                    var layerT2 = rfAsm.GetType("ResourceFinder.Layer");
                    foreach (var l in (IList)rfAsm.GetType("ResourceFinder.Layers").GetField("All").GetValue(null))
                    {
                        foreach (var v in ((IDictionary)layerT2.GetField("Pins").GetValue(l)).Values)
                        {
                            var pd = v as Minimap.PinData;
                            Plugin.Log.LogInfo($"[TEST] carte, épingle « {layerT2.GetField("Label").GetValue(l)} » : type={pd.m_type}, m_icon={(pd.m_icon != null ? pd.m_icon.name : "null")}, ui={PinTests.SpriteName(pd, "m_uiElement")}, icon={PinTests.SpriteName(pd, "m_iconElement")}");
                            break;
                        }
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning("[TEST] état des épingles : " + ex.Message); }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.ConfigPath, "finder_map.png"));
                yield return new WaitForSecondsRealtime(1f);
                Minimap.instance.SetMapMode(Minimap.MapMode.Small);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            rfT.GetMethod("ClearAllPins", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
            foreach (var b in boars) if (b != null) ZNetScene.instance.Destroy(b); // un sanglier peut avoir été détruit entre-temps (chute, despawn)
            h.Check("Créatures.capture fenêtre", System.IO.File.Exists(shot), shot);
        }
    }
}
