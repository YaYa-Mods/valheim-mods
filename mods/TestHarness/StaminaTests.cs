using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TestHarness
{
    /// <summary>
    /// Endurance gratuite hors combat (mod Movement) : hors combat UseStamina ne retire rien ; un coup reçu d'une
    /// créature ou un ennemi qui cible le joueur fait repasser en mode normal ; le réglage éteint rend le jeu vanilla.
    /// </summary>
    internal static class StaminaTests
    {
        public static IEnumerator Run(Plugin h, Player player)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Movement");
            if (asm == null) { h.Check("Endurance.mod chargé", false, "assembly Movement absent"); yield break; }
            var patches = asm.GetType("Movement.Patches");
            var pluginT = asm.GetType("Movement.Plugin");
            var freeCfg = (BepInEx.Configuration.ConfigEntry<bool>)pluginT.GetField("FreeStamina", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var setFight = patches.GetMethod("SetFight", BindingFlags.NonPublic | BindingFlags.Static);
            bool prevFree = freeCfg.Value;
            freeCfg.Value = true;

            // Hors combat : rien n'est retiré
            setFight.Invoke(null, new object[] { false });
            player.AddStamina(1000f);
            yield return null;
            bool targeted = player.IsTargeted();
            float before = player.GetStamina();
            player.UseStamina(20f);
            float after = player.GetStamina();
            h.Check("Endurance.hors combat : rien n'est consommé", !targeted && Mathf.Abs(after - before) < 0.01f, $"ciblé={targeted}, {before:0.#} → {after:0.#}");

            // Coup reçu d'une créature il y a un instant : l'endurance s'use
            setFight.Invoke(null, new object[] { true });
            before = player.GetStamina();
            player.UseStamina(20f);
            after = player.GetStamina();
            h.Check("Endurance.en combat : consommée normalement", before - after > 19f, $"{before:0.#} → {after:0.#}");

            // Coup donné à une créature réelle : un sanglier frappé par le joueur met en combat
            setFight.Invoke(null, new object[] { false });
            var boarPrefab = ZNetScene.instance.GetPrefab("Boar");
            var boar = UnityEngine.Object.Instantiate(boarPrefab, player.transform.position + player.transform.forward * 4f, Quaternion.identity);
            yield return new WaitForSeconds(0.5f);
            var hit = new HitData { m_point = boar.transform.position };
            hit.m_damage.m_blunt = 1f;
            hit.SetAttacker(player);
            boar.GetComponent<Character>().Damage(hit);
            yield return new WaitForSeconds(0.3f);
            player.AddStamina(1000f);
            before = player.GetStamina();
            player.UseStamina(20f);
            after = player.GetStamina();
            h.Check("Endurance.frapper une créature met en combat", before - after > 19f, $"{before:0.#} → {after:0.#}");
            ZNetScene.instance.Destroy(boar);

            // Réglage éteint : comportement du jeu
            setFight.Invoke(null, new object[] { false });
            freeCfg.Value = false;
            player.AddStamina(1000f);
            before = player.GetStamina();
            player.UseStamina(20f);
            after = player.GetStamina();
            h.Check("Endurance.réglage éteint : vanilla", before - after > 19f, $"{before:0.#} → {after:0.#}");

            freeCfg.Value = prevFree;
            setFight.Invoke(null, new object[] { false });
            player.AddStamina(1000f);
        }
    }
}
