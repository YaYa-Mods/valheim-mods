using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Chambre funéraire (monde de test), le parcours d'un joueur : « Cibler » depuis le Guide puis Traque, arrivée à
    /// l'entrée, cœurs ramassés mais coffres pleins (le repère reste), entrée (la pastille désigne un coffre ou un cœur de
    /// CETTE chambre), tout vidé (un seul message, pas de recherches en boucle, la Traque passe à une autre chambre, une
    /// nouvelle recherche ne la propose plus). L'état de la chambre est ensuite restauré et le joueur ramené. Chaque
    /// attente est bornée ; le personnage est invulnérable le temps du test (squelettes de l'entrée).
    /// </summary>
    internal static class DungeonTests
    {
        private static int s_searches;
        private static readonly List<string> s_messages = new List<string>();
        public static void CountSearch() { s_searches++; }
        public static void CountMessage(string msg) { s_messages.Add(msg ?? ""); }

        private static float H(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>Objets de la base du monde (ZDO, chargés ou non) dans les secteurs autour d'un point.</summary>
        private static List<ZDO> BaseZdos(Vector3 center, int ring)
        {
            var list = new List<ZDO>();
            var find = AccessTools.Method(typeof(ZDOMan), "FindObjects");
            var z = ZoneSystem.GetZone(center);
            for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++) // FindObjects note les secteurs lus dans le dernier argument : un ensemble neuf à chaque secteur
                    find.Invoke(ZDOMan.instance, new object[] { new Vector2s(z.x + dx, z.y + dy), list, Activator.CreateInstance(find.GetParameters()[2].ParameterType) });
            return list;
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "ResourceFinder");
            var rfT = asm.GetType("ResourceFinder.Plugin");
            var inst = rfT.GetField("s_instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var trackingF = rfT.GetField("Tracking", BindingFlags.NonPublic | BindingFlags.Static);
            var targetF = rfT.GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance);
            var finderF = rfT.GetField("_finder", BindingFlags.NonPublic | BindingFlags.Instance);
            var shownP = rfT.GetProperty("Shown", BindingFlags.NonPublic | BindingFlags.Instance);
            var resultT = asm.GetType("ResourceFinder.Result");
            var posF = resultT.GetField("Pos"); var prefabF = resultT.GetField("Prefab");
            var forget = asm.GetType("ResourceFinder.Dungeons").GetMethod("Forget", BindingFlags.NonPublic | BindingFlags.Static);
            var layers = asm.GetType("ResourceFinder.Layers");
            string Desc(object r) => r == null ? "aucune" : $"{prefabF.GetValue(r)} ({((Vector3)posF.GetValue(r)).x:0},{((Vector3)posF.GetValue(r)).z:0})";
            Vector3 PosOf(object r) => (Vector3)posF.GetValue(r);
            var instances = (IDictionary)typeof(ZNetScene).GetField("m_instances", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ZNetScene.instance);
            bool SearchLabel() => (bool)rfT.GetMethod("SearchLabel").Invoke(null, new object[] { "Chambre funéraire" });
            IEnumerable<object> FinderResults() => ((IEnumerable)finderF.FieldType.GetField("Results").GetValue(finderF.GetValue(inst))).Cast<object>();

            var harmony = new Harmony("vmods.testharness.dungeontests");
            harmony.Patch(AccessTools.Method(rfT, "OnSearchDone"), postfix: new HarmonyMethod(typeof(DungeonTests).GetMethod(nameof(CountSearch))));
            harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.Message)), prefix: new HarmonyMethod(typeof(DungeonTests).GetMethod(nameof(CountMessage))));
            var home = player.transform.position; var homeRot = player.transform.rotation;
            bool god = player.InGodMode();
            player.SetGodMode(true);
            // État d'origine de ce qu'on vide, restauré à la fin : le monde de test ne s'appauvrit pas d'un run à l'autre
            var saved = new List<(ZDO z, bool picked, bool filled, byte[] items)>();
            try
            {
                // Comme le Guide : « Cibler » depuis le point de départ, puis Traque
                bool ok = SearchLabel();
                trackingF.SetValue(null, true);
                yield return new WaitForSecondsRealtime(1f);
                var target = targetF.GetValue(inst);
                h.Check("Donjon.cible posée par le Guide", ok && target != null, Desc(target));
                if (target == null) yield break;
                var entrance = PosOf(target);

                // Le jeu refuse une téléportation juste après l'apparition du personnage (délai de recharge) : on insiste, borné
                float tp = Time.realtimeSinceStartup;
                while (!player.TeleportTo(entrance + Vector3.up * 2f, homeRot, true) && Time.realtimeSinceStartup - tp < 15f) yield return new WaitForSecondsRealtime(0.5f);
                h.Check("Donjon.téléporté à l'entrée", Time.realtimeSinceStartup - tp < 15f, $"au bout de {Time.realtimeSinceStartup - tp:0.0} s");
                float t0 = Time.realtimeSinceStartup;
                // Arrivée et intérieur chargé : son générateur, ses coffres et ses cœurs (à ~5 000 m au-dessus de l'entrée)
                ZDO gen = null; var loot = new List<ZDO>(); int chests = 0, cores = 0, prevBased = -1; float stableSince = 0f;
                while (Time.realtimeSinceStartup - t0 < 60f)
                {
                    yield return new WaitForSecondsRealtime(1f);
                    if (player.IsTeleporting() || !ZoneSystem.instance.IsZoneLoaded(entrance)) continue;
                    var near = BaseZdos(entrance, 1); // base d'objets du monde (tous, chargés ou non) : le jeu instancie l'intérieur par à-coups
                    gen = null; float gd = 48f;
                    foreach (var key in near)
                    {
                        var z = key as ZDO;
                        if (z == null || !z.IsValid() || z.GetPosition().y < 1500f) continue;
                        var pf = ZNetScene.instance.GetPrefab(z.GetPrefab());
                        if (pf != null && pf.GetComponent<DungeonGenerator>() != null && H(z.GetPosition(), entrance) < gd) { gd = H(z.GetPosition(), entrance); gen = z; }
                    }
                    if (gen == null) continue;
                    loot.Clear(); chests = cores = 0;
                    foreach (var key in near)
                    {
                        var z = key as ZDO;
                        if (z == null || !z.IsValid() || z.GetPosition().y < 1500f || H(z.GetPosition(), gen.GetPosition()) > 44f) continue;
                        var pf = ZNetScene.instance.GetPrefab(z.GetPrefab());
                        if (pf == null) continue;
                        if (pf.GetComponent<Container>() != null) { loot.Add(z); chests++; }
                        else if (pf.name == "Pickable_SurtlingCoreStand") { loot.Add(z); cores++; }
                    }
                    // Les salles apparaissent une à une pendant plusieurs secondes : on attend que l'intérieur ne bouge plus
                    int based = 0;
                    foreach (var bz in near) if (bz != null && bz.IsValid() && bz.GetPosition().y > 1500f && H(bz.GetPosition(), gen.GetPosition()) <= 44f) based++;
                    if (based != prevBased) { prevBased = based; stableSince = Time.realtimeSinceStartup; }
                    if (chests > 0 && Time.realtimeSinceStartup - stableSince >= 5f) break;
                }
                h.Check("Donjon.intérieur chargé : générateur et coffres", gen != null && chests > 0, $"{chests} coffre(s), {cores} cœur(s), au bout de {Time.realtimeSinceStartup - t0:0} s");
                if (gen == null || chests == 0) yield break;
                foreach (var z in loot) saved.Add((z, z.GetBool(ZDOVars.s_picked, false), z.GetBool(ZDOVars.s_addedDefaultItems, false), z.GetByteArray(ZDOVars.s_items, null)));
                foreach (var z in loot)
                {
                    var pf = ZNetScene.instance.GetPrefab(z.GetPrefab());
                    var c = pf.GetComponent<Container>();
                    if (c == null) continue;
                    byte[] data = z.GetByteArray(ZDOVars.s_items, null) ?? new byte[0]; int n = -1; string err = "";
                    try { var inv = new Inventory("", null, Mathf.Max(1, c.m_width), Mathf.Max(1, c.m_height)); if (data.Length > 0) inv.Load(new ZPackage(data)); n = inv.NrOfItems(); } catch (Exception ex) { err = ex.Message; }
                    Plugin.Log.LogInfo($"[TEST] donjon, coffre {pf.name} : créateur={z.GetLong(ZDOVars.s_creator, 0L)}, rempli={z.GetBool(ZDOVars.s_addedDefaultItems, false)}, données={data.Length} octets, objets={n} {err}");
                }

                // Cœurs ramassés, coffres encore pleins : la chambre n'est pas vidée, le repère reste (le cas réel signalé)
                s_searches = 0; s_messages.Clear();
                foreach (var z in loot) if (ZNetScene.instance.GetPrefab(z.GetPrefab()).GetComponent<Pickable>() != null) z.Set(ZDOVars.s_picked, true);
                forget.Invoke(null, null);
                yield return new WaitForSecondsRealtime(2.5f);
                var tg = targetF.GetValue(inst);
                h.Check("Donjon.cœurs pris, coffres pleins : repère gardé", tg != null && H(PosOf(tg), entrance) < 1f && !s_messages.Any(m => m.Contains("Donjon")),
                    $"cible {Desc(tg)}, {s_searches} recherche(s), messages : {string.Join(" | ", s_messages.Distinct())}");

                // Dedans : la pastille désigne un coffre de CETTE chambre, pas l'entrée ni un intérieur voisin
                var chest = loot.First(z => ZNetScene.instance.GetPrefab(z.GetPrefab()).GetComponent<Container>() != null);
                float t2 = Time.realtimeSinceStartup;
                while (!player.TeleportTo(chest.GetPosition() + Vector3.up * 1f, homeRot, false) && Time.realtimeSinceStartup - t2 < 10f) yield return new WaitForSecondsRealtime(0.5f);
                while (player.IsTeleporting() && Time.realtimeSinceStartup - t2 < 10f) yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForSecondsRealtime(1.5f);
                var shown = shownP.GetValue(inst); tg = targetF.GetValue(inst);
                var entryObj = rfT.GetMethod("TargetEntry", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(inst, null);
                Plugin.Log.LogInfo($"[TEST] donjon, joueur ({player.transform.position.x:0},{player.transform.position.y:0},{player.transform.position.z:0}) : " + asm.GetType("ResourceFinder.Dungeons").GetMethod("Describe", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { entrance, entryObj, player.transform.position }));
                bool shownHere = shown != null && !ReferenceEquals(shown, tg) && PosOf(shown).y > 1500f && H(PosOf(shown), gen.GetPosition()) <= 44f;
                h.Check("Donjon.dedans : la pastille désigne ce qui reste dans cette chambre", shownHere, $"pastille {Desc(shown)} à {(shown != null ? H(PosOf(shown), gen.GetPosition()) : -1f):0} m du centre");

                // Tout vider : un seul message, pas de recherches en boucle, la Traque passe à une autre chambre
                // Relu maintenant : des salles ont pu apparaître depuis l'arrivée (le joueur est entré), elles comptent aussi
                s_searches = 0; s_messages.Clear();
                foreach (var z in BaseZdos(gen.GetPosition(), 1))
                {
                    if (z == null || !z.IsValid() || z.GetPosition().y < 1500f || H(z.GetPosition(), gen.GetPosition()) > 44f) continue;
                    var pf = ZNetScene.instance.GetPrefab(z.GetPrefab());
                    if (pf == null) continue;
                    var c = pf.GetComponent<Container>();
                    bool core = pf.name == "Pickable_SurtlingCoreStand";
                    if (c == null && !core) continue;
                    if (!saved.Any(s => s.z == z)) saved.Add((z, z.GetBool(ZDOVars.s_picked, false), z.GetBool(ZDOVars.s_addedDefaultItems, false), z.GetByteArray(ZDOVars.s_items, null)));
                    if (core) { z.Set(ZDOVars.s_picked, true); continue; }
                    var empty = new Inventory("", null, Mathf.Max(1, c.m_width), Mathf.Max(1, c.m_height));
                    var pkg = new ZPackage(); empty.Save(pkg);
                    z.Set(ZDOVars.s_addedDefaultItems, true); z.Set(ZDOVars.s_items, pkg.GetArray());
                }
                forget.Invoke(null, null);
                yield return new WaitForSecondsRealtime(6f); // le scanner attend lui aussi un intérieur stable (4 s) avant de conclure
                Plugin.Log.LogInfo($"[TEST] donjon, tout vidé : " + asm.GetType("ResourceFinder.Dungeons").GetMethod("Describe", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { entrance, entryObj, player.transform.position }));
                tg = targetF.GetValue(inst);
                int emptiedMsgs = s_messages.Count(m => m.Contains("Donjon"));
                h.Check("Donjon.tout vidé : un seul message, pas de boucle", emptiedMsgs == 1 && s_searches <= 1, $"{emptiedMsgs} message(s) « vidé », {s_searches} recherche(s) : {string.Join(" | ", s_messages.Distinct().Take(4))}");
                h.Check("Donjon.tout vidé : la Traque passe à une autre chambre", tg == null || H(PosOf(tg), entrance) > 1f, $"cible {Desc(tg)}");
                bool inLayer = ((IEnumerable)layers.GetField("All").GetValue(null)).Cast<object>()
                    .SelectMany(l => ((IEnumerable)l.GetType().GetField("Results").GetValue(l)).Cast<object>()).Any(r => H(PosOf(r), entrance) < 1f);
                h.Check("Donjon.tout vidé : plus d'épingle", !inLayer);

                // Une nouvelle recherche (Cibler encore) ne la propose plus
                trackingF.SetValue(null, false);
                SearchLabel();
                yield return new WaitForSecondsRealtime(0.5f);
                bool proposed = FinderResults().Any(r => H(PosOf(r), entrance) < 1f);
                h.Check("Donjon.nouvelle recherche : chambre vidée écartée", !proposed, string.Join(", ", FinderResults().Select(Desc)));
            }
            finally
            {
                foreach (var (z, picked, filled, items) in saved)
                {
                    if (z == null || !z.IsValid()) continue;
                    z.Set(ZDOVars.s_picked, picked); z.Set(ZDOVars.s_addedDefaultItems, filled); if (items != null) z.Set(ZDOVars.s_items, items);
                }
                forget.Invoke(null, null);
                trackingF.SetValue(null, false);
                targetF.SetValue(inst, null);
                harmony.UnpatchSelf();
                player.TeleportTo(home + Vector3.up * 0.5f, homeRot, true);
            }
            float t1 = Time.realtimeSinceStartup;
            // Retour refusé (recharge après la téléportation précédente) : on redemande jusqu'à être rentré, borné
            while (!player.IsTeleporting() && Vector3.Distance(player.transform.position, home) > 20f && Time.realtimeSinceStartup - t1 < 15f)
            {
                player.TeleportTo(home + Vector3.up * 0.5f, homeRot, true);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            while ((player.IsTeleporting() || !ZoneSystem.instance.IsZoneLoaded(home)) && Time.realtimeSinceStartup - t1 < 40f) yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(1f);
            player.SetGodMode(god);
        }
    }
}
