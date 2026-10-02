using System;
using UnityEngine;

namespace AKI.Rhythm
{
    [RequireComponent(typeof(AudioSource))]
    [DefaultExecutionOrder(-150)]
    public sealed class RhythmConductor : MonoBehaviour
    {
        public static RhythmConductor Active { get; private set; }
        public AudioSource musicSource;
        [Min(0.05f)] public float scheduleLeadSeconds = 0.15f;
        [Min(0.01f)] public float beatPulseSeconds = 0.12f;
        [Min(0.01f)] public float accentPulseSeconds = 0.1f;
        [Tooltip("Positive = allow clicks this many seconds later to compensate audio/output latency.")]
        [Range(-0.2f, 0.2f)] public float inputLatencySeconds;
        public event Action Completed;
        public RhythmTrack Track { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool Looping { get; private set; }
        public int Session { get; private set; }
        public float BeatPulse { get; private set; }
        public float AccentPulse { get; private set; }
        public int CurrentBeat { get; private set; } = -1;
        public double StartedDspTime { get; private set; }
        public double SongSeconds => IsPlaying ? Math.Max(0d, AudioSettings.dspTime - StartedDspTime) : 0d;
        public double ClipSeconds => Track != null && Track.Duration > 0d && Looping ? SongSeconds % Track.Duration : SongSeconds;
        public long LoopIndex => Track != null && Track.Duration > 0d && Looping ? (long)(SongSeconds / Track.Duration) : 0;
        int nextAccent;
        long previousLoop;
        double lastAccent = -999d;
        float lastAccentStrength;

        void Awake()
        {
            if (musicSource == null) musicSource = GetComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            musicSource.pitch = 1f; // random pitch would invalidate the measured beat map
        }
        void OnEnable() { Active = this; }
        void OnDisable() { StopMusic(); if (Active == this) Active = null; }

        public bool PlayTrack(RhythmTrack track, bool loop, float volume = 1f, bool bypassUnderwater = true)
        {
            string error = null;
            if (track == null || !track.IsValid(out error))
            {
                Debug.LogError("RhythmConductor: invalid beat map " + (track == null ? "(null)" : error), this);
                return false;
            }
            StopMusic(); Track = track; Looping = loop; Session++;
            musicSource.clip = track.clip;
            musicSource.loop = loop;
            musicSource.pitch = 1f;
            musicSource.volume = Mathf.Clamp01(volume);
            musicSource.bypassListenerEffects = bypassUnderwater;
            StartedDspTime = AudioSettings.dspTime + Mathf.Max(0.05f, scheduleLeadSeconds);
            musicSource.PlayScheduled(StartedDspTime);
            nextAccent = 0; previousLoop = 0; lastAccent = -999d;
            CurrentBeat = -1; IsPlaying = true;
            return true;
        }
        public void StopMusic()
        {
            if (musicSource != null) musicSource.Stop();
            IsPlaying = false; BeatPulse = 0f; AccentPulse = 0f; CurrentBeat = -1; Session++;
        }
        public bool EvaluateAttempt(double dspTime, float early, float late, out long beatId, out float errorSeconds)
        {
            beatId = -1; errorSeconds = float.PositiveInfinity;
            if (!IsPlaying || Track == null || dspTime < StartedDspTime) return false;
            double seconds = dspTime - StartedDspTime - inputLatencySeconds;
            if (seconds < 0 || (!Looping && seconds >= Track.Duration)) return false;
            long cycle = Looping ? (long)(seconds / Track.Duration) : 0;
            double local = seconds - cycle * Track.Duration;
            int index = Track.NearestBeat(local);
            if (index < 0) return false;
            beatId = cycle * Track.BeatCount + index;
            errorSeconds = (float)(local - Track.beatTimes[index]);
            if (Looping)
            {
                float nextError = (float)(seconds - ((cycle + 1) * Track.Duration + Track.beatTimes[0]));
                if (Mathf.Abs(nextError) < Mathf.Abs(errorSeconds)) { errorSeconds = nextError; beatId = (cycle + 1) * Track.BeatCount; }
                if (cycle > 0)
                {
                    float prevError = (float)(seconds - ((cycle - 1) * Track.Duration + Track.beatTimes[Track.BeatCount - 1]));
                    if (Mathf.Abs(prevError) < Mathf.Abs(errorSeconds)) { errorSeconds = prevError; beatId = cycle * Track.BeatCount - 1; }
                }
            }
            return errorSeconds >= -early && errorSeconds <= late;
        }
        void Update()
        {
            if (!IsPlaying || Track == null || AudioSettings.dspTime < StartedDspTime) return;
            double seconds = SongSeconds;
            if (!Looping && seconds >= Track.Duration)
            {
                StopMusic(); Completed?.Invoke(); return;
            }
            long cycle = LoopIndex;
            if (cycle != previousLoop) { previousLoop = cycle; nextAccent = 0; lastAccent = -999d; }
            double local = ClipSeconds;
            CurrentBeat = Track.BeatAtOrBefore(local);
            BeatPulse = CurrentBeat >= 0 ? Mathf.Exp(-(float)(local - Track.beatTimes[CurrentBeat]) / Mathf.Max(.01f, beatPulseSeconds)) : 0f;
            while (nextAccent < Track.accentTimes.Length && Track.accentTimes[nextAccent] <= local)
            {
                lastAccent = Track.accentTimes[nextAccent];
                lastAccentStrength = Mathf.Clamp01(Track.accentStrengths[nextAccent]); nextAccent++;
            }
            AccentPulse = lastAccent < 0d ? 0f : lastAccentStrength * Mathf.Exp(-(float)(local - lastAccent) / Mathf.Max(.01f, accentPulseSeconds));
        }
    }
}
