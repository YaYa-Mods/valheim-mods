using System;
using System.Collections.Generic;
using ModsCommon;
using UnityEngine;
using UnityEngine.UI;

namespace ResourceFinder
{
    /// <summary>
    /// Fenêtre du scanner construite à partir du panneau « Compendium » du jeu (TextsDialog) : c'est le panneau du jeu
    /// qui est cloné, avec son cadre de bois, son titre, ses listes, ses barres de défilement et ses boutons. Rien n'est
    /// redessiné : mêmes sprites, mêmes polices, mêmes couleurs que les trophées, les succès ou les compétences.
    ///
    /// Disposition (reprise telle quelle du panneau du jeu) :
    ///   gauche  : la liste du catalogue (une ligne = une entrée, icône + nom, la ligne choisie est surlignée) ;
    ///   droite  : le détail de la recherche (titre, état, résultats cliquables) ;
    ///   bas     : les actions, avec le bouton « Fermer » du jeu.
    /// </summary>
    internal sealed class NativeWindow
    {
        private GameObject _root;              // clone du panneau « Texts »
        private TMPro.TMP_Text _topic;         // titre du panneau
        private RectTransform _listRoot;       // contenu de la liste de gauche
        private GameObject _rowPrefab;         // « SkillElement » du jeu : bouton + surlignage + nom
        private TMPro.TMP_Text _detailName, _detailText;
        private RectTransform _rightContent;
        private RectTransform _resultsRoot;   // nos lignes de résultats (hors du conteneur du jeu, qui impose sa mise en page)
        private GameObject _closeButton;       // bouton « Fermer » du jeu, réutilisé comme modèle
        private readonly List<GameObject> _rows = new List<GameObject>();
        private readonly List<GameObject> _resultRows = new List<GameObject>();
        private readonly List<GameObject> _actionButtons = new List<GameObject>();

        public bool Visible => _root != null && _root.activeSelf;
        public bool Built => _root != null;

        private static Transform Find(Transform root, string path)
        {
            var t = root.Find(path);
            if (t == null) Plugin.Log.LogWarning($"panneau natif : « {path} » introuvable (le jeu a peut-être changé sa fenêtre)");
            return t;
        }

        /// <summary>Clone le panneau du jeu une fois pour toutes. Renvoie faux si le jeu ne l'expose pas (version différente).</summary>
        public bool Ensure()
        {
            if (_root != null) return true;
            var gui = InventoryGui.instance;
            var model = gui != null ? gui.m_textsDialog : null;
            if (model == null) return false;

            _root = UnityEngine.Object.Instantiate(model.gameObject, model.transform.parent);
            _root.name = "ResourceFinderPanel";
            // Le panneau du jeu vit sous l'inventaire, qui est désactivé quand celui-ci est fermé : on remonte le clone
            // au canevas de l'interface pour qu'il puisse s'afficher seul (le cadre est ancré en plein écran, rien ne bouge).
            var canvas = model.GetComponentInParent<Canvas>();
            if (canvas != null) _root.transform.SetParent(canvas.transform, false);
            var logic = _root.GetComponent<TextsDialog>();
            if (logic != null) UnityEngine.Object.Destroy(logic); // le panneau ne doit obéir qu'à nous
            _root.SetActive(false);

            var frame = Find(_root.transform, "Texts_frame");
            if (frame == null) { Discard(); return false; }
            _topic = Find(frame, "topic")?.GetComponent<TMPro.TMP_Text>();
            _listRoot = Find(frame, "TextList/SkillList/ListRoot") as RectTransform;
            _rowPrefab = Find(frame, "TextList/SkillList/SkillElement")?.gameObject;
            _detailName = Find(frame, "TextArea/ScrollArea/Content/Name")?.GetComponent<TMPro.TMP_Text>();
            _detailText = Find(frame, "TextArea/ScrollArea/Content/Description")?.GetComponent<TMPro.TMP_Text>();
            _rightContent = Find(frame, "TextArea/ScrollArea/Content") as RectTransform;
            // Les résultats ont leur propre conteneur : le « Content » du jeu porte une mise en page automatique
            // qui écraserait nos lignes (hauteur nulle, tout empilé au même endroit).
            var textArea = Find(frame, "TextArea") as RectTransform;
            if (textArea != null)
            {
                var rootGo = new GameObject("ResultsRoot", typeof(RectTransform));
                _resultsRoot = (RectTransform)rootGo.transform;
                _resultsRoot.SetParent(textArea, false);
                _resultsRoot.anchorMin = new Vector2(0f, 0f); _resultsRoot.anchorMax = new Vector2(1f, 1f);
                _resultsRoot.offsetMin = new Vector2(12f, 12f); _resultsRoot.offsetMax = new Vector2(-12f, -12f);
            }
            BuildSearchField(frame);
            _closeButton = Find(frame, "Closebutton")?.gameObject;
            if (_listRoot == null || _rowPrefab == null || _detailText == null || _closeButton == null) { Discard(); return false; }

            // Les deux fermetures du jeu : le bouton et le clic hors du cadre
            foreach (var b in new[] { _closeButton.GetComponent<Button>(), _root.transform.Find("Closebutton")?.GetComponent<Button>() })
                if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(Close); }

