using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Guide
{
    /// <summary>
    /// Faits lus dans le jeu lui-même (jamais de mémoire) : ce qu'un autel de boss réclame (OfferingBowl : objet et
    /// nombre, ou nombre de supports quand l'autel utilise des ItemStand), les recettes (station, niveau, ingrédients)
    /// et les constructions (station, ingrédients). Les prefabs de lieux sont des références « souples » chargées à
    /// la demande, une seule fois. Tout est mis en cache pour la session.
    /// </summary>
    internal static class Facts
    {
        public struct Offering { public string Item; public int Count; public bool Valid; }

        private static readonly Dictionary<Chapter, Sprite> s_chapterIcons = new Dictionary<Chapter, Sprite>();
        /// <summary>Trophée du boss du chapitre (icône d'objet, en cache).</summary>
        public static Sprite ChapterIcon(Chapter c)
        {
            if (c == null || c.Icon == null) return null;
            if (s_chapterIcons.TryGetValue(c, out var sp)) return sp;
            try { var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(c.Icon) : null; sp = go != null ? go.GetComponent<ItemDrop>()?.m_itemData?.GetIcon() : null; } catch { sp = null; }
            if (ObjectDB.instance != null) s_chapterIcons[c] = sp;
            return sp;
        }

        private static readonly Dictionary<Step, Sprite> s_stepIcons = new Dictionary<Step, Sprite>();
        /// <summary>Icône de l'objet (Recipe) ou de la construction (Piece) d'une étape, null sinon. En cache par étape.</summary>
        public static Sprite StepIcon(Step s)
        {
            if (s_stepIcons.TryGetValue(s, out var sp)) return sp;
            try
            {
                string item = s.IconFunc?.Invoke() ?? s.Recipe;
                if (item != null && ObjectDB.instance != null)
                {
                    var go = ObjectDB.instance.GetItemPrefab(item);
                    sp = go != null ? go.GetComponent<ItemDrop>()?.m_itemData?.GetIcon() : null;
                }
                else if (s.Piece != null && ZNetScene.instance != null)
                {
                    var go = ZNetScene.instance.GetPrefab(s.Piece);
                    sp = go != null ? go.GetComponent<Piece>()?.m_icon : null;
                }
            }
            catch { sp = null; }
            if (ObjectDB.instance != null && ZNetScene.instance != null) s_stepIcons[s] = sp; // pas de cache avant le chargement du monde
            return sp;
        }

        private static readonly Dictionary<string, Offering> s_offerings = new Dictionary<string, Offering>();
        private static readonly Dictionary<string, string> s_recipeText = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> s_pieceText = new Dictionary<string, string>();

        public static string Loc(string token) => string.IsNullOrEmpty(token) ? "" : Localization.instance.Localize(token);

        /// <summary>Nom affiché d'un item (localisé), depuis son prefab.</summary>
        public static string ItemLabel(string prefab)
        {
            string shared = Checks.ItemName(prefab);
            return shared != null ? Loc(shared) : prefab;
        }

        /// <summary>Offrande de l'autel du boss d'un lieu (ex. « Eikthyrnir » → TrophyDeer ×2).</summary>
        public static Offering OfferingOf(string locationName)
        {
            if (s_offerings.TryGetValue(locationName, out var cached)) return cached;
            var result = new Offering();
            try
            {
                var loc = ZoneSystem.instance?.m_locations?.FirstOrDefault(l => l != null && l.m_prefabName == locationName);
                if (loc != null)
                {
                    loc.m_prefab.Load();
                    var go = loc.m_prefab.Asset;
                    var bowl = go != null ? go.GetComponentsInChildren<OfferingBowl>(true).FirstOrDefault(b => b.m_bossPrefab != null && b.m_bossPrefab.GetComponent<Character>() != null) : null;
                    if (bowl != null)
                    {
                        if (bowl.m_useItemStands)
                        {
                            // Tous les supports doivent être garnis : le nombre de supports est la vraie quantité.
                            var stands = go.GetComponentsInChildren<ItemStand>(true).Where(s => string.IsNullOrEmpty(bowl.m_itemStandPrefix) || s.name.StartsWith(bowl.m_itemStandPrefix)).ToList();
                            result.Count = stands.Count;
                            result.Item = bowl.m_bossItem != null ? bowl.m_bossItem.name : stands.SelectMany(s => s.m_supportedItems ?? new List<ItemDrop>()).FirstOrDefault(i => i != null)?.name;
                        }
                        else { result.Count = bowl.m_bossItems; result.Item = bowl.m_bossItem != null ? bowl.m_bossItem.name : null; }
                        result.Valid = result.Count > 0 && result.Item != null;
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"Offrande de {locationName} : {ex.Message}"); }
            if (ZoneSystem.instance != null) s_offerings[locationName] = result; // avant ZoneSystem : pas de cache
            return result;
        }

        /// <summary>« Établi niv. 2 : 10 bois, 8 restes de cuir » pour un objet fabricable, sinon vide.</summary>
        public static string RecipeText(string prefab)
        {
            if (s_recipeText.TryGetValue(prefab, out var t)) return t;
            t = "";
            try
            {
                var db = ObjectDB.instance;
                var go = db != null ? db.GetItemPrefab(prefab) : null;
                var recipe = go != null ? db.m_recipes.FirstOrDefault(r => r != null && r.m_enabled && r.m_item != null && r.m_item.name == prefab) : null;
                if (recipe != null)
                {
                    var sb = new StringBuilder();
                    sb.Append(recipe.m_craftingStation != null ? Loc(recipe.m_craftingStation.m_name) + (recipe.m_minStationLevel > 1 ? $" niv. {recipe.m_minStationLevel}" : "") : "à la main");
                    sb.Append(" : ");
                    sb.Append(string.Join(", ", recipe.m_resources.Where(r => r.m_resItem != null && r.m_amount > 0 && !r.m_resItem.name.StartsWith("Upgrader")).Select(r => $"{r.m_amount} {Loc(r.m_resItem.m_itemData.m_shared.m_name)}")));
                    if (recipe.m_amount > 1) sb.Append($" (×{recipe.m_amount})");
                    t = sb.ToString();
                }
            }
            catch { }
            if (ObjectDB.instance != null) s_recipeText[prefab] = t;
            return t;
        }

        /// <summary>« Établi : 20 pierre, 5 noyau de surtling » pour une construction, sinon vide.</summary>
        public static string PieceText(string prefab)
        {
            if (s_pieceText.TryGetValue(prefab, out var t)) return t;
            t = "";
            try
            {
                var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
                var piece = go != null ? go.GetComponent<Piece>() : null;
                if (piece != null)
                {
                    t = (piece.m_craftingStation != null ? Loc(piece.m_craftingStation.m_name) : "sans station") + " : " +
                        string.Join(", ", piece.m_resources.Where(r => r.m_resItem != null && r.m_amount > 0 && !r.m_resItem.name.StartsWith("Upgrader")).Select(r => $"{r.m_amount} {Loc(r.m_resItem.m_itemData.m_shared.m_name)}"));
                }
            }
            catch { }
            if (ZNetScene.instance != null) s_pieceText[prefab] = t;
            return t;
        }
    }
}
