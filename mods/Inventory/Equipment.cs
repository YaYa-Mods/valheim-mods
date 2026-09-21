using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using ModsCommon;

namespace InventoryMod
{
    /// <summary>
    /// Emplacements d'équipement : Tête, Torse, Jambes, Cape, Accessoire. Ce qui est équipé quitte la grille et
    /// s'affiche dans un petit panneau à côté d'elle, chaque pièce à sa place, comme le font les mods d'emplacements
    /// dédiés. Rien de nouveau dans la sauvegarde : les cinq cases sont la DERNIÈRE ligne de l'inventaire (une ligne
    /// de plus que le réglage InventoryRows), cachée dans la grille et redessinée dans le panneau. Mod retiré, cette
    /// ligne redevient une ligne ordinaire et tout est encore là.
    ///  - Équiper (clic droit, touche, roue) déplace la pièce dans sa case ; la retirer la renvoie dans la première
    ///    case libre de la grille (sinon elle reste là, simplement non équipée).
    ///  - Glisser une pièce sur sa case l'équipe ; une case n'accepte que son type ; glisser une pièce hors de sa case
    ///    la retire.
    ///  - Ramassage, tri, empilage ignorent ces cases.
    /// </summary>
    internal static class Equipment
    {
        internal static readonly ItemDrop.ItemData.ItemType[] Slots =
        {
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility,
        };
        private static readonly string[] s_labels = { "Tête", "Torse", "Jambes", "Cape", "Accessoire" };

        internal static bool Active => Plugin.Enabled.Value && Plugin.EquipmentSlots.Value;

        private static bool IsPlayerInventory(Inventory inv)
        {
            var p = Player.m_localPlayer;
            return p != null && inv != null && ReferenceEquals(inv, p.GetInventory());
        }

        /// <summary>Ligne réservée de cet inventaire (la dernière), ou -1 si ce n'est pas l'inventaire du joueur ou si les emplacements sont inactifs.</summary>
        internal static int ReservedRow(Inventory inv)
        {
            if (!Active || !IsPlayerInventory(inv) || inv.GetHeight() <= Plugin.InventoryRows.Value) return -1;
            return inv.GetHeight() - 1;
        }

        internal static int SlotOf(ItemDrop.ItemData item) => item == null ? -1 : Array.IndexOf(Slots, item.m_shared.m_itemType);
        internal static bool InReservedRow(Inventory inv, ItemDrop.ItemData item) => item != null && item.m_gridPos.y == ReservedRow(inv) && item.m_gridPos.y >= 0;

        // ------------------------------------------------------------------ placement

        /// <summary>Première case libre de la grille, hors ligne réservée. topFirst : la barre d'action d'abord (armes), sinon en dernier.</summary>
        internal static Vector2i FirstFree(Inventory inv, bool topFirst)
        {
            int w = inv.GetWidth(), h = inv.GetHeight(), reserved = ReservedRow(inv);
            int last = reserved >= 0 ? h - 1 : h;
            if (topFirst)
                for (int x = 0; x < w; x++) if (inv.GetItemAt(x, 0) == null) return new Vector2i(x, 0);
            for (int y = 1; y < last; y++)
                for (int x = 0; x < w; x++)
                    if (inv.GetItemAt(x, y) == null) return new Vector2i(x, y);
            if (!topFirst)
                for (int x = 0; x < w; x++) if (inv.GetItemAt(x, 0) == null) return new Vector2i(x, 0);
            return new Vector2i(-1, -1);
        }

