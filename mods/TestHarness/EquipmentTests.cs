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
    /// Emplacements d'équipement du mod Inventory : la dernière ligne est réservée, équiper y déplace la pièce, retirer
    /// la renvoie dans la grille, une case n'accepte que son type, le ramassage et le tri ignorent la ligne, et le
    /// panneau est dessiné à côté de la grille sans rien recouvrir.
    /// </summary>
    internal static class EquipmentTests
    {
        private static Rect ScreenRectOf(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return Rect.zero;
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            float minX = Mathf.Min(c[0].x, c[2].x), maxX = Mathf.Max(c[0].x, c[2].x), minY = Mathf.Min(c[0].y, c[2].y), maxY = Mathf.Max(c[0].y, c[2].y);
            return new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
        }

        private static ItemDrop.ItemData Give(Inventory inv, string prefab)
        {
            var go = ObjectDB.instance.GetItemPrefab(prefab);
            var item = go.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = go; item.m_stack = 1;
            return inv.AddItem(item) ? item : null;
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Inventory");
            var eqT = asm.GetType("InventoryMod.Equipment");
            var pluginT = asm.GetType("InventoryMod.Plugin");
            var rowsCfg = (BepInEx.Configuration.ConfigEntry<int>)pluginT.GetField("InventoryRows", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var slotsCfg = (BepInEx.Configuration.ConfigEntry<bool>)pluginT.GetField("EquipmentSlots", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            slotsCfg.Value = true;
            // Le test précédent a forcé 20 lignes à la main : on redonne la taille prévue par le réglage (20 + la ligne réservée)
            asm.GetType("InventoryMod.Patches").GetMethod("ApplyRows", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { player });
            yield return null;
            var inv = player.GetInventory();
            int reserved = (int)eqT.GetMethod("ReservedRow", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { inv });
            h.Check("Équipement.ligne réservée", inv.GetHeight() == rowsCfg.Value + 1 && reserved == inv.GetHeight() - 1, $"hauteur={inv.GetHeight()} (réglage {rowsCfg.Value}), ligne réservée={reserved}");
            if (reserved < 0) yield break;

            // Un objet ordinaire ne va jamais dans la ligne réservée
            var flint = Give(inv, "Flint");
            h.Check("Équipement.ramassage hors de la ligne réservée", flint != null && flint.m_gridPos.y != reserved, flint == null ? "objet non ajouté" : $"posé en {flint.m_gridPos.x},{flint.m_gridPos.y}");
            if (flint != null) inv.RemoveItem(flint);

            // Équiper : la pièce va dans sa case ; retirer : elle revient dans la grille
            ItemDrop.ItemData Equipped(string f) => typeof(Humanoid).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(player) as ItemDrop.ItemData;
            var prevHelmet = Equipped("m_helmetItem"); var prevChest = Equipped("m_chestItem"); var prevLegs = Equipped("m_legItem");
            var helmet = Give(inv, "HelmetLeather");
            var chest = Give(inv, "ArmorLeatherChest");
            var legs = Give(inv, "ArmorLeatherLegs");
            var before = helmet != null ? helmet.m_gridPos : new Vector2i(-1, -1);
            bool eq = helmet != null && player.EquipItem(helmet, false);
            yield return null;
            h.Check("Équipement.casque équipé va dans la case Tête", eq && helmet.m_gridPos == new Vector2i(0, reserved), helmet == null ? "casque absent" : $"équipé={eq}, {before.x},{before.y} → {helmet.m_gridPos.x},{helmet.m_gridPos.y}");
            bool eqC = chest != null && player.EquipItem(chest, false);
            bool eqL = legs != null && player.EquipItem(legs, false);
            yield return null;
            h.Check("Équipement.torse et jambes dans leurs cases", eqC && eqL && chest.m_gridPos == new Vector2i(1, reserved) && legs.m_gridPos == new Vector2i(2, reserved),
                $"torse {chest?.m_gridPos.x},{chest?.m_gridPos.y}, jambes {legs?.m_gridPos.x},{legs?.m_gridPos.y}");

            player.UnequipItem(helmet, false);
            yield return null;
            h.Check("Équipement.casque retiré revient dans la grille", !helmet.m_equipped && helmet.m_gridPos.y != reserved && helmet.m_gridPos.y > 0, $"équipé={helmet.m_equipped}, en {helmet.m_gridPos.x},{helmet.m_gridPos.y}");

            // Remplacement : un second casque équipé prend la case, le premier retourne dans la grille
            player.EquipItem(helmet, false);
            var helmet2 = Give(inv, "HelmetBronze");
            bool eq2 = helmet2 != null && player.EquipItem(helmet2, false);
            yield return null;
            h.Check("Équipement.remplacement : l'ancien casque sort de la case", eq2 && helmet2.m_gridPos == new Vector2i(0, reserved) && !helmet.m_equipped && helmet.m_gridPos.y != reserved,
                $"nouveau {helmet2?.m_gridPos.x},{helmet2?.m_gridPos.y} équipé={helmet2?.m_equipped}, ancien {helmet.m_gridPos.x},{helmet.m_gridPos.y} équipé={helmet.m_equipped}");

            // Glisser-déposer via la grille du jeu : le mauvais type est refusé, le bon est équipé, sortir de la case retire
            var gui = InventoryGui.instance;
            gui.Show(null, 1);
            yield return new WaitForSecondsRealtime(0.8f);
            var grid = gui.m_playerGrid;
            var wood = Give(inv, "Wood");
            bool refused = wood != null && !grid.DropItem(inv, wood, wood.m_stack, new Vector2i(3, reserved));
            h.Check("Équipement.la case Cape rejette le bois", refused && wood != null && wood.m_gridPos.y != reserved, wood == null ? "bois absent" : $"refusé={refused}, bois en {wood.m_gridPos.x},{wood.m_gridPos.y}");
            player.UnequipItem(helmet2, false);
            yield return null;
            bool dropped = grid.DropItem(inv, helmet2, 1, new Vector2i(0, reserved));
            yield return null;
            helmet2 = inv.GetItemAt(0, reserved) ?? helmet2; // le jeu remplace l'objet déplacé par une copie
            h.Check("Équipement.glisser un casque sur sa case l'équipe", dropped && helmet2.m_equipped && helmet2.m_gridPos == new Vector2i(0, reserved), $"déposé={dropped}, équipé={helmet2.m_equipped}, en {helmet2.m_gridPos.x},{helmet2.m_gridPos.y}");
            var freeSpot = (Vector2i)eqT.GetMethod("FirstFree", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { inv, false });
            bool draggedOut = grid.DropItem(inv, helmet2, 1, freeSpot);
            yield return null;
            helmet2 = inv.GetItemAt(freeSpot.x, freeSpot.y) ?? helmet2;
            h.Check("Équipement.glisser hors de la case retire la pièce", draggedOut && !helmet2.m_equipped && helmet2.m_gridPos == freeSpot, $"déposé={draggedOut}, équipé={helmet2.m_equipped}, en {helmet2.m_gridPos.x},{helmet2.m_gridPos.y} (attendu {freeSpot.x},{freeSpot.y})");

            // Tri : les pièces équipées restent dans leurs cases
            player.EquipItem(helmet2, false);
            yield return null;
            asm.GetType("InventoryMod.Grid").GetMethod("Sort", new[] { typeof(Player) }).Invoke(null, new object[] { player });
            yield return null;
            h.Check("Équipement.le tri laisse les cases d'équipement", helmet2.m_gridPos == new Vector2i(0, reserved) && chest.m_gridPos == new Vector2i(1, reserved) && legs.m_gridPos == new Vector2i(2, reserved),
                $"casque {helmet2.m_gridPos.x},{helmet2.m_gridPos.y}, torse {chest.m_gridPos.x},{chest.m_gridPos.y}, jambes {legs.m_gridPos.x},{legs.m_gridPos.y}");

            // Panneau : présent, cinq cases hors de la grille, rien recouvert (colonne d'outils, panneau de fabrication), capture
            yield return new WaitForSecondsRealtime(0.5f);
            var panel = GameObject.Find("EquipmentPanel");
            var panelRt = panel != null ? panel.GetComponent<RectTransform>() : null;
            int cells = panel != null ? panel.GetComponentsInChildren<InventoryElement>(false).Length : 0;
            var panelRect = ScreenRectOf(panelRt);
            var others = new List<KeyValuePair<string, Rect>>();
            foreach (var t in gui.m_player.GetComponentsInChildren<RectTransform>(false)) if (t.name.StartsWith("ModTool", StringComparison.Ordinal)) others.Add(new KeyValuePair<string, Rect>(t.name, ScreenRectOf(t)));
            others.Add(new KeyValuePair<string, Rect>("grille", ScreenRectOf(grid.GetComponent<RectTransform>())));
            if (gui.m_crafting != null) others.Add(new KeyValuePair<string, Rect>("fabrication", ScreenRectOf(gui.m_crafting)));
            var hits = others.Where(o => o.Value.width > 0 && o.Value.Overlaps(panelRect)).Select(o => o.Key).ToList();
            bool inScreen = panelRect.xMin >= 0 && panelRect.yMin >= 0 && panelRect.xMax <= Screen.width && panelRect.yMax <= Screen.height;
            h.Check("Équipement.panneau dessiné à côté de la grille", panel != null && panel.activeInHierarchy && cells == 5 && hits.Count == 0 && inScreen,
                $"panneau={(panel != null)}, cases={cells}, zone {panelRect.x:0},{panelRect.y:0} {panelRect.width:0}×{panelRect.height:0}, dans l'écran={inScreen}, recouvre : {(hits.Count == 0 ? "rien" : string.Join(" + ", hits))}");
            var tip = grid.m_tooltipAnchor; var pc = new Vector3[4]; if (panelRt != null) panelRt.GetWorldCorners(pc);
            h.Check("Équipement.info-bulle du jeu décalée à droite du panneau", tip != null && panelRt != null && tip.position.x >= Mathf.Max(pc[2].x, pc[3].x) - 1f, tip == null ? "ancre absente" : $"ancre x={tip.position.x:0}, bord droit du panneau x={(panelRt != null ? Mathf.Max(pc[2].x, pc[3].x) : 0f):0}");
            float contentH = grid.m_gridRoot.rect.height;
            h.Check("Équipement.la grille ne montre pas la ligne réservée", Mathf.Abs(contentH - (inv.GetHeight() - 1) * grid.m_elementSpace) < 1f, $"contenu {contentH:0} px pour {inv.GetHeight() - 1} lignes × {grid.m_elementSpace:0}");
            string shot = System.IO.Path.Combine(Paths.ConfigPath, "inventory_equipment.png");
            ScreenCapture.CaptureScreenshot(shot);
            yield return new WaitForSecondsRealtime(1f);
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.3f);

            // Nettoyage : pièces de test retirées et détruites, équipement d'origine remis
            var names = new HashSet<string> { "$item_helmet_leather", "$item_helmet_bronze", "$item_chest_leather", "$item_legs_leather" };
            foreach (var it in inv.GetAllItems().Where(i => names.Contains(i.m_shared.m_name) || ReferenceEquals(i, wood)).ToList())
            {
                if (it.m_equipped) player.UnequipItem(it, false);
                inv.RemoveItem(it);
            }
            if (prevHelmet != null && inv.ContainsItem(prevHelmet)) player.EquipItem(prevHelmet, false);
            if (prevChest != null && inv.ContainsItem(prevChest)) player.EquipItem(prevChest, false);
            if (prevLegs != null && inv.ContainsItem(prevLegs)) player.EquipItem(prevLegs, false);
            yield return null;
            h.Check("Équipement.nettoyage", !inv.GetAllItems().Any(i => i.m_gridPos.y == reserved && !i.m_equipped), $"objets restants dans la ligne réservée : {inv.GetAllItems().Count(i => i.m_gridPos.y == reserved)}");
        }
    }
}
