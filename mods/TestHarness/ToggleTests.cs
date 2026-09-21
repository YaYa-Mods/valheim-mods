using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// « Tout désactiver » (bouton du hub) doit rendre un jeu vanilla : plus de suivi de quête, plus d'indicateur du
    /// scanner, plus d'aides de touches, plus de colonne d'outils dans l'inventaire, et aucune fenêtre qui s'ouvre.
    /// « Tout réactiver » doit tout remettre. C'est ce que fait un joueur qui veut comparer avec le jeu d'origine :
    /// si un panneau reste affiché, il n'a aucun moyen de s'en débarrasser sans quitter.
    /// </summary>
    internal static class ToggleTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        private static ConfigEntry<bool> EnabledEntry(string guid)
        {
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info.Metadata.GUID != guid || info.Instance == null) continue;
                foreach (var e in info.Instance.Config)
                    if (e.Key.Key == "Enabled" && e.Value.SettingType == typeof(bool)) return (ConfigEntry<bool>)e.Value;
            }
            return null;
        }

        private static readonly string[] s_mods = { "vmods.resourcefinder", "vmods.guide", "vmods.inventory", "vmods.hometeleport", "vmods.recycle", "vmods.craftfromchests", "vmods.lumberjack", "vmods.longerfood", "vmods.nodurability", "vmods.shortnights", "vmods.movement" };

        public static IEnumerator Run(Plugin h, Player player)
        {
            var entries = s_mods.Select(g => new KeyValuePair<string, ConfigEntry<bool>>(g, EnabledEntry(g))).Where(kv => kv.Value != null).ToList();
            h.Check("Vanilla.interrupteurs trouvés", entries.Count >= 8, string.Join(", ", entries.Select(e => e.Key)));
            var previous = entries.ToDictionary(e => e.Key, e => e.Value.Value);

            var guideT = Asm("Guide").GetType("Guide.Plugin");
            var hudRects = guideT.GetMethod("HudRects", BindingFlags.Public | BindingFlags.Static);
            var rfT = Asm("ResourceFinder").GetType("ResourceFinder.Plugin");
            var rfOpen = rfT.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Static);
            var rfInst = UnityEngine.Object.FindObjectOfType(rfT);
            var hintBarT = Asm("ModHub").GetType("ModHub.KeyHintBar");
            var collect = hintBarT.GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Static);

            // Le suivi doit être visible pour que le test ait un sens (un run interrompu peut avoir laissé le mode « Masque » dans la config)
            var modeCfg = (BepInEx.Configuration.ConfigEntryBase)guideT.GetField("Mode", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            modeCfg.BoxedValue = Enum.Parse(modeCfg.SettingType, "Complet");
            yield return new WaitForSecondsRealtime(0.6f);
            int rectsBefore = ((Rect[])hudRects.Invoke(null, null)).Length;
            h.Check("Vanilla.suivi de quête visible au départ", rectsBefore > 0, $"zones déclarées par le guide : {rectsBefore}");

            // ---- tout éteindre
            foreach (var e in entries) e.Value.Value = false;
            yield return new WaitForSecondsRealtime(1.2f);

            int rects = ((Rect[])hudRects.Invoke(null, null)).Length;
            h.Check("Vanilla.suivi de quête retiré", rects == 0, $"zones déclarées par le guide : {rects}");

            rfT.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null); // un joueur qui appuie sur F7
            yield return new WaitForSecondsRealtime(0.6f);
            h.Check("Vanilla.fenêtre du scanner ne s'ouvre pas", !(bool)rfOpen.GetValue(null));

            int hints = ((IList)collect.Invoke(null, null)).Count;
            h.Check("Vanilla.aides de touches retirées", hints == 0, $"{hints} aide(s) encore proposée(s)");

            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(0.8f);
            int tools = InventoryGui.instance.m_player.GetComponentsInChildren<RectTransform>(false).Count(t => t.name.StartsWith("ModTool", StringComparison.Ordinal));
            h.Check("Vanilla.colonne d'outils retirée de l'inventaire", tools == 0, $"{tools} outil(s) encore affiché(s)");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "mods_disabled.png"));
            yield return new WaitForSecondsRealtime(0.6f);
            InventoryGui.instance.Hide();
            yield return new WaitForSecondsRealtime(0.4f);

            // ---- tout rallumer
            foreach (var e in entries) e.Value.Value = previous.TryGetValue(e.Key, out bool v) ? v : true;
            yield return new WaitForSecondsRealtime(1.2f);
            int rectsBack = ((Rect[])hudRects.Invoke(null, null)).Length;
            int hintsBack = ((IList)collect.Invoke(null, null)).Count;
            InventoryGui.instance.Show(null, 1);
            yield return new WaitForSecondsRealtime(0.8f);
            int toolsBack = InventoryGui.instance.m_player.GetComponentsInChildren<RectTransform>(false).Count(t => t.name.StartsWith("ModTool", StringComparison.Ordinal));
            InventoryGui.instance.Hide();
            yield return new WaitForSecondsRealtime(0.4f);
            h.Check("Vanilla.tout revient après réactivation", rectsBack > 0 && hintsBack > 0 && toolsBack >= 8,
                $"zones du guide={rectsBack}, aides={hintsBack}, outils={toolsBack}");
        }
    }
}
