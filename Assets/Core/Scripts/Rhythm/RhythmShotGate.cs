using UnityEngine;

namespace AKI.Rhythm
{
    public sealed class RhythmShotGate : MonoBehaviour
    {
        public RhythmGameFlow game;
        public RhythmConductor conductor;
        [Range(.01f, .2f)] public float earlyWindowSeconds = .1f;
        [Range(.01f, .2f)] public float lateWindowSeconds = .1f;
        [Tooltip("Every repeated click extends this quiet-time lockout. Holding Attack never repeats a shot.")]
        [Range(.05f, .5f)] public float spamQuietSeconds = .16f;
        public long LastAcceptedBeat { get; private set; } = -1;
        public float LastTimingError { get; private set; }
        public string LastDecision { get; private set; }
        public int AcceptedSession { get; private set; }
        long consumedBeat = -1;
        int lastSession = -1;
        double lastAttempt = -999d;

        public bool TryAuthorize() => TryAuthorize(true);
        public bool TryAuthorize(bool loaded)
        {
            if (game == null || conductor == null) { LastDecision = "No rhythm setup: blocked"; return false; }
            if (!game.CanShoot) { LastDecision = "Game phase blocks shooting"; return false; }
            double now = AudioSettings.dspTime;
            if (lastSession != conductor.Session) { lastSession = conductor.Session; consumedBeat = -1; lastAttempt = -999d; }
            bool quiet = now - lastAttempt >= spamQuietSeconds;
            lastAttempt = now;
            if (!loaded) { LastDecision = "Reloading: attempt recorded"; return false; }
            if (!quiet) { LastDecision = "Click spam lockout"; return false; }
            AcceptedSession = conductor.Session;
            LastAcceptedBeat = -1;
            if (game.Phase == RhythmGameFlow.GamePhase.GoldenFish) { LastDecision = "Golden fish: free shot"; return true; }
            bool timed = conductor.EvaluateAttempt(now, earlyWindowSeconds, lateWindowSeconds, out long beat, out float error);
            LastTimingError = error;
            if (beat < 0) { LastDecision = "No active music beat"; return false; }
            if (beat == consumedBeat) { LastDecision = "This beat already attempted"; return false; }
            consumedBeat = beat; // off-beat attempts also consume the opportunity: clicks cannot sweep the window
            if (!timed) { LastDecision = "Off beat"; return false; }
            LastAcceptedBeat = beat; LastDecision = "On beat"; return true;
        }
    }
}
