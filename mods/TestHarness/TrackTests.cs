using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Mode Traque du scanner : deux sangliers, recherche « Sanglier », traque activée ; le premier tué → la cible passe
    /// toute seule au second ; le second tué → la recherche est relancée depuis la position du joueur, rien ne reste →
    /// la traque s'arrête d'elle-même. Plus : pas de baisse de compétences à la mort (Skills.OnDeath court-circuité).
    /// </summary>
    internal static class TrackTests
    {
        private static Assembly Asm(string name) => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == name);

        /// <summary>Le jeu affiche-t-il ses propres aides de touches ? (nos aides s'effacent devant elles, c'est voulu)</summary>
        private static bool GameHintGroupActive()
        {
            var kh = KeyHints.instance;
            if (kh == null) return false;
            foreach (var name in new[] { "BuildHints", "CombatHints", "InventoryHints", "FishingHints", "RadialHints", "BarberHints" })
            {
                var g = kh.transform.Find(name);
                if (g != null && g.gameObject.activeSelf) return true;
            }
            return false;
        }

        public static IEnumerator Run(Plugin h, Player player)
        {
            var rfAsm = Asm("ResourceFinder");
            var rfT = rfAsm.GetType("ResourceFinder.Plugin");
            var rfInst = UnityEngine.Object.FindObjectOfType(rfT);
            var entryT = rfAsm.GetType("ResourceFinder.ResourceEntry");
            var entries = (IList)rfAsm.GetType("ResourceFinder.Catalog").GetField("Entries").GetValue(null);
            object boarEntry = null; foreach (var e in entries) if ((string)entryT.GetField("Label").GetValue(e) == "Sanglier") boarEntry = e;
            var targetF = rfT.GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance);
            var trackingF = rfT.GetField("Tracking", BindingFlags.NonPublic | BindingFlags.Static);
            var start = rfT.GetMethod("StartSearch", BindingFlags.NonPublic | BindingFlags.Instance);
            var toggle = rfT.GetMethod("ToggleTrack", BindingFlags.NonPublic | BindingFlags.Instance);

            var pos = player.transform.position;
            var a = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Boar"), pos + player.transform.forward * 6f, Quaternion.identity);
            var b = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Boar"), pos + player.transform.forward * 14f, Quaternion.identity);
            yield return null;
            var idA = a.GetComponent<ZNetView>().GetZDO().m_uid; var idB = b.GetComponent<ZNetView>().GetZDO().m_uid;

            trackingF.SetValue(null, false);
            start.Invoke(rfInst, new[] { boarEntry });
            yield return new WaitForSecondsRealtime(1.5f);
            toggle.Invoke(rfInst, null); // traque ON (cible déjà là : pas de relance)
            var t0 = targetF.GetValue(rfInst);
            ZDOID Id(object r) => r == null ? ZDOID.None : (ZDOID)r.GetType().GetField("Id").GetValue(r);
            bool first = Id(t0) == idA;

            // Un sanglier C arrive APRÈS la recherche, tout près du joueur (derrière lui) : il n'est pas dans la liste de départ
            var c = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Boar"), player.transform.position - player.transform.forward * 4f, Quaternion.identity);
            yield return null;
            var idC = c.GetComponent<ZNetView>().GetZDO().m_uid;

            ZNetScene.instance.Destroy(a); // « tué »
            yield return new WaitForSecondsRealtime(1.5f); // nouvelle recherche depuis ici
            var t1 = targetF.GetValue(rfInst);
            bool fresh = Id(t1) == idC; // le plus proche MAINTENANT, pas B (le suivant de la liste de départ)

            ZNetScene.instance.Destroy(c);
            yield return new WaitForSecondsRealtime(1.5f);
            var t1b = targetF.GetValue(rfInst);
            bool chained = Id(t1b) == idB;
            h.Check("Traque.la cible suivante est la plus proche de soi, cherchée à nouveau", fresh && chained,
                $"après A tué → {(fresh ? "C, apparu tout près après la recherche" : Id(t1) == idB ? "B, le suivant de la liste de départ (ancien comportement)" : "autre")}, après C tué → B : {chained}");

            ZNetScene.instance.Destroy(b);
            yield return new WaitForSecondsRealtime(2.5f); // relance de la recherche depuis ici, rien → arrêt
            var t2 = targetF.GetValue(rfInst);
            bool tracking2 = (bool)trackingF.GetValue(null);
            // Après B : soit plus rien à proximité → traque arrêtée, soit la relance a trouvé un sanglier sauvage du monde → traque continue sur lui
            bool stopped = t2 == null && !tracking2;
            bool relaunched = t2 != null && Id(t2) != idA && Id(t2) != idB && Id(t2) != idC && tracking2;
            h.Check("Traque.enchaîne puis relance ou s'arrête", first && chained && (stopped || relaunched), $"1re cible = A : {first}, puis C puis B : {chained}, après B tué → {(stopped ? "plus rien, traque arrêtée" : relaunched ? "relance : nouvelle cible sauvage" : $"incohérent (cible={t2 != null}, traque={tracking2})")}");
            trackingF.SetValue(null, false);
            rfT.GetMethod("ClearAllPins", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rfInst, null);

            // Aides de touches des mods dans le panneau du jeu : visibles quand le jeu n'affiche pas les siennes (mains vides)
            try { player.UnequipAllItems(); } catch { }
            // Le panneau du jeu peut encore montrer ses propres aides (combat, construction) juste après : on laisse
            // le temps qu'elles s'effacent, sinon on mesure l'état transitoire et non le comportement voulu.
            for (int wait = 0; wait < 20 && GameHintGroupActive(); wait++) yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForSecondsRealtime(1.2f);
            var modsHints = KeyHints.instance != null ? KeyHints.instance.transform.Find("ModsHints") : null;
            var texts = new List<string>();
            if (modsHints != null) foreach (var t in modsHints.GetComponentsInChildren<TMPro.TMP_Text>(false)) texts.Add(t.text);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "keyhints.png"));
            yield return new WaitForSecondsRealtime(1f);
            h.Check("Aides de touches des mods dans le panneau du jeu", modsHints != null && modsHints.gameObject.activeSelf && texts.Count >= 2, modsHints == null ? "groupe absent" : $"actif={modsHints.gameObject.activeSelf}, textes : {string.Join(" | ", texts)}");

            // Compétences conservées à la mort : Skills.OnDeath ne baisse plus rien
            try
            {
                var skills = player.GetSkills();
                var getSkill = typeof(Skills).GetMethod("GetSkill", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                var run = getSkill.Invoke(skills, new object[] { Skills.SkillType.Run });
                var levelF = run.GetType().GetField("m_level");
                float before = (float)levelF.GetValue(run); levelF.SetValue(run, 25f);
                skills.OnDeath();
                float after = (float)levelF.GetValue(run); levelF.SetValue(run, before);
                h.Check("Mort.compétences conservées", Mathf.Approximately(after, 25f), $"Course 25 → {after:0.##} après OnDeath");
            }
            catch (Exception ex) { h.Check("Mort.compétences conservées", false, ex.Message); }
        }
    }
}
