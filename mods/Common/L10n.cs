using System.Collections.Generic;
using UnityEngine;

namespace ModsCommon
{
    /// <summary>
    /// Localisation minimale : le français est la langue de référence dans le code (clés = phrases françaises), la
    /// table partagée L10nEn donne l'anglais ; toute autre langue du jeu reçoit l'anglais. La langue est celle choisie dans les
    /// options du jeu (préférence « language », relue toutes les 2 s : un changement en jeu est suivi sans redémarrer).
    /// Les noms d'objets, de biomes et de créatures viennent déjà du jeu et suivent sa langue tout seuls.
    /// </summary>
    internal static class L
    {
        private static readonly Dictionary<string, string> s_en = Load();

        /// <summary>Une table défectueuse (clé en double…) ne doit jamais empêcher un mod de se charger : on retombe sur le français.</summary>
        private static Dictionary<string, string> Load()
        {
            try { return new Dictionary<string, string>(L10nEn.Table); }
            catch (System.Exception ex) { Debug.LogError("[vmods] table de traduction inutilisable : " + ex.Message); return new Dictionary<string, string>(); }
        }
        private static float s_nextLangCheck;
        private static bool s_french = true;

        /// <summary>Le jeu est en français (langue de référence : les textes sont rendus tels quels).</summary>
        public static bool IsFrench
        {
            get
            {
                if (Time.realtimeSinceStartup >= s_nextLangCheck)
                {
                    s_nextLangCheck = Time.realtimeSinceStartup + 2f;
                    string lang = "English";
                    try { lang = PlayerPrefs.GetString("language", "English"); } catch { }
                    s_french = lang == "French";
                }
                return s_french;
            }
        }

        /// <summary>Enregistre une table français → anglais (appelée à l'Awake de chaque mod).</summary>
        public static void Register(Dictionary<string, string> frToEn)
        {
            foreach (var kv in frToEn) s_en[kv.Key] = kv.Value;
        }

        /// <summary>Texte dans la langue du jeu : la clé française telle quelle en français, sinon sa traduction (ou la clé si absente).</summary>
        public static string T(string fr)
        {
            if (fr == null || IsFrench) return fr;
            return s_en.TryGetValue(fr, out var en) ? en : fr;
        }

        /// <summary>Comme T, puis string.Format sur les arguments (les modèles utilisent {0}, {1}…).</summary>
        public static string F(string fr, params object[] args) => string.Format(T(fr), args);
    }
}
