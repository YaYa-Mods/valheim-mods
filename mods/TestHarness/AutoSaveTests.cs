using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Sauvegarde automatique : le mod règle l'intervalle que le jeu utilise pour sa propre sauvegarde (Game.m_saveInterval,
    /// lu par Game.UpdateSaving) ; 5 minutes par défaut, le réglage éteint rend la valeur du jeu. Le harnais remet ensuite
    /// son propre intervalle (aucune sauvegarde pendant les tests).
    /// </summary>
    internal static class AutoSaveTests
    {
        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AutoSave");
            if (asm == null) { h.Check("Sauvegarde auto.mod chargé", false, "assembly AutoSave absent"); yield break; }
            var t = asm.GetType("AutoSave.Plugin");
            var enabled = (BepInEx.Configuration.ConfigEntry<bool>)t.GetField("Enabled", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var minutes = (BepInEx.Configuration.ConfigEntry<int>)t.GetField("Minutes", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var apply = t.GetMethod("Apply", BindingFlags.NonPublic | BindingFlags.Static);
            float harness = Game.m_saveInterval;
            bool prevEnabled = enabled.Value; int prevMinutes = minutes.Value;
            try
            {
                // Le jeu lit bien ce champ dans sa boucle de sauvegarde (vérifié dans son code, pas supposé)
                var loop = typeof(Game).GetMethod("UpdateSaving", BindingFlags.NonPublic | BindingFlags.Instance);
                bool reads = false;
                if (loop != null)
                    foreach (var ins in HarmonyLib.PatchProcessor.GetCurrentInstructions(loop))
                        if (ins.operand is FieldInfo f && f.Name == "m_saveInterval") reads = true;
                h.Check("Sauvegarde auto.le jeu utilise l'intervalle réglé", reads, loop == null ? "Game.UpdateSaving introuvable" : "Game.UpdateSaving lit m_saveInterval");

                enabled.Value = true; minutes.Value = 5; apply.Invoke(null, null);
                float five = Game.m_saveInterval;
                minutes.Value = 12; apply.Invoke(null, null);
                float twelve = Game.m_saveInterval;
                enabled.Value = false; apply.Invoke(null, null);
                float off = Game.m_saveInterval;
                h.Check("Sauvegarde auto.toutes les 5 min, réglable, éteint = jeu", Mathf.Approximately(five, 300f) && Mathf.Approximately(twelve, 720f) && off > 1000f,
                    $"5 min → {five:0} s, 12 min → {twelve:0} s, éteint → {off:0} s");
            }
            finally
            {
                enabled.Value = prevEnabled; minutes.Value = prevMinutes;
                Game.m_saveInterval = harness; // pas de sauvegarde pendant les tests
            }
            yield return null;
        }
    }
}
