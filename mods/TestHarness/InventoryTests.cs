using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;

namespace TestHarness
{
    /// <summary>Inventaire : 20 lignes avec panneau à 9 lignes et grille à défilement ; tri ; empilage ; capture.</summary>
    internal static class InventoryTests
    {
        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Inventory");
            var gridT = asm.GetType("InventoryMod.Grid");
            var inv = player.GetInventory();
            int before = inv.GetHeight();

            // 20 lignes, panneau plafonné, ScrollRect présent
            player.SetInventorySize(20);
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(0.8f);
            var gui = InventoryGui.instance;
            var scroll = gui.m_playerGrid.GetComponent<ScrollRect>();
            float panelH = gui.m_player.sizeDelta.y;
            float playerHeight = (float)typeof(InventoryGui).GetField("m_playerHeight", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gui);
            float gridH = (float)typeof(InventoryGui).GetField("m_invGridHeight", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gui);
            float expected9 = playerHeight + 5f * gridH;
            var mask = gui.m_playerGrid.GetComponent<UnityEngine.UI.RectMask2D>();
            h.Check("Inventaire.20 lignes + défilement", inv.GetHeight() == 20 && scroll != null && mask != null && mask.enabled && scroll.content == gui.m_playerGrid.m_gridRoot && Mathf.Abs(panelH - expected9) < 1f,
                $"lignes={inv.GetHeight()}, scroll={(scroll != null)}, masque actif={(mask != null && mask.enabled)}, panneau={panelH:0} (9 lignes = {expected9:0}), contenu={gui.m_playerGrid.m_gridRoot.sizeDelta}");

