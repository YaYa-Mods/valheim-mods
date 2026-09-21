using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace HomeTeleport
{
    /// <summary>
    /// Retour au lit : téléporte le joueur sur son point d'apparition (le lit où il a dormi en dernier, celui que le
    /// jeu utilise à la mort), touche configurable ou entrée « Rentrer au lit » du menu radial. Sans lit : les pierres
    /// sacrificielles. Utilise Player.TeleportTo (le trajet des portails : écran de chargement, zone chargée avant
    /// l'arrivée), sans la restriction des portails sur le minerai. Refusé pendant qu'un ennemi vous prend pour cible.
    /// Une confirmation (fenêtre : touche/A pour confirmer, Échap/B pour annuler) évite le F8 accidentel.
    /// </summary>
    [BepInPlugin(Guid, "Home Teleport", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.hometeleport";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyCode> Key;
        internal static ConfigEntry<bool> Confirm;
        internal static ConfigEntry<bool> BlockWhenTargeted;
        internal static ConfigEntry<float> Cooldown;
        internal static bool DialogOpen;
        private static float s_lastTeleport = -1e9f;
        private static Plugin s_instance;
        private readonly Pad _pad = new Pad();
        private Rect _window;
        private string _where; private float _distance;

        private void Awake()
        {
            Log = Logger;
            s_instance = this;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le mod."));
            Key = Config.Bind("General", "Key", KeyCode.F8, L.T("Touche : rentrer au lit (point d'apparition). Aussi dans le menu radial, groupe Mods."));
            Confirm = Config.Bind("General", "Confirm", true, L.T("Demander confirmation avant de partir (touche à nouveau ou A pour confirmer, Échap ou B pour annuler)."));
            BlockWhenTargeted = Config.Bind("General", "BlockWhenTargeted", true, L.T("Refuser la téléportation quand un ennemi vous prend pour cible."));
            Cooldown = Config.Bind("General", "Cooldown", 0f, new ConfigDescription(L.T("Délai minimum (secondes) entre deux retours. 0 = aucun."), new AcceptableValueRange<float>(0f, 3600f)));
            Harmony.CreateAndPatchAll(typeof(Patches), Guid);
            ScrollGuard.Install(Guid, () => DialogOpen);
            Log.LogInfo($"Home Teleport chargé (touche {Key.Value})");
        }

        private void Update()
        {
            if (!Enabled.Value || Player.m_localPlayer == null) { DialogOpen = false; return; }
            if (DialogOpen && ModWindows.GameTookScreen()) DialogOpen = false; // inventaire, carte ou menu ouverts par-dessus : on s'efface
            if (DialogOpen)
            {
                // Confirmation : la même touche ou A ; annulation : Échap ou B
                if (ZInput.GetKeyDown(Key.Value, false) || ZInput.GetKeyDown(KeyCode.Return, false) || ZInput.GetButtonDown("JoyButtonA")) { DialogOpen = false; Teleport(); }
                else if (ZInput.GetKeyDown(KeyCode.Escape, false) || _pad.Update()) DialogOpen = false;
                return;
            }
            if (ZInput.GetKeyDown(Key.Value, false) && !TextInput.IsVisible() && !ModWindows.GameBusy()) Request();
        }

        /// <summary>Aides de touches (panneau du jeu, via Mod Hub).</summary>
        public static List<KeyValuePair<string, KeyCode>> KeyHints() => !Enabled.Value ? new List<KeyValuePair<string, KeyCode>>() : new List<KeyValuePair<string, KeyCode>>
        {
            new KeyValuePair<string, KeyCode>(L.T("Rentrer au lit"), Key.Value),
        };

        public static List<KeyValuePair<string, Action>> RadialEntries() => new List<KeyValuePair<string, Action>>
        {
            new KeyValuePair<string, Action>("Rentrer au lit|@home.png", () => { if (Enabled.Value) Request(); }),
        };

        /// <summary>Demande de retour : vérifications, puis confirmation (ou départ direct si désactivée).</summary>
        internal static void Request()
        {
            var player = Player.m_localPlayer;
            if (player == null || s_instance == null) return;
            if (!CanGo(player, out string where)) return;
            if (!Confirm.Value) { Teleport(); return; }
            s_instance._where = where;
            s_instance._distance = Vector3.Distance(player.transform.position, Destination(out _));
            s_instance._window = Theme.CenteredWindow(560f, 150f);
            ModWindows.TakeScreen();
            s_instance._pad.OnOpened();
            DialogOpen = true;
        }

        /// <summary>Fermeture demandée par un autre mod qui ouvre sa propre fenêtre (convention ModWindows).</summary>
        public static void CloseWindow() { DialogOpen = false; }

        private static bool CanGo(Player player, out string where)
        {
            where = null;
            if (player.IsTeleporting() || player.IsDead() || player.InCutscene()) return false;
            if (BlockWhenTargeted.Value && player.IsTargeted()) { player.Message(MessageHud.MessageType.Center, L.T("Impossible : un ennemi vous prend pour cible")); return false; }
            float since = Time.time - s_lastTeleport;
            if (Cooldown.Value > 0f && since < Cooldown.Value) { player.Message(MessageHud.MessageType.Center, L.F("Retour possible dans {0:0} s", Cooldown.Value - since)); return false; }
            Destination(out where);
            return true;
        }

        /// <summary>Point d'arrivée : lit (point de réapparition) sinon pierres sacrificielles.</summary>
        private static Vector3 Destination(out string where)
        {
            var profile = Game.instance.GetPlayerProfile();
            if (profile.HaveCustomSpawnPoint()) { where = L.T("votre lit"); return profile.GetCustomSpawnPoint(); }
            var target = PlayerProfile.m_originalSpawnPoint;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetLocationIcon(Game.instance.m_StartLocation, out var start)) target = start;
            where = L.T("les pierres sacrificielles (pas de lit)");
            return target;
        }

        /// <summary>Le départ lui-même (sans confirmation), utilisé aussi par les tests.</summary>
        internal static void GoHome() { if (Player.m_localPlayer != null && CanGo(Player.m_localPlayer, out _)) Teleport(); }

        private static void Teleport()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            var target = Destination(out string where);
            if (player.TeleportTo(target + Vector3.up * 0.3f, player.transform.rotation, true))
            {
                s_lastTeleport = Time.time;
                player.Message(MessageHud.MessageType.Center, L.T("Retour vers ") + where);
                Log.LogInfo($"Téléportation vers {where} : {target}");
            }
        }

        // ------------------------------------------------------------------ fenêtre de confirmation

        private void OnGUI()
        {
            if (!DialogOpen || Player.m_localPlayer == null) return;
            if (_pad.ConsumeSkipRepaint()) return;
            var prev = Theme.Begin();
            Theme.Fill(new Rect(0f, 0f, Theme.ScreenSize.x, Theme.ScreenSize.y), new Color(0f, 0f, 0f, 0.45f)); // le jeu s'assombrit derrière la question
            _window = GUILayout.Window(GetHashCode(), _window, DrawDialog, L.T("Rentrer au lit ?"));
            Theme.End(prev);
        }

        private static Texture2D s_icon; private static bool s_iconTried;
        private static Texture2D Icon
        {
            get
            {
                if (s_iconTried) return s_icon;
                s_iconTried = true;
                try
                {
                    using (var st = typeof(Plugin).Assembly.GetManifestResourceStream("home.png"))
                    {
                        if (st == null) return null;
                        var bytes = new byte[st.Length]; st.Read(bytes, 0, bytes.Length);
                        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(bytes); t.filterMode = FilterMode.Bilinear; s_icon = t;
                    }
                }
                catch { }
                return s_icon;
            }
        }

        private void DrawDialog(int id)
        {
            _pad.BeginWindow();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (Icon != null) { GUILayout.Label(GUIContent.none, GUILayout.Width(40f), GUILayout.Height(40f)); GUI.DrawTexture(GUILayoutUtility.GetLastRect(), Icon, ScaleMode.ScaleToFit, true); GUILayout.Space(8f); }
            GUILayout.BeginVertical();
            GUILayout.Label(L.F("Vous allez être téléporté vers <b>{0}</b>", L.T(_where)) + (_distance < 8f ? L.T(", vous y êtes déjà.") : L.F(", à <color=#f5a847>{0:0} m</color> d'ici.", _distance)), Theme.Muted);
            GUILayout.Label(Pad.Active ? L.T("<color=#7cc35a><b>A</b></color> partir    <color=#e0524a><b>B</b></color> rester") : L.F("{0} ou Entrée : partir   ·   Échap : rester", Key.Value), Theme.Muted);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (_pad.Button(L.T("Partir"), GUILayout.Height(34))) { DialogOpen = false; Teleport(); }
            if (_pad.Button(L.T("Rester"), GUILayout.Height(34))) DialogOpen = false;
            GUILayout.EndHorizontal();
            _pad.EndWindow();
        }
    }

    internal static class Patches
    {
        // Fenêtre ouverte = comme un champ texte actif : souris libre, joueur immobile (et le F8 ne déclenche rien d'autre).
        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        [HarmonyPostfix]
        private static void TextInput_IsVisible(ref bool __result)
        {
            if (Plugin.DialogOpen) __result = true;
        }
    }
}
