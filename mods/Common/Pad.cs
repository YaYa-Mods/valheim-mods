using System.Collections.Generic;
using UnityEngine;

namespace ModsCommon
{
    /// <summary>
    /// Navigation manette pour nos fenêtres IMGUI (hub, finder). Principe : chaque contrôle interactif est dessiné via
    /// les enveloppes <c>Pad.Button / Toggle / HorizontalSlider / BeginScrollView</c>, qui enregistrent son rectangle
    /// pendant le Repaint. Un contrôle « focalisé » est encadré ; la croix / le stick gauche déplacent le focus vers le
    /// contrôle le plus proche dans la direction voulue, A l'active, B ferme la fenêtre, le stick droit fait défiler.
    /// Le clavier et la souris continuent de fonctionner exactement comme avant (les enveloppes appellent GUILayout).
    ///
    /// Activation : le bouton A est lu dans Update() (hors OnGUI) et l'activation est rendue pendant l'événement Layout
    /// suivant, puis le Repaint de cette image est sauté (<see cref="SkipRepaint"/>) : la structure de la fenêtre peut
    /// changer sans jamais désynchroniser Layout et Repaint (erreurs « Getting control N's position… »).
    /// Une instance par fenêtre ; chaque fenêtre ne connaît que la sienne.
    /// </summary>
    internal sealed class Pad
    {
        private struct Item { public Rect Rect; public int Scroll; public bool Slider; }
        private struct ScrollGroup { public Rect Viewport; public Vector2 Scroll; public int FirstItem; }

        public const string HintsText = "<color=#7cc35a><b>A</b></color> valider    <color=#e0524a><b>B</b></color> fermer    <b>croix</b> ou <b>stick gauche</b> naviguer    <b>stick droit</b> défiler";

        /// <summary>Manette utilisée en dernier (le jeu bascule automatiquement clavier ↔ manette).</summary>
        public static bool Active => ZInput.IsGamepadActive();

        // --- état persistant entre les images ---
        private readonly List<Item> _items = new List<Item>();          // rectangles du dernier Repaint (coordonnées fenêtre)
        private readonly List<ScrollGroup> _scrolls = new List<ScrollGroup>();
        private readonly Dictionary<int, Vector2> _scrollDelta = new Dictionary<int, Vector2>(); // défilement demandé par groupe
        private int _focus;
        private int _activate = -1;        // index à activer au prochain Layout
        private float _sliderDelta;        // ajustement demandé sur le curseur focalisé (-1 / +1)
        private float _openedAt;
        private float _nextRepeat;
        private int _lastDir;

        // --- état d'un passage OnGUI ---
        private readonly List<Item> _pass = new List<Item>();
        private readonly List<ScrollGroup> _passScrolls = new List<ScrollGroup>();
        private int _index;
        private int _currentScroll = -1;
        private int _passScrollIndex;

        /// <summary>Vrai le temps d'une image après une activation : la fenêtre ne doit pas être dessinée au Repaint.</summary>
        public bool SkipRepaint { get; private set; }
        public int FocusIndex => _focus;
        public int ItemCount => _items.Count;

        public void OnOpened()
        {
            _focus = 0; _activate = -1; _sliderDelta = 0f; _openedAt = Time.unscaledTime;
            _items.Clear(); _scrolls.Clear(); _scrollDelta.Clear();
        }

        // ------------------------------------------------------------------ entrée (Update, hors OnGUI)

