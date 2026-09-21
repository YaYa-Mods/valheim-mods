using System;
using System.Collections.Generic;
using UnityEngine;
using ModsCommon;

namespace ResourceFinder
{
    /// <summary>
    /// Encombrement à l'écran des éléments du HUD du jeu (barres de vie, barre d'action, effets, mini-carte, aides de
    /// touches, messages, barre du boss), en unités 1080p comme nos fenêtres. La pastille de cible s'en écarte comme
    /// elle s'écarte du suivi de quête : elle ne recouvre jamais ce que le joueur doit lire.
    /// </summary>
    internal static class GameHud
    {
        private static readonly string[] s_names = { "healthpanel", "HotKeyBar", "StatusEffects", "minimap_small", "KeyHints", "TopLeftMsgs", "HudBaseBoss" }; // HudBaseBoss(Clone) : barre du boss en haut, quand il y en a un
        private static readonly List<RectTransform> s_parts = new List<RectTransform>();
        private static readonly List<Rect> s_rects = new List<Rect>();
        private static readonly Vector3[] s_corners = new Vector3[4];
        private static float s_partsNext, s_rectsNext;

        public static List<Rect> Rects()
        {
            if (Time.unscaledTime < s_rectsNext) return s_rects;
            s_rectsNext = Time.unscaledTime + 0.25f;
            if (Time.unscaledTime >= s_partsNext) { s_partsNext = Time.unscaledTime + 5f; Collect(); }
            s_rects.Clear();
            float k = Theme.UiScale;
            foreach (var rt in s_parts)
            {
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                var txt = rt.GetComponent<TMPro.TMP_Text>();
                if (txt != null && (string.IsNullOrEmpty(txt.text) || txt.color.a < 0.05f)) continue; // message effacé ou estompé : la zone est libre
                rt.GetWorldCorners(s_corners);
                float minX = Mathf.Min(s_corners[0].x, s_corners[2].x), maxX = Mathf.Max(s_corners[0].x, s_corners[2].x);
                float minY = Mathf.Min(s_corners[0].y, s_corners[2].y), maxY = Mathf.Max(s_corners[0].y, s_corners[2].y);
                float w = maxX - minX, h = maxY - minY;
                if (w < 4f || h < 4f || w * h > 0.5f * Screen.width * Screen.height) continue; // vide, ou conteneur plein écran
                s_rects.Add(new Rect(minX / k, (Screen.height - maxY) / k, w / k, h / k)); // y depuis le haut, comme l'IMGUI
            }
            return s_rects;
        }

        private static void Collect()
        {
            s_parts.Clear();
            var hud = Hud.instance;
            if (hud == null) return;
            try
            {
                foreach (var t in hud.transform.root.GetComponentsInChildren<Transform>(true))
                    foreach (var n in s_names)
                        if ((string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase) || t.name.StartsWith(n, StringComparison.OrdinalIgnoreCase) && n == "HudBaseBoss") && t is RectTransform rt) { s_parts.Add(rt); break; }
                if (MessageHud.instance != null)
                {
                    if (MessageHud.instance.m_messageText != null) s_parts.Add(MessageHud.instance.m_messageText.rectTransform);
                    if (MessageHud.instance.m_messageCenterText != null) s_parts.Add(MessageHud.instance.m_messageCenterText.rectTransform);
                }
            }
            catch { } // un nom qui change avec une mise à jour du jeu : on s'en passe
        }
    }
}