            // Tri : matériaux et arme placés en désordre en bas → après tri, familles croissantes de gauche à droite / haut en bas
            var added = new List<ItemDrop.ItemData>();
            ItemDrop.ItemData Add(string prefab, int stack, int x, int y)
            {
                var it = inv.AddItem(prefab, stack, 1, 0, 0L, "", false, false);
                if (it != null) { it.m_gridPos = new Vector2i(x, y); added.Add(it); }
                return it;
            }
            Add("Wood", 7, 0, 19); Add("Club", 1, 3, 18); Add("Stone", 5, 5, 17); Add("Wood", 9, 2, 16); Add("TrophyBoar", 1, 1, 15); Add("Raspberry", 4, 4, 14); Add("Mushroom", 3, 6, 13);
            // Mode de tri figé pour le test (la configuration persiste d'un run à l'autre)
            var sortCfg = (BepInEx.Configuration.ConfigEntryBase)asm.GetType("InventoryMod.Plugin").GetField("SortModeCfg", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            sortCfg.BoxedValue = Enum.Parse(sortCfg.SettingType, "Categorie");
            gridT.GetMethod("Sort", new[] { typeof(Player) }).Invoke(null, new object[] { player });
            var rows = inv.GetAllItems().Where(i => i.m_gridPos.y > 0).OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).ToList();
            var famM = gridT.GetMethod("Family", BindingFlags.NonPublic | BindingFlags.Static);
            bool ordered = true; int prev = -1;
            foreach (var it in rows) { int f = (int)famM.Invoke(null, new object[] { it }); if (f < prev) ordered = false; prev = f; }
            bool compact = rows.Select((it, i) => it.m_gridPos.x == i % inv.GetWidth() && it.m_gridPos.y == 1 + i / inv.GetWidth()).All(b => b);
            h.Check("Inventaire.tri par catégorie", ordered && compact, $"{rows.Count} objets hors barre, familles croissantes={ordered}, compact={compact}");
            // Mode Quantité : piles décroissantes ; mode Nom : ordre alphabétique
            var modeT = gridT.GetNestedType("SortMode", BindingFlags.NonPublic);
            var sortMode = gridT.GetMethod("Sort", new[] { typeof(Player), modeT });
            sortMode.Invoke(null, new object[] { player, Enum.Parse(modeT, "Quantite") });
            var byQty = inv.GetAllItems().Where(i => i.m_gridPos.y > 0).OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).Select(i => i.m_stack).ToList();
            bool qtyDesc = byQty.Zip(byQty.Skip(1), (a, b) => a >= b).All(x => x);
            sortMode.Invoke(null, new object[] { player, Enum.Parse(modeT, "Nom") });
            var byName = inv.GetAllItems().Where(i => i.m_gridPos.y > 0).OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).Select(i => Localization.instance.Localize(i.m_shared.m_name)).ToList();
            bool nameAsc = byName.Zip(byName.Skip(1), (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0).All(x => x);
            h.Check("Inventaire.tri par quantité et par nom", qtyDesc && nameAsc, $"quantités décroissantes={qtyDesc}, noms croissants={nameAsc}");

            // Catégorie mise en avant : sous-menu « catégorie … » → « Consommables » : triés en premier, les autres estompés
            {
                // Barre d'outils : [Empiler] [Tri] puis 7 icônes de catégorie (Toutes + 6) ; clic sur « Consommables »
                var catButtons = (IList)gridT.GetField("s_catButtons", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                bool subVisible = catButtons.Count == 7 && ((GameObject)catButtons[1].GetType().GetField("Go").GetValue(catButtons[1])).activeSelf;
                ((GameObject)catButtons[1].GetType().GetField("Go").GetValue(catButtons[1])).GetComponent<UnityEngine.UI.Button>().onClick.Invoke(); // Consommables
                yield return null;
                int focus = (int)gridT.GetField("FocusFamily", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                var first = inv.GetAllItems().Where(i => i.m_gridPos.y > 0).OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).ToList();
                int firstFam = first.Count > 0 ? (int)famM.Invoke(null, new object[] { first[0] }) : -1;
                int lastFam = first.Count > 0 ? (int)famM.Invoke(null, new object[] { first[first.Count - 1] }) : -1;
                var elementsList = (IList)typeof(InventoryGrid).GetField("m_elements", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gui.m_playerGrid);
                int dimmed = 0, full = 0;
                foreach (InventoryElement e in elementsList) if (e != null && e.m_used && e.m_icon != null) { if (e.m_icon.color.a < 0.5f) dimmed++; else full++; }
                h.Check("Inventaire.catégorie mise en avant", subVisible && focus == 0 && firstFam == 0 && lastFam != 0 && dimmed > 0 && full > 0, $"sous-menu={subVisible}, focus={focus}, 1er={firstFam}, dernier={lastFam}, estompés={dimmed}, pleins={full}");
                // Mode Masquer (défaut) : les cases hors catégorie sont invisibles (alpha 0) et non cliquables (UIInputHandler coupé)
                int hidden = 0, clickable = 0;
                foreach (InventoryElement e in elementsList) if (e != null && e.m_used && e.m_icon != null && e.m_icon.color.a < 0.01f) { hidden++; var ih = e.GetComponent<UIInputHandler>(); if (ih != null && ih.enabled) clickable++; }
                h.Check("Inventaire.catégorie : les autres objets sont masqués", hidden == dimmed && clickable == 0, $"masqués={hidden}/{dimmed}, encore cliquables={clickable}");
                yield return new WaitForSecondsRealtime(0.3f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.ConfigPath, "inventory_filter.png"));
                yield return new WaitForSecondsRealtime(0.8f);
                gridT.GetField("FocusFamily", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, -1);
            }

            // Empilage : une deuxième pile de bois est créée à la main (AddItem fusionne tout seul) → Empiler la fusionne
            var listF = typeof(Inventory).GetField("m_inventory", BindingFlags.NonPublic | BindingFlags.Instance);
            var rawList = (List<ItemDrop.ItemData>)listF.GetValue(inv);
            var wood = inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == "$item_wood");
            if (wood != null)
            {
                var second = wood.Clone(); second.m_stack = 5; second.m_gridPos = new Vector2i(6, 19);
                rawList.Add(second);
            }
            int woodStacksBefore = inv.GetAllItems().Count(i => i.m_shared.m_name == "$item_wood");
            int woodTotal = inv.CountItems("$item_wood");
            gridT.GetMethod("Stack").Invoke(null, new object[] { player });
            int woodStacksAfter = inv.GetAllItems().Count(i => i.m_shared.m_name == "$item_wood");
            h.Check("Inventaire.empilage", woodStacksBefore == 2 && woodStacksAfter == 1 && inv.CountItems("$item_wood") == woodTotal, $"piles de bois {woodStacksBefore}→{woodStacksAfter}, total {woodTotal}→{inv.CountItems("$item_wood")}");
            inv.RemoveItem("$item_wood", 5, -1, false);

            // Ramassage : un objet nouveau va dans la première case libre en partant du haut (hors barre d'action),
            // jamais tout en bas des 20 lignes comme le fait le jeu ; une arme, elle, va d'abord dans la barre.
            {
                var free = Enumerable.Range(1, inv.GetHeight() - 1).SelectMany(y => Enumerable.Range(0, inv.GetWidth()).Select(x => new Vector2i(x, y))).First(p => inv.GetItemAt(p.x, p.y) == null);
                var stonePrefab = ObjectDB.instance.GetItemPrefab("Flint");
                var picked = stonePrefab.GetComponent<ItemDrop>().m_itemData.Clone(); picked.m_dropPrefab = stonePrefab; picked.m_stack = 1;
                bool addedOk = inv.AddItem(picked);
                var at = picked.m_gridPos;
                h.Check("Inventaire.ramassage dans la première case libre", addedOk && at == free, $"attendu {free.x},{free.y}, obtenu {at.x},{at.y} (hauteur {inv.GetHeight()})");
                if (addedOk) inv.RemoveItem(picked);
                var barFree = Enumerable.Range(0, inv.GetWidth()).Select(x => new Vector2i(x, 0)).FirstOrDefault(p => inv.GetItemAt(p.x, 0) == null);
                var axePrefab = ObjectDB.instance.GetItemPrefab("AxeStone");
                var axe = axePrefab.GetComponent<ItemDrop>().m_itemData.Clone(); axe.m_dropPrefab = axePrefab; axe.m_stack = 1;
                bool axeOk = inv.AddItem(axe);
                h.Check("Inventaire.arme ramassée : barre d'action d'abord", axeOk && axe.m_gridPos.y == 0 && axe.m_gridPos == barFree, $"obtenu {axe.m_gridPos.x},{axe.m_gridPos.y}, première case libre de la barre {barFree.x},{barFree.y}");
                if (axeOk) inv.RemoveItem(axe);
            }

            // Capture puis nettoyage (objets ajoutés retirés, lignes restaurées si possible)
            gui.m_playerGrid.m_gridRoot.anchoredPosition = Vector2.zero; // en haut
            yield return new WaitForSecondsRealtime(0.3f);
            // Capture « humaine » : Consommables mis en avant (icône surlignée, estompage)
            var catButtons2 = (IList)gridT.GetField("s_catButtons", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            ((GameObject)catButtons2[1].GetType().GetField("Go").GetValue(catButtons2[1])).GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            {
                var ordered2 = inv.GetAllItems().Where(i => i.m_gridPos.y > 0).OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).Take(14).Select(i => $"{Localization.instance.Localize(i.m_shared.m_name)}({i.m_shared.m_itemType},F{famM.Invoke(null, new object[] { i })},x{i.m_stack})");
                Plugin.Log.LogInfo("[TEST] ordre après Consommables : " + string.Join(" | ", ordered2) + " ; focus=" + gridT.GetField("FocusFamily", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
            }
            yield return new WaitForSecondsRealtime(0.3f);
            string shot = System.IO.Path.Combine(Paths.ConfigPath, "inventory_scroll.png");
            ScreenCapture.CaptureScreenshot(shot);
            yield return new WaitForSecondsRealtime(1.5f);
            ((GameObject)catButtons2[0].GetType().GetField("Go").GetValue(catButtons2[0])).GetComponent<UnityEngine.UI.Button>().onClick.Invoke(); // Toutes
            gridT.GetField("FocusFamily", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, -1);
            InventoryGui.instance.Hide();
            foreach (var it in added.Where(i => inv.ContainsItem(i))) inv.RemoveItem(it);
            inv.RemoveItem("$item_wood", 16, -1, false);
            h.Check("Inventaire.capture", System.IO.File.Exists(shot), shot);
        }
    }
}
