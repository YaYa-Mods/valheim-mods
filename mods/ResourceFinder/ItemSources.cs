using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ResourceFinder
{
    /// <summary>
    /// D'où vient un matériau : lu dans les tables de butin du jeu, pas écrit à la main. Pour chaque prefab du monde
    /// (créature, tas d'os, rocher, arbre et ses rondins, plante à cueillir, objet destructible, générateur de créatures),
    /// on relève ce qu'il donne ; on obtient pour chaque objet la liste des prefabs qui le produisent. Chaque matériau que
    /// le catalogue fixe ne couvre pas déjà devient une entrée « Matériaux » (visible dès que le personnage l'a ramassé
    /// une fois), cherchable par son nom : « os » → fragments d'os → squelettes et ossuaires.
    /// </summary>
    internal static class ItemSources
    {
        private static bool s_built;

        /// <summary>Ajoute les entrées « Matériaux » au catalogue, une seule fois, dès que le jeu a chargé ses prefabs.</summary>
        public static void EnsureBuilt()
        {
            if (s_built || ZNetScene.instance == null || ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0) return;
            s_built = true;
            try { Build(); }
            catch (Exception ex) { Plugin.Log.LogWarning("Matériaux : " + ex.Message); }
        }

        private static void Build()
        {
            var sources = new Dictionary<string, HashSet<string>>(); // objet (nom de prefab) → prefabs du monde qui le donnent
            void Add(GameObject item, string source)
            {
                if (item == null || item.GetComponent<ItemDrop>() == null) return;
                if (!sources.TryGetValue(item.name, out var set)) sources[item.name] = set = new HashSet<string>();
                set.Add(source);
            }
            void AddTable(DropTable t, string source) { if (t?.m_drops != null) foreach (var d in t.m_drops) Add(d.m_item, source); }
            // Rondins d'un arbre abattu et leurs sous-rondins : leur bois compte pour l'arbre
            void AddLogs(GameObject log, string source, int depth)
            {
                if (log == null || depth > 4) return;
                var tl = log.GetComponent<TreeLog>();
                if (tl == null) return;
                AddTable(tl.m_dropWhenDestroyed, source);
                AddLogs(tl.m_subLogPrefab, source, depth + 1);
            }

            var byName = new Dictionary<string, GameObject>();
            foreach (var p in ZNetScene.instance.m_prefabs) if (p != null) byName[p.name] = p;
            var creatureDrops = new Dictionary<string, List<GameObject>>();
            foreach (var p in byName.Values)
            {
                if (p.GetComponent<Piece>() != null || p.GetComponent<ItemDrop>() != null || p.GetComponent<Player>() != null) continue;
                var cd = p.GetComponent<CharacterDrop>();
                if (cd?.m_drops != null)
                {
                    creatureDrops[p.name] = cd.m_drops.Where(d => d.m_prefab != null).Select(d => d.m_prefab).ToList();
                    foreach (var d in cd.m_drops) Add(d.m_prefab, p.name);
                }
                var mr = p.GetComponent<MineRock>(); if (mr != null) AddTable(mr.m_dropItems, p.name);
                var mr5 = p.GetComponent<MineRock5>(); if (mr5 != null) AddTable(mr5.m_dropItems, p.name);
                var tb = p.GetComponent<TreeBase>(); if (tb != null) { AddTable(tb.m_dropWhenDestroyed, p.name); AddLogs(tb.m_logPrefab, p.name, 0); }
                var dd = p.GetComponent<DropOnDestroyed>(); if (dd != null && p.GetComponent<TreeLog>() == null) AddTable(dd.m_dropWhenDestroyed, p.name);
                var pk = p.GetComponent<Pickable>(); if (pk != null) { Add(pk.m_itemPrefab, p.name); AddTable(pk.m_extraDrops, p.name); }
                // Bloc intact (filon de cuivre, gros rocher) : touché ou cassé, il laisse place à sa version en morceaux ;
                // c'est le bloc intact que le monde contient, c'est donc lui la source du butin des morceaux
                var de = p.GetComponent<Destructible>();
                if (de != null)
                    foreach (var spawned in new[] { de.m_spawnWhenDamaged, de.m_spawnWhenDestroyed })
                    {
                        if (spawned == null) continue;
                        var s5 = spawned.GetComponent<MineRock5>(); if (s5 != null) AddTable(s5.m_dropItems, p.name);
                        var s1 = spawned.GetComponent<MineRock>(); if (s1 != null) AddTable(s1.m_dropItems, p.name);
                        var sd = spawned.GetComponent<DropOnDestroyed>(); if (sd != null) AddTable(sd.m_dropWhenDestroyed, p.name);
                    }
            }
            // Générateurs (tas d'os, nids) : ce que donnent les créatures qu'ils font apparaître
            foreach (var p in byName.Values)
            {
                var sa = p.GetComponent<SpawnArea>();
                if (sa?.m_prefabs == null) continue;
                foreach (var s in sa.m_prefabs)
                    if (s?.m_prefab != null && creatureDrops.TryGetValue(s.m_prefab.name, out var drops))
                        foreach (var d in drops) Add(d, p.name);
            }

            // Ce que le catalogue fixe couvre déjà (le minerai de cuivre a son entrée « Cuivre ») : pas de doublon
            var covered = new HashSet<string>();
            foreach (var e in Catalog.Entries)
            {
                if (!string.IsNullOrEmpty(e.Icon)) covered.Add(e.Icon);
                foreach (var p in e.Prefabs) { var it = Icons.ItemForPrefab(p); if (it != null) covered.Add(it.name); }
            }

            int added = 0;
            foreach (var kv in sources.OrderBy(k => k.Key))
            {
                if (covered.Contains(kv.Key)) continue;
                var drop = ObjectDB.instance.GetItemPrefab(kv.Key)?.GetComponent<ItemDrop>();
                if (drop == null) continue;
                string label = Localization.instance != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : kv.Key;
                if (string.IsNullOrEmpty(label) || label.StartsWith("$", StringComparison.Ordinal)) continue;
                if (Catalog.Entries.Any(e => string.Equals(e.Label, label, StringComparison.OrdinalIgnoreCase))) continue;
                Catalog.Entries.Add(new ResourceEntry(label, kv.Value.OrderBy(s => s).ToArray(), icon: kv.Key)
                {
                    Category = Category.Materiau,
                    ItemName = drop.m_itemData.m_shared.m_name,
                });
                added++;
            }
            Plugin.Log.LogInfo($"Matériaux : {added} entrées ajoutées ({sources.Count} objets donnés par le monde)");
        }

        /// <summary>Sources d'une entrée « Matériaux », pour les tests et le diagnostic.</summary>
        public static string[] SourcesOf(string label) => Catalog.Entries.FirstOrDefault(e => e.Category == Category.Materiau && e.Label == label)?.Prefabs ?? Array.Empty<string>();
    }
}
