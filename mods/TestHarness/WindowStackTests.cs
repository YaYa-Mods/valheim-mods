using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Une seule chose à l'écran à la fois : ouvrir une fenêtre de mod ferme les autres fenêtres de mods, l'inventaire
    /// et la grande carte ; et si le jeu reprend l'écran (inventaire, carte, menu) pendant qu'une fenêtre est ouverte,
    /// elle se ferme. Rien ne se superpose donc jamais.
    /// </summary>
    internal static class WindowStackTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        private sealed class Win
        {
            public string Name; private readonly Type _t; private readonly string _open, _flag;
            public Win(string mod, string type, string open, string flag) { Name = mod; _t = Asm(mod).GetType(type); _open = open; _flag = flag; }
            public bool Open => (bool)_t.GetField(_flag, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).GetValue(null);
            public void Show()
            {
                var m = _t.GetMethod(_open, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                m.Invoke(m.IsStatic ? null : UnityEngine.Object.FindObjectOfType(_t), null);
            }
            public void Close() => _t.GetMethod("CloseWindow", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var finder = new Win("ResourceFinder", "ResourceFinder.Plugin", "Open", "WindowOpen");
            var guide = new Win("Guide", "Guide.Plugin", "Toggle", "WindowOpen");
            var hub = new Win("ModHub", "ModHub.Plugin", "Toggle", "WindowOpen");
            var home = new Win("HomeTeleport", "HomeTeleport.Plugin", "Request", "DialogOpen");
            var all = new[] { finder, guide, hub, home };
            string State() => string.Join(", ", all.Select(w => $"{w.Name}={(w.Open ? "ouvert" : "fermé")}"));
            foreach (var w in all) w.Close();
            yield return null;

            // ---- chaque fenêtre chasse la précédente
            foreach (var w in all)
            {
                w.Show();
                yield return new WaitForSecondsRealtime(0.5f);
                int open = all.Count(x => x.Open);
                h.Check($"Fenêtres.{w.Name} seule à l'écran", w.Open && open == 1, State());
            }
            foreach (var w in all) w.Close();
            yield return null;

            // ---- l'inventaire ouvert : ouvrir une fenêtre le ferme
            var gui = InventoryGui.instance;
            gui.Show(null, 1);
            yield return new WaitForSecondsRealtime(0.6f);
            bool invBefore = InventoryGui.IsVisible();
            finder.Show();
            yield return new WaitForSecondsRealtime(0.6f);
            h.Check("Fenêtres.le scanner ferme l'inventaire", invBefore && !InventoryGui.IsVisible() && finder.Open, $"inventaire avant={invBefore}, après={InventoryGui.IsVisible()}, scanner={finder.Open}");
            finder.Close();
            yield return null;

            // ---- la grande carte ouverte : ouvrir une fenêtre la referme
            var map = Minimap.instance;
            map.SetMapMode(Minimap.MapMode.Large);
            yield return new WaitForSecondsRealtime(0.6f);
            bool mapBefore = Minimap.IsOpen();
            guide.Show();
            yield return new WaitForSecondsRealtime(0.6f);
            h.Check("Fenêtres.le guide ferme la grande carte", mapBefore && !Minimap.IsOpen() && guide.Open, $"carte avant={mapBefore}, après={Minimap.IsOpen()}, guide={guide.Open}");
            guide.Close();
            yield return null;

            // ---- fenêtre ouverte, le jeu reprend l'écran : la fenêtre se ferme
            hub.Show();
            yield return new WaitForSecondsRealtime(0.5f);
            bool hubBefore = hub.Open;
            gui.Show(null, 1);
            yield return new WaitForSecondsRealtime(0.8f);
            h.Check("Fenêtres.l'inventaire par-dessus ferme le hub", hubBefore && !hub.Open, $"hub avant={hubBefore}, après={hub.Open}, inventaire={InventoryGui.IsVisible()}");
            gui.Hide();
            yield return new WaitForSecondsRealtime(0.5f);

            finder.Show();
            yield return new WaitForSecondsRealtime(0.5f);
            bool finderBefore = finder.Open;
            map.SetMapMode(Minimap.MapMode.Large);
            yield return new WaitForSecondsRealtime(0.8f);
            h.Check("Fenêtres.la grande carte par-dessus ferme le scanner", finderBefore && !finder.Open, $"scanner avant={finderBefore}, après={finder.Open}, carte={Minimap.IsOpen()}");
            map.SetMapMode(Minimap.MapMode.Small);
            yield return new WaitForSecondsRealtime(0.5f);

            // ---- menu pause par-dessus (Menu.Show est privé selon les versions : réflexion, test ignoré s'il manque)
            var show = typeof(Menu).GetMethod("Show", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            var hide = typeof(Menu).GetMethod("Hide", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (show != null && hide != null && Menu.instance != null)
            {
                guide.Show();
                yield return new WaitForSecondsRealtime(0.5f);
                bool guideBefore = guide.Open;
                show.Invoke(Menu.instance, null);
                yield return new WaitForSecondsRealtime(0.8f);
                bool menuShown = Menu.IsVisible();
                h.Check("Fenêtres.le menu pause par-dessus ferme le guide", guideBefore && menuShown && !guide.Open, $"guide avant={guideBefore}, menu={menuShown}, guide après={guide.Open}");
                hide.Invoke(Menu.instance, null);
                yield return new WaitForSecondsRealtime(0.5f);
                // menu ouvert : les touches des fenêtres sont ignorées
                show.Invoke(Menu.instance, null);
                yield return new WaitForSecondsRealtime(0.5f);
                bool busy = (bool)Asm("Guide").GetType("ModsCommon.ModWindows").GetMethod("GameBusy", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                h.Check("Fenêtres.menu pause = touches ignorées", Menu.IsVisible() && busy, $"menu={Menu.IsVisible()}, GameBusy={busy}");
                hide.Invoke(Menu.instance, null);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            else Plugin.Log.LogInfo("[TEST] Menu.Show/Hide introuvables : test du menu pause ignoré");

            foreach (var w in all) w.Close();
            yield return null;
            h.Check("Fenêtres.tout refermé", all.All(w => !w.Open) && !InventoryGui.IsVisible() && !Minimap.IsOpen() && !Menu.IsVisible(), State());
        }
    }
}
