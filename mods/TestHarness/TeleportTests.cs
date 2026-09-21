using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace TestHarness
{
    /// <summary>Retour au lit : GoHome() amène le joueur sur son point d'apparition (lit ou pierres). En dernier : déplace le joueur.</summary>
    internal static class TeleportTests
    {
        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "HomeTeleport");
            var t = asm.GetType("HomeTeleport.Plugin");
            var profile = Game.instance.GetPlayerProfile();
            // Même cible que le mod : le lit, sinon les pierres sacrificielles (icône du lieu StartTemple)
            Vector3 target = profile.HaveCustomSpawnPoint() ? profile.GetCustomSpawnPoint() : PlayerProfile.m_originalSpawnPoint;
            if (!profile.HaveCustomSpawnPoint() && ZoneSystem.instance != null && ZoneSystem.instance.GetLocationIcon(Game.instance.m_StartLocation, out var start)) target = start;
            // Condition neutralisée pour le test : aucun délai de rappel restant
            t.GetField("s_lastTeleport", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, -1000f);
            // Confirmation : Request() ouvre la fenêtre sans partir ; capture ; fermeture
            t.GetMethod("Request", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.8f);
            var dialogF = t.GetField("DialogOpen", BindingFlags.NonPublic | BindingFlags.Static);
            bool dialog = (bool)dialogF.GetValue(null);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Paths.ConfigPath, "home_confirm.png"));
            yield return new WaitForSecondsRealtime(1f);
            dialogF.SetValue(null, false);
            h.Check("HomeTeleport.confirmation avant départ", dialog && !player.IsTeleporting(), $"fenêtre={dialog}, téléportation lancée={player.IsTeleporting()}, ciblé={player.IsTargeted()}");
            var from = player.transform.position;
            t.GetMethod("GoHome", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            float deadline = Time.time + 30f;
            yield return null;
            while (player.IsTeleporting() && Time.time < deadline) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            float d = Vector3.Distance(player.transform.position, target);
            h.Check("HomeTeleport.retour au lit", d < 6f, $"lit={profile.HaveCustomSpawnPoint()}, départ à {Vector3.Distance(from, target):0} m, arrivée à {d:0.#} m du point");
        }
    }
}
