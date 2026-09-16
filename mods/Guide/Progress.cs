using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Guide
{
    /// <summary>
    /// État du guide pour le personnage : étapes accomplies (collantes : une fois cochée, une étape reste cochée même
    /// si l'objet est consommé ensuite), étapes ignorées par le joueur, chapitre suivi à la main. Stocké dans
    /// Player.m_customData["vmods.guide"], des données que le jeu sauvegarde avec le personnage sans les interpréter.
    /// L'évaluation relit le jeu toutes les secondes ; elle ne coûte que des recherches dans des dictionnaires.
    /// </summary>
    internal static class Progress
    {
        private const string Key = "vmods.guide";

        public static readonly HashSet<string> Done = new HashSet<string>();
        public static readonly HashSet<string> Skipped = new HashSet<string>();
        public static readonly HashSet<string> Revealed = new HashSet<string>();   // chapitres futurs révélés volontairement
        public static string PinnedChapter;                 // null = suivi automatique
        public static readonly Dictionary<string, Status> Last = new Dictionary<string, Status>();
        public static event Action<Chapter, Step> StepCompleted;

        private static readonly Checks s_checks = new Checks();
        private static Player s_loadedFor;
        private static float s_nextEval;
        private static bool s_firstPass;   // premier passage après chargement : on coche sans notifier (déjà accompli avant)

        /// <summary>« chapitre.étape » : figé sur l'étape (une étape n'appartient qu'à un chapitre), Get() est appelé pour chaque étape à chaque passage OnGUI.</summary>
        public static string StepKey(Chapter c, Step s) => s.CachedKey ?? (s.CachedKey = c.Id + "." + s.Id);

        // ------------------------------------------------------------------ persistance

        public static void Load(Player player)
        {
            Done.Clear(); Skipped.Clear(); Revealed.Clear(); PinnedChapter = null; Last.Clear();
            s_loadedFor = player;
            s_firstPass = true;
            if (player == null || !player.m_customData.TryGetValue(Key, out var data)) return;
            foreach (var part in data.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                if (k == "done") foreach (var id in v.Split(',')) if (id.Length > 0) Done.Add(id);
                if (k == "skip") foreach (var id in v.Split(',')) if (id.Length > 0) Skipped.Add(id);
                if (k == "chapter" && v.Length > 0) PinnedChapter = v;
                if (k == "reveal") foreach (var id in v.Split(',')) if (id.Length > 0) Revealed.Add(id);
            }
        }

        public static void Save()
        {
            var player = s_loadedFor;
            if (player == null) return;
            var sb = new StringBuilder();
            sb.Append("done=").Append(string.Join(",", Done));
            sb.Append("|skip=").Append(string.Join(",", Skipped));
            sb.Append("|chapter=").Append(PinnedChapter ?? "");
            sb.Append("|reveal=").Append(string.Join(",", Revealed));
            player.m_customData[Key] = sb.ToString();
        }

        // ------------------------------------------------------------------ évaluation

        public static void Tick(Player player, bool force = false)
        {
            if (player == null) return;
            if (s_loadedFor != player) Load(player);
            if (!force && Time.unscaledTime < s_nextEval) return;
            s_nextEval = Time.unscaledTime + 1f;
            s_checks.Player = player;
            bool changed = false;
            foreach (var chapter in Chapters.All)
                foreach (var step in chapter.Steps)
                {
                    string key = StepKey(chapter, step);
                    if (Done.Contains(key) && !step.Volatile) { if (!Last.ContainsKey(key)) Last[key] = Status.Bool(true); continue; } // collante : plus rien à vérifier
                    Status st;
                    try { st = step.Check(s_checks); }
                    catch (Exception ex) { st = Status.Bool(false); Plugin.Log.LogWarning($"Étape {key} : {ex.Message}"); }
                    if (step.Volatile) { Last[key] = st; continue; } // état du moment, ni mémorisé ni notifié
                    if (Done.Contains(key)) st.Done = true;
                    else if (st.Done) { Done.Add(key); changed = true; if (!s_firstPass) StepCompleted?.Invoke(chapter, step); }
                    Last[key] = st;
                }
            s_firstPass = false;
            if (changed) Save();
        }

        public static Status Get(Chapter c, Step s) => Last.TryGetValue(StepKey(c, s), out var st) ? st : Status.Bool(Done.Contains(StepKey(c, s)));
        public static bool IsSkipped(Chapter c, Step s) => Skipped.Contains(StepKey(c, s));
        public static void SetSkipped(Chapter c, Step s, bool skipped)
        {
            if (skipped) Skipped.Add(StepKey(c, s)); else Skipped.Remove(StepKey(c, s));
            Save();
        }
        public static void Pin(string chapterId) { PinnedChapter = chapterId; Save(); }
        public static void Reveal(Chapter c) { Revealed.Add(c.Id); Save(); }

        /// <summary>Chapitre visible sans spoiler : atteint (boss précédent vaincu), ou déjà terminé, ou révélé, ou option coupée.</summary>
        public static bool IsUnlocked(Chapter c)
        {
            if (!Plugin.HideFuture.Value || Revealed.Contains(c.Id) || ChapterDone(c)) return true;
            int i = Chapters.All.IndexOf(c);
            return i <= 0 || ChapterDone(Chapters.All[i - 1]);
        }

        public static bool ChapterDone(Chapter c) => s_checks.Player != null && s_checks.BossDefeated(c.Boss);

        /// <summary>Chapitre suivi : celui choisi par le joueur, sinon le premier dont le boss n'est pas vaincu.</summary>
        public static Chapter Current()
        {
            if (PinnedChapter != null)
                foreach (var c in Chapters.All) if (c.Id == PinnedChapter) return c;
            foreach (var c in Chapters.All) if (!ChapterDone(c)) return c;
            return Chapters.All[Chapters.All.Count - 1];
        }

        /// <summary>Prochaines étapes à faire (non cochées, non ignorées), obligatoires d'abord.</summary>
        private static readonly Need[] s_needOrder = { Need.Required, Need.Advised, Need.Optional };
        /// <param name="essentials">Vrai (suivi HUD) : les optionnelles n'apparaissent que s'il ne reste rien d'autre, un journal de quête montre l'essentiel.</param>
        public static List<Step> Next(Chapter c, int max, bool essentials = false)
        {
            var list = new List<Step>();
            foreach (Need need in s_needOrder)
            {
                if (essentials && need == Need.Optional && list.Count > 0) break;
                foreach (var s in c.Steps)
                {
                    if (s.Need != need || Get(c, s).Done || IsSkipped(c, s)) continue;
                    list.Add(s);
                    if (list.Count >= max) return list;
                }
            }
            return list;
        }
    }
}
