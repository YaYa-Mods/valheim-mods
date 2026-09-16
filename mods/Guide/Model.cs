using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Guide
{
    internal enum Need { Required, Advised, Optional }

    internal struct Status
    {
        public bool Done;
        public string Progress; // « 1/2 », vide si binaire
        public static Status Bool(bool b) => new Status { Done = b, Progress = "" };
        public static Status Count(float have, int need) => new Status { Done = have >= need, Progress = $"{Mathf.Min(Mathf.FloorToInt(have), need)}/{need}" };
    }

    internal sealed class Step
    {
        public string Id;
        public string Title;
        public string Detail;
        public Need Need = Need.Required;
        /// <summary>Libellé d'une entrée du catalogue du scanner : bouton « Cibler ».</summary>
        public string Finder;
        /// <summary>Titre calculé à l'affichage (quantités lues dans le jeu), sinon Title.</summary>
        public Func<string> TitleFunc;
        /// <summary>Prefab d'objet dont la recette réelle est affichée sous le texte d'aide.</summary>
        public string Recipe;
        /// <summary>Prefab de construction dont le coût réel est affiché sous le texte d'aide.</summary>
        public string Piece;
        public Func<Checks, Status> Check;
        internal string CachedKey;
        /// <summary>Prefab d'objet dont on affiche seulement l'icône (offrandes : lu dans l'autel).</summary>
        public Func<string> IconFunc;
        /// <summary>Étape d'état (nourriture…) : jamais figée, réévaluée à chaque fois, elle redevient « à faire » quand l'effet cesse.</summary>
        public bool Volatile;

        public Step(string id, Need need, string title, string detail, Func<Checks, Status> check, string finder = null, string recipe = null, string piece = null, Func<string> titleFunc = null)
        { Id = id; Need = need; Title = title; Detail = detail; Check = check; Finder = finder; Recipe = recipe; Piece = piece; TitleFunc = titleFunc; }

        public string DisplayTitle { get { try { return TitleFunc?.Invoke() ?? Title; } catch { return Title; } } }

        private string _detailCache;
        public string DisplayDetail
        {
            get
            {
                if (_detailCache != null) return _detailCache;
                string extra = Recipe != null ? Facts.RecipeText(Recipe) : Piece != null ? Facts.PieceText(Piece) : "";
                string d = extra.Length == 0 ? (Detail ?? "") : (string.IsNullOrEmpty(Detail) ? "" : Detail + "  ") + "<color=#f5a847>▸</color> " + extra;
                // Les recettes ne sont connues qu'une fois ObjectDB chargée : on ne fige le texte qu'à ce moment-là.
                if (ObjectDB.instance != null && ZNetScene.instance != null) _detailCache = d;
                return d;
            }
        }
    }

    internal sealed class Chapter
    {
        public string Id;
        public string Title;
        public Func<string> TitleFunc;
        public string Boss;       // prefab du boss : la clé de victoire est lue dessus (Character.m_defeatSetGlobalKey)
        public string Intro;
        public string Reward;
        public string Finder;     // entrée du scanner pour l'autel
        public string Location;   // nom (sous-chaîne) du lieu de l'autel dans ZoneSystem, pour l'épingle automatique
        public string AltarLabel; // nom de l'épingle posée sur la carte
        public string Icon;       // prefab d'objet (trophée du boss) affiché devant le titre
        public List<Step> Steps = new List<Step>();
        public string DisplayTitle { get { try { return TitleFunc?.Invoke() ?? Title; } catch { return Title; } } }
    }

    /// <summary>
    /// Ce que le jeu enregistre déjà, lu tel quel : statistiques du personnage (objets fabriqués/ramassés, pièces
    /// posées, créatures tuées, clés = noms partagés « $item_… », « $piece_… », « $enemy_… »), matériaux et stations
    /// connus, inventaire, clés globales du monde (boss), biomes visités, carte explorée. Les noms de prefabs sont
    /// traduits en noms partagés à la volée (ObjectDB / ZNetScene), avec cache.
    /// </summary>
    internal sealed class Checks
    {
        private static readonly Dictionary<string, string> s_itemName = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> s_pieceName = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> s_enemyName = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> s_bossKey = new Dictionary<string, string>();
        private static readonly AccessTools.FieldRef<Player, HashSet<string>> s_knownBiome = AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownBiome");
        private static readonly AccessTools.FieldRef<Player, Dictionary<string, int>> s_knownStations = AccessTools.FieldRefAccess<Player, Dictionary<string, int>>("m_knownStations");
        private static readonly Func<Minimap, Vector3, bool> s_isExplored = AccessTools.MethodDelegate<Func<Minimap, Vector3, bool>>(AccessTools.Method(typeof(Minimap), "IsExplored", new[] { typeof(Vector3) }));

        public Player Player;
        public bool IsExplored(Vector3 pos) => Minimap.instance != null && s_isExplored(Minimap.instance, pos);

        // ---- noms partagés ----
        public static string ItemName(string prefab)
        {
            if (s_itemName.TryGetValue(prefab, out var n)) return n;
            var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            n = go != null ? go.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name : null;
            if (n != null) s_itemName[prefab] = n;
            return n;
        }
        public static string PieceName(string prefab)
        {
            if (s_pieceName.TryGetValue(prefab, out var n)) return n;
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            n = go != null ? go.GetComponent<Piece>()?.m_name : null;
            if (n != null) s_pieceName[prefab] = n;
            return n;
        }
        public static string EnemyName(string prefab)
        {
            if (s_enemyName.TryGetValue(prefab, out var n)) return n;
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            n = go != null ? go.GetComponent<Character>()?.m_name : null;
            if (n != null) s_enemyName[prefab] = n;
            return n;
        }
        public static string BossKey(string prefab)
        {
            if (s_bossKey.TryGetValue(prefab, out var k)) return k;
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            k = go != null ? go.GetComponent<Character>()?.m_defeatSetGlobalKey : null;
            if (!string.IsNullOrEmpty(k)) s_bossKey[prefab] = k;
            return k;
        }

        // ---- statistiques du profil ----
        private static float Stat(Func<PlayerProfile.PlayerStats, Dictionary<string, float>> pick, string key)
        {
            if (key == null) return 0f;
            float total = 0f;
            try
            {
                var stats = Game.instance?.GetPlayerProfile()?.m_playerStats;
                if (stats == null) return 0f;
                foreach (var s in stats)
                {
                    var d = s != null ? pick(s) : null;
                    if (d != null && d.TryGetValue(key, out float v)) total += v;
                }
            }
            catch { }
            return total;
        }

        public float Crafted(string prefab) => Stat(s => s.m_itemCraftStats, ItemName(prefab));
        public float PickedUp(string prefab) => Stat(s => s.m_itemPickupStats, ItemName(prefab));
        public float Placed(string prefab) => Stat(s => s.m_piecesPlacedStats, PieceName(prefab));
        public float Kills(string prefab)
        {
            string name = EnemyName(prefab);
            if (name == null) return 0f;
            float total = 0f;
            try
            {
                var stats = Game.instance?.GetPlayerProfile()?.m_playerStats;
                if (stats == null) return 0f;
                foreach (var s in stats)
                    if (s?.m_enemyStats != null)
                        foreach (var d in s.m_enemyStats)
                            if (d != null && d.TryGetValue(name, out float v)) total += v;
            }
            catch { }
            return total;
        }

        // ---- état du joueur ----
        public int InInventory(string prefab)
        {
            string name = ItemName(prefab);
            return name != null && Player != null ? Player.GetInventory().CountItems(name) : 0;
        }
        public bool Known(string prefab) { string n = ItemName(prefab); return n != null && Player != null && Player.IsMaterialKnown(n); }
        public bool Equipped(string prefab)
        {
            string name = ItemName(prefab);
            if (name == null || Player == null) return false;
            foreach (var it in Player.GetInventory().GetEquippedItems()) if (it.m_shared.m_name == name) return true;
            return false;
        }
        public bool KnownStation(string prefab)
        {
            string name = PieceName(prefab);
            var known = Player != null ? s_knownStations(Player) : null;
            return name != null && known != null && known.ContainsKey(name);
        }
        /// <summary>Objet obtenu : fabriqué ou ramassé au moins n fois, ou en inventaire, ou équipé.</summary>
        public float Obtained(string prefab) => Mathf.Max(Mathf.Max(Crafted(prefab), PickedUp(prefab)), Mathf.Max(InInventory(prefab), Equipped(prefab) ? 1 : 0));
        /// <summary>Pièce construite : posée au moins une fois, ou station déjà utilisée.</summary>
        public bool Built(string prefab) => Placed(prefab) > 0f || KnownStation(prefab);
        public int Foods() => Player != null ? Player.GetFoods().Count : 0;

        // ---- monde ----
        /// <summary>Boss vaincu : clé globale du monde posée par le boss (Character.m_defeatSetGlobalKey) ; si le boss n'en
        /// déclare aucune (boss du Nord profond en 1.0), les statistiques de kills du personnage.</summary>
        public bool BossDefeated(string prefab)
        {
            string key = BossKey(prefab);
            if (!string.IsNullOrEmpty(key)) return ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
            return Kills(prefab) > 0f;
        }
        public bool BiomeKnown(Heightmap.Biome biome)
        {
            var known = Player != null ? s_knownBiome(Player) : null;
            if (known == null) return false;
            string name = biome.ToString();
            foreach (var k in known) if (k.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private readonly Dictionary<string, KeyValuePair<float, bool>> _explored = new Dictionary<string, KeyValuePair<float, bool>>();

        /// <summary>Un lieu de ce nom (sous-chaîne) est sur une partie explorée de la carte. Parcourt tous les lieux du
        /// monde (des milliers) : résultat gardé 5 s, et pour toujours une fois vrai.</summary>
        public bool LocationExplored(string pattern)
        {
            if (_explored.TryGetValue(pattern, out var c) && (c.Value || Time.unscaledTime - c.Key < 5f)) return c.Value;
            bool r = ComputeLocationExplored(pattern);
            _explored[pattern] = new KeyValuePair<float, bool>(Time.unscaledTime, r);
            return r;
        }

        private static bool ComputeLocationExplored(string pattern)
        {
            var zs = ZoneSystem.instance; var map = Minimap.instance;
            if (zs == null || map == null) return false;
            foreach (var kv in zs.m_locationInstances)
            {
                string n = kv.Value.m_location?.m_prefabName ?? "";
                if (n.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (s_isExplored(map, kv.Value.m_position)) return true;
            }
            return false;
        }
    }
}
