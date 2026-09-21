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
    /// Recyclage : ce qui est proposé (fabriqué, non équipé), ce qui est rendu (part du coût lu dans la recette,
    /// améliorations et piles comprises, arrondi vers le bas), ce qui se passe quand on recycle (objet retiré,
    /// matériaux reçus, message), et la fenêtre (liste, capture, chasse les autres fenêtres).
    /// </summary>
    internal static class RecycleTests
    {
        private static ItemDrop.ItemData Give(Inventory inv, string prefab, int stack = 1, int quality = 1)
        {
            var go = ObjectDB.instance.GetItemPrefab(prefab);
            var item = go.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = go; item.m_stack = stack; item.m_quality = quality;
            return inv.AddItem(item) ? item : null;
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Recycle");
            if (asm == null) { h.Check("Recyclage.mod chargé", false, "assembly Recycle absent"); yield break; }
            var t = asm.GetType("Recycle.Plugin");
            var ratioCfg = (BepInEx.Configuration.ConfigEntry<float>)t.GetField("Ratio", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            float prevRatio = ratioCfg.Value; ratioCfg.Value = 0.34f;
            var recipeOf = t.GetMethod("RecipeOf", BindingFlags.NonPublic | BindingFlags.Static);
            var yieldOf = t.GetMethod("YieldOf", BindingFlags.NonPublic | BindingFlags.Static);
            var recycle = t.GetMethod("Recycle", BindingFlags.NonPublic | BindingFlags.Static);
            var inv = player.GetInventory();

            // Hache de bronze : 4 bois, 8 bronze, 2 lambeaux de cuir dans le jeu ; un tiers arrondi vers le bas
            var axe = Give(inv, "AxeBronze");
            var recipe = recipeOf.Invoke(null, new object[] { axe }) as Recipe;
            string costText = recipe == null ? "pas de recette" : string.Join(" + ", recipe.m_resources.Select(r => $"{r.GetAmount(1)} × {r.m_resItem?.name}"));
            Plugin.Log.LogInfo($"[TEST] recette AxeBronze : {costText}, amélioration niv. 2 : {(recipe == null ? "" : string.Join(" + ", recipe.m_resources.Select(r => $"{r.GetAmount(2)} × {r.m_resItem?.name}")))}");
            var yield1 = recipe == null ? null : (List<KeyValuePair<ItemDrop, int>>)yieldOf.Invoke(null, new object[] { axe, recipe });
            bool yieldOk = recipe != null && yield1.All(y => y.Value == Mathf.FloorToInt(recipe.m_resources.First(r => r.m_resItem == y.Key).GetAmount(1) * 0.34f + 0.0001f))
                && recipe.m_resources.Where(r => Mathf.FloorToInt(r.GetAmount(1) * 0.34f + 0.0001f) > 0).Count() == yield1.Count;
            h.Check("Recyclage.rendement = un tiers du coût, arrondi vers le bas", yieldOk, $"coût {costText} → {(yield1 == null ? "?" : string.Join(", ", yield1.Select(y => $"{y.Value} × {y.Key.name}")))}");

            // Niveau 3 : le coût des améliorations (niveaux 2 et 3) s'ajoute
            var axe3 = Give(inv, "AxeBronze", 1, 3);
            var yield3 = (List<KeyValuePair<ItemDrop, int>>)yieldOf.Invoke(null, new object[] { axe3, recipe });
            int bronze1 = yield1.Where(y => y.Key.name == "Bronze").Select(y => y.Value).FirstOrDefault(), bronze3 = yield3.Where(y => y.Key.name == "Bronze").Select(y => y.Value).FirstOrDefault();
            var bronzeReq = recipe.m_resources.First(r => r.m_resItem != null && r.m_resItem.name == "Bronze");
            int expected3 = Mathf.FloorToInt((bronzeReq.GetAmount(1) + bronzeReq.GetAmount(2) + bronzeReq.GetAmount(3)) * 0.34f + 0.0001f);
            h.Check("Recyclage.niveau 3 rend aussi une part des améliorations", bronze3 == expected3 && bronze3 > bronze1, $"bronze niv. 1 → {bronze1}, niv. 3 → {bronze3} (attendu {expected3})");

            // Équipé : jamais proposé ; matériau brut : pas de recette ; bois : jamais
            player.EquipItem(axe3, false);
            yield return null;
            bool equippedHidden = recipeOf.Invoke(null, new object[] { axe3 }) == null;
            player.UnequipItem(axe3, false);
            var wood = Give(inv, "Wood", 5);
            bool woodHidden = recipeOf.Invoke(null, new object[] { wood }) == null;
            h.Check("Recyclage.l'équipé et le bois ne sont pas proposés", equippedHidden && woodHidden, $"hache équipée proposée={!equippedHidden}, bois proposé={!woodHidden}");

            // Flèches (pile) : une fabrication en donne plusieurs, le coût se répartit ; la pile entière compte
            var arrows = Give(inv, "ArrowFlint", 40);
            var arrowRecipe = recipeOf.Invoke(null, new object[] { arrows }) as Recipe;
            var arrowYield = arrowRecipe == null ? null : (List<KeyValuePair<ItemDrop, int>>)yieldOf.Invoke(null, new object[] { arrows, arrowRecipe });
            bool arrowsOk = arrowRecipe != null && arrowYield.Count > 0 && arrowYield.All(y => y.Value == Mathf.FloorToInt(arrowRecipe.m_resources.First(r => r.m_resItem == y.Key).GetAmount(1) * 40f / Mathf.Max(1, arrowRecipe.m_amount) * 0.34f + 0.0001f));
            h.Check("Recyclage.pile de flèches : coût réparti par fabrication", arrowsOk, arrowRecipe == null ? "pas de recette" : $"recette ×{arrowRecipe.m_amount}, 40 flèches → {string.Join(", ", arrowYield.Select(y => $"{y.Value} × {y.Key.name}"))}");

            // Recycler pour de vrai : la hache disparaît, les matériaux arrivent
            int bronzeBefore = inv.CountItems("$item_bronze"), woodBefore = inv.CountItems("$item_wood");
            bool done = (bool)recycle.Invoke(null, new object[] { axe });
            yield return null;
            int bronzeGot = inv.CountItems("$item_bronze") - bronzeBefore, woodGot = inv.CountItems("$item_wood") - woodBefore;
            int expWood = yield1.Where(y => y.Key.name == "Wood").Select(y => y.Value).FirstOrDefault();
            h.Check("Recyclage.la hache disparaît, les matériaux arrivent", done && !inv.ContainsItem(axe) && bronzeGot == bronze1 && woodGot == expWood, $"fait={done}, hache encore là={inv.ContainsItem(axe)}, bronze +{bronzeGot} (attendu {bronze1}), bois +{woodGot} (attendu {expWood})");

            // Fenêtre : s'ouvre, liste ce qui reste (hache niv. 3, flèches), capture, se ferme
            t.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            yield return new WaitForSecondsRealtime(1f);
            bool open = (bool)t.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var inst = UnityEngine.Object.FindObjectOfType(t);
            var entries = (IList)t.GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(inst);
            var listed = new List<string>();
            foreach (var e in entries) listed.Add(((ItemDrop.ItemData)e.GetType().GetField("Item").GetValue(e)).m_shared.m_name);
            string shot = System.IO.Path.Combine(Paths.ConfigPath, "recycle_window.png");
            ScreenCapture.CaptureScreenshot(shot);
            yield return new WaitForSecondsRealtime(1f);
            t.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            yield return null;
            bool closed = !(bool)t.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            h.Check("Recyclage.fenêtre : hache et flèches listées, pas le bois", open && closed && listed.Contains("$item_axe_bronze") && listed.Contains("$item_arrow_flint") && !listed.Contains("$item_wood"), $"ouverte={open}, fermée={closed}, listés : {string.Join(", ", listed)}");
            h.Check("Recyclage.capture", System.IO.File.Exists(shot), shot);

            // Nettoyage
            foreach (var it in new[] { axe3, wood, arrows }) if (it != null && inv.ContainsItem(it)) inv.RemoveItem(it);
            if (bronzeGot > 0) inv.RemoveItem("$item_bronze", bronzeGot, -1, false);
            if (woodGot > 0) inv.RemoveItem("$item_wood", woodGot, -1, false);
            inv.RemoveItem("$item_leatherscraps", yield1.Where(y => y.Key.name == "LeatherScraps").Select(y => y.Value).FirstOrDefault(), -1, false);
            ratioCfg.Value = prevRatio;
        }
    }
}
