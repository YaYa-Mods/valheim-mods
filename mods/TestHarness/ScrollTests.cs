using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Molette dans les fenêtres des mods : le jeu ne doit ni zoomer la caméra ni changer d'objet dans la barre
    /// d'action pendant qu'une fenêtre est ouverte. On vérifie que ce que lit la caméra (relevé dans le code de
    /// GameCamera) est bien ce que les mods neutralisent, que le correctif est posé par chaque mod, et que la valeur
    /// renvoyée est mise à zéro fenêtre ouverte (et seulement à ce moment).
    /// </summary>
    internal static class ScrollTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);

        /// <summary>Méthodes appelées par une méthode du jeu (lecture de l'IL) : sert à savoir ce que la caméra lit vraiment.</summary>
        private static List<string> CallsOf(MethodBase m)
        {
            var calls = new List<string>();
            try
            {
                foreach (var ins in PatchProcessor.GetCurrentInstructions(m))
                    if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.operand is MethodBase called)
                        calls.Add(called.DeclaringType?.Name + "." + called.Name);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TEST] lecture IL : " + ex.Message); }
            return calls;
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            // ---- ce que la caméra lit pour le zoom
            var cam = AccessTools.Method(typeof(GameCamera), "UpdateCamera", new[] { typeof(float) }) ?? AccessTools.Method(typeof(GameCamera), "UpdateCamera");
            var camCalls = cam != null ? CallsOf(cam) : new List<string>();
            var zoomCalls = camCalls.Where(c => c.IndexOf("Scroll", StringComparison.OrdinalIgnoreCase) >= 0 || c.IndexOf("Zoom", StringComparison.OrdinalIgnoreCase) >= 0).Distinct().ToList();
            Plugin.Log.LogInfo("[TEST] zoom caméra, appels : " + (zoomCalls.Count > 0 ? string.Join(", ", zoomCalls) : "aucun trouvé | " + string.Join(", ", camCalls.Distinct().Take(30))));
            h.Check("Molette.la caméra lit GetMouseScrollWheel", zoomCalls.Any(c => c == "ZInput.GetMouseScrollWheel"),
                "appels de zoom trouvés : " + string.Join(", ", zoomCalls));

            // ---- correctif posé par chaque mod à fenêtre
            var scrollTarget = AccessTools.Method(typeof(ZInput), "GetMouseScrollWheel", Type.EmptyTypes);
            var owners = scrollTarget != null ? (HarmonyLib.Harmony.GetPatchInfo(scrollTarget)?.Owners ?? new List<string>().AsEnumerable()) : new List<string>().AsEnumerable();
            Plugin.Log.LogInfo("[TEST] correctifs sur GetMouseScrollWheel : " + string.Join(", ", owners));
            foreach (var name in new[] { "ResourceFinder", "Guide", "ModHub", "HomeTeleport", "Inventory" })
            {
                var asm = Asm(name);
                var guardT = asm?.GetType("ModsCommon.ScrollGuard");
                var installed = guardT?.GetField("Installed", BindingFlags.Public | BindingFlags.Static);
                bool ok = installed != null && (bool)installed.GetValue(null);
                h.Check($"Molette.{name} garde posée", ok,
                    guardT == null ? "type ScrollGuard absent de l'assembly" : installed == null ? "champ Installed absent" : $"Installed={ok}");
            }

            // ---- effet réel : fenêtre fermée la valeur passe, fenêtre ouverte elle est annulée
            var target = AccessTools.Method(typeof(ZInput), "GetMouseScrollWheel", Type.EmptyTypes);
            h.Check("Molette.méthode du jeu présente", target != null);
            var rfAsm = Asm("ResourceFinder");
            var rfT = rfAsm.GetType("ResourceFinder.Plugin");
            var openF = rfT.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Static);
            var rfInst = UnityEngine.Object.FindObjectOfType(rfT);
            // Un postfix de test injecte une valeur non nulle AVANT celui des mods (priorité First) : on observe ce qui ressort.
            var probe = new Harmony("vmods.testharness.scrollprobe");
            try
            {
                probe.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(ScrollTests), nameof(Inject))) { priority = Priority.First });
                yield return null;
                float closed = (float)target.Invoke(null, null);
                rfT.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
                yield return null;
                float opened = (float)target.Invoke(null, null);
                rfT.GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
                yield return null;
                float closedAgain = (float)target.Invoke(null, null);
                h.Check("Molette.neutralisée fenêtre ouverte", Mathf.Approximately(opened, 0f), $"valeur lue={opened}");
                h.Check("Molette.intacte fenêtre fermée", Mathf.Approximately(closed, 1f) && Mathf.Approximately(closedAgain, 1f), $"avant={closed}, après={closedAgain}");
                h.Check("Molette.fenêtre refermée", !(bool)openF.GetValue(null));
            }
            finally { probe.UnpatchSelf(); }
        }

        /// <summary>Simule un cran de molette (posé avant le garde des mods).</summary>
        private static void Inject(ref float __result) { __result = 1f; }
    }
}
