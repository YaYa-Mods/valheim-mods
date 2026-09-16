using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ModsCommon;

namespace Lumberjack
{
    /// <summary>
    /// Bûcheronnage progressif. Config : BepInEx/config/vmods.lumberjack.cfg.
    ///
    ///  - Compétence : chaque coup de hache enlève AU MOINS (niveau Bûcheron %) des PV max de la cible
    ///    (arbre debout, tronc au sol, souche/buisson). Au niveau 50 → 2 coups max, au niveau 100 → 1 coup.
    ///    Les dégâts vanilla de la hache restent le plancher, et le jeu continue d'exiger le tier d'outil.
    ///
    ///  - Auto-chop : quand le joueur local abat un arbre, le tronc qui tombe (puis ses sous-troncs, puis la
    ///    souche) se découpent tout seuls une fois posés au sol. Tout passe par les RPC normaux du jeu
    ///    (mêmes drops, mêmes effets). Les troncs déjà présents dans le monde ne sont pas concernés.
    /// </summary>
    [BepInPlugin(Guid, "Lumberjack", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "vmods.lumberjack";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> HpPercentAtMaxLevel;
        internal static ConfigEntry<bool> AutoChopLogs;
        internal static ConfigEntry<float> AutoChopDelay;
        internal static ConfigEntry<bool> AutoChopChainReaction;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, L.T("Active le mod (compétence en % des PV et auto-découpe)."));

            HpPercentAtMaxLevel = Config.Bind("Skill", "HpPercentAtMaxLevel", 100f,
                new ConfigDescription(
                    L.T("Pourcentage des PV max d'un arbre enlevé au minimum par coup au niveau 100 de Bûcheron. ") +
                    L.T("La garantie est proportionnelle au niveau : à 100, le niveau 50 enlève 50% (2 coups), le niveau 100 enlève 100% (1 coup). ") +
                    L.T("0 = désactivé."),
                    new AcceptableValueRange<float>(0f, 100f)));
            AutoChopLogs = Config.Bind("AutoChop", "AutoChopLogs", true,
                L.T("Les troncs, sous-troncs et souches des arbres que vous abattez se découpent tout seuls."));
            AutoChopChainReaction = Config.Bind("AutoChop", "AutoChopChainReaction", true,
                L.T("Les arbres renversés par la chute d'un arbre que vous avez abattu (réaction en chaîne) se découpent aussi."));
            AutoChopDelay = Config.Bind("AutoChop", "AutoChopDelay", 1.5f,
                new ConfigDescription(
                    L.T("Secondes d'attente avant qu'un tronc tombé se découpe (laisse le temps à l'animation de chute)."),
                    new AcceptableValueRange<float>(0.3f, 10f)));

            Harmony.CreateAndPatchAll(typeof(SkillDamage), Guid);
            Harmony.CreateAndPatchAll(typeof(AutoChop), Guid);
            Log.LogInfo($"Lumberjack chargé (HpPercentAtMaxLevel={HpPercentAtMaxLevel.Value}, AutoChop={AutoChopLogs.Value}, delay={AutoChopDelay.Value}s)");
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Compétence Bûcheron → plancher de dégâts en % des PV max
    // ------------------------------------------------------------------------------------------------
    internal static class SkillDamage
    {
        internal static bool IsLocalPlayerHit(HitData hit)
        {
            return Player.m_localPlayer != null && hit.GetAttacker() == Player.m_localPlayer;
        }

        // Même formule que TreeBase/TreeLog.Awake : les PV grimpent avec le niveau de monde (New Game+).
        private static float MaxHealth(float baseHealth)
        {
            return baseHealth + Game.m_worldLevel * baseHealth * Game.instance.m_worldLevelMineHPMultiplier;
        }

        /// <summary>
        /// Relève les dégâts "chop" du coup pour que, une fois les résistances de la cible appliquées,
        /// les dégâts effectifs atteignent au moins (niveau/100 × HpPercentAtMaxLevel/100) × PV max.
        /// </summary>
        private static void EnsureMinimumDamage(HitData hit, float maxHealth, HitData.DamageModifiers modifiers)
        {
            float percent = Plugin.HpPercentAtMaxLevel.Value;
            if (!Plugin.Enabled.Value || percent <= 0f || hit.m_damage.m_chop <= 0f || !IsLocalPlayerHit(hit)) return;

            float level = Player.m_localPlayer.GetSkills().GetSkillLevel(Skills.SkillType.WoodCutting);
            float floor = maxHealth * (level / 100f) * (percent / 100f);
            if (floor <= 0f) return;

            // Dégâts effectifs de ce coup tel quel, et part due au chop seul (les résistances sont linéaires par type).
            var probe = hit.Clone();
            probe.ApplyResistance(modifiers, out _);
            float effective = probe.GetTotalDamage();
            if (effective >= floor) return;

            var chopOnly = hit.Clone();
            chopOnly.m_damage = new HitData.DamageTypes { m_chop = hit.m_damage.m_chop };
            chopOnly.ApplyResistance(modifiers, out _);
            float chopEffective = chopOnly.GetTotalDamage();
            if (chopEffective <= 0f) return; // cible immunisée au chop : on ne force rien

            float otherEffective = effective - chopEffective;
            float neededChopEffective = (floor - otherEffective) * 1.001f; // petite marge flottante
            hit.m_damage.m_chop *= neededChopEffective / chopEffective;
        }

        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        [HarmonyPrefix]
        private static void TreeBase_RPC_Damage(TreeBase __instance, HitData hit)
        {
            EnsureMinimumDamage(hit, MaxHealth(__instance.m_health), __instance.m_damageModifiers);
        }

        [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
        [HarmonyPrefix]
        private static void TreeLog_RPC_Damage(TreeLog __instance, HitData hit)
        {
            EnsureMinimumDamage(hit, MaxHealth(__instance.m_health), __instance.m_damages);
        }

        [HarmonyPatch(typeof(Destructible), "RPC_Damage")]
        [HarmonyPrefix]
        private static void Destructible_RPC_Damage(Destructible __instance, HitData hit)
        {
            if (__instance.m_destructibleType != DestructibleType.Tree) return;
            EnsureMinimumDamage(hit, MaxHealth(__instance.m_health), __instance.m_damages);
        }
    }

    // ------------------------------------------------------------------------------------------------
    // Auto-chop des troncs / sous-troncs / souches issus d'un arbre abattu par le joueur local
    // ------------------------------------------------------------------------------------------------
    internal static class AutoChop
    {
        // Hash pré-calculé : évite de hacher la chaîne à chaque Awake d'arbre ou de souche chargé.
        private static readonly int ZdoKey = "Lumberjack.AutoChop".GetStableHashCode();

        // Vrai uniquement pendant que le jeu instancie des objets issus d'un abattage du joueur local :
        // SpawnLog (tronc + souche) d'un arbre qu'il a frappé, ou Destroy d'un tronc déjà marqué.
        private static bool s_marking;
        private static bool s_markingSubLogs; // vrai pendant TreeLog.Destroy : les sous-troncs se découpent sans délai « visuel »
        private static bool s_treeHitByLocal;

        // Réaction en chaîne : ImpactEffect (sur le tronc qui tombe) appelle Damage() de manière synchrone sur ce
        // qu'il percute ; pendant cet appel, si le tronc est marqué, tout arbre qu'il renverse l'est aussi.
        private static bool s_impactFromMarkedLog;

        private static bool Enabled => Plugin.Enabled.Value && Plugin.AutoChopLogs.Value;

        private static void Mark(GameObject go)
        {
            if (!Enabled) return;
            var nview = go.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;

            var zdo = nview.GetZDO();
            if (s_marking && nview.IsOwner())
                zdo.Set(ZdoKey, true);
            else if (!zdo.GetBool(ZdoKey, false))
                return;

            if (go.GetComponent<AutoChopBehaviour>() == null)
            {
                var b = go.AddComponent<AutoChopBehaviour>();
                b.Quick = s_markingSubLogs || go.GetComponent<TreeLog>() == null; // sous-troncs et souche : rapide
                Plugin.Log.LogInfo($"AutoChop : {go.name} marqué{(b.Quick ? " (rapide)" : "")}");
            }
        }

        private static bool IsMarked(Component c)
        {
            var nview = c.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() && nview.GetZDO().GetBool(ZdoKey, false);
        }

        // --- TreeBase : on retient si le coup vient du joueur local (ou d'un tronc marqué), SpawnLog instancie tronc + souche ---

        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        [HarmonyPrefix]
        private static void TreeBase_RPC_Damage_Prefix(HitData hit)
        {
            bool local = SkillDamage.IsLocalPlayerHit(hit);
            bool chain = Plugin.AutoChopChainReaction.Value && s_impactFromMarkedLog;
            s_treeHitByLocal = local || chain;
        }

        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        [HarmonyPostfix]
        private static void TreeBase_RPC_Damage_Postfix() => s_treeHitByLocal = false;

        [HarmonyPatch(typeof(TreeBase), "SpawnLog")]
        [HarmonyPrefix]
        private static void TreeBase_SpawnLog_Prefix() { s_marking = Enabled && s_treeHitByLocal; s_markingSubLogs = false; }

        [HarmonyPatch(typeof(TreeBase), "SpawnLog")]
        [HarmonyPostfix]
        private static void TreeBase_SpawnLog_Postfix() => s_marking = false;

        // --- ImpactEffect : collision d'un tronc qui tombe (exécute Damage() de la cible dans le même appel) ---

        [HarmonyPatch(typeof(ImpactEffect), "OnCollisionEnter")]
        [HarmonyPrefix]
        private static void ImpactEffect_OnCollisionEnter_Prefix(ImpactEffect __instance)
        {
            var log = __instance.GetComponent<TreeLog>();
            s_impactFromMarkedLog = Enabled && log != null && IsMarked(log);
        }

        [HarmonyPatch(typeof(ImpactEffect), "OnCollisionEnter")]
        [HarmonyFinalizer]
        private static void ImpactEffect_OnCollisionEnter_Finalizer() => s_impactFromMarkedLog = false;

        // --- TreeLog : un tronc marqué (ou percuté par un tronc marqué) qui se détruit instancie des sous-troncs ---

        [HarmonyPatch(typeof(TreeLog), "Destroy")]
        [HarmonyPrefix]
        private static void TreeLog_Destroy_Prefix(TreeLog __instance)
        {
            s_marking = Enabled && (IsMarked(__instance) || s_impactFromMarkedLog);
            s_markingSubLogs = true;
        }

        [HarmonyPatch(typeof(TreeLog), "Destroy")]
        [HarmonyPostfix]
        private static void TreeLog_Destroy_Postfix() { s_marking = false; s_markingSubLogs = false; }

        // --- Awake des objets candidats : on marque (si en cours d'abattage) ou on reprend un marquage existant ---

        [HarmonyPatch(typeof(TreeLog), "Awake")]
        [HarmonyPostfix]
        private static void TreeLog_Awake(TreeLog __instance) => Mark(__instance.gameObject);

        [HarmonyPatch(typeof(Destructible), "Awake")]
        [HarmonyPostfix]
        private static void Destructible_Awake(Destructible __instance)
        {
            if (__instance.m_destructibleType == DestructibleType.Tree) Mark(__instance.gameObject);
        }
    }

    /// <summary>
    /// Posé sur un tronc/souche marqué : attend le délai, attend que la physique se calme, puis frappe
    /// l'objet via son RPC de dégâts normal avec un coup suffisant pour le détruire.
    /// </summary>
    internal class AutoChopBehaviour : MonoBehaviour
    {
        /// <summary>Sous-tronc ou souche : pas de chute à laisser jouer, on découpe presque tout de suite.</summary>
        public bool Quick;

        private IEnumerator Start()
        {
            // TreeLog.Damage ignore tout pendant 0,2 s après l'apparition (m_firstFrame) : minimum 0,3 s.
            yield return new WaitForSeconds(Quick ? 0.35f : Plugin.AutoChopDelay.Value);

            // Laisser le tronc principal se poser (jusqu'à 4 s) ; les sous-troncs apparaissent déjà au sol.
            var body = GetComponent<Rigidbody>();
            float deadline = Time.time + (Quick ? 1f : 4f);
            while (body != null && !body.IsSleeping() && body.linearVelocity.sqrMagnitude > 0.25f && Time.time < deadline)
                yield return null;

            var nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) yield break;

            var hit = new HitData();
            hit.m_damage.m_chop = 99999f;
            hit.m_toolTier = 99;
            hit.m_point = transform.position;
            hit.m_dir = Vector3.down;
            hit.m_hitType = HitData.HitType.PlayerHit;
            if (Player.m_localPlayer != null) hit.SetAttacker(Player.m_localPlayer);

            Plugin.Log.LogInfo($"AutoChop : découpe de {name}");
            var log = GetComponent<TreeLog>();
            if (log != null) { log.Damage(hit); yield break; }

            var destructible = GetComponent<Destructible>();
            if (destructible != null) destructible.Damage(hit);
        }
    }
}
