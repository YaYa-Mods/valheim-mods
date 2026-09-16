using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace InventoryMod
{
    /// <summary>
    /// Mort sans perte :
    ///  - KeepInventoryOnDeath : la création de la pierre tombale est court-circuitée (c'est exactement ce que fait le
    ///    jeu avec sa clé interne DeathKeepInventory, que l'interface des modificateurs n'expose pas). Tout reste sur
    ///    le joueur ; compétences et nourriture suivent la règle du curseur du monde.
    ///  - RecoverTombstones : à l'apparition, les tombes du joueur encore dans le monde (mort avant l'activation de
    ///    l'option, ou option coupée) sont vidées à distance dans son inventaire, puis supprimées. Le contenu est lu
    ///    directement dans le ZDO (chaîne "items" du conteneur), pas besoin que la zone soit chargée.
    /// </summary>
    internal static class Death
    {
        [HarmonyPatch(typeof(Player), "CreateTombStone")]
        [HarmonyPrefix]
        private static bool Player_CreateTombStone(Player __instance)
        {
            if (!Plugin.Enabled.Value || !Plugin.KeepInventoryOnDeath.Value || __instance != Player.m_localPlayer) return true;
            Plugin.Log.LogInfo("Mort : inventaire conservé (pas de pierre tombale)");
            return false;
        }

        // KeepSkillsOnDeath : pas de baisse de compétences à la mort (Skills.OnDeath = LowerAllSkills(m_DeathLowerFactor) +
        // message « compétences réduites »). Le curseur « pénalité de mort » du monde ne couvre pas ce cas quand on veut
        // garder l'inventaire ET les compétences : on court-circuite la méthode.
        [HarmonyPatch(typeof(Skills), "OnDeath")]
        [HarmonyPrefix]
        private static bool Skills_OnDeath(Skills __instance)
        {
            if (!Plugin.Enabled.Value || !Plugin.KeepSkillsOnDeath.Value) return true;
            // (Skills.m_player est privé : en solo/hôte seules les compétences du joueur local passent par ici)
            Plugin.Log.LogInfo("Mort : compétences conservées");
            return false;
        }

        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        private static void Player_OnSpawned(Player __instance)
        {
            if (!Plugin.Enabled.Value || !Plugin.RecoverTombstones.Value || __instance != Player.m_localPlayer) return;
            __instance.StartCoroutine(Recover(__instance));
        }

        private static IEnumerator Recover(Player player)
        {
            yield return new WaitForSeconds(3f);
            var man = ZDOMan.instance;
            if (man == null || player == null) yield break;
            long myId = Game.instance.GetPlayerProfile().GetPlayerID();

            var zdos = new List<ZDO>();
            int index = 0;
            while (!man.GetAllZDOsWithPrefabIterative("Player_tombstone", zdos, ref index)) yield return null;

            int recoveredItems = 0, tombs = 0;
            Plugin.Log.LogInfo($"Tombes : {zdos.Count} ZDO Player_tombstone dans le monde, mon id={myId}");
            foreach (var zdo in zdos)
            {
                if (zdo == null || !zdo.IsValid()) continue;
                byte[] data = zdo.GetByteArray(ZDOVars.s_items, null);
                Plugin.Log.LogInfo($"Tombe {zdo.m_uid} : owner={zdo.GetLong(ZDOVars.s_owner, 0L)} name={zdo.GetString(ZDOVars.s_ownerName, "")} items={(data?.Length ?? 0)} octets");
                if (zdo.GetLong(ZDOVars.s_owner, 0L) != myId) continue;
                if (data == null || data.Length == 0) continue;

                var tomb = new Inventory("tomb", null, 8, 9);
                try { tomb.Load(new ZPackage(data)); }
                catch (System.Exception ex) { Plugin.Log.LogWarning($"Tombe illisible : {ex.Message}"); continue; }

                var inv = player.GetInventory();
                // 1. Vider la tombe AVANT de donner les objets : aucun doublon possible même si la suppression échoue.
                var empty = new ZPackage(); new Inventory("tomb", null, 8, 9).Save(empty);
                zdo.SetOwner(ZDOMan.GetSessionID());
                zdo.Set(ZDOVars.s_items, empty.GetArray());
                var moved = new HashSet<ItemDrop.ItemData>();
                foreach (var item in tomb.GetAllItems())
                {
                    if (inv.AddItem(item.Clone())) { moved.Add(item); recoveredItems++; }
                    else Plugin.Log.LogWarning($"Inventaire plein : {item.m_shared.m_name} ×{item.m_stack} laissé dans la tombe");
                }
                if (moved.Count == tomb.GetAllItems().Count)
                {
                    // 2. Supprimer la tombe (DestroyZDO exige d'en être le propriétaire réseau : fait juste au-dessus).
                    man.DestroyZDO(zdo);
                    tombs++;
                }
                else
                {
                    // Réécrire la tombe avec ce qui n'a pas pu être récupéré
                    var rest = new Inventory("tomb", null, 8, 9);
                    foreach (var item in tomb.GetAllItems()) if (!moved.Contains(item)) rest.AddItem(item.Clone());
                    var pkg = new ZPackage(); rest.Save(pkg); zdo.Set(ZDOVars.s_items, pkg.GetArray());
                }
            }
            if (recoveredItems > 0)
            {
                player.Message(MessageHud.MessageType.Center, L.F("{0} objet(s) récupéré(s) de votre tombe", recoveredItems));
                Plugin.Log.LogInfo($"Tombes récupérées à distance : {tombs}, objets : {recoveredItems}");
            }
        }
    }
}
