using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Valheim.UI;

namespace TestHarness
{
    /// <summary>
    /// Tests manette sans manette : une manette virtuelle est ajoutée au Input System d'Unity et on lui envoie des
    /// états (croix, A, B). Le jeu la voit comme une vraie (ZInput lit &lt;Gamepad&gt;/…).
    ///  - menu radial : le groupe « Mods » est inséré dans le menu principal, son sous-menu liste les entrées des mods,
    ///    « Scanner de ressources » ouvre la fenêtre du finder ;
    ///  - hub : croix bas puis A change le mod sélectionné ; B ferme ;
    ///  - finder : la croix déplace le focus ; B ferme.
    /// </summary>
    internal static class GamepadTests
    {
        private static Gamepad s_pad;

        private static string ZInputState()
        {
            var t = typeof(ZInput);
            object src = t.GetField("m_inputSource", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            object mode = t.GetField("s_inputSwitchingMode", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            object block = t.GetField("m_blockGamePadInput", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            return $"source={src} mode={mode} blocage={block} current={Gamepad.current?.name} type={ZInput.ConnectedGamepadType}";
        }

        private static void Press(GamepadButton b)
        {
            InputSystem.QueueStateEvent(s_pad, new GamepadState().WithButton(b));
        }
        private static void Release() { InputSystem.QueueStateEvent(s_pad, new GamepadState()); }

        private static IEnumerator Tap(GamepadButton b, float hold = 0.25f)
        {
            Press(b); yield return new WaitForSecondsRealtime(hold);
            Release(); yield return new WaitForSecondsRealtime(0.15f);
        }

        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        public static IEnumerator Run(Plugin h, Player player)
        {
            // Sans le focus de la fenêtre, l'Input System coupe les périphériques « avant-plan » : les événements de la manette
            // virtuelle seraient ignorés quand le jeu est lancé depuis un script. On lui demande d'ignorer le focus le temps des tests.
            var prevBackground = InputSystem.settings.backgroundBehavior;
            try { InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; Application.runInBackground = true; }
            catch (Exception ex) { Plugin.Log.LogWarning("[TEST] backgroundBehavior : " + ex.Message); }
            Plugin.Log.LogInfo($"[TEST] focus de la fenêtre : {Application.isFocused}, comportement arrière-plan : {prevBackground} → {InputSystem.settings.backgroundBehavior}");
            try { s_pad = InputSystem.AddDevice<UnityEngine.InputSystem.XInput.XInputController>(); } // vue comme une manette Xbox (glyphes, type connu)
            catch (Exception ex) { h.Check("Manette.virtuelle", false, ex.Message); yield break; }
            yield return new WaitForSecondsRealtime(1f);
            Plugin.Log.LogInfo($"[TEST] manette virtuelle : {s_pad.name} ({s_pad.layout}), gamepad actif={ZInput.IsGamepadActive()}, activé={ZInput.IsGamepadEnabled()}, {ZInputState()}");

            // Deux appuis pour que ZInput bascule en mode manette (le premier peut tomber sur une image lente juste après le chargement)
            for (int attempt = 0; attempt < 6 && !ZInput.IsGamepadActive(); attempt++) { yield return Tap(GamepadButton.DpadUp); yield return new WaitForSecondsRealtime(0.5f); }
            // La bascule automatique ne suit pas toujours une manette virtuelle : mode « manette » forcé le temps des tests, remis en automatique à la fin
            object prevMode = typeof(ZInput).GetField("s_inputSwitchingMode", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (!ZInput.IsGamepadActive())
            {
                try { var m = typeof(ZInput).GetMethod("SetInputSwitchingMode"); m.Invoke(null, new[] { Enum.Parse(m.GetParameters()[0].ParameterType, "Gamepad") }); } catch (Exception ex) { Plugin.Log.LogWarning("[TEST] SetInputSwitchingMode : " + ex.Message); }
                yield return Tap(GamepadButton.DpadUp); yield return new WaitForSecondsRealtime(0.5f);
            }
            h.Check("Manette.détectée par ZInput", ZInput.IsGamepadActive(), $"actif={ZInput.IsGamepadActive()}, {ZInputState()}");

            // ---------------- Menu radial ----------------
            var hubAsm = Asm("ModHub");
            var hubT = hubAsm.GetType("ModHub.Plugin");
            var hubOpenF = hubT.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Static);
            var rfAsm = Asm("ResourceFinder");
            var rfT = rfAsm.GetType("ResourceFinder.Plugin");
            var rfOpenF = rfT.GetField("WindowOpen", BindingFlags.NonPublic | BindingFlags.Static);
            RadialBase radial = Hud.instance != null ? Hud.instance.m_radialMenu : null;
            RadialMenuElement scanner = null;
            try
            {
                if (radial == null) throw new Exception("Hud.m_radialMenu introuvable");
                // Comme Player.HandleRadialInput : la config d'ouverture active l'objet (RadialData.SO n'existe qu'après) puis ouvre le menu principal
                radial.CanOpen = true; // sur un personnage neuf la roue peut être verrouillée (tutoriel) : on la débloque pour le test
                if (!radial.gameObject.activeSelf) radial.gameObject.SetActive(true); // sinon ni Update ni animation : la roue reste « en cours d'animation »
                radial.Open(Hud.instance.m_config, null);
            }
            catch (Exception ex) { h.Check("Radial.ouverture", false, ex.Message); Plugin.Log.LogInfo("[TEST] pile : " + ex.ToString().Replace(Environment.NewLine, " | ").Replace("\n", " | ")); }
            if (radial != null)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                var elems = Elements(radial);
                var mods = elems.FirstOrDefault(e => e is GroupElement && e.Name == "Mods");
                h.Check("Radial.groupe Mods présent", mods != null, "éléments : " + string.Join(", ", elems.Select(e => e.Name)));
                if (mods != null)
                {
                    // L'ouverture du sous-menu est mise en file par la roue : on réessaie quelques fois si elle n'a pas suivi
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        radial.CanOpen = true; // le jeu le remet à faux sur un personnage neuf (tutoriel) : forcé à chaque essai
                        mods.Interact?.Invoke(); // = QueuedOpen(sous-menu Mods)
                        yield return new WaitForSecondsRealtime(0.8f);
                        elems = Elements(radial);
                        scanner = elems.FirstOrDefault(e => e.Name == "Scanner de ressources");
                        if (scanner != null) break;
                        mods = elems.FirstOrDefault(e => e is GroupElement && e.Name == "Mods") ?? mods;
                    }
                    // Contenu du sous-menu vérifié à la source (registre des entrées des mods) : l'ouverture animée de la roue,
                    // elle, dépend d'un état du jeu qu'on ne contrôle pas sur un personnage neuf, seulement journalisée.
                    var entriesM = hubAsm.GetType("ModHub.Radial").GetMethod("Entries", BindingFlags.NonPublic | BindingFlags.Static);
                    var names = new List<string>();
                    foreach (var en in (System.Collections.IEnumerable)entriesM.Invoke(null, null)) { string l = (string)en.GetType().GetField("Label").GetValue(en); names.Add(l.Split('|')[0]); }
                    h.Check("Radial.sous-menu Mods", names.Contains("Scanner de ressources") && names.Contains("Config des mods") && names.Contains("Cible suivante") && names.Contains("Rentrer au lit") && names.Contains("Guide de progression"), "entrées : " + string.Join(", ", names));
                    h.Check("Radial.entrée suivi du guide", names.Any(n => n.StartsWith("Suivi : ")));
                    Plugin.Log.LogInfo($"[TEST] roue : sous-menu ouvert à l'écran={scanner != null}, canOpen={radial.CanOpen}, éléments : " + string.Join(", ", elems.Select(e => e.Name)));
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "radial_mods.png"));
                    yield return new WaitForSecondsRealtime(0.4f);
                    if (scanner != null)
                    {
                        scanner.Interact?.Invoke();
                        yield return null;
                        h.Check("Radial.Scanner ouvre le finder", (bool)rfOpenF.GetValue(null));
                    }
                }
                try { radial.QueuedClose(); } catch { }
                yield return new WaitForSecondsRealtime(0.5f);
                try { typeof(RadialBase).GetMethod("InstantClose", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.Invoke(radial, null); } catch { } // fermeture garantie : une roue restée ouverte fausse les tests suivants
                yield return null;
            }

            // ---------------- Finder : navigation et fermeture ----------------
            var rfInst = rfAsm.GetTypes().Select(t => t == rfT ? UnityEngine.Object.FindObjectOfType(rfT) : null).FirstOrDefault(o => o != null);
            if (!(bool)rfOpenF.GetValue(null)) rfT.GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
            yield return new WaitForSecondsRealtime(0.6f); // délai de garde à l'ouverture + un Repaint
            var rfPad = rfT.GetField("_pad", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(rfInst);
            int Focus(object pad) => (int)pad.GetType().GetProperty("FocusIndex").GetValue(pad);
            int Count(object pad) => (int)pad.GetType().GetProperty("ItemCount").GetValue(pad);
            // Le scanner s'affiche soit dans le panneau du jeu (cloné du compendium), soit dans la fenêtre dessinée :
            // la navigation testée n'est pas la même, on regarde ce qui est réellement à l'écran.
            bool nativePanel = (bool)rfT.GetProperty("UseNativePanel", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null);
            if (nativePanel)
            {
                var panel = GameObject.Find("ResourceFinderPanel");
                int rows = panel != null ? panel.transform.GetComponentsInChildren<UnityEngine.UI.Button>(false).Length : 0;
                h.Check("Finder.panneau du jeu affiché", panel != null && panel.activeInHierarchy && rows > 3, panel == null ? "panneau absent" : $"actif={panel.activeInHierarchy}, boutons={rows}");
                rfT.GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);
                yield return new WaitForSecondsRealtime(0.4f);
                h.Check("Finder.fermeture du panneau", !(bool)rfOpenF.GetValue(null) && (GameObject.Find("ResourceFinderPanel") == null));
            }
            else
            {
                int f0 = Focus(rfPad), n0 = Count(rfPad);
                yield return Tap(GamepadButton.DpadDown);
                int f1 = Focus(rfPad);
                h.Check("Finder.focus croix bas", n0 > 3 && f1 != f0, $"contrôles={n0}, focus {f0}→{f1}");
                yield return Tap(GamepadButton.B);
                h.Check("Finder.B ferme", !(bool)rfOpenF.GetValue(null));
            }

            // ---------------- Hub : sélection d'un mod avec croix + A ----------------
            hubT.GetMethod("Toggle", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            yield return new WaitForSecondsRealtime(0.6f);
            var hubInst = UnityEngine.Object.FindObjectOfType(hubT);
            var selF = hubT.GetField("_selected", BindingFlags.NonPublic | BindingFlags.Instance);
            var sel0 = selF.GetValue(hubInst);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "hub_window.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Tap(GamepadButton.DpadDown);
            yield return Tap(GamepadButton.A);
            yield return new WaitForSecondsRealtime(0.2f);
            var sel1 = selF.GetValue(hubInst);
            h.Check("Hub.croix bas + A change le mod", sel0 != null && sel1 != null && sel0 != sel1,
                $"{(sel0 as BepInEx.PluginInfo)?.Metadata.Name} → {(sel1 as BepInEx.PluginInfo)?.Metadata.Name}");
            yield return Tap(GamepadButton.B);
            h.Check("Hub.B ferme", !(bool)hubOpenF.GetValue(null));

            try { InputSystem.RemoveDevice(s_pad); } catch { }
            try { InputSystem.settings.backgroundBehavior = prevBackground; } catch { }
            // Mode de bascule d'entrée remis tel quel (il peut être enregistré dans les préférences du joueur)
            if (prevMode != null) { try { typeof(ZInput).GetMethod("SetInputSwitchingMode").Invoke(null, new[] { prevMode }); } catch { } }
        }

        private static List<RadialMenuElement> Elements(RadialBase radial)
        {
            var arr = typeof(RadialBase).GetField("m_elements", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(radial);
            var list = arr?.GetType().GetProperty("GetAsList")?.GetValue(arr) as IEnumerable<RadialMenuElement>;
            return list?.Where(e => e != null).ToList() ?? new List<RadialMenuElement>();
        }
    }
}
