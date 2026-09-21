using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ModsCommon
{
    /// <summary>
    /// Une seule chose à l'écran à la fois. Quand un mod ouvre sa fenêtre, il ferme d'abord ce qui prend déjà l'écran :
    /// l'inventaire, la grande carte et les fenêtres des autres mods (convention : méthode statique publique
    /// « void CloseWindow() » sur le plugin, trouvée par réflexion, aucune dépendance entre DLL). Tant que sa fenêtre
    /// est ouverte, si le jeu reprend l'écran (inventaire, carte, menu, boutique, mort), la fenêtre se ferme d'elle-même.
    /// Rien ne se superpose donc jamais à une fenêtre de mod, et une fenêtre de mod ne recouvre jamais un écran du jeu.
    /// </summary>
    internal static class ModWindows
    {
        private static MethodInfo[] s_closers = new MethodInfo[0];
        private static float s_closersNext;
        private static float s_openedAt = -10f;

        /// <summary>Le jeu affiche déjà un écran qui prend tout (menu pause, boutique, console, chat, mort) : la touche est ignorée.</summary>
        public static bool GameBusy()
        {
            if (Menu.IsVisible() || StoreGui.IsVisible() || Console.IsVisible()) return true;
            if (Chat.instance != null && Chat.instance.HasFocus()) return true;
            if (Minimap.instance != null && Minimap.InTextInput()) return true;
            var p = Player.m_localPlayer;
            return p == null || p.IsDead() || p.IsTeleporting();
        }

        /// <summary>À appeler quand la fenêtre s'ouvre : ferme l'inventaire, la grande carte et les fenêtres des autres mods.</summary>
        public static void TakeScreen()
        {
            s_openedAt = Time.unscaledTime;
            try { if (InventoryGui.IsVisible()) InventoryGui.instance.Hide(); } catch { }
            try { if (Minimap.instance != null && Minimap.IsOpen()) Minimap.instance.SetMapMode(Minimap.MapMode.Small); } catch { }
            foreach (var m in Closers())
            {
                try { m.Invoke(null, null); } catch { } // un autre mod qui refuse de se fermer ne doit pas bloquer celui-ci
            }
        }

        /// <summary>Vrai quand le jeu a repris l'écran pendant que la fenêtre était ouverte : elle doit se fermer.</summary>
        public static bool GameTookScreen()
        {
            if (Time.unscaledTime - s_openedAt < 0.3f) return false; // l'inventaire que l'on vient de fermer met deux images à disparaître
            if (InventoryGui.IsVisible() || Menu.IsVisible() || StoreGui.IsVisible()) return true;
            if (Minimap.instance != null && Minimap.IsOpen()) return true;
            var p = Player.m_localPlayer;
            return p == null || p.IsDead() || p.IsTeleporting();
        }

        /// <summary>« CloseWindow() » des autres mods, relu toutes les 5 s (les mods peuvent être activés ou désactivés en jeu).</summary>
        private static MethodInfo[] Closers()
        {
            if (Time.unscaledTime < s_closersNext) return s_closers;
            s_closersNext = Time.unscaledTime + 5f;
            var found = new List<MethodInfo>();
            var self = typeof(ModWindows).Assembly;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == self) continue;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Namespace == null || !t.IsClass || t.Name != "Plugin") continue;
                        var m = t.GetMethod("CloseWindow", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                        if (m != null && m.ReturnType == typeof(void)) found.Add(m);
                    }
                }
                catch { } // assembly non inspectable : sans importance, on s'en passe
            }
            s_closers = found.ToArray();
            return s_closers;
        }
    }
}
