using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using Valheim.UI;

namespace ModHub
{
    /// <summary>
    /// Entrée « Mods » dans le menu radial natif du jeu (manette : clic stick droit). Elle ouvre un sous-menu avec
    /// « Config des mods » et tout ce que les autres plugins publient via une méthode statique publique
    /// <c>RadialEntries()</c> renvoyant <c>List&lt;KeyValuePair&lt;string, Action&gt;&gt;</c> (libellé, éventuellement
    /// suivi de « |NomDePrefab » ou « |@fichier.png » (ressource embarquée dans sa DLL) pour l'icône, → action). Aucune dépendance entre les DLL : lecture par réflexion.
    /// Mécanique : le menu principal (ValheimRadialConfig) construit sa liste d'éléments puis appelle
    /// RadialBase.ConstructRadial ; on y insère notre GroupElement, exactement comme le jeu ajoute ses groupes.
    /// </summary>
    internal static class Radial
    {
        private static readonly MethodInfoCache s_setName = new MethodInfoCache("Name");
        private static readonly MethodInfoCache s_setDesc = new MethodInfoCache("Description");

        private sealed class MethodInfoCache
        {
            private readonly System.Reflection.MethodInfo _m;
            public MethodInfoCache(string prop) { _m = AccessTools.PropertySetter(typeof(RadialMenuElement), prop); }
            public void Set(RadialMenuElement e, string v) => _m?.Invoke(e, new object[] { v });
        }

        [HarmonyPatch(typeof(RadialBase), nameof(RadialBase.ConstructRadial))]
        [HarmonyPrefix]
        private static void RadialBase_ConstructRadial(RadialBase __instance, List<RadialMenuElement> elements)
        {
            try
            {
                if (!(__instance.CurrentConfig is ValheimRadialConfig main) || elements == null || RadialData.SO == null) return;
                // Après le dernier groupe du jeu (objets, emotes…), avant les cases « vide » / « dernier utilisé ».
                int at = 0;
                for (int i = 0; i < elements.Count; i++) if (elements[i] is GroupElement) at = i + 1;
                var group = UnityEngine.Object.Instantiate(RadialData.SO.GroupElement);
                group.Init(new ModsConfig(), main, __instance);
                elements.Insert(at, group);
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[ModHub] menu radial : " + ex.Message); }
        }

        /// <summary>Sous-menu « Mods ».</summary>
        private sealed class ModsConfig : IRadialConfig
        {
            public string LocalizedName => "Mods";
            public Sprite Sprite => IconSprite("@mods.png", typeof(Radial).Assembly);

            public void InitRadialConfig(RadialBase radial)
            {
                var elements = new List<RadialMenuElement>();
                foreach (var entry in Entries())
                {
                    string label = entry.Label, icon = null;
                    int bar = label.IndexOf('|');
                    if (bar >= 0) { icon = label.Substring(bar + 1); label = label.Substring(0, bar); }
                    var action = entry.Action;

                    var e = UnityEngine.Object.Instantiate(RadialData.SO.EmoteElement);
                    s_setName.Set(e, label);
                    s_setDesc.Set(e, "");
                    e.Interact = () => { try { action(); } catch (Exception ex) { UnityEngine.Debug.LogWarning("[ModHub] action radiale : " + ex.Message); } return true; };
                    e.CloseOnInteract = () => true;
                    var sprite = IconSprite(icon, entry.Source);
                    if (e.Icon != null) { e.Icon.sprite = sprite; e.Icon.gameObject.SetActive(sprite != null); }
                    elements.Add(e);
                }
                radial.ConstructRadial(elements);
            }
        }

        private struct Entry { public string Label; public Action Action; public System.Reflection.Assembly Source; }

        private static IEnumerable<Entry> Entries()
        {
            yield return new Entry { Label = "Config des mods|@config.png", Action = Plugin.Toggle, Source = typeof(Radial).Assembly };
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info.Instance == null || info.Metadata.GUID == Plugin.Guid) continue;
                var m = info.Instance.GetType().GetMethod("RadialEntries", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m == null || m.GetParameters().Length != 0) continue;
                List<KeyValuePair<string, Action>> list = null;
                try { list = m.Invoke(null, null) as List<KeyValuePair<string, Action>>; }
                catch (Exception ex) { UnityEngine.Debug.LogWarning($"[ModHub] RadialEntries de {info.Metadata.Name} : {ex.Message}"); }
                if (list == null) continue;
                foreach (var kv in list) if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null) yield return new Entry { Label = kv.Key, Action = kv.Value, Source = info.Instance.GetType().Assembly };
            }
        }

        private static readonly Dictionary<string, Sprite> s_embedded = new Dictionary<string, Sprite>();

        /// <summary>« @fichier.png » = ressource embarquée dans la DLL du plugin qui publie l'entrée ; sinon icône d'un item.</summary>
        internal static Sprite IconSprite(string icon, System.Reflection.Assembly source)
        {
            if (string.IsNullOrEmpty(icon)) return null;
            if (!icon.StartsWith("@")) return ItemSprite(icon);
            string key = source.GetName().Name + ":" + icon;
            if (s_embedded.TryGetValue(key, out var cached)) return cached;
            Sprite sprite = null;
            try
            {
                using (var stream = source.GetManifestResourceStream(icon.Substring(1)))
                {
                    if (stream != null)
                    {
                        var bytes = new byte[stream.Length]; stream.Read(bytes, 0, bytes.Length);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                        if (ImageConversion.LoadImage(tex, bytes))
                            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    }
                    else UnityEngine.Debug.LogWarning($"[ModHub] icône {icon} absente de {source.GetName().Name}");
                }
            }
            catch (Exception ex) { UnityEngine.Debug.LogWarning($"[ModHub] icône {icon} : {ex.Message}"); }
            s_embedded[key] = sprite;
            return sprite;
        }

        private static Sprite ItemSprite(string prefab)
        {
            var db = ObjectDB.instance;
            var item = db != null ? db.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            var icons = item?.m_itemData?.m_shared?.m_icons;
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }
    }
}