        private static readonly System.Reflection.MethodInfo s_changed = AccessTools.Method(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) });
        private static void Changed(Inventory inv) { try { s_changed?.Invoke(inv, new object[] { true, false }); } catch { } }

        /// <summary>Une pièce équipée va dans sa case ; ce qui l'occupait prend sa place d'avant.</summary>
        private static void PlaceEquipped(Inventory inv, ItemDrop.ItemData item)
        {
            int slot = SlotOf(item), reserved = ReservedRow(inv);
            if (slot < 0 || reserved < 0) return;
            var target = new Vector2i(slot, reserved);
            if (item.m_gridPos == target) return;
            var other = inv.GetItemAt(target.x, target.y);
            var from = item.m_gridPos;
            if (other != null && other != item) other.m_gridPos = from;
            item.m_gridPos = target;
            Changed(inv);
        }

        /// <summary>Une pièce retirée quitte sa case pour la première place libre de la grille ; sans place, elle reste là.</summary>
        private static void PlaceUnequipped(Inventory inv, ItemDrop.ItemData item)
        {
            if (s_holdPlace || !InReservedRow(inv, item)) return;
            var free = FirstFree(inv, false);
            if (free.x < 0) return;
            item.m_gridPos = free;
            Changed(inv);
        }

        /// <summary>Remet chaque chose à sa place : pièces équipées dans leurs cases, le reste hors de la ligne réservée (première installation, apparition, réactivation).</summary>
        internal static void Sync(Player player)
        {
            var inv = player?.GetInventory();
            if (inv == null || ReservedRow(inv) < 0) return;
            int reserved = ReservedRow(inv);
            bool changed = false;
            foreach (var item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
            {
                int slot = SlotOf(item);
                var want = new Vector2i(slot, reserved);
                if (item.m_equipped && slot >= 0)
                {
                    if (item.m_gridPos == want) continue;
                    var other = inv.GetItemAt(want.x, want.y);
                    if (other != null) other.m_gridPos = item.m_gridPos;
                    item.m_gridPos = want; changed = true;
                }
                else if (item.m_gridPos.y == reserved && (!item.m_equipped || slot < 0 || item.m_gridPos.x != slot))
                {
                    var free = FirstFree(inv, item.IsWeapon());
                    if (free.x < 0) continue;
                    item.m_gridPos = free; changed = true;
                }
            }
            if (changed) Changed(inv);
        }

        /// <summary>Emplacements désactivés : tout ce qui est dans la ligne réservée redescend dans la grille, puis la ligne peut être retirée.</summary>
        internal static void Release(Player player)
        {
            var inv = player?.GetInventory();
            if (inv == null) return;
            int row = inv.GetHeight() - 1;
            bool changed = false;
            foreach (var item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
            {
                if (item.m_gridPos.y != row) continue;
                var free = new Vector2i(-1, -1);
                int w = inv.GetWidth();
                for (int y = 0; y < row && free.x < 0; y++) for (int x = 0; x < w; x++) if (inv.GetItemAt(x, y) == null) { free = new Vector2i(x, y); break; }
                if (free.x < 0) break;
                item.m_gridPos = free; changed = true;
            }
            if (changed) Changed(inv);
        }

        // ------------------------------------------------------------------ correctifs : équiper, retirer, glisser

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        [HarmonyPostfix]
        private static void Humanoid_EquipItem(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || !Active || !(__instance is Player p) || p != Player.m_localPlayer) return;
            try { PlaceEquipped(p.GetInventory(), item); } catch (Exception ex) { Plugin.Log.LogWarning("équipement : " + ex.Message); }
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        [HarmonyPostfix]
        private static void Humanoid_UnequipItem(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (!Active || item == null || !(__instance is Player p) || p != Player.m_localPlayer) return;
            try { PlaceUnequipped(p.GetInventory(), item); } catch (Exception ex) { Plugin.Log.LogWarning("équipement : " + ex.Message); }
        }

        // Glisser-déposer : une case réservée n'accepte que son type ; y poser une pièce l'équipe, l'en sortir la retire.
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        [HarmonyPrefix]
        private static bool InventoryGrid_DropItem_Prefix(InventoryGrid __instance, ItemDrop.ItemData item, Vector2i pos, ref bool __result, out DropState __state)
        {
            __state = default;
            var inv = __instance.GetInventory();
            int reserved = ReservedRow(inv);
            if (reserved < 0 || item == null) return true;
            var p = Player.m_localPlayer;
            var existing = inv.GetItemAt(pos.x, pos.y);
            bool fromReserved = item.m_gridPos.y == reserved && IsPlayerInventory(inv);
            __state = new DropState { From = item.m_gridPos, FromReserved = fromReserved, Existing = existing };
            if (pos.y == reserved && SlotOf(item) != pos.x) return Refuse(p, pos.x, ref __result);
            // Sortir une pièce de sa case sur un objet qui n'y a pas sa place : refusé (il faut une case libre ou une pièce du même type)
            if (fromReserved && pos.y != reserved && existing != null && existing != item && SlotOf(existing) != item.m_gridPos.x) return Refuse(p, item.m_gridPos.x, ref __result);
            // Le jeu remplace l'objet déplacé par une copie : ce qui est équipé doit être retiré AVANT le déplacement,
            // sinon le personnage garde en main une référence morte. La pièce reste où elle est, le déplacement suit.
            s_holdPlace = true;
            try
            {
                if (fromReserved && item.m_equipped && !(pos.y == reserved && pos.x == item.m_gridPos.x)) p.UnequipItem(item, false);
                if (pos.y == reserved && existing != null && existing != item && existing.m_equipped) p.UnequipItem(existing, false);
            }
            finally { s_holdPlace = false; }
            return true;
        }

        private static bool s_holdPlace;

        private static bool Refuse(Player p, int slot, ref bool result)
        {
            p?.Message(MessageHud.MessageType.Center, L.F("Case réservée : {0}", L.T(s_labels[Mathf.Clamp(slot, 0, s_labels.Length - 1)])));
            result = false;
            return false;
        }

        private struct DropState { public Vector2i From; public bool FromReserved; public ItemDrop.ItemData Existing; }

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        [HarmonyPostfix]
        private static void InventoryGrid_DropItem_Postfix(InventoryGrid __instance, ItemDrop.ItemData item, Vector2i pos, bool __result, DropState __state)
        {
            var p = Player.m_localPlayer;
            if (!__result || item == null || p == null) return;
            var inv = __instance.GetInventory();
            int reserved = ReservedRow(inv);
            try
            {
                // MoveItemToThis remplace l'objet par une copie : on relit ce qui se trouve vraiment dans les cases
                var pInv = p.GetInventory();
                var placed = inv.GetItemAt(pos.x, pos.y);
                if (reserved >= 0 && pos.y == reserved && placed != null && !placed.m_equipped) p.EquipItem(placed, true);
                else if (__state.FromReserved && placed != null && placed.m_equipped && !InReservedRow(pInv, placed)) p.UnequipItem(placed, true);
                // La pièce délogée (échange) revenue dans la grille était équipée dans sa case : on la retire
                if (__state.Existing != null && ReferenceEquals(inv, pInv))
                {
                    var swapped = pInv.GetItemAt(__state.From.x, __state.From.y);
                    if (swapped != null && swapped != placed && swapped.m_equipped && !InReservedRow(pInv, swapped) && SlotOf(swapped) >= 0) p.UnequipItem(swapped, true);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("équipement (glisser) : " + e.Message); }
        }

        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        private static void Player_OnSpawned(Player __instance) { if (__instance == Player.m_localPlayer) Sync(__instance); }

        // ------------------------------------------------------------------ panneau : la ligne réservée redessinée à côté de la grille

        private static GameObject s_panel;
        private static readonly List<GameObject> s_labelGos = new List<GameObject>();
        private static readonly Vector3[] s_corners = new Vector3[4];
        private const float Cell = 70f, Gap = 6f, LabelW = 120f, TitleH = 30f;

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        [HarmonyPostfix]
        private static void InventoryGrid_UpdateGui(InventoryGrid __instance)
        {
            var gui = InventoryGui.instance;
            if (gui == null || __instance != gui.m_playerGrid) return;
            var inv = __instance.GetInventory();
            int reserved = ReservedRow(inv);
            if (reserved < 0) { if (s_panel != null) s_panel.SetActive(false); return; }
            try
            {
                EnsurePanel(gui);
                s_panel.SetActive(true);
                var elements = Traverse.Create(__instance).Field("m_elements").GetValue<List<InventoryElement>>();
                if (elements == null) return;
                float space = __instance.m_elementSpace;
                foreach (var e in elements)
                {
                    if (e == null || e.Position.y != reserved) continue;
                    var go = (e as Component)?.gameObject;
                    if (go == null) continue;
                    var ert = go.GetComponent<RectTransform>();
                    if (ert.parent != s_panel.transform)
                    {
                        ert.SetParent(s_panel.transform, false);
                        ert.anchorMin = ert.anchorMax = new Vector2(0f, 1f); ert.pivot = new Vector2(0f, 1f);
                    }
                    int slot = Mathf.Clamp(e.Position.x, 0, Slots.Length - 1);
                    ert.anchoredPosition = new Vector2(0f, -TitleH - slot * (Cell + Gap));
                    if (e.Position.x >= Slots.Length) go.SetActive(false); // grille plus large que cinq colonnes : le surplus de la ligne n'est pas montré
                }
                // La grille ne montre pas la ligne réservée : son contenu fait une ligne de moins
                __instance.m_gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (inv.GetHeight() - 1) * space);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("panneau d'équipement : " + ex.Message); }
        }

        private static void EnsurePanel(InventoryGui gui)
        {
            if (s_panel != null) return;
            s_panel = new GameObject("EquipmentPanel", typeof(RectTransform));
            var rt = s_panel.GetComponent<RectTransform>();
            rt.SetParent(gui.m_player, false);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(26f + 46f + 28f, -8f); // à droite de la colonne d'outils
            rt.sizeDelta = new Vector2(Cell + Gap + LabelW, TitleH + Slots.Length * (Cell + Gap));
            ShiftTooltip(gui, true); // l'info-bulle du jeu se pose à droite de la grille, là où est le panneau : on la décale d'autant

            var font = FindFont("Valheim-AveriaSansLibre");
            var titleFont = FindFont("Valheim-Norsebold") ?? font;
            MakeLabel(rt, L.T("Équipement"), titleFont, 20, Theme.Accent, new Vector2(0f, 0f), new Vector2(Cell + Gap + LabelW, TitleH), TMPro.TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < Slots.Length; i++)
                MakeLabel(rt, L.T(s_labels[i]), font, 16, Theme.MutedColor, new Vector2(Cell + Gap, -TitleH - i * (Cell + Gap)), new Vector2(LabelW, Cell), TMPro.TextAlignmentOptions.MidlineLeft);
        }

        private static TMPro.TMP_FontAsset FindFont(string name)
        {
            foreach (var f in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>()) if (f != null && f.name == name) return f;
            return null;
        }

        private static void MakeLabel(RectTransform parent, string text, TMPro.TMP_FontAsset font, float size, Color color, Vector2 pos, Vector2 dim, TMPro.TextAlignmentOptions align)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos; rt.sizeDelta = dim;
            var t = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size; t.color = color; t.text = text; t.alignment = align; t.raycastTarget = false;
            t.enableWordWrapping = false; t.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            s_labelGos.Add(go);
        }

        /// <summary>Le réglage change en jeu : on remet tout en place (ou on libère la ligne) puis on ajuste le nombre de lignes.</summary>
        internal static void OnToggle(Player player)
        {
            if (player == null) return;
            if (InventoryGui.instance != null) ShiftTooltip(InventoryGui.instance, Active);
            if (!Active) Release(player);
            Patches.ApplyRows(player);
            if (Active) Sync(player);
            if (s_panel != null) s_panel.SetActive(Active && InventoryGui.IsVisible());
        }

        private static Vector2? s_tipOrigin;
        /// <summary>L'ancre de l'info-bulle de la grille du joueur, décalée de la largeur du panneau (et remise en place quand il disparaît).</summary>
        private static void ShiftTooltip(InventoryGui gui, bool shifted)
        {
            var anchor = gui.m_playerGrid != null ? gui.m_playerGrid.m_tooltipAnchor : null;
            if (anchor == null) return;
            if (s_tipOrigin == null) s_tipOrigin = anchor.anchoredPosition;
            anchor.anchoredPosition = shifted ? s_tipOrigin.Value + new Vector2(Cell + Gap + LabelW + 30f, 0f) : s_tipOrigin.Value;
        }
    }
}
