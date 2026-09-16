using System.Collections.Generic;
using UnityEngine;

namespace ResourceFinder
{
    /// <summary>
    /// Icônes des ressources, tirées du jeu : un item a son icône ; une ressource du monde (veine, buisson, arbre)
    /// est représentée par l'item qu'elle laisse tomber (table de butin du prefab). Le même item sert à savoir si
    /// la ressource est « découverte » (matériau connu du joueur).
    /// </summary>
    internal static class Icons
    {
        private static readonly Dictionary<string, ItemDrop> s_itemCache = new Dictionary<string, ItemDrop>();

        /// <summary>Item représentant un prefab : lui-même s'il est un item, sinon son butin. Null si introuvable.</summary>
        public static ItemDrop ItemForPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName) || ZNetScene.instance == null) return null;
            if (s_itemCache.TryGetValue(prefabName, out var cached)) return cached;
            ItemDrop item = null;
            try
            {
                var go = ZNetScene.instance.GetPrefab(prefabName.GetStableHashCode());
                item = go != null ? Resolve(go, 0) : null;
            }
            catch { item = null; }
            s_itemCache[prefabName] = item;
            return item;
        }

        /// <summary>Icône d'un prefab (item ou objet du monde), null si introuvable.</summary>
        public static Sprite ForPrefab(string prefabName)
        {
            var item = ItemForPrefab(prefabName);
            return item != null ? item.m_itemData?.GetIcon() : null;
        }

        /// <summary>Icône d'une entrée du catalogue : item explicite, sinon premier prefab résolu.</summary>
        public static Sprite ForEntry(ResourceEntry e)
        {
            if (e == null) return null;
            if (!string.IsNullOrEmpty(e.Icon)) { var s = ForPrefab(e.Icon); if (s != null) return s; }
            foreach (var p in e.Prefabs) { var s = ForPrefab(p); if (s != null) return s; }
            return null;
        }

        private static ItemDrop Resolve(GameObject go, int depth)
        {
            if (go == null || depth > 3) return null;

            var item = go.GetComponent<ItemDrop>();
            if (item != null) return item;

            var pickable = go.GetComponent<Pickable>();
            if (pickable != null && pickable.m_itemPrefab != null) return Resolve(pickable.m_itemPrefab, depth + 1);

            // Rocher de surface (rock4_copper…) : un Destructible qui laisse place au vrai gisement (MineRock_Copper)
            var destructible = go.GetComponent<Destructible>();
            if (destructible != null && destructible.m_spawnWhenDestroyed != null) { var r = Resolve(destructible.m_spawnWhenDestroyed, depth + 1); if (r != null) return r; }

            var rock5 = go.GetComponent<MineRock5>();
            if (rock5 != null) return FromTable(rock5.m_dropItems, depth);
            var rock = go.GetComponent<MineRock>();
            if (rock != null) return FromTable(rock.m_dropItems, depth);

            var log = go.GetComponent<TreeLog>();
            if (log != null)
                return FromTable(log.m_dropWhenDestroyed, depth) ?? (log.m_subLogPrefab != null ? Resolve(log.m_subLogPrefab, depth + 1) : null);

            var tree = go.GetComponent<TreeBase>();
            if (tree != null)
                return (tree.m_logPrefab != null ? Resolve(tree.m_logPrefab, depth + 1) : null) ?? FromTable(tree.m_dropWhenDestroyed, depth);

            var drop = go.GetComponent<DropOnDestroyed>();
            if (drop != null) return FromTable(drop.m_dropWhenDestroyed, depth);

            // Créature : son trophée de préférence, sinon son premier butin
            var cdrop = go.GetComponent<CharacterDrop>();
            if (cdrop?.m_drops != null)
            {
                ItemDrop first = null;
                foreach (var d in cdrop.m_drops)
                {
                    var it = d.m_prefab != null ? d.m_prefab.GetComponent<ItemDrop>() : null;
                    if (it == null) continue;
                    if (d.m_prefab.name.StartsWith("Trophy")) return it;
                    if (first == null) first = it;
                }
                return first;
            }

            return null;
        }

        /// <summary>Premier butin de la table, en préférant autre chose que la pierre (un gisement de cuivre donne pierre + minerai).</summary>
        private static ItemDrop FromTable(DropTable table, int depth)
        {
            if (table?.m_drops == null) return null;
            ItemDrop stone = null;
            foreach (var d in table.m_drops)
            {
                var s = d.m_item != null ? Resolve(d.m_item, depth + 1) : null;
                if (s == null) continue;
                if (s.m_itemData?.m_shared?.m_name == "$item_stone") { if (stone == null) stone = s; continue; }
                return s;
            }
            return stone;
        }

        /// <summary>Dessine un sprite (éventuellement issu d'un atlas) dans un rectangle IMGUI.</summary>
        public static void Draw(Rect rect, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;
            Rect uv;
            try
            {
                var tr = sprite.textureRect;
                var tex = sprite.texture;
                uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            }
            catch { uv = new Rect(0, 0, 1, 1); }
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv, true);
        }

        /// <summary>Réserve une case carrée dans le layout courant et y dessine l'icône.</summary>
        public static void DrawLayout(Sprite sprite, float size)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (Event.current.type == EventType.Repaint) Draw(r, sprite);
        }
    }
}
