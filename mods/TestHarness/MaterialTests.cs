using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Scanner, catégorie « Matériaux » : les sources de chaque objet sont lues dans les tables de butin du jeu. Cas réel
    /// qui a motivé l'ajout : des fragments d'os ramassés, besoin d'en trouver d'autres, et aucune entrée du scanner.
    /// Taper « os » doit donner les fragments d'os, leurs sources doivent comprendre les squelettes et les ossuaires,
    /// l'entrée n'apparaît qu'une fois l'objet ramassé, et une vraie recherche trouve un squelette posé à côté.
    /// </summary>
    internal static class MaterialTests
    {
        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "ResourceFinder");
            var rfT = asm.GetType("ResourceFinder.Plugin");
            var entryT = asm.GetType("ResourceFinder.ResourceEntry");
            asm.GetType("ResourceFinder.ItemSources").GetMethod("EnsureBuilt").Invoke(null, null);
            var entries = (IList)asm.GetType("ResourceFinder.Catalog").GetField("Entries").GetValue(null);
            var mats = new List<object>();
            foreach (var e in entries) if (entryT.GetField("Category").GetValue(e).ToString() == "Materiau") mats.Add(e);
            string Label(object e) => (string)entryT.GetField("Label").GetValue(e);
            string[] Prefabs(object e) => (string[])entryT.GetField("Prefabs").GetValue(e);
            string Item(object e) => (string)entryT.GetField("ItemName").GetValue(e);

            var bones = mats.FirstOrDefault(e => Item(e) == "$item_bonefragments");
            h.Check("Matériaux.entrées tirées des tables de butin", mats.Count >= 10 && bones != null, $"{mats.Count} entrées, dont : {string.Join(", ", mats.Take(12).Select(Label))}");
            if (bones == null) yield break;
            var src = Prefabs(bones);
            h.Check("Matériaux.os : squelettes et ossuaires parmi les sources", src.Contains("Skeleton") && src.Any(s => s.StartsWith("BonePile", StringComparison.Ordinal)), $"« {Label(bones)} » ← {string.Join(", ", src)}");

            // Relevé des sources des os : nature, visibilité, où le jeu les place (pour comprendre un emplacement « vide »)
            var zns = ZNetScene.instance;
            try
            {
                var biomesOf = new Dictionary<string, HashSet<string>>();
                foreach (var v in ZoneSystem.instance.m_vegetation) if (v?.m_prefab != null && v.m_enable) { if (!biomesOf.TryGetValue(v.m_prefab.name, out var s)) biomesOf[v.m_prefab.name] = s = new HashSet<string>(); s.Add("végétation " + v.m_biome); }
                foreach (var ss in UnityEngine.Object.FindObjectsOfType<SpawnSystem>())
                    foreach (var list in ss.m_spawnLists)
                        foreach (var sd in list.m_spawners)
                            if (sd?.m_prefab != null && sd.m_enabled) { if (!biomesOf.TryGetValue(sd.m_prefab.name, out var s)) biomesOf[sd.m_prefab.name] = s = new HashSet<string>(); s.Add("apparition " + sd.m_biome + (sd.m_spawnAtNight && !sd.m_spawnAtDay ? " (nuit)" : "")); }
                var sb = new System.Text.StringBuilder("# Sources des fragments d'os\n");
                foreach (var p in src)
                {
                    var go = zns.GetPrefab(p);
                    if (go == null) { sb.AppendLine($"{p} : ABSENT"); continue; }
                    var comps = string.Join(",", go.GetComponents<Component>().Select(c => c.GetType().Name).Where(n => n != "Transform" && n != "ZNetView" && n != "ZSyncTransform" && n != "LODGroup"));
                    int renderers = go.GetComponentsInChildren<Renderer>(true).Length;
                    var ch = go.GetComponent<Character>();
                    string where = biomesOf.TryGetValue(p, out var w) ? string.Join(" ; ", w) : "aucune apparition ni végétation (lieux / générateurs seulement)";
                    sb.AppendLine($"{p} : {comps} | rendus={renderers}{(ch != null ? $" | créature « {Localization.instance.Localize(ch.m_name)} »" : "")} | {where}");
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "bone_sources.txt"), sb.ToString());
                Plugin.Log.LogInfo("[TEST] sources des os relevées dans bone_sources.txt");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TEST] relevé des sources des os : " + ex.Message); }

            // Aucune source n'est une construction du joueur ni un objet posé au sol
            var bad = mats.SelectMany(e => Prefabs(e)).Distinct().Where(p => { var go = zns.GetPrefab(p); return go == null || go.GetComponent<Piece>() != null || go.GetComponent<ItemDrop>() != null; }).ToList();
            h.Check("Matériaux.sources : que des objets du monde", bad.Count == 0, bad.Count == 0 ? "aucune construction ni objet au sol" : string.Join(", ", bad.Take(10)));

            var knownF = typeof(Player).GetField("m_knownMaterial", BindingFlags.NonPublic | BindingFlags.Instance);
            var known = knownF?.GetValue(player) as HashSet<string>;
            bool had = known != null && known.Contains("$item_bonefragments");
            var disc = asm.GetType("ResourceFinder.Discovery");
            var cacheF = disc.GetField("s_cache", BindingFlags.NonPublic | BindingFlags.Static);

            // Recherche tapée « os » avec des fragments d'os déjà ramassés (le cas réel) → les fragments d'os, pas un autre « os »
            known?.Add("$item_bonefragments");
            (cacheF?.GetValue(null) as IDictionary)?.Clear();
            var resolve = rfT.GetMethod("ResolveSearch", BindingFlags.NonPublic | BindingFlags.Static);
            var hit = resolve.Invoke(null, new object[] { "os" });
            if (!had) known?.Remove("$item_bonefragments");
            (cacheF?.GetValue(null) as IDictionary)?.Clear();
            h.Check("Matériaux.« os » tapé → fragments d'os", hit == bones, $"« os » → {(hit == null ? "rien" : Label(hit))}");

            // Découverte : l'entrée n'apparaît qu'une fois l'objet ramassé
            var computeWithoutSeen = disc.GetMethod("ComputeWithoutSeen", BindingFlags.NonPublic | BindingFlags.Static);
            known?.Remove("$item_bonefragments");
            bool before = (bool)computeWithoutSeen.Invoke(null, new[] { bones });
            known?.Add("$item_bonefragments");
            bool after = (bool)computeWithoutSeen.Invoke(null, new[] { bones });
            if (!had) known?.Remove("$item_bonefragments");
            (cacheF?.GetValue(null) as IDictionary)?.Clear();
            h.Check("Matériaux.visible une fois l'objet ramassé", known != null && !before && after, $"jamais ramassé → {before}, ramassé → {after}");

            // Recherche réelle : un squelette posé à 12 m est trouvé
            var skel = UnityEngine.Object.Instantiate(zns.GetPrefab("Skeleton"), player.transform.position + player.transform.right * 12f, Quaternion.identity);
            yield return new WaitForSeconds(1f);
            rfT.GetMethod("SearchLabel").Invoke(null, new object[] { Label(bones) });
            var inst = UnityEngine.Object.FindObjectOfType(rfT);
            var finder = rfT.GetField("_finder", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(inst);
            var finderT = finder.GetType();
            float t0 = Time.realtimeSinceStartup;
            while (finderT.GetProperty("State").GetValue(finder).ToString() != "Done" && Time.realtimeSinceStartup - t0 < 20f) yield return null;
            var results = (IList)finderT.GetField("Results").GetValue(finder);
            bool found = false;
            foreach (var r in results) { var pos = (Vector3)r.GetType().GetField("Pos").GetValue(r); if (Vector3.Distance(pos, skel.transform.position) < 3f) found = true; }
            h.Check("Matériaux.recherche des os : le squelette voisin est trouvé", found, $"{results.Count} résultat(s), état {finderT.GetProperty("State").GetValue(finder)}");

            // Cas réel d'un emplacement « vide » : pas de piège enterré parmi les sources, et un squelette dont il ne reste
            // qu'un souvenir (ZDO sans objet chargé, comme un squelette de nuit parti au jour) n'est pas proposé
            h.Check("Matériaux.os : pas de piège enterré parmi les sources", !src.Any(s => s.IndexOf("Surprise", StringComparison.OrdinalIgnoreCase) >= 0), string.Join(", ", src.Where(s => s.Contains("Surprise"))));
            var ghost = ZDOMan.instance.CreateNewZDO(player.transform.position + player.transform.forward * 30f + Vector3.up * 1000f, "Skeleton".GetStableHashCode());
            ghost.SetPrefab("Skeleton".GetStableHashCode());
            ghost.SetPosition(player.transform.position + player.transform.forward * 30f);
            var addZdo = finderT.GetMethod("AddZdo", BindingFlags.NonPublic | BindingFlags.Instance);
            int beforeGhost = results.Count;
            addZdo.Invoke(finder, new object[] { ghost });
            bool ghostListed = false;
            foreach (var r in (IList)finderT.GetField("Results").GetValue(finder)) if (((ZDOID)r.GetType().GetField("Id").GetValue(r)) == ghost.m_uid) ghostListed = true;
            ZDOMan.instance.DestroyZDO(ghost);
            h.Check("Matériaux.squelette non chargé (souvenir) ignoré", !ghostListed, $"souvenir proposé={ghostListed}");

            // La source est nommée : dans la liste et sur la pastille (« Fragments d'os · Squelette »)
            var displayName = rfT.GetMethod("DisplayName", BindingFlags.NonPublic | BindingFlags.Static);
            string skelName = null;
            foreach (var r in results) if ((string)r.GetType().GetField("Prefab").GetValue(r) == "Skeleton") skelName = (string)displayName.Invoke(null, new[] { r });
            var pileProbe = Activator.CreateInstance(results.Count > 0 ? results[0].GetType() : asm.GetType("ResourceFinder.Result"));
            pileProbe.GetType().GetField("Prefab").SetValue(pileProbe, "BonePileSpawner");
            string pileName = (string)displayName.Invoke(null, new[] { pileProbe });
            h.Check("Matériaux.sources nommées : squelette, tas d'os", skelName == "Squelette" && !string.IsNullOrEmpty(pileName) && pileName != "Fragments d'os" && !pileName.Contains("Bone"), $"squelette « {skelName} », ossuaire « {pileName} »");
            ZNetScene.instance.Destroy(skel);
            var clear = rfT.GetMethod("ClearAllPinsConfirmed", BindingFlags.NonPublic | BindingFlags.Instance); clear?.Invoke(inst, null); clear?.Invoke(inst, null); // deux appuis : confirmation
        }
    }
}
