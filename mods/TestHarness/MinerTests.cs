using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Mineur : un coup de pioche sur un filon de cuivre (bloc intact, puis filon en morceaux) fait plus de dégâts et, sur
    /// les morceaux, en touche plusieurs ; le même coup sur un rocher de pierre reste celui du jeu ; le mod éteint rend le
    /// jeu vanilla. Chaque étape est bornée dans le temps et ne suppose rien de la structure du prefab sans la vérifier.
    /// </summary>
    internal static class MinerTests
    {
        private static IList Areas(MineRock5 rock) => rock == null ? null : (IList)typeof(MineRock5).GetField("m_hitAreas", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rock);
        private static float Health(object area) => (float)area.GetType().GetField("m_health").GetValue(area);
        private static Collider Col(object area) => (Collider)area.GetType().GetField("m_collider").GetValue(area);

        private static HitData Pickaxe(Player player, Vector3 point, Collider col)
        {
            var hit = new HitData { m_point = point, m_hitCollider = col, m_toolTier = 10 };
            hit.m_damage.m_pickaxe = 10f;
            hit.SetAttacker(player);
            return hit;
        }

        /// <summary>Filon en morceaux posé devant le joueur, un coup de 10 sur le premier morceau : [morceaux touchés, dégâts sur le premier, morceaux].</summary>
        private static IEnumerator StrikeFragments(GameObject prefab, Player player, float[] outcome)
        {
            outcome[0] = -1;
            yield return new WaitForSeconds(0.5f); // le rocher du coup précédent, posé au même endroit, a le temps de disparaître
            // Posé au niveau du sol : en l'air, les morceaux non soutenus s'effondrent et le rocher disparaît pendant le test
            var pos = player.transform.position + player.transform.forward * 8f;
            if (ZoneSystem.instance != null) pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
            try
            {
                float t0 = Time.realtimeSinceStartup;
                MineRock5 rock = null; IList areas = null;
                while (Time.realtimeSinceStartup - t0 < 5f && go != null) { rock = go.GetComponent<MineRock5>(); areas = Areas(rock); if (areas != null && areas.Count > 0) break; yield return null; }
                if (go == null || rock == null || areas == null || areas.Count == 0) yield break;
                var before = new List<float>(); foreach (var a in areas) before.Add(Health(a));
                var first = areas[0];
                if (Col(first) == null) yield break;
                rock.Damage(Pickaxe(player, Col(first).bounds.center, Col(first)));
                // Relevé tout de suite (le RPC vers soi-même est immédiat), puis une image plus tard si rien n'a encore bougé ;
                // plus tard, le jeu retire les morceaux non soutenus et peut détruire le rocher
                int touched = 0;
                for (int pass = 0; pass < 2 && touched == 0; pass++)
                {
                    if (pass == 1) yield return null;
                    if (go == null || rock == null) break;
                    areas = Areas(rock);
                    touched = 0;
                    for (int i = 0; i < areas.Count && i < before.Count; i++) if (Health(areas[i]) < before[i] - 0.01f) touched++;
                }
                outcome[0] = touched;
                outcome[1] = areas != null && areas.Count > 0 ? before[0] - Mathf.Max(0f, Health(areas[0])) : 0f;
                outcome[2] = before.Count;
            }
            finally { if (go != null) ZNetScene.instance.Destroy(go); }
        }

        /// <summary>Bloc intact : un coup de 10, renvoie les PV perdus (lus dans ses données réseau), ou -1.</summary>
        private static IEnumerator StrikeBlock(GameObject prefab, Player player, float[] outcome)
        {
            outcome[0] = -1;
            var go = UnityEngine.Object.Instantiate(prefab, player.transform.position + player.transform.forward * 8f + Vector3.up * 0.2f, Quaternion.identity);
            try
            {
                yield return new WaitForSeconds(1f);
                var d = go != null ? go.GetComponent<Destructible>() : null;
                var nview = go != null ? go.GetComponent<ZNetView>() : null;
                if (d == null || nview == null || !nview.IsValid()) yield break;
                float before = nview.GetZDO().GetFloat(ZDOVars.s_health, d.m_health);
                var col = go.GetComponentInChildren<Collider>();
                d.Damage(Pickaxe(player, col != null ? col.bounds.center : go.transform.position, col));
                yield return new WaitForSeconds(0.6f);
                if (go == null || !nview.IsValid()) { outcome[0] = before; outcome[1] = 1f; yield break; } // cassé d'un coup
                outcome[0] = before - nview.GetZDO().GetFloat(ZDOVars.s_health, d.m_health);
            }
            finally { if (go != null) ZNetScene.instance.Destroy(go); }
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Miner");
            if (asm == null) { h.Check("Mineur.mod chargé", false, "assembly Miner absent"); yield break; }
            var pluginT = asm.GetType("Miner.Plugin");
            var enabled = (BepInEx.Configuration.ConfigEntry<bool>)pluginT.GetField("Enabled", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            bool prev = enabled.Value;
            float mult = (float)pluginT.GetMethod("Multiplier", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);

            // Le filon de cuivre tel que le monde le pose (bloc intact) et sa version en morceaux, relevée dans le jeu
            var block = ZNetScene.instance.GetPrefab("rock4_copper");
            var blockD = block != null ? block.GetComponent<Destructible>() : null;
            var frac = blockD != null ? (blockD.m_spawnWhenDamaged ?? blockD.m_spawnWhenDestroyed) : null;
            if (frac == null && block != null && block.GetComponent<MineRock5>() != null) frac = block;
            GameObject stone = null;
            foreach (var p in ZNetScene.instance.m_prefabs)
            {
                var mr = p != null ? p.GetComponent<MineRock5>() : null;
                if (mr?.m_dropItems?.m_drops == null || mr.m_dropItems.m_drops.Count == 0 || !p.name.StartsWith("rock", StringComparison.OrdinalIgnoreCase)) continue;
                if (mr.m_dropItems.m_drops.All(d => d.m_item != null && d.m_item.name == "Stone")) { stone = p; break; }
            }
            if (stone != null)
            {
                var isOre = asm.GetType("Miner.Patches").GetMethod("IsOre", BindingFlags.NonPublic | BindingFlags.Static);
                var smr = stone.GetComponent<MineRock5>();
                Plugin.Log.LogInfo($"[TEST] rocher de pierre {stone.name} : butin {string.Join(", ", smr.m_dropItems.m_drops.Select(d => d.m_item != null ? d.m_item.name : "?"))}, minerai selon le mod={isOre?.Invoke(null, new object[] { smr, smr.m_dropItems })}, composants {string.Join(", ", stone.GetComponents<Component>().Select(c => c.GetType().Name))}");
            }
            h.Check("Mineur.rochers de test trouvés", frac != null && frac.GetComponent<MineRock5>() != null && stone != null,
                $"bloc={(block != null ? block.name : "absent")} ({(blockD != null ? "intact" : "sans bloc")}), morceaux={(frac != null ? frac.name : "absent")}, pierre={(stone != null ? stone.name : "absente")}");
            if (frac == null || frac.GetComponent<MineRock5>() == null || stone == null) yield break;

            try
            {
                enabled.Value = false;
                var vanilla = new float[3]; yield return StrikeFragments(frac, player, vanilla);
                enabled.Value = true;
                var modded = new float[3]; yield return StrikeFragments(frac, player, modded);
                enabled.Value = false;
                var plainVanilla = new float[3]; yield return StrikeFragments(stone, player, plainVanilla);
                yield return new WaitForSeconds(0.5f);
                enabled.Value = true;
                var plain = new float[3]; yield return StrikeFragments(stone, player, plain);

                h.Check("Mineur.filon de cuivre : dégâts multipliés", vanilla[1] > 0f && (modded[1] >= vanilla[1] * (mult - 0.5f) || modded[1] >= vanilla[1] + 20f),
                    $"premier morceau : jeu {vanilla[1]:0.#}, mod {modded[1]:0.#} (×{mult:0.#} attendu, un morceau détruit plafonne à sa vie)");
                h.Check("Mineur.filon de cuivre : les morceaux voisins sont touchés", vanilla[0] == 1 && modded[0] >= 3,
                    $"morceaux touchés : jeu {vanilla[0]:0}, mod {modded[0]:0} (sur {modded[2]:0})");
                h.Check("Mineur.pierre ordinaire : même coup qu'avec le jeu seul", plain[0] >= 0 && plain[0] == plainVanilla[0] && Mathf.Abs(plain[1] - plainVanilla[1]) < 0.01f, $"rocher {stone.name} : jeu {plainVanilla[0]:0} morceau(x) / {plainVanilla[1]:0.#} dégâts, avec le mod {plain[0]:0} / {plain[1]:0.#}");

                // Bloc intact : s'il laisse place aux morceaux dès le premier coup (m_spawnWhenDamaged), c'est le filon en
                // morceaux qui compte (testé ci-dessus) ; sinon ses propres dégâts doivent être multipliés
                if (blockD != null && blockD.m_spawnWhenDamaged != null)
                    Plugin.Log.LogInfo($"[TEST] filon intact {block.name} : remplacé par {blockD.m_spawnWhenDamaged.name} dès le premier coup, rien de plus à accélérer");
                else if (blockD != null)
                {
                    enabled.Value = false;
                    var bv = new float[2]; yield return StrikeBlock(block, player, bv);
                    enabled.Value = true;
                    var bm = new float[2]; yield return StrikeBlock(block, player, bm);
                    if (bv[1] > 0f) Plugin.Log.LogInfo($"[TEST] filon intact {block.name} : cassé d'un seul coup même sans le mod ({bv[0]:0.#} PV), rien à accélérer");
                    else h.Check("Mineur.filon intact : dégâts multipliés aussi", bv[0] > 0f && (bm[1] > 0f || bm[0] >= bv[0] * (mult - 0.5f) || bm[0] >= bv[0] + 20f),
                        $"PV perdus au premier coup : jeu {bv[0]:0.#}, mod {bm[0]:0.#} (×{mult:0.#})");
                }
            }
            finally { enabled.Value = prev; }
        }
    }
}
