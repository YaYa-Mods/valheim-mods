using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using ModsCommon;

namespace Recycle
{
    /// <summary>
    /// Le Recycleur : un meuble à poser au marteau (onglet Meubles, établi à portée, 10 bois + 10 pierre). On y jette
    /// ce qu'on ne veut plus, un bouton « Recycler » dans son panneau transforme tout ce qui a une recette en matériaux
    /// (mêmes règles que la fenêtre F3), qui restent dedans ; le reste n'est pas touché. Le modèle est un coffre que le
    /// jeu ne laisse jamais fabriquer (coffre dvergr), impossible à confondre avec un coffre ordinaire ; il reçoit les
    /// composants de placement et d'usure du coffre du jeu. Aucune ressource externe : tout est cloné en jeu.
    /// </summary>
    internal static class Recycler
    {
        public const string PrefabName = "vmods_recycler";
        private static readonly string[] s_models = { "TreasureChest_dvergrtown", "TreasureChest_fCrypt", "TreasureChest_blackforest", "piece_chest" };
        private static GameObject s_prefab, s_holder;
        private static Sprite s_icon;

        // ------------------------------------------------------------------ prefab

        /// <summary>Avant que la scène réseau ne recense ses prefabs : le nôtre est ajouté à la liste (il recevra son hash comme les autres).</summary>
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPrefix]
        private static void ZNetScene_Awake(ZNetScene __instance)
        {
            try
            {
                if (s_prefab == null) Build(__instance.m_prefabs);
                if (s_prefab != null && !__instance.m_prefabs.Contains(s_prefab)) __instance.m_prefabs.Add(s_prefab);
            }
            catch (Exception ex) { Plugin.Log.LogError("Recycleur : prefab impossible : " + ex); }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        private static void ObjectDB_Awake(ObjectDB __instance) => AddToHammer(__instance);

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        private static void ObjectDB_CopyOtherDB(ObjectDB __instance) => AddToHammer(__instance);

        private static void AddToHammer(ObjectDB db)
        {
            try
            {
                if (s_prefab == null || db == null) return;
                var hammer = db.GetItemPrefab("Hammer");
                var table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces : null;
                if (table == null) return;
                if (!table.m_pieces.Contains(s_prefab)) { table.m_pieces.Add(s_prefab); Plugin.Log.LogInfo("Recycleur ajouté au marteau"); }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Recycleur : marteau : " + ex.Message); }
        }

        private static GameObject Find(List<GameObject> prefabs, string name) => prefabs.FirstOrDefault(p => p != null && p.name == name);

        private static void Build(List<GameObject> prefabs)
        {
            GameObject model = null;
            foreach (var n in s_models) { model = Find(prefabs, n); if (model != null) break; }
            var chest = Find(prefabs, "piece_chest") ?? Find(prefabs, "piece_chest_wood");
            var wood = Find(prefabs, "Wood"); var stone = Find(prefabs, "Stone");
            if (model == null || chest == null || wood == null || stone == null) { Plugin.Log.LogWarning($"Recycleur : modèle ou coffre introuvable (modèle={(model != null)}, coffre={(chest != null)})"); return; }

            // Conteneur inactif pour porter le prefab : rien ne s'instancie tant qu'il n'est pas posé
            s_holder = new GameObject("vmods.recycle.prefabs"); s_holder.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(s_holder);
            s_prefab = UnityEngine.Object.Instantiate(model, s_holder.transform);
            s_prefab.name = PrefabName;

            // Ce coffre de lieu a des réflexes de butin : tirage d'objets à l'ouverture, autodestruction une fois vidé, apparition aléatoire
            var container = s_prefab.GetComponent<Container>();
            if (container == null) container = s_prefab.AddComponent<Container>();
            container.m_name = ""; // le panneau du coffre montre notre bouton à la place du nom ; le nom est rendu au survol (GetHoverText)
            container.m_width = 8; container.m_height = 4;
            container.m_autoDestroyEmpty = false;
            container.m_checkGuardStone = true;
            if (container.m_defaultItems != null) { container.m_defaultItems.m_drops = new List<DropTable.DropData>(); container.m_defaultItems.m_dropChance = 0f; }
            foreach (var junk in s_prefab.GetComponents<MonoBehaviour>().Where(c => c != null && (c.GetType().Name == "RandomSpawn" || c.GetType().Name == "DungeonGenerator")).ToList())
                UnityEngine.Object.DestroyImmediate(junk);
            var nview = s_prefab.GetComponent<ZNetView>() ?? s_prefab.AddComponent<ZNetView>();
            nview.m_persistent = true;

            // Placement et démolition au marteau : les réglages du coffre du jeu
            var piece = s_prefab.GetComponent<Piece>() ?? s_prefab.AddComponent<Piece>();
            CopyFields(chest.GetComponent<Piece>(), piece);
            piece.m_name = L.T("Recycleur");
            piece.m_description = L.T("Jetez-y ce qui ne sert plus : le bouton « Recycler » de son panneau rend une part des matériaux de fabrication.");
            piece.m_icon = Icon();
            piece.m_category = Piece.PieceCategory.Furniture;
            piece.m_comfort = 0; piece.m_comfortGroup = default; piece.m_comfortObject = null;
            piece.m_resources = new[]
            {
                new Piece.Requirement { m_resItem = wood.GetComponent<ItemDrop>(), m_amount = Plugin.PieceWood.Value, m_recover = true },
                new Piece.Requirement { m_resItem = stone.GetComponent<ItemDrop>(), m_amount = Plugin.PieceStone.Value, m_recover = true },
            };
            var wear = s_prefab.GetComponent<WearNTear>() ?? s_prefab.AddComponent<WearNTear>();
            var wsrc = chest.GetComponent<WearNTear>();
            if (wsrc != null) CopyFields(wsrc, wear);
            wear.m_new = wear.m_worn = wear.m_broken = wear.m_wet = null; wear.m_snow = wear.m_snowWorn = wear.m_snowBroken = null; wear.m_fragmentRoots = null;
            wear.m_health = 400f; wear.m_supports = false;
            Plugin.Log.LogInfo($"Recycleur : prefab construit sur {model.name}, {container.m_width}×{container.m_height} cases, coût {Plugin.PieceWood.Value} bois + {Plugin.PieceStone.Value} pierre");
        }

        /// <summary>Copie tous les champs publics sérialisés d'un composant vers un autre du même type (les références Unity restent partagées).</summary>
        private static void CopyFields<T>(T from, T to) where T : Component
        {
            if (from == null || to == null) return;
            foreach (var f in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.FieldType == typeof(Action) || typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                try { f.SetValue(to, f.GetValue(from)); } catch { }
            }
        }

        private static Sprite Icon()
        {
            if (s_icon != null) return s_icon;
            try
            {
                using (var st = typeof(Plugin).Assembly.GetManifestResourceStream("recycle.png"))
                {
                    if (st == null) return null;
                    var bytes = new byte[st.Length]; st.Read(bytes, 0, bytes.Length);
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(bytes); t.filterMode = FilterMode.Bilinear;
                    s_icon = Sprite.Create(t, new Rect(0f, 0f, t.width, t.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Recycleur : icône : " + ex.Message); }
            return s_icon;
        }

        internal static bool IsRecycler(Container c) => c != null && c.name.StartsWith(PrefabName, StringComparison.Ordinal);

        // Le nom retiré du panneau revient dans le texte de survol du meuble
        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        [HarmonyPostfix]
        private static void Container_GetHoverText(Container __instance, ref string __result)
        {
            if (IsRecycler(__instance)) __result = L.T("Recycleur") + __result;
        }

        // ------------------------------------------------------------------ recyclage du contenu

        /// <summary>Tout ce qui a une recette dans le meuble devient matériaux (laissés dedans) ; le reste ne bouge pas.</summary>
        internal static int RecycleAll(Container c)
        {
            var inv = c?.GetInventory();
            var player = Player.m_localPlayer;
            if (inv == null || player == null) return 0;
            var totals = new Dictionary<string, int>(); var names = new Dictionary<string, string>();
            int count = 0;
            foreach (var item in inv.GetAllItems().ToList())
            {
                var recipe = Plugin.RecipeOf(item);
                if (recipe == null) continue;
                var yield = Plugin.YieldOf(item, recipe);
                inv.RemoveItem(item);
                count++;
                foreach (var y in yield)
                {
                    var added = inv.AddItem(y.Key.name, y.Value, 1, 0, 0L, "", false, false);
                    if (added == null) ItemDrop.DropItem(y.Key.m_itemData, y.Value, c.transform.position + Vector3.up * 1.2f, Quaternion.identity);
                    totals[y.Key.name] = (totals.TryGetValue(y.Key.name, out int n) ? n : 0) + y.Value;
                    names[y.Key.name] = Localization.instance.Localize(y.Key.m_itemData.m_shared.m_name);
                }
            }
            if (count == 0) { player.Message(MessageHud.MessageType.Center, L.T("Rien à recycler dans le Recycleur")); return 0; }
            string got = totals.Count == 0 ? L.T("rien récupéré") : string.Join(", ", totals.Select(kv => $"{kv.Value} × {names[kv.Key]}"));
            player.Message(MessageHud.MessageType.Center, L.F("{0} objet(s) recyclé(s) → {1}", count, got));
            Plugin.Log.LogInfo($"Recycleur : {count} objet(s) → {got}");
            return count;
        }

        // ------------------------------------------------------------------ bouton dans le panneau du coffre

        private static readonly AccessTools.FieldRef<InventoryGui, Container> s_current = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        private static GameObject s_button; private static TMPro.TMP_Text s_buttonText;
        private static float s_armedUntil;

        /// <summary>À appeler chaque image : le bouton n'existe que pendant qu'un Recycleur est ouvert.</summary>
        internal static void Update()
        {
            var gui = InventoryGui.instance;
            if (gui == null) return;
            var current = InventoryGui.IsVisible() ? s_current(gui) : null;
            bool show = Plugin.Enabled.Value && IsRecycler(current);
            if (!show) { if (s_button != null && s_button.activeSelf) s_button.SetActive(false); return; }
            if (s_button == null) BuildButton(gui);
            if (s_button == null) return;
            if (!s_button.activeSelf) s_button.SetActive(true);
            bool armed = Time.unscaledTime < s_armedUntil;
            string want = armed ? L.T("Confirmer ?") : L.T("Recycler");
            if (s_buttonText != null && s_buttonText.text != want) s_buttonText.text = want;
        }

        private static void BuildButton(InventoryGui gui)
        {
            var model = gui.m_stackAllButton != null ? gui.m_stackAllButton : gui.m_takeAllButton;
            if (model == null) return;
            s_button = UnityEngine.Object.Instantiate(model.gameObject, model.transform.parent);
            s_button.name = "RecycleAllButton";
            var rt = s_button.GetComponent<RectTransform>(); var mrt = model.GetComponent<RectTransform>();
            // À la place du nom du coffre, entre « Tout prendre » et « Objets similaires » (le Recycleur n'a pas de nom affiché dans le panneau)
            var nameRt = gui.m_containerName != null ? gui.m_containerName.rectTransform : null;
            if (nameRt != null)
            {
                var corners = new Vector3[4]; nameRt.GetWorldCorners(corners);
                var center = (corners[0] + corners[2]) * 0.5f;
                rt.SetParent(nameRt.parent, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(mrt.rect.width, mrt.rect.height);
                rt.position = center;
            }
            else rt.anchoredPosition = mrt.anchoredPosition - new Vector2(mrt.rect.width + 12f, 0f);
            var btn = s_button.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(OnClick);
            s_buttonText = s_button.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (s_buttonText != null) s_buttonText.text = L.T("Recycler");
            var tip = s_button.GetComponentInChildren<UITooltip>(true);
            if (tip != null) { tip.m_text = L.T("Transforme tout ce qui a une recette dans ce meuble en matériaux (une part du coût), laissés dedans."); tip.m_topic = ""; }
            foreach (var pad in s_button.GetComponentsInChildren<UIGamePad>(true)) { if (pad.m_hint != null) UnityEngine.Object.Destroy(pad.m_hint); UnityEngine.Object.Destroy(pad); } // le badge « RS » vaut pour l'original, pas pour la copie
        }

        private static void OnClick()
        {
            var gui = InventoryGui.instance;
            var current = gui != null ? s_current(gui) : null;
            if (!IsRecycler(current)) return;
            if (Time.unscaledTime < s_armedUntil) { s_armedUntil = 0f; RecycleAll(current); }
            else s_armedUntil = Time.unscaledTime + 4f;
        }
    }
}
