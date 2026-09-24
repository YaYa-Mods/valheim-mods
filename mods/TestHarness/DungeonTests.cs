using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Chambre funéraire (monde de test) : le joueur est emmené à l'entrée de la plus proche ; son intérieur, une fois
    /// généré, contient des coffres ou des cœurs de surtling, le repère reste ; à l'intérieur, la pastille désigne ce qui
    /// reste ; tout vidé (cœurs ramassés, coffres vides), le repère n'a plus lieu d'être. Joueur ramené ensuite à son
    /// point de départ. Chaque attente est bornée.
    /// </summary>
    internal static class DungeonTests
    {
        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "ResourceFinder");
            var dungeons = asm.GetType("ResourceFinder.Dungeons");
            var resultT = asm.GetType("ResourceFinder.Result");
            var entryT = asm.GetType("ResourceFinder.ResourceEntry");
            object entry = null;
            foreach (var e in (IList)asm.GetType("ResourceFinder.Catalog").GetField("Entries").GetValue(null)) if ((string)entryT.GetField("Label").GetValue(e) == "Chambre funéraire") entry = e;
            var emptied = dungeons.GetMethod("Emptied", BindingFlags.Public | BindingFlags.Static);
            var nearestInside = dungeons.GetMethod("NearestInside", BindingFlags.Public | BindingFlags.Static);
            var forget = dungeons.GetMethod("Forget", BindingFlags.NonPublic | BindingFlags.Static);

            // Chambre funéraire la plus proche du point de départ
            var home = player.transform.position; var homeRot = player.transform.rotation;
            Vector3? crypt = null; float best = float.MaxValue;
            foreach (var kv in ZoneSystem.instance.m_locationInstances)
            {
                string n = kv.Value.m_location?.m_prefabName ?? "";
                if (!n.StartsWith("Crypt", StringComparison.Ordinal)) continue;
                float d = Vector3.Distance(kv.Value.m_position, home);
                if (d < best) { best = d; crypt = kv.Value.m_position; }
            }
            h.Check("Donjon.chambre funéraire trouvée", entry != null && crypt != null, crypt == null ? "aucune dans le monde de test" : $"à {best:0} m du départ");
            if (entry == null || crypt == null) yield break;

            var result = Activator.CreateInstance(resultT);
            resultT.GetField("Pos").SetValue(result, crypt.Value);
            resultT.GetField("Prefab").SetValue(result, "Crypt");
            resultT.GetField("IsLocation").SetValue(result, true);

            try
            {
                player.TeleportTo(crypt.Value + Vector3.up * 2f, homeRot, true);
                float t0 = Time.realtimeSinceStartup;
                // Arrivée, zone chargée, intérieur généré (des objets à ~5 000 m au-dessus de l'entrée)
                bool generated = false;
                while (Time.realtimeSinceStartup - t0 < 60f)
                {
                    yield return new WaitForSecondsRealtime(1f);
                    if (player.IsTeleporting() || !ZoneSystem.instance.IsZoneLoaded(crypt.Value)) continue;
                    forget.Invoke(null, null);
                    if (!(bool)emptied.Invoke(null, new[] { result, entry }) && nearestInside.Invoke(null, new object[] { result, entry, crypt.Value + Vector3.up * 5000f }) != null) { generated = true; break; }
                }
                h.Check("Donjon.intérieur relevé : il reste à prendre, repère gardé", generated, $"au bout de {Time.realtimeSinceStartup - t0:0} s");
                if (!generated) yield break;

                // À l'intérieur : la pastille désigne ce qui reste (un coffre ou un cœur), plus l'entrée
                var inside = (ZDO)nearestInside.Invoke(null, new object[] { result, entry, crypt.Value + Vector3.up * 5000f });
                var insidePrefab = inside != null ? ZNetScene.instance.GetPrefab(inside.GetPrefab()) : null;
                h.Check("Donjon.à l'intérieur : guidage vers ce qui reste", inside != null && inside.GetPosition().y > 1500f, insidePrefab != null ? $"{insidePrefab.name} à {inside.GetPosition().y:0} m d'altitude" : "rien");

                // Tout vider : cœurs ramassés, coffres vides (comme un joueur qui a tout pris)
                var instances = (IDictionary)typeof(ZNetScene).GetField("m_instances", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ZNetScene.instance);
                int cleared = 0;
                foreach (var key in instances.Keys)
                {
                    var zdo = key as ZDO;
                    if (zdo == null || !zdo.IsValid()) continue;
                    var pos = zdo.GetPosition();
                    if (pos.y < 1500f || new Vector2(pos.x - crypt.Value.x, pos.z - crypt.Value.z).magnitude > 90f) continue;
                    var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
                    if (prefab == null) continue;
                    if (prefab.GetComponent<Pickable>() != null) { zdo.Set(ZDOVars.s_picked, true); cleared++; }
                    var c = prefab.GetComponent<Container>();
                    if (c != null && prefab.GetComponent<Piece>() == null)
                    {
                        var empty = new Inventory("", null, Mathf.Max(1, c.m_width), Mathf.Max(1, c.m_height));
                        var pkg = new ZPackage(); empty.Save(pkg);
                        zdo.Set(ZDOVars.s_addedDefaultItems, true); zdo.Set(ZDOVars.s_items, pkg.GetBase64()); cleared++;
                    }
                }
                forget.Invoke(null, null);
                bool gone = (bool)emptied.Invoke(null, new[] { result, entry });
                h.Check("Donjon.tout vidé : le repère disparaît", cleared > 0 && gone, $"{cleared} objet(s) vidé(s), vidé selon le scanner={gone}");
            }
            finally
            {
                player.TeleportTo(home + Vector3.up * 0.5f, homeRot, true);
            }
            float t1 = Time.realtimeSinceStartup;
            while ((player.IsTeleporting() || !ZoneSystem.instance.IsZoneLoaded(home)) && Time.realtimeSinceStartup - t1 < 40f) yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(1f);
        }
    }
}
