using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TestHarness
{
    /// <summary>
    /// Le Recycleur (meuble) : prefab enregistré et proposé par le marteau (onglet Meubles, 10 bois + 10 pierre),
    /// posé dans le monde, ouvert comme un coffre avec un bouton « Recycler » qui n'écrase rien ; deux clics
    /// transforment le contenu recyclable en matériaux laissés dedans, le reste ne bouge pas.
    /// </summary>
    internal static class RecyclerTests
    {
        private static Rect ScreenRectOf(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return Rect.zero;
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            float minX = Mathf.Min(c[0].x, c[2].x), maxX = Mathf.Max(c[0].x, c[2].x), minY = Mathf.Min(c[0].y, c[2].y), maxY = Mathf.Max(c[0].y, c[2].y);
            return new Rect(minX, Screen.height - maxY, maxX - minX, maxY - minY);
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var prefab = ZNetScene.instance.GetPrefab("vmods_recycler");
            var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            var cont = prefab != null ? prefab.GetComponent<Container>() : null;
            h.Check("Recycleur.prefab enregistré", prefab != null && piece != null && cont != null && prefab.GetComponent<WearNTear>() != null && prefab.GetComponent<ZNetView>() != null,
                prefab == null ? "prefab absent de ZNetScene" : $"Piece={(piece != null)}, Container={(cont != null)} {cont?.m_width}×{cont?.m_height}, WearNTear={(prefab.GetComponent<WearNTear>() != null)}");
            if (prefab == null || piece == null || cont == null) yield break;

            var hammer = ObjectDB.instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            bool inHammer = hammer != null && hammer.m_pieces.Contains(prefab);
            string cost = string.Join(" + ", piece.m_resources.Select(r => $"{r.m_amount} × {r.m_resItem?.name}"));
            h.Check("Recycleur.dans le marteau, onglet Meubles, 10 bois + 10 pierre", inHammer && piece.m_category == Piece.PieceCategory.Furniture && piece.m_icon != null && cost == "10 × Wood + 10 × Stone" && piece.m_craftingStation != null,
                $"marteau={inHammer}, onglet={piece.m_category}, icône={(piece.m_icon != null)}, coût {cost}, station={(piece.m_craftingStation != null ? piece.m_craftingStation.name : "aucune")}");
            h.Check("Recycleur.pas un coffre à butin", !cont.m_autoDestroyEmpty && (cont.m_defaultItems == null || cont.m_defaultItems.m_drops == null || cont.m_defaultItems.m_drops.Count == 0) && prefab.GetComponent<RandomSpawn>() == null,
                $"autodestruction={cont.m_autoDestroyEmpty}, butin={cont.m_defaultItems?.m_drops?.Count ?? 0}, apparition aléatoire={(prefab.GetComponent<RandomSpawn>() != null)}");

            // Posé dans le monde, comme si le marteau l'avait placé
            var placed = UnityEngine.Object.Instantiate(prefab, player.transform.position + player.transform.right * 3f, Quaternion.identity);
            yield return new WaitForSeconds(1f);
            var container = placed.GetComponent<Container>();
            var inv = container.GetInventory();
            var gui = InventoryGui.instance;
            try
            {
                h.Check("Recycleur.posé et en réseau", placed.GetComponent<ZNetView>().IsValid() && inv != null && inv.GetWidth() == 8 && inv.GetHeight() == 4, $"nview valide={placed.GetComponent<ZNetView>().IsValid()}, inventaire {inv?.GetWidth()}×{inv?.GetHeight()}");
                // Contenu : une hache de bronze (recyclable), une pile de bois (jamais recyclée)
                inv.AddItem("AxeBronze", 1, 1, 0, 0L, "", false, false);
                inv.AddItem("Wood", 5, 1, 0, 0L, "", false, false);
                gui.Show(container, 1);
                yield return new WaitForSecondsRealtime(0.8f);
                var button = GameObject.Find("RecycleAllButton");
                var brt = button != null ? button.GetComponent<RectTransform>() : null;
                var bRect = ScreenRectOf(brt);
                var others = new List<KeyValuePair<string, Rect>>
                {
                    new KeyValuePair<string, Rect>("tout prendre", ScreenRectOf(gui.m_takeAllButton?.GetComponent<RectTransform>())),
                    new KeyValuePair<string, Rect>("tout empiler", ScreenRectOf(gui.m_stackAllButton?.GetComponent<RectTransform>())),
                    new KeyValuePair<string, Rect>("grille du coffre", ScreenRectOf(AccessTools.Field(typeof(InventoryGui), "m_containerGrid")?.GetValue(gui) is InventoryGrid cg ? cg.GetComponent<RectTransform>() : null)),
                };
                var hits = others.Where(o => o.Value.width > 0 && o.Value.Overlaps(bRect)).Select(o => o.Key).ToList();
                var panel = ScreenRectOf(gui.m_container);
                bool inside = panel.width > 0 && bRect.width > 0 && bRect.xMin >= panel.xMin - 2f && bRect.yMin >= panel.yMin - 2f && bRect.xMax <= panel.xMax + 2f && bRect.yMax <= panel.yMax + 2f;
                h.Check("Recycleur.panneau du coffre (4 lignes) entièrement à l'écran", panel.width > 0 && panel.yMin >= -1f && panel.yMax <= Screen.height + 1f && panel.xMax <= Screen.width + 1f, $"panneau {panel.x:0},{panel.y:0} {panel.width:0}×{panel.height:0}, écran {Screen.width}×{Screen.height}, panneau joueur {ScreenRectOf(gui.m_player).height:0} px de haut");
                h.Check("Recycleur.bouton dans le panneau, n'écrase rien", button != null && button.activeInHierarchy && hits.Count == 0 && inside,
                    $"bouton={(button != null && button.activeInHierarchy)}, zone {bRect.x:0},{bRect.y:0} {bRect.width:0}×{bRect.height:0}, dans le panneau={inside}, recouvre : {(hits.Count == 0 ? "rien" : string.Join(" + ", hits))}");
                string shot = System.IO.Path.Combine(Paths.ConfigPath, "recycler_piece.png");
                ScreenCapture.CaptureScreenshot(shot);
                yield return new WaitForSecondsRealtime(1f);

                // Deux clics : le premier arme, le second recycle
                var btn = button != null ? button.GetComponent<Button>() : null;
                btn?.onClick.Invoke();
                yield return null;
                var txt = button != null ? button.GetComponentInChildren<TMPro.TMP_Text>() : null;
                yield return null;
                string armedText = txt != null ? txt.text : "";
                btn?.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.5f);
                bool axeGone = !inv.GetAllItems().Any(i => i.m_shared.m_name == "$item_axe_bronze");
                int bronze = inv.CountItems("$item_bronze"), wood = inv.CountItems("$item_wood");
                h.Check("Recycleur.deux clics : hache recyclée dedans, bois intact", armedText.Length > 0 && axeGone && bronze == 2 && wood == 6,
                    $"texte armé « {armedText} », hache partie={axeGone}, bronze={bronze} (attendu 2), bois={wood} (attendu 5 + 1)");
                h.Check("Recycleur.capture", System.IO.File.Exists(shot), shot);
                gui.Hide();
                yield return new WaitForSecondsRealtime(0.4f);
                bool hidden = button == null || !button.activeInHierarchy;
                h.Check("Recycleur.bouton retiré quand le meuble est fermé", hidden, $"actif={!hidden}");
            }
            finally { ZNetScene.instance.Destroy(placed); }
        }
    }
}
