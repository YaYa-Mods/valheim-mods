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
    /// Recherche réelle de chaque entrée du catalogue, de bout en bout :
    ///  - chaque prefab (ressource, arbre, créature, nid…) est instancié devant le joueur, la recherche est lancée et doit
    ///    renvoyer ce ZDO précis (hachage, itération des ZDO, résultat, position) ; puis il est détruit et le résultat doit
    ///    être vu comme disparu ;
    ///  - chaque entrée « lieu » doit donner au moins un résultat immédiat (lieux connus pour toute la carte).
    /// La génération de zones est coupée pendant le test (MaxScanRadius = 0) : on ne teste pas la carte, on teste le mod.
    /// Rapport détaillé : config/catalog_search.txt.
    /// </summary>
    internal static class CatalogSearchTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        public static IEnumerator Run(Plugin h, Player player)
        {
            var rfAsm = Asm("ResourceFinder");
            var finderT = rfAsm.GetType("ResourceFinder.Finder");
            var entryT = rfAsm.GetType("ResourceFinder.ResourceEntry");
            var pluginT = rfAsm.GetType("ResourceFinder.Plugin");
            var entries = (IList)rfAsm.GetType("ResourceFinder.Catalog").GetField("Entries").GetValue(null);
            var maxScan = (BepInEx.Configuration.ConfigEntry<float>)pluginT.GetField("MaxScanRadius", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            float prevScan = maxScan.Value; maxScan.Value = 0f;
            var stateP = finderT.GetProperty("State"); var tick = finderT.GetMethod("Tick"); var start = finderT.GetMethod("Start");
            var resultsF = finderT.GetField("Results");

            var report = new System.Text.StringBuilder();
            var failures = new List<string>();
            int prefabsOk = 0, prefabsTotal = 0, locationsOk = 0, locationsTotal = 0;
            var origin = player.transform.position + player.transform.forward * 8f;

            foreach (var e in entries)
            {
                if (entryT.GetField("Category").GetValue(e).ToString() == "Materiau") continue; // vérifiées à part (MaterialTests)
                string label = (string)entryT.GetField("Label").GetValue(e);
                var prefabs = (string[])entryT.GetField("Prefabs").GetValue(e);
                var locations = (string[])entryT.GetField("Locations").GetValue(e);

                // ---- lieux : résultat immédiat attendu
                if (locations.Length > 0)
                {
                    locationsTotal++;
                    var finder = Activator.CreateInstance(finderT);
                    // entrée « lieux seulement » pour ne pas dépendre des prefabs
                    var locEntry = Activator.CreateInstance(entryT, label, null, locations, null, Enum.ToObject(typeof(Heightmap.Biome), 0));
                    start.Invoke(finder, new[] { locEntry, (object)player.transform.position });
                    float t = Time.time + 5f;
                    while (stateP.GetValue(finder).ToString() != "Done" && Time.time < t) { tick.Invoke(finder, new object[] { player.transform.position }); yield return null; }
                    int n = ((IList)resultsF.GetValue(finder)).Count;
                    if (n > 0) locationsOk++; else failures.Add($"{label} : lieux {string.Join("/", locations)} → 0 résultat");
                    report.AppendLine($"{label} | lieux {string.Join("/", locations)} : {n} résultat(s)");
                }

                // ---- prefabs : posé devant le joueur, doit être trouvé, puis disparaître
                foreach (var p in prefabs)
                {
                    prefabsTotal++;
                    var prefab = ZNetScene.instance.GetPrefab(p);
                    if (prefab == null) { failures.Add($"{label} : prefab {p} absent"); report.AppendLine($"{label} | {p} : ABSENT"); continue; }
                    GameObject go = null; ZDOID id = ZDOID.None;
                    try
                    {
                        go = UnityEngine.Object.Instantiate(prefab, origin + Vector3.up * 0.5f, Quaternion.identity);
                        var nv = go.GetComponent<ZNetView>();
                        if (nv == null || nv.GetZDO() == null) { failures.Add($"{label} : {p} sans ZNetView/ZDO"); report.AppendLine($"{label} | {p} : pas de ZDO"); UnityEngine.Object.Destroy(go); continue; }
                        id = nv.GetZDO().m_uid;
                    }
                    catch (Exception ex) { failures.Add($"{label} : {p} instanciation {ex.GetType().Name}"); report.AppendLine($"{label} | {p} : exception {ex.Message}"); if (go != null) UnityEngine.Object.Destroy(go); continue; }
                    yield return null;

                    var finder = Activator.CreateInstance(finderT);
                    var one = Activator.CreateInstance(entryT, label, new[] { p }, null, null, Enum.ToObject(typeof(Heightmap.Biome), 0));
                    start.Invoke(finder, new[] { one, (object)player.transform.position });
                    float t = Time.time + 8f;
                    while (stateP.GetValue(finder).ToString() != "Done" && Time.time < t) { tick.Invoke(finder, new object[] { player.transform.position }); yield return null; }
                    object mine = null;
                    foreach (var r in (IList)resultsF.GetValue(finder)) if ((ZDOID)r.GetType().GetField("Id").GetValue(r) == id) mine = r;
                    bool found = mine != null;
                    bool gone = false;
                    if (found)
                    {
                        // Disparition : destruction réseau (ZDO retiré) → StillExists faux
                        ZNetScene.instance.Destroy(go); go = null;
                        yield return null;
                        gone = !(bool)mine.GetType().GetMethod("StillExists").Invoke(mine, null);
                    }
                    if (go != null) ZNetScene.instance.Destroy(go);
                    if (found && gone) prefabsOk++;
                    else failures.Add($"{label} : {p} {(found ? "trouvé mais pas vu disparaître" : "posé devant le joueur mais NON trouvé")}");
                    report.AppendLine($"{label} | {p} : trouvé={found}, disparu={gone}");
                }
            }
            maxScan.Value = prevScan;
            string path = System.IO.Path.Combine(Paths.ConfigPath, "catalog_search.txt");
            System.IO.File.WriteAllText(path, report.ToString(), System.Text.Encoding.UTF8);
            h.Check("Catalogue.chaque prefab posé est trouvé puis vu disparaître", prefabsOk == prefabsTotal, $"{prefabsOk}/{prefabsTotal} prefabs" + (failures.Count > 0 ? ", " + string.Join(" ; ", failures.Take(12)) : ""));
            h.Check("Catalogue.chaque lieu donne un résultat immédiat", locationsOk == locationsTotal, $"{locationsOk}/{locationsTotal} entrées lieux");
        }
    }
}
