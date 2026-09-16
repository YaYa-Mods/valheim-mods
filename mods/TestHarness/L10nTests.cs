using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Localisation : la table français → anglais est cohérente (pas de valeur vide, mêmes {n} dans les modèles), le
    /// passage du jeu en anglais bascule les textes des mods (L.T) et deux captures en anglais sont prises (scanner,
    /// guide) ; la langue d'origine est rétablie ensuite, même si un test a échoué (Restore).
    /// </summary>
    internal static class L10nTests
    {
        private static string s_prevLanguage;
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        public static IEnumerator Run(Plugin h, Player player)
        {
            var rfAsm = Asm("ResourceFinder");
            var tM = rfAsm.GetType("ModsCommon.L").GetMethod("T", BindingFlags.Public | BindingFlags.Static);
            Func<string, string> T = s => (string)tM.Invoke(null, new object[] { s });
            var table = (Dictionary<string, string>)rfAsm.GetType("ModsCommon.L10nEn").GetField("Table").GetValue(null);

            // ---- cohérence de la table
            h.Check("L10n.table remplie", table.Count > 400, $"{table.Count} entrées");
            var empty = table.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
            h.Check("L10n.aucune valeur vide", empty.Count == 0, string.Join(" | ", empty.Take(5)));
            var dashes = table.Where(kv => kv.Value.IndexOf((char)0x2014) >= 0 || kv.Value.IndexOf((char)0x2013) >= 0).Select(kv => kv.Key).ToList();
            h.Check("L10n.aucun tiret long", dashes.Count == 0, string.Join(" | ", dashes.Take(5)));
            var fmt = new Regex(@"\{(\d+)");
            var badFmt = table.Where(kv =>
            {
                var a = fmt.Matches(kv.Key).Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(x => x);
                var b = fmt.Matches(kv.Value).Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(x => x);
                return !a.SequenceEqual(b);
            }).Select(kv => kv.Key).ToList();
            h.Check("L10n.modèles cohérents", badFmt.Count == 0, string.Join(" | ", badFmt.Take(5)));

            // ---- toutes les clés L.T/L.F du code ont une traduction (les clés déjà anglaises ou purement décoratives sont tolérées)
            var untranslated = new List<string>();
            foreach (var asm in new[] { "ResourceFinder", "Guide", "Inventory", "ModHub", "HomeTeleport" })
            {
                var catT = Asm(asm).GetType(asm + ".Catalog");
                if (catT == null) continue;
                var entryT = Asm(asm).GetType(asm + ".ResourceEntry");
                foreach (var e in (IList)catT.GetField("Entries").GetValue(null))
                {
                    string label = (string)entryT.GetField("Label").GetValue(e);
                    if (!table.ContainsKey(label) && Regex.IsMatch(label, "[àâéèêëîïôùûç ]")) untranslated.Add(label);
                }
            }
            h.Check("L10n.catalogue traduit", untranslated.Count == 0, string.Join(" | ", untranslated.Take(8)));

            // ---- bascule en anglais
            s_prevLanguage = PlayerPrefs.GetString("language", "French");
            SetLanguage("English");
            yield return new WaitForSecondsRealtime(2.5f); // L relit la préférence toutes les 2 s
            h.Check("L10n.anglais actif", T("Chercher") == "Search" && T("Rentrer au lit") == "Go home to bed" && T("Sanglier") == "Boar",
                $"Chercher={T("Chercher")}, Rentrer au lit={T("Rentrer au lit")}, Sanglier={T("Sanglier")}");
            h.Check("L10n.clé inconnue rendue telle quelle", T("clé qui n'existe pas") == "clé qui n'existe pas");

            // Captures : fenêtre du scanner, puis guide (fenêtre + suivi)
            var rfT = rfAsm.GetType("ResourceFinder.Plugin");
            var rfInst = UnityEngine.Object.FindObjectOfType(rfT);
            rfT.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
            yield return new WaitForSecondsRealtime(0.8f);
            string shotFinder = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "finder_en.png");
            ScreenCapture.CaptureScreenshot(shotFinder);
            yield return new WaitForSecondsRealtime(1f);
            rfT.GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);

            var guideT = Asm("Guide").GetType("Guide.Plugin");
            guideT.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.8f);
            string shotGuide = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "guide_en.png");
            ScreenCapture.CaptureScreenshot(shotGuide);
            yield return new WaitForSecondsRealtime(1f);
            guideT.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.5f);
            h.Check("L10n.captures", System.IO.File.Exists(shotFinder) && System.IO.File.Exists(shotGuide));

            // ---- retour à la langue d'origine
            Restore();
            yield return new WaitForSecondsRealtime(2.5f);
            h.Check("L10n.langue rétablie", PlayerPrefs.GetString("language", "") == s_prevLanguage && (s_prevLanguage != "French" || T("Chercher") == "Chercher"),
                $"langue={PlayerPrefs.GetString("language", "")}, Chercher={T("Chercher")}");
            s_prevLanguage = null;
        }

        /// <summary>Rétablit la langue d'origine si un test a été interrompu après la bascule.</summary>
        public static void Restore()
        {
            if (s_prevLanguage == null) return;
            SetLanguage(s_prevLanguage);
        }

        private static void SetLanguage(string lang)
        {
            try
            {
                var m = typeof(Localization).GetMethod("SetLanguage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m != null && Localization.instance != null) m.Invoke(Localization.instance, new object[] { lang });
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TEST] SetLanguage : " + (ex.InnerException?.Message ?? ex.Message)); }
            PlayerPrefs.SetString("language", lang);
        }
    }
}
