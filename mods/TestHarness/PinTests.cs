using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Épingles du finder :
    ///  - une cueillette ramassée (drapeau « picked » du ZDO) disparaît de la couche, des cartes et de la cible ;
    ///  - « Effacer toutes les épingles du mod » retire nos épingles mais pas celles du joueur ;
    ///  - les icônes embarquées du menu radial se chargent.
    /// </summary>
    internal static class PinTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);
        private static IList Pins(Minimap map) => (IList)typeof(Minimap).GetField("m_pins", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(map);

        public static IEnumerator Run(Plugin h, Player player)
        {
            var rfAsm = Asm("ResourceFinder");
            var layersT = rfAsm.GetType("ResourceFinder.Layers");
            var layerT = rfAsm.GetType("ResourceFinder.Layer");
            var resultT = rfAsm.GetType("ResourceFinder.Result");
            var entryT = rfAsm.GetType("ResourceFinder.ResourceEntry");
            var all = (IList)layersT.GetField("All").GetValue(null);
            var map = Minimap.instance;
            GameObject bush = null;
            try
            {
                // Buisson de framboises devant le joueur
                var prefab = ZNetScene.instance.GetPrefab("RaspberryBush");
                if (prefab == null) throw new Exception("prefab RaspberryBush introuvable");
                bush = UnityEngine.Object.Instantiate(prefab, player.transform.position + player.transform.forward * 3f, Quaternion.identity);
                var zdo = bush.GetComponent<ZNetView>().GetZDO();

                var result = Activator.CreateInstance(resultT);
                resultT.GetField("Pos").SetValue(result, bush.transform.position);
                resultT.GetField("Prefab").SetValue(result, "RaspberryBush");
                resultT.GetField("Hash").SetValue(result, "RaspberryBush".GetStableHashCode());
                resultT.GetField("Id").SetValue(result, zdo.m_uid);
                var entry = Activator.CreateInstance(entryT, "Test framboise", new[] { "RaspberryBush" }, null, null, Enum.ToObject(typeof(Heightmap.Biome), 0));
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(resultT));
                list.Add(result);
                var layer = layersT.GetMethod("Merge").Invoke(null, new object[] { entry, list });
                layersT.GetMethod("RefreshPins").Invoke(null, null);
                var pins = (ICollection)layerT.GetField("Pins").GetValue(layer);
                int pinsBefore = Pins(map).Count;
                h.Check("Épingles.couche créée", all.Contains(layer) && pins.Count == 1, $"épingles couche={pins.Count}");

                bool existsBefore = (bool)resultT.GetMethod("StillExists").Invoke(result, null);
                zdo.Set(ZDOVars.s_picked, true); // = ramassée, comme Pickable.SetPicked
                bool existsAfter = (bool)resultT.GetMethod("StillExists").Invoke(result, null);
                layersT.GetMethod("Purge").Invoke(null, null);
                layersT.GetMethod("Tick").Invoke(null, null); // pose/retire les épingles (pinsDirty)
                var results = (IList)layerT.GetField("Results").GetValue(layer);
                h.Check("Épingles.cueillette ramassée disparaît", existsBefore && !existsAfter && results.Count == 0 && pins.Count == 0 && Pins(map).Count == pinsBefore - 1,
                    $"existait={existsBefore}→{existsAfter}, résultats={results.Count}, épingles={pins.Count}, carte {pinsBefore}→{Pins(map).Count}");

                // Effacer tout : nos épingles partent, celle du joueur reste
                zdo.Set(ZDOVars.s_picked, false);
                list.Clear(); list.Add(result);
                layersT.GetMethod("Merge").Invoke(null, new object[] { entry, list });
                layersT.GetMethod("RefreshPins").Invoke(null, null);
                var mine = map.AddPin(player.transform.position + Vector3.right * 5f, Minimap.PinType.Icon0, "test joueur", true, false, 0L, default(Splatform.PlatformUserID));
                int layersBefore = all.Count;
                var rfT = rfAsm.GetType("ResourceFinder.Plugin");
                var rfInst = UnityEngine.Object.FindObjectOfType(rfT);
                rfT.GetMethod("ClearAllPins", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
                bool minePresent = Pins(map).Contains(mine);
                h.Check("Épingles.effacer celles du mod, garder celles du joueur", layersBefore >= 1 && all.Count == 0 && minePresent,
                    $"couches {layersBefore}→{all.Count}, épingle joueur présente={minePresent}");
                map.RemovePin(mine);
            }
            catch (Exception ex) { h.Check("Épingles", false, ex.InnerException?.Message ?? ex.Message); }
            finally { if (bush != null) ZNetScene.instance.Destroy(bush); }

            // Icônes embarquées
            try
            {
                var hubAsm = Asm("ModHub");
                var radialT = hubAsm.GetType("ModHub.Radial");
                var iconM = radialT.GetMethod("IconSprite", BindingFlags.NonPublic | BindingFlags.Static);
                var ok = new List<string>();
                foreach (var (asm, name) in new[] { (hubAsm, "@mods.png"), (hubAsm, "@config.png"), (rfAsm, "@scanner.png"), (rfAsm, "@target.png"), (rfAsm, "@clear.png") })
                {
                    var s = iconM.Invoke(null, new object[] { name, asm }) as Sprite;
                    ok.Add($"{name}={(s != null ? s.texture.width + "px" : "null")}");
                }
                h.Check("Radial.icônes embarquées", ok.All(x => !x.EndsWith("null")), string.Join(", ", ok));
            }
            catch (Exception ex) { h.Check("Radial.icônes embarquées", false, ex.InnerException?.Message ?? ex.Message); }
            yield break;
        }
    }
}
