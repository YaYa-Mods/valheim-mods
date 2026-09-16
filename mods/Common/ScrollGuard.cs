using System;
using HarmonyLib;
using UnityEngine;

namespace ModsCommon
{
    /// <summary>
    /// Molette de la souris pendant qu'une fenêtre du mod est ouverte : elle doit faire défiler la liste, pas zoomer la
    /// caméra ni changer d'objet dans la barre d'action (le jeu fait la même chose en mode construction, où la molette
    /// tourne la pièce). ZInput.GetMouseScrollWheel est ce que lisent la caméra et la barre d'action : on lui fait
    /// renvoyer 0 tant que la fenêtre est ouverte. L'IMGUI, lui, lit Event.current : nos listes défilent normalement.
    /// Le stick droit n'est pas touché (Pad s'en sert pour défiler à la manette).
    /// </summary>
    internal static class ScrollGuard
    {
        private static Func<bool> s_blocked;
        /// <summary>Vrai si le correctif a bien été posé (vérifié par le harnais : il casserait à une mise à jour du jeu).</summary>
        public static bool Installed;

        /// <summary>À appeler dans Awake, avec le prédicat « une de mes fenêtres est ouverte ».</summary>
        public static void Install(string guid, Func<bool> windowOpen)
        {
            s_blocked = windowOpen;
            if (Installed) return;
            try
            {
                var target = AccessTools.Method(typeof(ZInput), "GetMouseScrollWheel", Type.EmptyTypes);
                if (target == null) { Debug.LogWarning("[vmods] ZInput.GetMouseScrollWheel introuvable : la molette zoomera la caméra dans les fenêtres."); return; }
                new Harmony(guid + ".scrollguard").Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(ScrollGuard), nameof(Zero))));
                Installed = true;
            }
            catch (Exception ex) { Debug.LogWarning("[vmods] garde de molette : " + ex.Message); }
        }

        private static void Zero(ref float __result)
        {
            if (__result != 0f && s_blocked != null && s_blocked()) __result = 0f;
        }
    }
}
