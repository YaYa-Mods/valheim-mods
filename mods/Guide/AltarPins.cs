using System;
using System.Collections.Generic;
using UnityEngine;

namespace Guide
{
    /// <summary>
    /// Épingle automatique de l'autel du chapitre courant : dès qu'un exemplaire du lieu se trouve sur une partie
    /// explorée de la carte (et seulement là, rien n'est dévoilé), une épingle « boss » nommée est posée, comme le
    /// ferait la pierre runique du vanilla. Une épingle déjà présente à moins de 40 m (vegvisir lu, épingle du joueur,
    /// la nôtre d'une session précédente : elles sont sauvegardées avec le personnage) est respectée.
    /// </summary>
    internal static class AltarPins
    {
        private static float s_next;
        private static readonly HashSet<string> s_doneThisSession = new HashSet<string>();

        public static void Tick()
        {
            if (!Plugin.AltarPin.Value || Time.unscaledTime < s_next) return;
            s_next = Time.unscaledTime + 10f;
            var map = Minimap.instance; var zs = ZoneSystem.instance;
            if (map == null || zs == null || Player.m_localPlayer == null) return;
            var chapter = Progress.Current();
            if (chapter == null || string.IsNullOrEmpty(chapter.Location) || Progress.ChapterDone(chapter) || !Progress.IsUnlocked(chapter)) return;
            if (s_doneThisSession.Contains(chapter.Id)) return;

            var checks = new Checks { Player = Player.m_localPlayer };
            foreach (var kv in zs.m_locationInstances)
            {
                string n = kv.Value.m_location?.m_prefabName ?? "";
                if (n.IndexOf(chapter.Location, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var pos = kv.Value.m_position;
                if (!checks.IsExplored(pos)) continue;
                if (HasPinNear(map, pos, 40f)) { s_doneThisSession.Add(chapter.Id); return; }
                map.AddPin(pos, Minimap.PinType.Boss, chapter.AltarLabel ?? chapter.DisplayTitle, true, false, 0L);
                s_doneThisSession.Add(chapter.Id);
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Guide : " + (chapter.AltarLabel ?? "autel") + " épinglé sur la carte");
                Plugin.Log.LogInfo($"Épingle posée : {chapter.AltarLabel} {pos}");
                return;
            }
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<Minimap, List<Minimap.PinData>> s_pins = HarmonyLib.AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        private static bool HasPinNear(Minimap map, Vector3 pos, float radius)
        {
            foreach (var p in s_pins(map))
                if (p != null && Vector3.Distance(p.m_pos, pos) < radius) return true;
            return false;
        }
    }
}
