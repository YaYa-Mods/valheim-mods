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

                // Icône de la ressource sur l'épingle (sinon toutes les épingles du mod se ressemblent sur la carte)
                Minimap.PinData pin = null;
                foreach (var v in ((IDictionary)layerT.GetField("Pins").GetValue(layer)).Values) { pin = v as Minimap.PinData; break; }
                var layerIcon = layerT.GetMethod("Icon").Invoke(layer, null) as Sprite;
                Plugin.Log.LogInfo($"[TEST] épingle : type={pin?.m_type}, m_icon={(pin?.m_icon != null ? pin.m_icon.name : "null")}, icône de la couche={(layerIcon != null ? layerIcon.name : "null")}");
                h.Check("Épingles.icône de la ressource", pin != null && layerIcon != null && pin.m_icon == layerIcon,
                    $"m_icon={(pin?.m_icon != null ? pin.m_icon.name : "null")}, attendue={(layerIcon != null ? layerIcon.name : "null")}");

                // Ce que la carte DESSINE réellement (le champ peut être bon et l'affichage utiliser autre chose)
                typeof(Minimap).GetMethod("UpdatePins", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(map, null);
                var fields = typeof(Minimap.PinData).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Plugin.Log.LogInfo("[TEST] champs de PinData : " + string.Join(", ", fields.Select(f => f.Name + "=" + SafeValue(f, pin))));
                string drawn = null;
                foreach (var f in fields)
                {
                    var v = f.GetValue(pin);
                    var go = v as GameObject ?? (v as Component)?.gameObject;
                    if (go == null) continue;
                    foreach (var img in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                        if (img.sprite != null && drawn == null) drawn = f.Name + "→" + img.sprite.name;
                }
                Plugin.Log.LogInfo("[TEST] sprite dessinée pour l'épingle : " + (drawn ?? "aucune trouvée"));
                h.Check("Épingles.icône dessinée (mini-carte)", drawn != null && layerIcon != null && drawn.EndsWith(layerIcon.name), "dessinée : " + (drawn ?? "aucune"));

                // Grande carte : le jeu y utilise d'autres éléments d'interface, c'est là que les épingles paraissaient toutes pareilles
                var prevMode = map.m_mode;
                map.SetMapMode(Minimap.MapMode.Large);
                typeof(Minimap).GetMethod("UpdatePins", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(map, null);
                string drawnLarge = SpriteOf(pin, "m_uiElement") ?? SpriteOf(pin, "m_iconElement");
                Plugin.Log.LogInfo($"[TEST] grande carte, sprite de l'épingle : m_uiElement={SpriteOf(pin, "m_uiElement") ?? "null"}, m_iconElement={SpriteOf(pin, "m_iconElement") ?? "null"}");
                h.Check("Épingles.icône dessinée (grande carte)", drawnLarge != null && layerIcon != null && drawnLarge.EndsWith(layerIcon.name), "dessinée : " + (drawnLarge ?? "aucune"));
                map.SetMapMode(prevMode);

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
                // Depuis la roue : un seul appui ne doit rien effacer (pas de bouton « Confirmer ? » sous les yeux)
                layersT.GetMethod("Merge").Invoke(null, new object[] { entry, list });
                var confirmM = rfT.GetMethod("ClearAllPinsConfirmed", BindingFlags.NonPublic | BindingFlags.Instance);
                int beforeRadial = all.Count;
                confirmM.Invoke(rfInst, null);
                bool keptAfterFirst = all.Count == beforeRadial;
                confirmM.Invoke(rfInst, null);
                h.Check("Épingles.roue d'action : confirmation demandée", beforeRadial > 0 && keptAfterFirst && all.Count == 0,
                    $"couches {beforeRadial} → après 1 appui {(keptAfterFirst ? beforeRadial : all.Count)} → après 2 appuis {all.Count}");
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
        /// <summary>Nom de la sprite affichée par un élément d'interface d'une épingle (champ de PinData), ou null.</summary>
        internal static string SpriteName(Minimap.PinData pin, string field) => SpriteOf(pin, field) ?? "null";

        private static string SpriteOf(Minimap.PinData pin, string field)
        {
            var f = typeof(Minimap.PinData).GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var v = f?.GetValue(pin);
            var go = v as GameObject ?? (v as Component)?.gameObject;
            var img = go != null ? go.GetComponent<UnityEngine.UI.Image>() : null;
            var shown = img != null ? (img.overrideSprite ?? img.sprite) : null;
            return shown != null ? shown.name : null;
        }

        private static string SafeValue(System.Reflection.FieldInfo f, object o)
        {
            try { var v = f.GetValue(o); return v == null ? "null" : (v is UnityEngine.Object uo ? uo.name : v.ToString()); } catch { return "?"; }
        }
    }
}