        /// <summary>À appeler dans Update() quand la fenêtre est ouverte. Renvoie vrai si B (fermer) a été pressé.</summary>
        public bool Update()
        {
            if (Time.unscaledTime - _openedAt < 0.3f) return false; // le bouton qui a ouvert la fenêtre ne doit pas agir dedans
            if (ZInput.GetButtonDown("JoyButtonB")) return true;
            if (ZInput.GetButtonDown("JoyButtonA") && _items.Count > 0) _activate = Mathf.Clamp(_focus, 0, _items.Count - 1);

            int dir = 0; // 1 haut, 2 bas, 3 gauche, 4 droite
            if (ZInput.GetButton("JoyDPadUp") || ZInput.GetButton("JoyLStickUp")) dir = 1;
            else if (ZInput.GetButton("JoyDPadDown") || ZInput.GetButton("JoyLStickDown")) dir = 2;
            else if (ZInput.GetButton("JoyDPadLeft") || ZInput.GetButton("JoyLStickLeft")) dir = 3;
            else if (ZInput.GetButton("JoyDPadRight") || ZInput.GetButton("JoyLStickRight")) dir = 4;
            if (dir != 0)
            {
                bool fire = dir != _lastDir || Time.unscaledTime >= _nextRepeat;
                if (fire)
                {
                    _nextRepeat = Time.unscaledTime + (dir != _lastDir ? 0.4f : 0.12f);
                    Move(dir);
                }
            }
            _lastDir = dir;

            // Stick droit : défilement du groupe contenant le focus (ou du premier)
            float ry = ZInput.GetJoyRightStickY();
            if (Mathf.Abs(ry) > 0.25f && _scrolls.Count > 0)
            {
                int g = _focus < _items.Count && _items[_focus].Scroll >= 0 ? _items[_focus].Scroll : 0;
                AddScroll(g, new Vector2(0f, ry * 900f * Time.unscaledDeltaTime));
            }
            return false;
        }

        private void Move(int dir)
        {
            if (_items.Count == 0) return;
            _focus = Mathf.Clamp(_focus, 0, _items.Count - 1);
            var cur = _items[_focus];
            if (cur.Slider && (dir == 3 || dir == 4)) { _sliderDelta = dir == 3 ? -1f : 1f; return; }

            var c = cur.Rect.center;
            int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < _items.Count; i++)
            {
                if (i == _focus) continue;
                var o = _items[i].Rect.center;
                float dx = o.x - c.x, dy = o.y - c.y;
                float along, across;
                switch (dir)
                {
                    case 1: along = -dy; across = dx; break;
                    case 2: along = dy; across = dx; break;
                    case 3: along = -dx; across = dy; break;
                    default: along = dx; across = dy; break;
                }
                // Il faut être devant (au moins 4 px) ; l'écart latéral compte double pour rester dans la colonne / ligne.
                if (along < 4f) continue;
                float score = along + Mathf.Abs(across) * 2f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best < 0) return;
            _focus = best;
            EnsureVisible(_items[best]);
        }

        private void EnsureVisible(Item it)
        {
            if (it.Scroll < 0 || it.Scroll >= _scrolls.Count) return;
            var g = _scrolls[it.Scroll];
            float top = it.Rect.y, bottom = it.Rect.yMax;
            if (top < g.Viewport.y) AddScroll(it.Scroll, new Vector2(0f, top - g.Viewport.y - 4f));
            else if (bottom > g.Viewport.yMax) AddScroll(it.Scroll, new Vector2(0f, bottom - g.Viewport.yMax + 4f));
        }

        private void AddScroll(int group, Vector2 delta)
        {
            _scrollDelta.TryGetValue(group, out var d);
            _scrollDelta[group] = d + delta;
        }

        // ------------------------------------------------------------------ passage OnGUI

        /// <summary>Début du dessin de la fenêtre (dans la fonction de fenêtre).</summary>
        public void BeginWindow()
        {
            _index = 0; _currentScroll = -1; _passScrollIndex = 0;
            if (Event.current.type == EventType.Repaint) { _pass.Clear(); _passScrolls.Clear(); }
        }

        /// <summary>Fin du dessin : publie les rectangles du Repaint ; après une activation, saute le Repaint de l'image.</summary>
        public void EndWindow()
        {
            var t = Event.current.type;
            if (t == EventType.Repaint)
            {
                _items.Clear(); _items.AddRange(_pass);
                _scrolls.Clear(); _scrolls.AddRange(_passScrolls);
                if (_focus >= _items.Count) _focus = Mathf.Max(0, _items.Count - 1);
            }
            else if (t == EventType.Layout && _activate >= 0) { _activate = -1; SkipRepaint = true; }
        }

