using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Exporte dans BepInEx/config/guide_facts.txt ce que le jeu dit vraiment de la progression, pour vérifier le
    /// guide contre les données et non contre la mémoire : offrandes des autels de boss (OfferingBowl), portes à clé,
    /// butin des boss, recettes (station, niveau, ingrédients) et constructions (station, ingrédients) des objets cités
    /// par le guide, et d'où viennent les matériaux (cueillette, rochers, arbres, créatures, objets détruits, coffres).
    /// </summary>
    internal static class GuideAudit
    {
        private static readonly string[] BossLocations = { "Eikthyrnir", "GDKing", "Bonemass", "Dragonqueen", "GoblinKing", "Mistlands_DvergrBossEntrance1", "FaderLocation", "DN_Bossroom" };
        private static readonly string[] Bosses = { "Eikthyr", "gd_king", "Bonemass", "Dragon", "GoblinKing", "SeekerQueen", "Fader", "FrozenKing", "Bjorn" };
        private static readonly string[] Items =
        {
            "Hammer", "AxeStone", "Bow", "ArrowFlint", "HelmetLeather", "ArmorLeatherChest", "ArmorLeatherLegs", "TrophyDeer",
            "PickaxeAntler", "HardAntler", "CopperOre", "TinOre", "Copper", "Tin", "Bronze", "SurtlingCore", "AxeBronze", "FineWood", "AncientSeed",
            "BowFineWood", "ArmorBronzeChest", "ArmorTrollLeatherChest",
            "CryptKey", "IronScrap", "Iron", "WitheredBone", "MeadPoisonResist", "MeadBasePoisonResist", "MaceIron", "ArmorIronChest",
            "Wishbone", "MeadFrostResist", "MeadBaseFrostResist", "CapeWolf", "SilverOre", "Silver", "DragonEgg", "BowDraugrFang", "ArmorWolfChest", "DragonTear",
            "GoblinTotem", "BlackMetalScrap", "BlackMetal", "SwordBlackmetal", "AxeBlackMetal", "KnifeBlackMetal", "AtgeirBlackmetal", "BarleyWine", "BarleyWineBase", "Barley", "Flax", "ArmorPaddedCuirass", "CapeLox", "YagluthDrop",
            "Wisp", "Demister", "DvergrKeyFragment", "DvergrKey", "Eitr", "Sap", "SwordMistwalker", "ArmorCarapaceChest", "QueenDrop",
            "BellFragment", "Bell", "FlametalOreNew", "Flametal", "FaderDrop",
            "Frostwood", "FrostCore", "Gold", "GoldOre", "HatefulBlood", "FrozenKingDrop", "BloodGoldKey", "MoldKeys", "MoldSmallParts",
            "CeramicPlate", "LinenThread", "BlackCore", "YggdrasilWood", "BlackMarble", "Softtissue", "DvergrNeedle", "Carapace", "Honey", "Thistle", "Cloudberry", "IronNails", "Chain", "ElderBark", "Guck", "WolfPelt", "TrollHide", "LeatherScraps", "DeerHide", "Feathers", "Flint", "Coal", "Bloodbag", "GreydwarfEye", "NeckTail",
        };
        private static readonly string[] Pieces =
        {
            "piece_workbench", "bed", "smelter", "charcoal_kiln", "forge", "piece_cauldron", "fermenter", "piece_stonecutter", "piece_artisanstation",
            "blastfurnace", "windmill", "piece_spinningwheel", "piece_sapcollector", "eitrrefinery", "blackforge", "piece_magetable", "VikingShip_Ashlands", "piece_MeadCauldron", "piece_beehive", "piece_bathtub",
        };

        public static void Run(Plugin h)
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine("# Faits du jeu pour le guide (export automatique), " + DateTime.Now);
                sb.AppendLine();
                sb.AppendLine("## Autels de boss (OfferingBowl) et portes");
                foreach (var name in BossLocations)
                {
                    var loc = ZoneSystem.instance.m_locations.FirstOrDefault(l => l != null && l.m_prefabName == name);
                    if (loc == null) { sb.AppendLine($"{name} : LIEU INTROUVABLE"); continue; }
                    GameObject go = null;
                    try { loc.m_prefab.Load(); go = loc.m_prefab.Asset; } catch (Exception ex) { sb.AppendLine($"{name} : chargement impossible ({ex.Message})"); }
                    sb.AppendLine($"{name} : biome={loc.m_biome} quantité={loc.m_quantity} prefab={(go != null ? go.name : "null")}");
                    if (go == null) continue;
                    foreach (var bowl in go.GetComponentsInChildren<OfferingBowl>(true))
                        sb.AppendLine($"  OfferingBowl '{bowl.m_name}' : item={(bowl.m_bossItem != null ? bowl.m_bossItem.name : "null")} ×{bowl.m_bossItems} boss={(bowl.m_bossPrefab != null ? bowl.m_bossPrefab.name : "null")} itemStands={bowl.m_useItemStands} prefix='{bowl.m_itemStandPrefix}' setKey='{bowl.m_setGlobalKey}' useText='{bowl.m_useItemText}'");
                    foreach (var door in go.GetComponentsInChildren<Door>(true))
                        sb.AppendLine($"  Door '{door.name}' : clé={(door.m_keyItem != null ? door.m_keyItem.name : "aucune")}");
                    foreach (var stand in go.GetComponentsInChildren<ItemStand>(true).Take(12))
                        sb.AppendLine($"  ItemStand '{stand.name}'");
                    foreach (var c in go.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct().Where(n => n.IndexOf("Altar", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Bell", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0))
                        sb.AppendLine($"  composant : {c}");
                }

                sb.AppendLine();
                sb.AppendLine("## Boss : clé de victoire, butin");
                foreach (var b in Bosses)
                {
                    var go = ZNetScene.instance.GetPrefab(b);
                    var ch = go != null ? go.GetComponent<Character>() : null;
                    if (ch == null) { sb.AppendLine($"{b} : INTROUVABLE"); continue; }
                    sb.AppendLine($"{b} ({ch.m_name}) : defeatKey='{ch.m_defeatSetGlobalKey}' bossEvent='{ch.m_bossEvent}' boss={ch.m_boss}");
                    var drop = go.GetComponent<CharacterDrop>();
                    if (drop != null) foreach (var d in drop.m_drops) sb.AppendLine($"  drop {(d.m_prefab != null ? d.m_prefab.name : "null")} ×{d.m_amountMin}-{d.m_amountMax} chance={d.m_chance} unParJoueur={d.m_onePerPlayer}");
                }

                sb.AppendLine();
                sb.AppendLine("## Recettes des objets du guide (station niv. : ingrédients)");
                foreach (var it in Items)
                {
                    var go = ObjectDB.instance.GetItemPrefab(it);
                    var item = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (item == null) { sb.AppendLine($"{it} : OBJET INTROUVABLE"); continue; }
                    var recipes = ObjectDB.instance.m_recipes.Where(r => r != null && r.m_item != null && r.m_item.name == it && r.m_enabled).ToList();
                    string tele = item.m_itemData.m_shared.m_teleportable ? "" : " [non téléportable]";
                    if (recipes.Count == 0) { sb.AppendLine($"{it} ({item.m_itemData.m_shared.m_name}) : pas de recette{tele}"); continue; }
                    foreach (var r in recipes)
                        sb.AppendLine($"{it} ({item.m_itemData.m_shared.m_name}) ×{r.m_amount} : {(r.m_craftingStation != null ? r.m_craftingStation.m_name : "sans station")} niv.{r.m_minStationLevel} : " +
                                      string.Join(", ", r.m_resources.Select(x => $"{(x.m_resItem != null ? x.m_resItem.name : "?")}×{x.m_amount}")) + tele);
                }

                sb.AppendLine();
                sb.AppendLine("## Constructions du guide (station : ingrédients)");
                foreach (var p in Pieces)
                {
                    var go = ZNetScene.instance.GetPrefab(p);
                    var piece = go != null ? go.GetComponent<Piece>() : null;
                    if (piece == null) { sb.AppendLine($"{p} : PIÈCE INTROUVABLE"); continue; }
                    sb.AppendLine($"{p} ({piece.m_name}) : {(piece.m_craftingStation != null ? piece.m_craftingStation.m_name : "sans station")} : " +
                                  string.Join(", ", piece.m_resources.Select(x => $"{(x.m_resItem != null ? x.m_resItem.name : "?")}×{x.m_amount}")));
                }

                sb.AppendLine();
                sb.AppendLine("## Sources des matériaux (qui lâche / donne quoi)");
                var wanted = new HashSet<string>(Items);
                var sources = new Dictionary<string, List<string>>();
                void Add(string item, string src) { if (!wanted.Contains(item)) return; if (!sources.TryGetValue(item, out var l)) sources[item] = l = new List<string>(); if (!l.Contains(src)) l.Add(src); }
                foreach (var go in ZNetScene.instance.m_prefabs)
                {
                    if (go == null) continue;
                    var cd = go.GetComponent<CharacterDrop>();
                    if (cd != null) foreach (var d in cd.m_drops) if (d.m_prefab != null) Add(d.m_prefab.name, $"créature {go.name} ({d.m_amountMin}-{d.m_amountMax}, {d.m_chance:0.##})");
                    var pk = go.GetComponent<Pickable>();
                    if (pk != null && pk.m_itemPrefab != null) Add(pk.m_itemPrefab.name, $"cueillette {go.name} ×{pk.m_amount}");
                    var r5 = go.GetComponent<MineRock5>(); if (r5 != null) foreach (var d in r5.m_dropItems.m_drops) if (d.m_item != null) Add(d.m_item.name, $"rocher {go.name}");
                    var rk = go.GetComponent<MineRock>(); if (rk != null) foreach (var d in rk.m_dropItems.m_drops) if (d.m_item != null) Add(d.m_item.name, $"rocher {go.name}");
                    var dd = go.GetComponent<DropOnDestroyed>(); if (dd != null) foreach (var d in dd.m_dropWhenDestroyed.m_drops) if (d.m_item != null) Add(d.m_item.name, $"destructible {go.name}");
                    var tl = go.GetComponent<TreeLog>(); if (tl != null) foreach (var d in tl.m_dropWhenDestroyed.m_drops) if (d.m_item != null) Add(d.m_item.name, $"tronc {go.name}");
                    var ct = go.GetComponent<Container>(); if (ct != null && ct.m_defaultItems != null) foreach (var d in ct.m_defaultItems.m_drops) if (d.m_item != null) Add(d.m_item.name, $"coffre {go.name}");
                    var sm = go.GetComponent<Smelter>(); if (sm != null) foreach (var c in sm.m_conversion) if (c.m_to != null && c.m_from != null) Add(c.m_to.name, $"{go.name} : {c.m_from.name} → {c.m_to.name}");
                    var fm = go.GetComponent<Fermenter>(); if (fm != null) foreach (var c in fm.m_conversion) if (c.m_to != null && c.m_from != null) Add(c.m_to.name, $"{go.name} : {c.m_from.name} → {c.m_to.name} ×{c.m_producedItems}");
                }
                foreach (var it in Items)
                    sb.AppendLine($"{it} : " + (sources.TryGetValue(it, out var l) ? string.Join(" | ", l) : "(aucune source trouvée dans les prefabs)"));



                sb.AppendLine();
                sb.AppendLine("## Autres conversions et vendeurs (cuisson, incinérateur, marchands) pour les objets recherchés");
                foreach (var go in ZNetScene.instance.m_prefabs)
                {
                    if (go == null) continue;
                    var cs = go.GetComponent<CookingStation>();
                    if (cs != null) foreach (var c in cs.m_conversion) if (c.m_to != null && wanted.Contains(c.m_to.name)) sb.AppendLine($"cuisson {go.name} : {c.m_from?.name} → {c.m_to.name}");
                    var inc = go.GetComponent<Incinerator>();
                    if (inc != null) foreach (var c in inc.m_conversions) { var t = c.GetType(); var to = t.GetField("m_result")?.GetValue(c) as ItemDrop; if (to != null && wanted.Contains(to.name)) sb.AppendLine($"incinérateur {go.name} → {to.name} (requiert : {string.Join(", ", ((IEnumerable<object>)(t.GetField("m_requirements")?.GetValue(c) as System.Collections.IEnumerable ?? new object[0]).Cast<object>()).Select(r => r.GetType().GetField("m_resItem")?.GetValue(r) is ItemDrop rd ? rd.name + "×" + r.GetType().GetField("m_amount")?.GetValue(r) : "?"))})"); }
                    var tr = go.GetComponent<Trader>();
                    if (tr != null) foreach (var it in tr.m_items) if (it.m_prefab != null && wanted.Contains(it.m_prefab.name)) sb.AppendLine($"marchand {go.name} : {it.m_prefab.name} ×{it.m_stack} pour {it.m_price} (clé '{it.m_requiredGlobalKey}')");
                }
                sb.AppendLine();
                sb.AppendLine("## Placement dans le monde des gisements : table de végétation (biome, altitude, densité) et lieux qui les contiennent");
                var veins = new[] { "goldvein", "silvervein", "rock3_silver", "rock4_copper", "MineRock_Tin", "TrollFrost_Frac", "MineRock_Meteorite", "giant_brain", "Pickable_Flametal" };
                foreach (var v in veins)
                {
                    var lines = new List<string>();
                    foreach (var veg in ZoneSystem.instance.m_vegetation)
                        if (veg?.m_prefab != null && veg.m_prefab.name.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0)
                            lines.Add($"végétation {veg.m_prefab.name} : biome={veg.m_biome} altitude={veg.m_minAltitude:0}..{veg.m_maxAltitude:0} min/max par zone={veg.m_min}..{veg.m_max} groupe={veg.m_groupSizeMin}..{veg.m_groupSizeMax} activé={veg.m_enable}");
                    foreach (var loc in ZoneSystem.instance.m_locations)
                    {
                        if (loc?.m_prefab == null) continue;
                        GameObject lgo = null;
                        try { loc.m_prefab.Load(); lgo = loc.m_prefab.Asset; } catch { }
                        if (lgo == null) continue;
                        int n = 0; foreach (var t in lgo.GetComponentsInChildren<Transform>(true)) if (t.name.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0) n++;
                        if (n > 0) lines.Add($"lieu {loc.m_prefabName} ({loc.m_biome}, ×{loc.m_quantity}) contient {n} × {v}");
                    }
                    sb.AppendLine($"{v} : {(lines.Count > 0 ? string.Join(" | ", lines) : "NULLE PART (ni végétation, ni lieu)")}");
                }

                sb.AppendLine();
                sb.AppendLine("## Lieux du Nord profond : objets et composants notables dans les prefabs");
                foreach (var loc in ZoneSystem.instance.m_locations.Where(l => l != null && (l.m_biome == Heightmap.Biome.DeepNorth || l.m_prefabName.IndexOf("Fimbul", StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    GameObject go = null;
                    try { loc.m_prefab.Load(); go = loc.m_prefab.Asset; } catch { }
                    if (go == null) { sb.AppendLine($"{loc.m_prefabName} : prefab non chargé"); continue; }
                    var found = new List<string>();
                    foreach (var id in go.GetComponentsInChildren<ItemDrop>(true)) found.Add("item " + id.name);
                    foreach (var pk in go.GetComponentsInChildren<Pickable>(true)) found.Add($"cueillette {pk.name}→{pk.m_itemPrefab?.name}");
                    foreach (var ct in go.GetComponentsInChildren<Container>(true)) found.Add("coffre " + ct.name);
                    foreach (var ob in go.GetComponentsInChildren<OfferingBowl>(true)) found.Add($"autel {ob.name} ({ob.m_bossItem?.name}×{ob.m_bossItems}→{ob.m_bossPrefab?.name}, clé '{ob.m_setGlobalKey}')");
                    foreach (var dr in go.GetComponentsInChildren<Door>(true)) found.Add($"porte {dr.name} (clé {dr.m_keyItem?.name ?? "aucune"})");
                    foreach (var rs in go.GetComponentsInChildren<RuneStone>(true)) found.Add($"pierre {rs.name} : {Localization.instance.Localize(rs.m_text ?? "").Replace("\n", " ")}");
                    sb.AppendLine($"{loc.m_prefabName} ×{loc.m_quantity} : {(found.Count > 0 ? string.Join(" | ", found.Distinct().Take(40)) : "-")}");
                }

                sb.AppendLine();
                sb.AppendLine("## Où trouver HatefulBlood / GiantBloodSack : salles de donjon, LootSpawner, Destructible.m_spawnWhenDestroyed");
                var blood = new HashSet<string> { "HatefulBlood", "GiantBloodSack", "FrozenKingDrop", "BloodGoldKey", "KeysGoldUncooked" };
                foreach (var go in ZNetScene.instance.m_prefabs)
                {
                    if (go == null) continue;
                    var ls = go.GetComponent<LootSpawner>();
                    if (ls != null && ls.m_items?.m_drops != null) foreach (var d in ls.m_items.m_drops) if (d.m_item != null && blood.Contains(d.m_item.name)) sb.AppendLine($"LootSpawner {go.name} → {d.m_item.name}");
                    var ds = go.GetComponent<Destructible>();
                    if (ds != null && ds.m_spawnWhenDestroyed != null && blood.Contains(ds.m_spawnWhenDestroyed.name)) sb.AppendLine($"Destructible {go.name} → {ds.m_spawnWhenDestroyed.name}");
                    var pk = go.GetComponent<Pickable>();
                    if (pk != null && pk.m_itemPrefab != null && blood.Contains(pk.m_itemPrefab.name)) sb.AppendLine($"Pickable {go.name} → {pk.m_itemPrefab.name}");
                    var cs = go.GetComponent<CookingStation>();
                    if (cs != null) foreach (var c in cs.m_conversion) if (c.m_to != null && (blood.Contains(c.m_to.name) || blood.Contains(c.m_from?.name ?? ""))) sb.AppendLine($"cuisson {go.name} : {c.m_from?.name} → {c.m_to.name}");
                    var sm = go.GetComponent<Smelter>();
                    if (sm != null) foreach (var c in sm.m_conversion) if (c.m_to != null && (blood.Contains(c.m_to.name) || blood.Contains(c.m_from?.name ?? ""))) sb.AppendLine($"fonderie {go.name} : {c.m_from?.name} → {c.m_to.name}");
                    var fm = go.GetComponent<Fermenter>();
                    if (fm != null) foreach (var c in fm.m_conversion) if (c.m_to != null && (blood.Contains(c.m_to.name) || blood.Contains(c.m_from?.name ?? ""))) sb.AppendLine($"fermenteur {go.name} : {c.m_from?.name} → {c.m_to.name}");
                    var cd = go.GetComponent<CharacterDrop>();
                    if (cd != null) foreach (var d in cd.m_drops) if (d.m_prefab != null && blood.Contains(d.m_prefab.name)) sb.AppendLine($"créature {go.name} → {d.m_prefab.name} ({d.m_amountMin}-{d.m_amountMax}, {d.m_chance})");
                }
                foreach (var r in ObjectDB.instance.m_recipes)
                    if (r != null && r.m_item != null && (blood.Contains(r.m_item.name) || r.m_resources.Any(x => x.m_resItem != null && blood.Contains(x.m_resItem.name))))
                        sb.AppendLine($"recette {r.m_item.name} ({(r.m_craftingStation != null ? r.m_craftingStation.m_name : "sans station")}) : {string.Join(", ", r.m_resources.Select(x => x.m_resItem?.name + "×" + x.m_amount))} activée={r.m_enabled}");
                int roomsScanned = 0;
                foreach (var rd in DungeonDB.GetRooms())
                {
                    if (rd == null || rd.m_theme.ToString().IndexOf("Morkhalla", StringComparison.OrdinalIgnoreCase) < 0 && rd.m_theme.ToString().IndexOf("DeepNorth", StringComparison.OrdinalIgnoreCase) < 0 && rd.m_theme.ToString().IndexOf("Frost", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    GameObject room = null;
                    try { rd.m_prefab.Load(); room = rd.m_prefab.Asset; } catch { }
                    if (room == null) continue;
                    roomsScanned++;
                    var found = new List<string>();
                    foreach (var pk in room.GetComponentsInChildren<Pickable>(true)) if (pk.m_itemPrefab != null && blood.Contains(pk.m_itemPrefab.name)) found.Add($"cueillette {pk.name}→{pk.m_itemPrefab.name}");
                    foreach (var id in room.GetComponentsInChildren<ItemDrop>(true)) if (blood.Contains(id.name.Replace("(Clone)", ""))) found.Add("item " + id.name);
                    foreach (var ob in room.GetComponentsInChildren<OfferingBowl>(true)) found.Add($"autel {ob.name} ({ob.m_bossItem?.name}×{ob.m_bossItems}→{ob.m_bossPrefab?.name})");
                    foreach (var sp in room.GetComponentsInChildren<CreatureSpawner>(true)) if (sp.m_creaturePrefab != null) found.Add("spawn " + sp.m_creaturePrefab.name);
                    foreach (var ct in room.GetComponentsInChildren<Container>(true)) found.Add("coffre " + ct.name);
                    if (found.Count > 0) sb.AppendLine($"salle {room.name} [{rd.m_theme}] : {string.Join(" | ", found.Distinct().Take(30))}");
                }
                sb.AppendLine($"(salles Morkhalla/Nord scannées : {roomsScanned} ; thèmes : {string.Join(", ", DungeonDB.GetRooms().Where(r => r != null).Select(r => r.m_theme.ToString()).Distinct())})");

                sb.AppendLine();
                sb.AppendLine("## Qui référence BlackIce_* (glace noire → HatefulBlood) : champs GameObject des composants, listes d'apparition, lieux");
                bool IsIce(UnityEngine.Object o) => o != null && o.name.StartsWith("BlackIce", StringComparison.OrdinalIgnoreCase);
                void ScanObject(string owner, UnityEngine.Object root)
                {
                    var go2 = root as GameObject; if (go2 == null) return;
                    foreach (var comp in go2.GetComponentsInChildren<Component>(true))
                    {
                        if (comp == null) continue;
                        var t = comp.GetType();
                        foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                        {
                            object v; try { v = f.GetValue(comp); } catch { continue; }
                            if (v is GameObject g && IsIce(g)) sb.AppendLine($"{owner} : {t.Name}.{f.Name} → {g.name}");
                            else if (v is System.Collections.IEnumerable en && !(v is string))
                                foreach (var e in en)
                                {
                                    if (e == null) continue;
                                    if (e is GameObject g2) { if (IsIce(g2)) sb.AppendLine($"{owner} : {t.Name}.{f.Name}[] → {g2.name}"); continue; }
                                    var et = e.GetType();
                                    if (et.IsPrimitive || et == typeof(string)) break;
                                    foreach (var ef in et.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                                    {
                                        object ev; try { ev = ef.GetValue(e); } catch { continue; }
                                        if (ev is GameObject g3 && IsIce(g3)) sb.AppendLine($"{owner} : {t.Name}.{f.Name}[].{ef.Name} → {g3.name}");
                                    }
                                }
                        }
                    }
                }
                foreach (var go in ZNetScene.instance.m_prefabs) if (go != null && !IsIce(go)) ScanObject("prefab " + go.name, go);
                foreach (var loc in ZoneSystem.instance.m_locations.Where(l => l != null && l.m_biome == Heightmap.Biome.DeepNorth))
                {
                    GameObject g = null; try { loc.m_prefab.Load(); g = loc.m_prefab.Asset; } catch { }
                    if (g != null) ScanObject("lieu " + loc.m_prefabName, g);
                }
                foreach (var ss in UnityEngine.Object.FindObjectsOfType<SpawnSystem>())
                    foreach (var list in ss.m_spawnLists) foreach (var sd in list.m_spawners) if (sd.m_prefab != null && IsIce(sd.m_prefab)) sb.AppendLine($"SpawnSystem {list.name} : {sd.m_name} → {sd.m_prefab.name} biome={sd.m_biome} clé='{sd.m_requiredGlobalKey}'");
                foreach (var ev in RandEventSystem.instance != null ? RandEventSystem.instance.m_events : new List<RandomEvent>())
                    foreach (var sd in ev.m_spawn) if (sd.m_prefab != null && IsIce(sd.m_prefab)) sb.AppendLine($"événement {ev.m_name} → {sd.m_prefab.name} clés requises={string.Join(",", ev.m_requiredGlobalKeys)}");
                var ice = ZNetScene.instance.GetPrefab("BlackIce_Core");
                if (ice != null) sb.AppendLine("BlackIce_Core : composants = " + string.Join(", ", ice.GetComponents<Component>().Select(c => c.GetType().Name)) + " ; enfants = " + string.Join(", ", ice.GetComponentsInChildren<Transform>(true).Select(t => t.name).Distinct().Take(20)));
                var start = ZNetScene.instance.GetPrefab("BlackIce_Start");
                if (start != null) sb.AppendLine("BlackIce_Start : composants = " + string.Join(", ", start.GetComponentsInChildren<Component>(true).Select(c => c.GetType().Name).Distinct()));

                sb.AppendLine();
                sb.AppendLine("## Événements persistants (glace noire, etc.) : PersistentEventSystem.m_possibleEvents");
                var pes = UnityEngine.Object.FindObjectOfType<PersistentEventSystem>();
                if (pes == null) sb.AppendLine("(PersistentEventSystem absent de la scène)");
                else foreach (var pe in pes.m_possibleEvents)
                {
                    var objs = new List<string>();
                    foreach (var o in pe.objectsToSpawn)
                    {
                        if (o == null) continue;
                        var ot = o.GetType();
                        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        string nm = ot.GetField("name", flags)?.GetValue(o)?.ToString();
                        var pl = ot.GetField("prefabs", flags)?.GetValue(o) as System.Collections.IEnumerable;
                        var pn = new List<string>(); if (pl != null) foreach (var p in pl) pn.Add(p is GameObject pg ? pg.name : p?.ToString());
                        objs.Add($"{nm}[{string.Join(",", pn)}]×{ot.GetField("minCount", flags)?.GetValue(o)}-{ot.GetField("maxCount", flags)?.GetValue(o)}");
                    }
                    sb.AppendLine($"{pe.internalName} carte='{Localization.instance.Localize(pe.mapTokenString ?? "")}' biomes={pe.biomes} lieu='{pe.locationToSpawn}' rayon={pe.minRadius}-{pe.maxRadius} maxSimultanés={pe.maxConcurrent} durée={(pe.hasDuration ? pe.minDurationInSeconds + "-" + pe.maxDurationInSeconds + " s" : "illimitée")} objets=[{string.Join(", ", objs)}]");
                }
                sb.AppendLine();
                sb.AppendLine("## Corbeau (tutoriels Hugin / Munin), texte du jeu localisé");
                var tut = Tutorial.instance;
                if (tut != null && tut.m_texts != null)
                    foreach (var t in tut.m_texts)
                    {
                        string txt = Localization.instance.Localize(t.m_text ?? "").Replace("\n", " ");
                        sb.AppendLine($"[{t.m_name}] {(t.m_isMunin ? "Munin" : "Hugin")} clé='{t.m_globalKeyTrigger}' déclencheur='{t.m_tutorialTrigger}', {Localization.instance.Localize(t.m_topic ?? "")} / {Localization.instance.Localize(t.m_label ?? "")} : {txt}");
                    }
                else sb.AppendLine("(Tutorial.instance indisponible)");

                sb.AppendLine();
                sb.AppendLine("## Pierres runiques (RuneStone) : ce que disent les pierres des boss et des lieux");
                foreach (var go in ZNetScene.instance.m_prefabs)
                {
                    var rs = go != null ? go.GetComponent<RuneStone>() : null;
                    if (rs == null) continue;
                    string main = Localization.instance.Localize(rs.m_text ?? "").Replace("\n", " ");
                    sb.AppendLine($"{go.name} : lieu='{rs.m_locationName}' épingle='{Localization.instance.Localize(rs.m_pinName ?? "")}' sujet='{Localization.instance.Localize(rs.m_topic ?? "")}' : {main}");
                    if (rs.m_randomTexts != null) foreach (var r in rs.m_randomTexts.Take(6)) sb.AppendLine($"   · {Localization.instance.Localize(r.m_topic ?? "")} : {Localization.instance.Localize(r.m_text ?? "").Replace("\n", " ")}");
                }
                sb.AppendLine();
                sb.AppendLine("## Lieux cités (biome, quantité)");
                foreach (var n in new[] { "Eikthyrnir", "GDKing", "Bonemass", "Dragonqueen", "GoblinKing", "Mistlands_DvergrBossEntrance1", "FaderLocation", "DN_Bossroom", "SunkenCrypt4", "TrollCave02", "Crypt2", "GoblinCamp2", "Mistlands_DvergrTownEntrance1", "Mistlands_DvergrTownEntrance2", "CharredFortress", "Vendor_BlackForest", "Hildir_camp", "BogWitch_Camp" })
                {
                    var loc = ZoneSystem.instance.m_locations.FirstOrDefault(l => l != null && l.m_prefabName == n);
                    sb.AppendLine(loc == null ? $"{n} : INTROUVABLE" : $"{n} : {loc.m_biome} ×{loc.m_quantity} unique={loc.m_unique}");
                }
            }
            catch (Exception ex) { sb.AppendLine("ERREUR : " + ex); }
            string path = System.IO.Path.Combine(Paths.ConfigPath, "guide_facts.txt");
            System.IO.File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            h.Check("Guide.export des faits", System.IO.File.Exists(path), path);
        }
    }
}
