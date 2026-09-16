using System.Text;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Samples
{
    /// <summary>
    /// An IMGUI readout, so the sample needs no UI assets: health and poise for
    /// both fighters, the player's tags and cooldowns, and the controls. Every
    /// number here is read through a public accessor the package already had -
    /// a HUD is a reader, never a second source of truth.
    /// </summary>
    public sealed class SampleHud : MonoBehaviour
    {
        [SerializeField] private SampleFighter player;
        [SerializeField] private SampleFighter enemy;

        private readonly StringBuilder line = new();
        private GUIStyle label;

        private void OnGUI()
        {
            label ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };

            GUILayout.BeginArea(new Rect(12f, 12f, 460f, 320f));
            Describe("Player", player);
            Describe("Enemy", enemy);
            GUILayout.Space(8f);
            GUILayout.Label(
                "Move: WASD    Attack: J (tap again inside the combo window)\n" +
                "Roll: K (invulnerable while it travels)    Block: L (hold; early frames parry)",
                label);
            GUILayout.EndArea();
        }

        private void Describe(string title, SampleFighter fighter)
        {
            if (fighter == null)
            {
                return;
            }

            AbilitySystem system = fighter.System;
            AttributeSet attributes = fighter.Attributes;

            line.Clear();
            line.Append("<b>").Append(title).Append("</b>  health ");
            line.Append(Mathf.RoundToInt(attributes.GetCurrent(SampleFighter.Health)));
            line.Append("   poise ");
            line.Append(Mathf.RoundToInt(attributes.GetCurrent(SampleFighter.Poise)));

            if (system.IsActive)
            {
                line.Append("   running ").Append(system.ActiveAbility.name);
                line.Append(" step ").Append(system.ActiveStepIndex + 1);
                line.Append(' ').Append(system.ActivePhase);
                line.Append(" frame ").Append(system.ActiveFrame);
            }

            GUILayout.Label(line.ToString(), label);

            line.Clear();
            line.Append("    tags:");
            foreach (GameplayTag tag in system.ActiveTags)
            {
                line.Append(' ').Append(tag.Value);
            }

            GUILayout.Label(line.ToString(), label);
        }
    }
}