            // Les lignes existantes appartiennent au compendium du jeu : on repart d'une liste vide
            for (int i = _listRoot.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_listRoot.GetChild(i).gameObject);
            if (_topic != null) _topic.text = L.T("Scanner de ressources");
            return true;
        }

        /// <summary>Champ de recherche : le champ de saisie du jeu (celui des panneaux et des enseignes), posé au-dessus de la liste.</summary>
        private void BuildSearchField(Transform frame)
        {
            var list = frame.Find("TextList") as RectTransform;
            var rowText = _rowPrefab != null ? _rowPrefab.transform.Find("name")?.GetComponent<TMPro.TMP_Text>() : null;
            if (list == null || rowText == null) { Plugin.Log.LogInfo("panneau natif : champ de recherche non construit, la liste seule fera l'affaire"); return; }

            // Construit avec les pièces du jeu (sprite de ses listes, sa police) plutôt que cloné d'un champ déjà rempli
            var go = new GameObject("Search", typeof(RectTransform), typeof(Image), typeof(TMPro.TMP_InputField));
            go.transform.SetParent(list.parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = list.anchorMin; rt.anchorMax = list.anchorMax; rt.pivot = list.pivot;
            rt.anchoredPosition = list.anchoredPosition + new Vector2(0f, 40f);
            rt.sizeDelta = new Vector2(list.rect.width, 34f);
            // La liste laisse la place au champ
            list.sizeDelta = new Vector2(list.sizeDelta.x, list.sizeDelta.y - 44f);
            list.anchoredPosition += new Vector2(0f, -22f);

            // Fond : le sprite que le jeu emploie pour ses listes
            var bg = go.GetComponent<Image>();
            var listBg = frame.Find("TextList/SkillList")?.GetComponent<Image>();
            if (listBg != null && listBg.sprite != null) { bg.sprite = listBg.sprite; bg.type = Image.Type.Sliced; }
            bg.color = new Color(0f, 0f, 0f, 0.62f);

            TMPro.TMP_Text MakeText(string name, Color color)
            {
                var t = new GameObject(name, typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
                t.transform.SetParent(go.transform, false);
                var trt = (RectTransform)t.transform;
                trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(10f, 2f); trt.offsetMax = new Vector2(-10f, -2f);
                var tmp = t.GetComponent<TMPro.TextMeshProUGUI>();
                tmp.font = rowText.font; tmp.fontSize = 20f; tmp.color = color;
                tmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                tmp.enableWordWrapping = false; tmp.richText = false;
                tmp.text = "";
                return tmp;
            }
            var placeholder = MakeText("Placeholder", new Color(1f, 1f, 1f, 0.45f));
            placeholder.text = L.T("Chercher");
            var typed = MakeText("Text", Color.white);

            _search = go.GetComponent<TMPro.TMP_InputField>();
            _search.textViewport = rt;
            _search.textComponent = typed;
            _search.placeholder = placeholder;
            _search.targetGraphic = bg;
            _search.lineType = TMPro.TMP_InputField.LineType.SingleLine;
            _search.characterLimit = 40;
            _search.text = "";
            _search.onValueChanged.AddListener(_ => Rebuild());
            _search.onSubmit.AddListener(s => Plugin.SearchTextFromPanel(s));
        }

        private TMPro.TMP_InputField _search;
        /// <summary>Texte tapé dans le champ de recherche (vide si le champ n'existe pas).</summary>
        public string SearchText => _search != null ? _search.text : "";

        private void Discard()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        public void Open()
        {
            if (!Ensure()) return;
            _root.SetActive(true);
            Rebuild();
        }

        public void Close()
        {
            if (_root != null) _root.SetActive(false);
            Plugin.NativeClosed();
        }

        // ------------------------------------------------------------------ contenu

        private GameObject MakeRow(RectTransform parent, string label, Sprite icon, bool selected, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(_rowPrefab, parent);
            go.name = "Row";
            go.SetActive(true);
            var sel = go.transform.Find("selected");
            if (sel != null) sel.gameObject.SetActive(selected);
            var text = go.transform.Find("name")?.GetComponent<TMPro.TMP_Text>();
            if (text != null)
            {
                text.text = label;
                text.enableWordWrapping = false;                       // une ligne = une entrée, comme dans le compendium
                text.overflowMode = TMPro.TextOverflowModes.Ellipsis;  // un nom trop long se termine par « … » au lieu de déborder
                text.enableAutoSizing = false; text.fontSize = 24f;    // sans ça, une ligne chargée rapetisse et la liste perd son rythme
                // place pour l'icône : le texte commence après elle (le jeu n'a pas d'icône sur ces lignes)
                var trt = text.rectTransform;
                trt.offsetMin = new Vector2(icon != null ? 34f : 4f, trt.offsetMin.y);
            }
            if (icon != null)
            {
                var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(go.transform, false);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = new Vector2(0f, 0.5f); irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.anchoredPosition = new Vector2(4f, 0f);
                irt.sizeDelta = new Vector2(26f, 26f);
                var img = iconGo.GetComponent<Image>();
                img.sprite = icon; img.preserveAspect = true; img.raycastTarget = false;
            }
            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                if (onClick != null) btn.onClick.AddListener(() => { try { onClick(); } catch (Exception ex) { Plugin.Log.LogWarning("panneau natif : " + ex.Message); } });
            }
            return go;
        }

        /// <summary>Bouton d'action : clone du bouton « Fermer » du jeu (même sprite, même police, même taille).</summary>
        private GameObject MakeButton(RectTransform parent, string label, Vector2 anchored, float width, Action onClick)
        {
            var go = UnityEngine.Object.Instantiate(_closeButton, parent);
            go.name = "Action";
            go.SetActive(true);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f); rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            var text = go.transform.Find("Text")?.GetComponent<TMPro.TMP_Text>();
            if (text != null) text.text = label;
            // L'aide manette « B » appartient au bouton « Fermer » : elle est posée par un composant du jeu, on l'enlève de nos copies
            foreach (var pad in go.GetComponentsInChildren<UIGamePad>(true)) UnityEngine.Object.Destroy(pad);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform && t.name.IndexOf("hint", StringComparison.OrdinalIgnoreCase) >= 0) t.gameObject.SetActive(false);
            var btn = go.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                if (onClick != null) btn.onClick.AddListener(() => { try { onClick(); } catch (Exception ex) { Plugin.Log.LogWarning("panneau natif : " + ex.Message); } });
            }
            return go;
        }

        private void ClearRows(List<GameObject> list)
        {
            foreach (var go in list) if (go != null) UnityEngine.Object.Destroy(go);
            list.Clear();
        }

        private const float RowHeight = 32f, RowSpacing = 36f; // pas du compendium (TextsDialog.m_spacing)

        /// <summary>Place une ligne dans une liste verticale, comme le jeu le fait dans son compendium.</summary>
        private static void PlaceRow(GameObject row, float y, float? width = null)
        {
            var rt = (RectTransform)row.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, -y);
            if (width.HasValue) rt.sizeDelta = new Vector2(width.Value, RowHeight);
        }

        /// <summary>Remplit la liste de gauche (catalogue) et le détail de droite.</summary>
        public void Rebuild()
        {
            if (!Visible) return;
            ClearRows(_rows);
            float y = 0f;
            Category? lastCategory = null;
            foreach (var e in Plugin.VisibleCatalogEntries())
            {
                var entry = e;
                // En-tête de catégorie : une ligne non cliquable, comme les intertitres du jeu
                if (lastCategory != entry.Category)
                {
                    lastCategory = entry.Category;
                    var header = MakeRow(_listRoot, "<color=#ffb75c>" + Catalog.CategoryLabel(entry.Category).ToUpperInvariant() + "</color>", null, false, null);
                    var hb = header.GetComponent<Button>(); if (hb != null) hb.interactable = false;
                    var hbg = header.transform.Find("bkg"); if (hbg != null) hbg.gameObject.SetActive(false);
                    var ht = header.transform.Find("name")?.GetComponent<TMPro.TMP_Text>();
                    if (ht != null) { ht.fontSize = 18f; ht.alignment = TMPro.TextAlignmentOptions.BottomLeft; }
                    PlaceRow(header, y);
                    _rows.Add(header);
                    y += RowSpacing * 0.8f;
                }
                var row = MakeRow(_listRoot, Plugin.RowLabel(entry), Icons.ForEntry(entry), Plugin.IsCurrentEntry(entry), () => Plugin.SearchFromPanel(entry));
                PlaceRow(row, y);
                _rows.Add(row);
                y += RowSpacing;
            }
            // Hauteur du contenu : c'est elle qui donne sa course à la barre de défilement du jeu
            _listRoot.sizeDelta = new Vector2(_listRoot.sizeDelta.x, Mathf.Max(y, 1f));
            _listRoot.anchoredPosition = new Vector2(_listRoot.anchoredPosition.x, 0f);
            // La liste repart du haut à chaque ouverture (sinon elle garde la position de la fois précédente)
            var scroll = _listRoot.GetComponentInParent<ScrollRect>();
            if (scroll != null) { scroll.content = _listRoot; scroll.verticalNormalizedPosition = 1f; }
            RefreshDetail();
        }

        /// <summary>Partie droite : ce que la recherche en cours raconte, et ses résultats.</summary>
        public void RefreshDetail()
        {
            if (!Visible) return;
            if (_detailName != null) _detailName.text = Plugin.DetailTitle();
            if (_detailText != null) _detailText.text = Plugin.DetailBody();

            ClearRows(_resultRows);
            ClearRows(_actionButtons);
            if (_resultsRoot == null) return;

            // Les résultats commencent sous le titre et le texte de description de la zone de droite
            Canvas.ForceUpdateCanvases(); // sans ça, les rectangles ne sont pas encore calculés et les lignes naissent de largeur nulle
            float width = _resultsRoot.rect.width > 50f ? _resultsRoot.rect.width : 800f;
            float y = 24f;
            if (_detailName != null) y += _detailName.rectTransform.rect.height;
            if (_detailText != null) y += Mathf.Max(_detailText.preferredHeight, 24f) + 16f;
            foreach (var r in Plugin.ShownResults())
            {
                var result = r;
                var row = MakeRow(_resultsRoot, Plugin.ResultLabel(result), Plugin.ResultIcon(result), Plugin.IsTarget(result), () => Plugin.TargetFromPanel(result));
                PlaceRow(row, y, width);
                _resultRows.Add(row);
                y += RowSpacing;
            }
            // Actions : une rangée au-dessus du bouton « Fermer » du jeu, alignée sur la colonne de droite (jamais sur la liste)
            var frame = (RectTransform)_closeButton.transform.parent;
            float bx = 344f, bw = 200f;
            foreach (var a in Plugin.PanelActions())
            {
                var action = a;
                _actionButtons.Add(MakeButton(frame, action.Key, new Vector2(bx, 86f), bw, () => { action.Value(); RefreshDetail(); }));
                bx += bw + 12f;
            }
        }
    }
}
