using System;
using UnityEngine;

namespace AKI.Rhythm
{
    [CreateAssetMenu(menuName = "AKI/Rhythm/Track", fileName = "RhythmTrack")]
    public sealed class RhythmTrack : ScriptableObject
    {
        public AudioClip clip;
        [Tooltip("Reference tempo only. These performances vary in tempo; recorded beat times are authoritative.")]
        [Min(1f)] public float referenceBpm = 140f;
        [Min(0f)] public float firstBeatOffsetSeconds;
        [TextArea(3, 8)] public string analysisNotes;
        [Tooltip("Offline measured pulse times (seconds from the decoded AudioClip start), sorted ascending.")]
        public float[] beatTimes = Array.Empty<float>();
        [Tooltip("Strong transient times, independent of the main pulse grid.")]
        public float[] accentTimes = Array.Empty<float>();
        public float[] accentStrengths = Array.Empty<float>();

        public int BeatCount => beatTimes != null ? beatTimes.Length : 0;
        public double Duration => clip != null ? (double)clip.samples / clip.frequency : 0d;

        public int BeatAtOrBefore(double seconds)
        {
            int lo = 0, hi = BeatCount;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (beatTimes[mid] <= seconds) lo = mid + 1;
                else hi = mid;
            }
            return lo - 1;
        }

        public int NearestBeat(double seconds)
        {
            if (BeatCount == 0) return -1;
            int previous = BeatAtOrBefore(seconds);
            if (previous < 0) return 0;
            if (previous + 1 >= BeatCount) return previous;
            return seconds - beatTimes[previous] <= beatTimes[previous + 1] - seconds ? previous : previous + 1;
        }

        public bool IsValid(out string error)
        {
            if (clip == null || BeatCount < 2) { error = "Clip and at least two recorded beats are required."; return false; }
            for (int i = 0; i < BeatCount; i++)
                if (float.IsNaN(beatTimes[i]) || beatTimes[i] < 0f || beatTimes[i] >= Duration || (i > 0 && beatTimes[i] <= beatTimes[i - 1]))
                { error = "Beat times must be finite, ascending, and inside the clip."; return false; }
            if (accentTimes == null || accentStrengths == null || accentTimes.Length != accentStrengths.Length)
            { error = "Accent time and strength arrays must match."; return false; }
            for (int i = 0; i < accentTimes.Length; i++)
                if (float.IsNaN(accentTimes[i]) || float.IsNaN(accentStrengths[i]) || float.IsInfinity(accentStrengths[i]) || accentTimes[i] < 0f || accentTimes[i] >= Duration || (i > 0 && accentTimes[i] <= accentTimes[i - 1]))
                { error = "Accent times must be finite, ascending, and inside the clip."; return false; }
            error = null; return true;
        }
    }
}
