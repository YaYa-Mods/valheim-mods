using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using ModsCommon;
using UnityEngine.UI;

namespace ModHub
{
    /// <summary>
    /// Boutons de mods dans le panneau d'inventaire du jeu (Tab), à côté des boutons natifs Compétences / Textes /
    /// Trophées. Le bouton « Trophées » (celui dont le onClick vise InventoryGui.OnOpenTrophies) sert de modèle : il
    /// est cloné, décalé dans la même direction que la rangée native, son icône remplacée, son onClick rebranché.
    /// Entrées : « Config des mods » + ce que chaque plugin publie via <c>public static List&lt;KeyValuePair&lt;string,
    /// Action&gt;&gt; InventoryEntries()</c> (même convention « libellé|icône » que le menu radial). Recréés si le jeu
    /// reconstruit son interface (retour au menu principal).
    /// </summary>
    internal static class InventoryButtons
    {
        private static readonly List<GameObject> s_created = new List<GameObject>();

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        [HarmonyPostfix]
        private static void InventoryGui_Show(InventoryGui __instance)
        {
            try { Ensure(__instance); }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[ModHub] boutons d'inventaire : " + ex.Message); }
        }

        private static void Ensure(InventoryGui gui)
        {
            if (s_created.Count > 0 && s_created[0] != null) return;
            s_created.Clear();

            Button model = null;
            foreach (var b in gui.GetComponentsInChildren<Button>(true))
            {
                int n = b.onClick.GetPersistentEventCount();
                for (int i = 0; i < n; i++) if (b.onClick.GetPersistentMethodName(i) == "OnOpenTrophies") model = b;
            }
            if (model == null) { UnityEngine.Debug.LogWarning("[ModHub] bouton Trophées introuvable : pas de boutons de mods dans l'inventaire"); return; }

            // La rangée native : les boutons frères du modèle (compendium, compétences, trophées, PvP…), triés par x.
            var parent = model.transform.parent;
            bool layout = parent.GetComponent<LayoutGroup>() != null;
            var natives = new List<RectTransform>();
            foreach (Transform t in parent) if (t.GetComponent<Selectable>() != null && t.gameObject.activeSelf) natives.Add(t.GetComponent<RectTransform>()); // boutons et interrupteurs (PvP)
            natives.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));

            var entries = new List<Entry>(Entries());
            var created = new List<RectTransform>();
            foreach (var entry in entries)
            {
                string label = entry.Label, icon = null;
                int bar = label.IndexOf('|');
                if (bar >= 0) { icon = label.Substring(bar + 1); label = label.Substring(0, bar); }
                label = L.T(label); // libellé dans la langue du jeu
                var action = entry.Action;

                var go = UnityEngine.Object.Instantiate(model.gameObject, parent);
                go.name = "ModButton_" + label;
                go.transform.SetAsLastSibling();

                var btn = go.GetComponent<Button>();
                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener(() => { try { action(); } catch (Exception ex) { UnityEngine.Debug.LogWarning("[ModHub] bouton d'inventaire : " + ex.Message); } });

                var sprite = Radial.IconSprite(icon, entry.Source);
                if (sprite != null)
                {
                    // Le bouton natif dessine son icône deux fois : une copie sombre agrandie (ombre) puis l'icône. Nos
                    // icônes sont des traits, pas des silhouettes : l'ombre agrandie ferait un halo. On met notre sprite
                    // sur l'Image enfant la plus claire (l'icône) et on éteint les autres Image enfants.
                    Image best = null; float bright = -1f;
                    foreach (var img in go.GetComponentsInChildren<Image>(true))
                    {
                        if (img.gameObject == go) continue;
                        float g = img.color.grayscale * img.color.a;
                        if (g > bright) { bright = g; best = img; }
                    }
                    foreach (var img in go.GetComponentsInChildren<Image>(true))
                    {
                        if (img.gameObject == go) continue;
                        if (img == best) img.sprite = sprite; else img.enabled = false;
                    }
                }
                foreach (var tip in go.GetComponentsInChildren<UITooltip>(true)) { tip.m_text = label; tip.m_topic = ""; }
                foreach (var txt in go.GetComponentsInChildren<TMPro.TMP_Text>(true)) txt.text = label;

                s_created.Add(go);
                created.Add(go.GetComponent<RectTransform>());
            }

            // Une seule rangée régulière : natifs + nôtres, resserrés et légèrement réduits pour tenir dans le panneau
            // (5 natifs occupent déjà presque toute la largeur). La rangée reste centrée là où elle était.
            if (!layout && natives.Count >= 2)
            {
                float spacing = (natives[natives.Count - 1].anchoredPosition.x - natives[0].anchoredPosition.x) / (natives.Count - 1);
                float center = (natives[natives.Count - 1].anchoredPosition.x + natives[0].anchoredPosition.x) / 2f;
                float y = model.GetComponent<RectTransform>().anchoredPosition.y;
                var row = new List<RectTransform>(natives); row.AddRange(created);
                float ratio = Mathf.Clamp((natives.Count + 0.5f) / row.Count, 0.5f, 1f); // +0.5 : les marges libres du panneau
                float newSpacing = spacing * ratio, scale = Mathf.Lerp(0.6f, 1f, ratio);
                for (int i = 0; i < row.Count; i++)
                {
                    row[i].anchoredPosition = new Vector2(center + (i - (row.Count - 1) / 2f) * newSpacing, y);
                    row[i].localScale = Vector3.one * scale;
                }
                UnityEngine.Debug.Log($"[ModHub] {created.Count} bouton(s) de mods dans l'inventaire : rangée de {row.Count}, pas {spacing:0}→{newSpacing:0}, échelle {scale:0.00}");
            }
            else UnityEngine.Debug.Log($"[ModHub] {created.Count} bouton(s) de mods ajoutés à l'inventaire (mise en page automatique)");
        }

        private struct Entry { public string Label; public Action Action; public System.Reflection.Assembly Source; }

        private static IEnumerable<Entry> Entries()
        {
            yield return new Entry { Label = "Config des mods|@config.png", Action = () => { InventoryGui.instance?.Hide(); Plugin.Toggle(); }, Source = typeof(Radial).Assembly };
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info.Instance == null || info.Metadata.GUID == Plugin.Guid) continue;
                var m = info.Instance.GetType().GetMethod("InventoryEntries", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m == null || m.GetParameters().Length != 0) continue;
                List<KeyValuePair<string, Action>> list = null;
                try { list = m.Invoke(null, null) as List<KeyValuePair<string, Action>>; }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"[ModHub] InventoryEntries de {info.Metadata.Name} : {ex.Message}"); }
                if (list == null) continue;
                foreach (var kv in list) if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null) yield return new Entry { Label = kv.Key, Action = kv.Value, Source = info.Instance.GetType().Assembly };
            }
        }
    }
}
