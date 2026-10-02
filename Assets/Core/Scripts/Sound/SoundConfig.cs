using System;
using System.Collections.Generic;
using UnityEngine;

namespace Core.Scripts.Sound
{
    [CreateAssetMenu(fileName = "SoundConfig", menuName = "Custom/SoundConfig")]
    public class SoundConfig : ScriptableObject
    {
        [Header("Music and ambient")]
        [field: SerializeField] public AudioClipConfig MenuGamelan { get; private set; }
        [field: SerializeField] public AudioClipConfig FishingGamelan { get; private set; }
        [field: SerializeField] public AudioClipConfig NagaGamelan { get; private set; }
        [field: SerializeField] public AudioClipConfig WaterAmbient { get; private set; }
        [Header("loop sounds")]
        [field: SerializeField] public AudioClipConfig WavesSound { get; private set; }
        [field: SerializeField] public AudioClipConfig SeagoolSound { get; private set; }
        [field: SerializeField] public AudioClipConfig ArrowMoveSound { get; private set; }
        [field: SerializeField] public AudioClipConfig FishSwimmingSound { get; private set; }
        [field: SerializeField] public AudioClipConfig NagaSwimmingSound { get; private set; }
        [Header("Player sounds")]
        [field:SerializeField] public List<AudioClipConfig> SwimmingSound { get; private set; }
        [field: SerializeField] public List<AudioClipConfig> SplashSound { get; private set; }
        [field: SerializeField] public List<AudioClipConfig> BreatheOutSound { get; private set; }
        [Header("Harpoon sounds")]
        [field: SerializeField] public AudioClipConfig TriggerSound { get; private set; }
        [field: SerializeField] public AudioClipConfig HitSound { get; private set; }
        [field: SerializeField] public AudioClipConfig FishDeathSound { get; private set; }
        [field: SerializeField] public AudioClipConfig ShootSound { get; private set; }
        [Header("Naga settings")]
        [field: SerializeField] public float NagaDistanceSeconds { get; private set; } = 3;
        [field: SerializeField] public List<AudioClipConfig> NagaSounds { get; private set; }
    }

    [Serializable]
    public class AudioClipConfig
    {
        public AudioClip Clip;
        public bool Is3D;
        [Tooltip("Not muffled under water (e.g. music).")]
        public bool IgnoreUnderwater;
        public float Volume = 1f;
        [Range(-3f, 3f)] public float Pitch = 1f;
        [Space(20)]
        [Range(0, 1)] public float RandomAdd = 0.1f;

        public float GetPitch()
        {
            return Pitch + UnityEngine.Random.Range(-RandomAdd, RandomAdd);
        }

        public float GetVolume()
        {
            return Volume + UnityEngine.Random.Range(-RandomAdd, RandomAdd);
        }
    }
}