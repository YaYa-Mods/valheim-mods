using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Vérifie chaque entrée du catalogue du scanner contre le jeu : le prefab existe-t-il, et où le monde le place-t-il
    /// (table de végétation, lieux, salles de donjon, listes de spawn des créatures) ? Une entrée dont aucun prefab n'est
    /// placé nulle part ne sera jamais trouvée (cas de « goldvein »). Rapport : config/catalog_audit.txt ; un test
    /// échoue si une entrée est introuvable ou si une icône explicite ne se résout pas.
    /// </summary>
    internal static class CatalogAudit
    {
        public static void Run(Plugin h)
        {
            var sb = new StringBuilder();
            var problems = new List<string>();
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "ResourceFinder");
                var entryT = asm.GetType("ResourceFinder.ResourceEntry");
                var entries = (IList)asm.GetType("ResourceFinder.Catalog").GetField("Entries").GetValue(null);
                var zs = ZoneSystem.instance; var zns = ZNetScene.instance;

                // Index : prefab → placements
                var veg = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in zs.m_vegetation)
                    if (v?.m_prefab != null && v.m_enable)
                    {
                        if (!veg.TryGetValue(v.m_prefab.name, out var l)) veg[v.m_prefab.name] = l = new List<string>();
                        l.Add($"{v.m_biome} alt {v.m_minAltitude:0}..{v.m_maxAltitude:0}");
                    }
                var spawn = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var ss in UnityEngine.Object.FindObjectsOfType<SpawnSystem>())
                    foreach (var list in ss.m_spawnLists) foreach (var sd in list.m_spawners)
                        if (sd.m_prefab != null && sd.m_enabled)
                        {
                            if (!spawn.TryGetValue(sd.m_prefab.name, out var l)) spawn[sd.m_prefab.name] = l = new List<string>();
                            l.Add($"{sd.m_biome}{(string.IsNullOrEmpty(sd.m_requiredGlobalKey) ? "" : " clé " + sd.m_requiredGlobalKey)}");
                        }
                // Contenu des lieux et des salles de donjon : noms de tous les enfants (un passage, en cache)
                var inLocations = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); // child name → lieux
                void Index(GameObject go, string owner)
                {
                    foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    {
                        string n = t.name.Replace("(Clone)", "").Trim();
                        int paren = n.IndexOf(" ("); if (paren > 0) n = n.Substring(0, paren);
                        if (!inLocations.TryGetValue(n, out var set)) inLocations[n] = set = new HashSet<string>();
                        set.Add(owner);
                    }
                }
                var locInstances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in zs.m_locationInstances) { string n = kv.Value.m_location?.m_prefabName ?? ""; locInstances[n] = locInstances.TryGetValue(n, out int c) ? c + 1 : 1; }
                foreach (var loc in zs.m_locations)
                {
                    if (loc?.m_prefab == null || !loc.m_enable) continue;
                    GameObject go = null; try { loc.m_prefab.Load(); go = loc.m_prefab.Asset; } catch { }
                    if (go != null) Index(go, "lieu " + loc.m_prefabName);
                }
                foreach (var rd in DungeonDB.GetRooms())
                {
                    if (rd?.m_prefab == null) continue;
                    GameObject go = null; try { rd.m_prefab.Load(); go = rd.m_prefab.Asset; } catch { }
                    if (go != null) Index(go, "salle " + rd.m_prefab.Name);
                }

                sb.AppendLine("# Audit du catalogue du scanner : existence et placement de chaque prefab / lieu");
                foreach (var e in entries)
                {
                    if (entryT.GetField("Category").GetValue(e).ToString() == "Materiau") continue; // tirées des tables de butin, vérifiées à part (MaterialTests)
                    string label = (string)entryT.GetField("Label").GetValue(e);
                    var prefabs = (string[])entryT.GetField("Prefabs").GetValue(e);
                    var locations = (string[])entryT.GetField("Locations").GetValue(e);
                    string icon = (string)entryT.GetField("Icon").GetValue(e);
                    string category = entryT.GetField("Category").GetValue(e).ToString();
                    var lines = new List<string>();
                    bool anyPlaced = false, anyMissing = false;
                    foreach (var p in prefabs)
                    {
                        bool exists = zns.GetPrefab(p) != null;
                        if (!exists) { lines.Add($"  PREFAB ABSENT : {p}"); anyMissing = true; continue; }
                        var where = new List<string>();
                        if (veg.TryGetValue(p, out var vl)) where.Add("végétation " + string.Join(" / ", vl.Distinct().Take(4)));
                        if (spawn.TryGetValue(p, out var sl)) where.Add("spawn " + string.Join(" / ", sl.Distinct().Take(4)));
                        if (inLocations.TryGetValue(p, out var ll)) where.Add("dans " + string.Join(", ", ll.Take(4)) + (ll.Count > 4 ? $" (+{ll.Count - 4})" : ""));
                        var go = zns.GetPrefab(p);
                        if (go.GetComponent<Character>() != null && where.Count == 0) where.Add("créature (spawner/événement seulement)");
                        if (where.Count > 0) anyPlaced = true;
                        lines.Add($"  {p} : {(where.Count > 0 ? string.Join(" ; ", where) : "NULLE PART")}");
                    }
                    foreach (var l in locations)
                    {
                        var matches = zs.m_locations.Where(x => x != null && x.m_enable && (x.m_prefabName ?? "").IndexOf(l, StringComparison.OrdinalIgnoreCase) >= 0).Select(x => x.m_prefabName).Distinct().ToList();
                        int inst = 0; foreach (var m in matches) if (locInstances.TryGetValue(m, out int c)) inst += c;
                        if (matches.Count > 0 && inst > 0) anyPlaced = true; else anyMissing = true;
                        lines.Add($"  lieu « {l} » : {(matches.Count > 0 ? string.Join(", ", matches.Take(5)) + $", {inst} instance(s) dans ce monde" : "AUCUN LIEU NE CORRESPOND")}");
                    }
                    bool iconOk = string.IsNullOrEmpty(icon) || ObjectDB.instance.GetItemPrefab(icon) != null;
                    if (!iconOk) lines.Add($"  ICÔNE ABSENTE : {icon}");
                    bool ok = anyPlaced && iconOk && (!anyMissing || anyPlaced);
                    // Une créature n'est pas forcément dans les listes de spawn (événements, spawners de lieux) : signalée mais pas en échec
                    bool creature = category == "Creature";
                    if (!anyPlaced && !creature) problems.Add(label);
                    if (!iconOk) problems.Add(label + " (icône)");
                    sb.AppendLine($"{(ok ? "ok " : creature && !anyPlaced ? "?  " : "!! ")}{label} [{category}]");
                    foreach (var l in lines) sb.AppendLine(l);
                }
            }
            catch (Exception ex) { sb.AppendLine("ERREUR : " + ex); problems.Add("exception " + ex.Message); }
            // Recherche libre pour les entrées douteuses : toute végétation (même désactivée) et tout enfant de lieu/salle dont le nom contient le motif
            try
            {
                sb.AppendLine();
                sb.AppendLine("# Recherche libre (motifs) : végétation même désactivée, enfants de lieux et de salles");
                foreach (var pat in new[] { "flametal", "meteor", "Rockstand", "pillar", "spire", "lava", "Pickable_Oat", "onion", "bogiron" })
                {
                    var found = new List<string>();
                    foreach (var v in ZoneSystem.instance.m_vegetation)
                        if (v?.m_prefab != null && v.m_prefab.name.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0)
                            found.Add($"végétation {v.m_prefab.name} ({v.m_biome}, activé={v.m_enable}, alt {v.m_minAltitude:0}..{v.m_maxAltitude:0})");
                    foreach (var loc in ZoneSystem.instance.m_locations)
                    {
                        if (loc?.m_prefab == null) continue;
                        GameObject go = null; try { loc.m_prefab.Load(); go = loc.m_prefab.Asset; } catch { }
                        if (go == null) continue;
                        var names = go.GetComponentsInChildren<Transform>(true).Select(t => t.name).Where(n => n.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0).Distinct().Take(3).ToList();
                        if (names.Count > 0) found.Add($"lieu {loc.m_prefabName} (activé={loc.m_enable}) : {string.Join(", ", names)}");
                    }
                    foreach (var rd in DungeonDB.GetRooms())
                    {
                        if (rd?.m_prefab == null) continue;
                        GameObject go = null; try { rd.m_prefab.Load(); go = rd.m_prefab.Asset; } catch { }
                        if (go == null) continue;
                        var names = go.GetComponentsInChildren<Transform>(true).Select(t => t.name).Where(n => n.IndexOf(pat, StringComparison.OrdinalIgnoreCase) >= 0).Distinct().Take(3).ToList();
                        if (names.Count > 0) found.Add($"salle {rd.m_prefab.Name} : {string.Join(", ", names)}");
                    }
                    sb.AppendLine($"{pat} : {(found.Count > 0 ? string.Join(" | ", found.Take(30)) : "rien")}");
                }
            }
            catch (Exception ex) { sb.AppendLine("ERREUR recherche libre : " + ex.Message); }
            string path = System.IO.Path.Combine(Paths.ConfigPath, "catalog_audit.txt");
            System.IO.File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            h.Check("Catalogue.toutes les entrées existent et sont placées dans le monde", problems.Count == 0, problems.Count == 0 ? path : string.Join(" ; ", problems));
        }
    }
}
