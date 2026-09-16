using System.Collections.Generic;
using UnityEngine;

namespace Fofuxo.GameplayAbilitySystem.Samples
{
    /// <summary>
    /// A presenter with no art: cues become coloured primitives, so the sample
    /// shows the cue lifecycle rather than a particle system. It is the shape a
    /// real presenter has - keyed by
    /// <see cref="GameplayCueParameters.Handle"/>, pooling by that handle, and
    /// deciding nothing about gameplay.
    ///
    /// Registering late is safe: the dispatcher replays every live persistent
    /// cue as <see cref="GameplayCueEvent.WhileActive"/> and never replays a
    /// burst that already happened.
    /// </summary>
    public sealed class SampleCuePresenter : MonoBehaviour, IGameplayCuePresenter
    {
        [Tooltip("Seconds a burst stays on screen.")]
        [SerializeField, Min(0.02f)] private float burstLifetime = 0.18f;

        [Tooltip("Size of a burst marker, in metres.")]
        [SerializeField, Min(0.01f)] private float burstSize = 0.35f;

        private readonly Dictionary<GameplayCueHandle, GameObject> persistent = new();
        private readonly List<GameObject> bursts = new();
        private readonly List<float> burstDeaths = new();
        private AbilitySystem system;

        private void Awake()
        {
            system = GetComponentInParent<AbilitySystem>();
        }

        private void OnEnable()
        {
            system?.Cues.AddPresenter(this);
        }

        private void OnDisable()
        {
            system?.Cues.RemovePresenter(this);
        }

        private void Update()
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                if (Time.time < burstDeaths[i])
                {
                    continue;
                }

                Destroy(bursts[i]);
                bursts.RemoveAt(i);
                burstDeaths.RemoveAt(i);
            }
        }

        /// <summary>
        /// The one method a presenter implements. The parameters are read-only
        /// and carry no runtime object: there is nothing here to change, which
        /// is how the package keeps cosmetics cosmetic.
        /// </summary>
        public void OnGameplayCue(in GameplayCueParameters parameters)
        {
            switch (parameters.Event)
            {
                case GameplayCueEvent.Execute:
                    SpawnBurst(parameters);
                    break;

                case GameplayCueEvent.Add:
                case GameplayCueEvent.WhileActive:
                    AddOrRefreshPersistent(parameters);
                    break;

                case GameplayCueEvent.Remove:
                    RemovePersistent(parameters.Handle);
                    break;
            }
        }

        private void SpawnBurst(in GameplayCueParameters parameters)
        {
            GameObject marker = NewMarker(
                parameters.Cue.Value,
                ColorFor(parameters.Outcome),
                parameters.Location != Vector3.zero
                    ? parameters.Location
                    : transform.position + Vector3.up,
                burstSize);
            bursts.Add(marker);
            burstDeaths.Add(Time.time + burstLifetime);
        }

        private void AddOrRefreshPersistent(in GameplayCueParameters parameters)
        {
            if (persistent.TryGetValue(parameters.Handle, out GameObject existing))
            {
                existing.transform.localScale =
                    Vector3.one * (burstSize * (1f + 0.25f * (parameters.StackCount - 1)));
                return;
            }

            GameObject marker = NewMarker(
                parameters.Cue.Value,
                ColorFor(parameters.Outcome),
                transform.position + Vector3.up * 2f,
                burstSize);
            marker.transform.SetParent(transform, worldPositionStays: true);
            persistent[parameters.Handle] = marker;
        }

        private void RemovePersistent(GameplayCueHandle handle)
        {
            if (persistent.TryGetValue(handle, out GameObject marker))
            {
                persistent.Remove(handle);
                Destroy(marker);
            }
        }

        private GameObject NewMarker(string label, Color color, Vector3 position, float size)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Cue {label}";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * size;
            marker.GetComponent<Renderer>().material.color = color;
            return marker;
        }

        private static Color ColorFor(GameplayCueOutcome outcome)
        {
            return outcome switch
            {
                GameplayCueOutcome.Missed => new Color(0.6f, 0.6f, 0.6f),
                GameplayCueOutcome.Blocked => new Color(0.3f, 0.6f, 1f),
                GameplayCueOutcome.Parried => new Color(1f, 0.9f, 0.2f),
                GameplayCueOutcome.Immune => new Color(0.5f, 0.3f, 0.8f),
                _ => new Color(1f, 0.35f, 0.25f)
            };
        }
    }
}
