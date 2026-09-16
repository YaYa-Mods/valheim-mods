using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.UI;

namespace ModHub
{
    /// <summary>
    /// Aides de touches des mods dans le panneau d'aides du jeu (en bas à droite, « Attaque [Souris 1] »…), avec ses
    /// propres éléments (clones d'une entrée « Bloquer » : texte + touche dans son cartouche) : rien d'étranger à l'IHM.
    /// Chaque mod publie <c>public static List&lt;KeyValuePair&lt;string, KeyCode&gt;&gt; KeyHints()</c> (libellé, touche),
    /// contextuel (le scanner propose « Cible suivante » seulement quand une cible existe). Notre groupe n'apparaît que
    /// lorsque le jeu n'affiche aucun des siens (construction, combat, inventaire…) et disparaît avec le HUD.
    /// </summary>
    internal static class KeyHintBar
    {
        private const int MaxHints = 4;
        private static GameObject s_group;             // notre conteneur (clone de CombatHints/Keyboard)
        private static GameObject s_template;          // une entrée du jeu (texte + cartouche de touche)
        private static readonly List<GameObject> s_entries = new List<GameObject>();
        private static readonly List<Transform> s_gameGroups = new List<Transform>();
        private static float s_nextRefresh;
        private static bool s_failed;

        public static void Update()
        {
            if (s_failed || !Plugin.ShowKeyHints.Value) { if (s_group != null && s_group.activeSelf) s_group.SetActive(false); return; }
            var kh = KeyHints.instance;
            if (kh == null || Player.m_localPlayer == null) return;
            if (s_group == null && !Build(kh)) return;

            // Visible seulement quand aucun groupe du jeu ne l'est, HUD affiché, pas de fenêtre de mod ouverte
            bool gameHint = false;
            foreach (var g in s_gameGroups) if (g != null && g.gameObject.activeSelf) { gameHint = true; break; }
            bool show = !gameHint && !Hud.IsUserHidden() && !Plugin.WindowOpen && !TextInput.IsVisible() && !Minimap.IsOpen() && !Player.m_localPlayer.IsDead() && !Player.m_localPlayer.InCutscene();
            if (s_group.activeSelf != show) s_group.SetActive(show);
            if (!show || Time.unscaledTime < s_nextRefresh) return;
            s_nextRefresh = Time.unscaledTime + 0.5f;
            Refresh();
        }

        private static bool Build(KeyHints kh)
        {
            try
            {
                var combat = kh.transform.Find("CombatHints/Keyboard");
                var block = combat != null ? combat.Find("Block") : null;
                if (combat == null || block == null) { s_failed = true; Plugin.Log.LogWarning("Aides de touches : structure du panneau inconnue, désactivées"); return false; }
                foreach (var name in new[] { "BuildHints", "CombatHints", "InventoryHints", "FishingHints", "RadialHints", "BarberHints" })
                {
                    var g = kh.transform.Find(name);
                    if (g != null) s_gameGroups.Add(g);
                }
                s_template = block.gameObject;
                s_group = UnityEngine.Object.Instantiate(combat.gameObject, kh.transform);
                s_group.name = "ModsHints";
                foreach (Transform child in s_group.transform) UnityEngine.Object.Destroy(child.gameObject);
                // même position/ancrage que le groupe copié : le panneau du jeu place ses groupes au même endroit
                var rt = (RectTransform)s_group.transform; var src = (RectTransform)combat;
                rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.pivot = src.pivot; rt.anchoredPosition = src.anchoredPosition; rt.sizeDelta = src.sizeDelta;
                // le parent du groupe du jeu (CombatHints) peut être inactif : notre groupe est enfant direct de KeyHints, comme lui
                var parentRt = (RectTransform)combat.parent;
                rt.anchoredPosition += parentRt.anchoredPosition;
                s_group.SetActive(false);
                return true;
            }
            catch (Exception ex) { s_failed = true; Plugin.Log.LogWarning("Aides de touches : " + ex.Message); return false; }
        }

        private static void Refresh()
        {
            var hints = Collect();
            while (s_entries.Count < hints.Count && s_entries.Count < MaxHints)
            {
                var e = UnityEngine.Object.Instantiate(s_template, s_group.transform);
                e.name = "ModHint" + s_entries.Count;
                s_entries.Add(e);
            }
            for (int i = 0; i < s_entries.Count; i++)
            {
                bool used = i < hints.Count;
                if (s_entries[i].activeSelf != used) s_entries[i].SetActive(used);
                if (!used) continue;
                SetTexts(s_entries[i], hints[i].Key, KeyLabel(hints[i].Value));
            }
        }

        private static void SetTexts(GameObject entry, string label, string key)
        {
            var t = entry.transform.Find("Text")?.GetComponent<TMPro.TMP_Text>();
            if (t != null && t.text != label) t.text = label;
            var k = entry.transform.Find("key_bkg/Key")?.GetComponent<TMPro.TMP_Text>();
            if (k != null && k.text != key) k.text = key;
        }

        /// <summary>Nom de touche comme le jeu l'écrit dans ses cartouches (F7, Échap, Souris 1…).</summary>
        private static string KeyLabel(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Escape: return "Echap";
                case KeyCode.Mouse0: return "Souris 1"; case KeyCode.Mouse1: return "Souris 2"; case KeyCode.Mouse2: return "Souris 3";
                case KeyCode.LeftShift: case KeyCode.RightShift: return "Maj";
                case KeyCode.LeftControl: case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftAlt: case KeyCode.RightAlt: return "Alt";
                case KeyCode.Space: return "Espace";
                case KeyCode.Return: return "Entrée";
                case KeyCode.Tab: return "Tab";
                case KeyCode.Backspace: return "Retour";
                default:
                    string s = k.ToString();
                    if (s.StartsWith("Alpha") && s.Length == 6) return s.Substring(5);
                    if (s.StartsWith("Keypad")) return "Pavé " + s.Substring(6);
                    return s;
            }
        }

        private static readonly List<KeyValuePair<string, KeyCode>> s_collected = new List<KeyValuePair<string, KeyCode>>();
        private static List<KeyValuePair<string, KeyCode>> Collect()
        {
            s_collected.Clear();
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info.Instance == null) continue;
                var m = info.Instance.GetType().GetMethod("KeyHints", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m == null || m.GetParameters().Length != 0) continue;
                List<KeyValuePair<string, KeyCode>> list = null;
                try { list = m.Invoke(null, null) as List<KeyValuePair<string, KeyCode>>; }
                catch (Exception ex) { Plugin.Log.LogWarning($"KeyHints de {info.Metadata.Name} : {ex.Message}"); }
                if (list == null) continue;
                foreach (var kv in list)
                {
                    if (string.IsNullOrEmpty(kv.Key) || kv.Value == KeyCode.None) continue;
                    s_collected.Add(kv);
                    if (s_collected.Count >= MaxHints) return s_collected;
                }
            }
            return s_collected;
        }
    }
}
