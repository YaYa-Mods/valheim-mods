using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace TestHarness
{
    /// <summary>
    /// Relevé des panneaux natifs du jeu (compétences, trophées / textes) : hiérarchie, sprites, polices, tailles et
    /// couleurs, plus une capture de chacun. Sert de référence pour que nos fenêtres aient exactement la même facture
    /// que celles du jeu au lieu d'un style « mod ».
    /// </summary>
    internal static class NativeUiProbe
    {
        private static void Dump(StringBuilder sb, Transform t, int depth)
        {
            var rt = t as RectTransform;
            string size = rt != null ? $" {rt.rect.width:0}×{rt.rect.height:0}" : "";
            var img = t.GetComponent<Image>();
            string sprite = img != null ? $" image[sprite={(img.sprite != null ? img.sprite.name : "null")}, type={img.type}, couleur=#{ColorUtility.ToHtmlStringRGBA(img.color)}]" : "";
            var txt = t.GetComponent<TMPro.TMP_Text>();
            string text = txt != null ? $" texte[police={(txt.font != null ? txt.font.name : "null")}, taille={txt.fontSize:0.#}, couleur=#{ColorUtility.ToHtmlStringRGBA(txt.color)}, style={txt.fontStyle}, align={txt.alignment}, « {(txt.text ?? "").Replace("\n", " ")} »]" : "";
            var btn = t.GetComponent<Button>();
            string button = btn != null ? " bouton" : "";
            sb.AppendLine(new string(' ', depth * 2) + t.name + size + (t.gameObject.activeSelf ? "" : " (inactif)") + sprite + text + button);
            if (depth < 7) foreach (Transform c in t) Dump(sb, c, depth + 1);
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var gui = InventoryGui.instance;
            var sb = new StringBuilder();
            sb.AppendLine("=== Panneaux natifs du jeu (référence de style) ===");

            // Quels champs de InventoryGui pointent vers ces panneaux
            foreach (var f in typeof(InventoryGui).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (f.Name.IndexOf("dialog", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.IndexOf("skill", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0)
                    sb.AppendLine($"InventoryGui.{f.Name} : {f.FieldType.Name} = {(f.GetValue(gui) is UnityEngine.Object o && o != null ? o.name : "null")}");

            gui.Show(null, 1);
            yield return new WaitForSecondsRealtime(0.8f);

            // ---- compétences
            var skills = UnityEngine.Object.FindObjectOfType<SkillsDialog>();
            if (skills != null)
            {
                skills.Setup(player);
                yield return new WaitForSecondsRealtime(0.8f);
                sb.AppendLine().AppendLine("--- SkillsDialog ---");
                Dump(sb, skills.transform, 0);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "native_skills.png"));
                yield return new WaitForSecondsRealtime(0.8f);
                foreach (var f in typeof(SkillsDialog).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    sb.AppendLine($"SkillsDialog.{f.Name} : {f.FieldType.Name} = {(f.GetValue(skills) is UnityEngine.Object so && so != null ? so.name : f.GetValue(skills)?.ToString() ?? "null")}");
                skills.gameObject.SetActive(false);
                yield return new WaitForSecondsRealtime(0.3f);
            }
            else sb.AppendLine("SkillsDialog introuvable");

            // ---- trophées / textes (journal)
            var texts = UnityEngine.Object.FindObjectOfType<TextsDialog>(true);
            if (texts != null)
            {
                texts.gameObject.SetActive(true);
                try { texts.Setup(player); } catch (Exception ex) { Plugin.Log.LogWarning("[TEST] TextsDialog.Setup : " + ex.Message); }
                yield return new WaitForSecondsRealtime(1f);
                sb.AppendLine().AppendLine("--- TextsDialog ---");
                Dump(sb, texts.transform, 0);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "native_texts.png"));
                yield return new WaitForSecondsRealtime(0.8f);
                foreach (var f in typeof(TextsDialog).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    sb.AppendLine($"TextsDialog.{f.Name} : {f.FieldType.Name} = {(f.GetValue(texts) is UnityEngine.Object to && to != null ? to.name : f.GetValue(texts)?.ToString() ?? "null")}");
                texts.gameObject.SetActive(false);
            }
            else sb.AppendLine("TextsDialog introuvable");

            gui.Hide();
            yield return new WaitForSecondsRealtime(0.4f);

            // ---- notre fenêtre du scanner, construite sur le panneau du jeu : même relevé, pour comparer
            var rfT = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "ResourceFinder").GetType("ResourceFinder.Plugin");
            var rfInst = UnityEngine.Object.FindObjectOfType(rfT);
            // Une recherche qui donne vraiment des résultats (des hêtres autour des pierres sacrificielles) :
            // la capture montre alors la liste de droite telle qu'un joueur la voit.
            rfT.GetMethod("SearchLabel").Invoke(null, new object[] { "Hêtre" });
            yield return new WaitForSecondsRealtime(2.5f);
            rfT.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
            yield return new WaitForSecondsRealtime(1.2f);
            var panel = GameObject.Find("ResourceFinderPanel");
            bool nativeStyle = (bool)rfT.GetProperty("UseNativePanel", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            bool drawnOpen = (bool)rfT.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            if (nativeStyle) h.Check("Scanner.panneau natif construit", panel != null && panel.activeSelf, panel == null ? "panneau absent" : "actif=" + panel.activeSelf);
            else h.Check("Scanner.fenêtre dessinée ouverte", drawnOpen && panel == null, $"WindowOpen={drawnOpen}, panneau natif={(panel != null)}");
            if (panel != null) { sb.AppendLine().AppendLine("--- Notre panneau (scanner) ---"); Dump(sb, panel.transform, 0); }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "finder_native.png"));
            yield return new WaitForSecondsRealtime(1f);
            rfT.GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
            yield return new WaitForSecondsRealtime(0.5f);

            string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "native_ui.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            h.Check("Référence.panneaux natifs relevés", sb.Length > 2000, $"{sb.Length} caractères dans native_ui.txt");
        }
    }
}
