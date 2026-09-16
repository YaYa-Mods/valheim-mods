using System;
using System.Collections.Generic;
using HarmonyLib;

namespace InventoryMod
{
    /// <summary>
    /// Filet de sécurité inspiré d'« Equipment and Quick Slots » (RandyKnapp) : à chaque sauvegarde du personnage,
    /// l'inventaire complet est aussi sérialisé (avec le sérialiseur du jeu, Inventory.Save) dans
    /// Player.m_customData, des données que le jeu conserve sans les interpréter, donc qui survivent même au
    /// retrait du mod. Au chargement, on compare l'inventaire chargé à cette copie : le jeu ayant sauvegardé les
    /// deux au même instant, toute différence est une perte survenue AU CHARGEMENT (pile tronquée par un max plus
    /// bas, objet hors grille ou de prefab inconnu détruit) et peut être restaurée sans risque de doublon.
    /// </summary>
    internal static class Backup
    {
        private const string Key = "vmods.inventory.backup";
        private const int Version = 1;

        private static readonly AccessTools.FieldRef<Inventory, List<ItemDrop.ItemData>> s_items =
            AccessTools.FieldRefAccess<Inventory, List<ItemDrop.ItemData>>("m_inventory");
        private static readonly Action<Inventory, bool, bool> s_changed =
            AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) }));

        // ---- écriture : juste avant Player.Save ----
        [HarmonyPatch(typeof(Player), nameof(Player.Save))]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        private static void Player_Save(Player __instance)
        {
            if (!Plugin.BackupEnabled.Value || __instance != Player.m_localPlayer) return;
            try
            {
                var inv = __instance.GetInventory();
                var pkg = new ZPackage();
                inv.Save(pkg);
                var env = new ZPackage();
                env.Write(Version);
                env.Write(DateTime.Now.ToString("u"));
                env.Write(inv.GetWidth());
                env.Write(inv.GetHeight());
                env.WriteCompressed(pkg);
                __instance.m_customData[Key] = env.GetBase64();
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Sauvegarde de secours de l'inventaire impossible : {ex.Message}"); }
        }

        // ---- lecture : juste après Player.Load ----
        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        [HarmonyPostfix]
        private static void Player_Load(Player __instance)
        {
            if (!Plugin.BackupEnabled.Value) return;
            if (!__instance.m_customData.TryGetValue(Key, out var b64) || string.IsNullOrEmpty(b64)) return;
            try
            {
                var env = new ZPackage(b64);
                if (env.ReadInt() != Version) return;
                string date = env.ReadString();
                int w = env.ReadInt(), h = env.ReadInt();
                var backup = new Inventory(Key, null, w, h);
                backup.Load(env.ReadCompressedPackage());

                var inv = __instance.GetInventory();
                var items = s_items(inv);
                int restoredStacks = 0, restoredItems = 0;

                foreach (var b in backup.GetAllItems())
                {
                    var cur = inv.GetItemAt(b.m_gridPos.x, b.m_gridPos.y);
                    if (cur != null && cur.m_shared.m_name == b.m_shared.m_name && cur.m_quality == b.m_quality)
                    {
                        if (cur.m_stack < b.m_stack)
                        {
                            Plugin.Log.LogWarning($"Restauration : {b.m_shared.m_name} en ({b.m_gridPos.x},{b.m_gridPos.y}) pile {cur.m_stack} → {b.m_stack}");
                            cur.m_stack = b.m_stack;
                            restoredStacks++;
                        }
                        continue;
                    }
                    if (cur != null) continue; // case occupée par autre chose : on ne touche pas

                    var clone = b.Clone();
                    clone.m_gridPos = b.m_gridPos;
                    items.Add(clone);
                    restoredItems++;
                    Plugin.Log.LogWarning($"Restauration : {b.m_shared.m_name} ×{b.m_stack} remis en ({b.m_gridPos.x},{b.m_gridPos.y})");
                }

                if (restoredStacks + restoredItems > 0)
                {
                    s_changed(inv, true, false);
                    Plugin.Log.LogWarning($"Inventaire restauré depuis la copie de secours du {date} : {restoredStacks} pile(s), {restoredItems} objet(s)");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Lecture de la copie de secours impossible : {ex.Message}"); }
        }
    }
}