        /// <summary>À appeler dans OnGUI avant GUILayout.Window : vrai = ne pas dessiner la fenêtre à ce Repaint.</summary>
        public bool ConsumeSkipRepaint()
        {
            if (!SkipRepaint || Event.current.type != EventType.Repaint) return false;
            SkipRepaint = false;
            return true;
        }

        private bool Register(bool clicked, bool slider = false)
        {
            int idx = _index++;
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                var r = GUILayoutUtility.GetLastRect();
                if (idx == _focus && Active) GUI.Box(r, GUIContent.none, Theme.Focus);
                _pass.Add(new Item { Rect = r, Scroll = _currentScroll, Slider = slider });
            }
            else if (e.type == EventType.Layout && _activate == idx) return true;
            return clicked;
        }

        // ---- enveloppes des contrôles ----

        public bool Button(string text, params GUILayoutOption[] options) => Register(GUILayout.Button(text, options));
        public bool Button(string text, GUIStyle style, params GUILayoutOption[] options) => Register(GUILayout.Button(text, style, options));

        public bool Toggle(bool value, string text, params GUILayoutOption[] options)
        {
            bool v = GUILayout.Toggle(value, text, options);
            return Register(v != value) ? !value : value;
        }
        public bool Toggle(bool value, string text, GUIStyle style, params GUILayoutOption[] options)
        {
            bool v = GUILayout.Toggle(value, text, style, options);
            return Register(v != value) ? !value : value;
        }

        /// <summary>Curseur : gauche/droite sur la croix ajuste de 5 % de la plage quand il est focalisé.</summary>
        public float HorizontalSlider(float value, float min, float max, params GUILayoutOption[] options)
        {
            float v = GUILayout.HorizontalSlider(value, min, max, options);
            int idx = _index;
            Register(false, slider: true);
            if (Event.current.type == EventType.Layout && idx == _focus && _sliderDelta != 0f)
            {
                v = Mathf.Clamp(value + _sliderDelta * (max - min) / 20f, min, max);
                _sliderDelta = 0f;
            }
            return v;
        }

        public Vector2 BeginScrollView(Vector2 scroll, params GUILayoutOption[] options)
        {
            int g = _passScrollIndex++;
            if (_scrollDelta.TryGetValue(g, out var d) && Event.current.type == EventType.Layout)
            {
                scroll += d; scroll.y = Mathf.Max(0f, scroll.y); scroll.x = Mathf.Max(0f, scroll.x);
                _scrollDelta.Remove(g);
            }
            var s = GUILayout.BeginScrollView(scroll, options);
            _currentScroll = g;
            if (Event.current.type == EventType.Repaint) _passScrolls.Add(new ScrollGroup { Scroll = s, FirstItem = _pass.Count });
            return s;
        }

        public void EndScrollView()
        {
            GUILayout.EndScrollView();
            int g = _currentScroll; _currentScroll = -1;
            if (Event.current.type != EventType.Repaint || g < 0 || g >= _passScrolls.Count) return;
            var viewport = GUILayoutUtility.GetLastRect();
            var grp = _passScrolls[g]; grp.Viewport = viewport; _passScrolls[g] = grp;
            // Les rectangles enregistrés dans le groupe sont relatifs au contenu : passage en coordonnées fenêtre.
            var offset = viewport.position - grp.Scroll;
            for (int i = grp.FirstItem; i < _pass.Count; i++)
            {
                var it = _pass[i]; it.Rect.position += offset; _pass[i] = it;
            }
        }

        /// <summary>Ligne d'aide en bas de fenêtre (seulement quand la manette est active).</summary>
        public static void Hints(GUIStyle style, string keyboardHints = null)
        {
            if (Active) GUILayout.Label(L.T(HintsText), style);
            else if (keyboardHints != null) GUILayout.Label(keyboardHints, style);
        }
    }
}
