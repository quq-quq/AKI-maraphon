using System;
using System.Collections.Generic;
using System.Reflection;
using AKI.Player;
using AKI.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AKI.Rhythm.Editor
{
    /// <summary>
    /// Small Edit Mode checks against the actual rhythm methods, without NUnit, scene assets or audio playback.
    /// Private state is seeded only on inactive, hidden objects in a temporary preview scene.
    /// </summary>
    public static class RhythmRegressionChecks
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks;

        [MenuItem("AKI/Rhythm/Run isolated regression checks")]
        public static void RunFromMenu() => RunAll();

        public static string RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Rhythm regression checks require Edit Mode; leave Play Mode first.");

            checks = 0;
            var failures = new List<string>();
            using (var scope = new TestScope())
            {
                RunGroup("Track validation", () => CheckTrackValidation(scope), failures);
                RunGroup("DSP beat windows and loop seams", () => CheckConductor(scope), failures);
                RunGroup("Shot spam, reload and beat consumption", () => CheckShotGate(scope), failures);
                RunGroup("Rejected boss hits cannot restore oxygen", () => CheckBossHitRejection(scope), failures);
            }
            if (failures.Count > 0)
                throw new InvalidOperationException("Rhythm regression failures:\n" + string.Join("\n", failures));

            string result = "Rhythm regression checks passed: " + checks + " assertions. No scene assets changed or audio played.";
            Debug.Log(result);
            return result;
        }

        static void RunGroup(string name, Action action, List<string> failures)
        {
            try { action(); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); }
        }

        static void Expect(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Fields);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }

        static void SetProperty(object target, string name, object value) => SetField(target, "<" + name + ">k__BackingField", value);

        static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, Fields);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return (T)field.GetValue(target);
        }

        static void CheckTrackValidation(TestScope scope)
        {
            RhythmTrack track = scope.Track(1f, 2f, 3f);
            Expect(track.IsValid(out _), "Synthetic recorded track should be valid.");
            Expect(track.BeatAtOrBefore(.9d) == -1 && track.BeatAtOrBefore(2d) == 1, "Beat search must handle before-first and exact beats.");
            Expect(track.NearestBeat(1.5d) == 0 && track.NearestBeat(1.51d) == 1, "Nearest beat ties should prefer the previous beat.");

            track.beatTimes = new[] { 1f, 1f };
            Expect(!track.IsValid(out _), "Duplicate beats must fail validation.");
            track.beatTimes = new[] { 2f, 1f };
            Expect(!track.IsValid(out _), "Unsorted beats must fail validation.");
            track.beatTimes = new[] { 1f, 10f };
            Expect(!track.IsValid(out _), "Beat at the clip end must fail validation.");
            track.beatTimes = new[] { 1f, float.NaN };
            Expect(!track.IsValid(out _), "NaN beat must fail validation.");
            track.beatTimes = new[] { 1f, float.PositiveInfinity };
            Expect(!track.IsValid(out _), "Infinite beat must fail validation.");
            track.beatTimes = new[] { 1f, 2f };
            track.accentTimes = new[] { 1f };
            Expect(!track.IsValid(out _), "Mismatched accent arrays must fail validation.");
            track.accentStrengths = new[] { float.NaN };
            Expect(!track.IsValid(out _), "NaN accent strength must fail validation.");
            track.accentStrengths = new[] { float.PositiveInfinity };
            Expect(!track.IsValid(out _), "Infinite accent strength must fail validation.");
            track.accentStrengths = new[] { .8f };
            Expect(track.IsValid(out _), "Finite accent data should be accepted.");
            track.clip = null;
            Expect(!track.IsValid(out _), "A missing clip must fail validation.");
        }

        static RhythmConductor SeedClock(TestScope scope, RhythmTrack track, double start = 100d, bool loop = false, int session = 7)
        {
            RhythmConductor clock = scope.Component<RhythmConductor>("Test music clock");
            SetProperty(clock, "Track", track);
            SetProperty(clock, "StartedDspTime", start);
            SetProperty(clock, "IsPlaying", true);
            SetProperty(clock, "Looping", loop);
            SetProperty(clock, "Session", session);
            return clock;
        }

        static void CheckConductor(TestScope scope)
        {
            RhythmConductor clock = SeedClock(scope, scope.Track(1f, 2f));
            const float early = .055f, late = .065f;
            const double start = 100d;
            Expect(clock.EvaluateAttempt(start + 1d, early, late, out long id, out float error) && id == 0 && error == 0f,
                "An exact beat must authorize its beat id.");
            Expect(clock.EvaluateAttempt(start + 1d - early, early, late, out _, out _), "The early boundary must be inclusive.");
            Expect(clock.EvaluateAttempt(start + 1d + late, early, late, out _, out _), "The late boundary must be inclusive.");
            Expect(!clock.EvaluateAttempt(start + 1d - early - .001d, early, late, out _, out _), "One millisecond outside the early window must be rejected.");
            Expect(!clock.EvaluateAttempt(start + 1d + late + .001d, early, late, out _, out _), "One millisecond outside the late window must be rejected.");
            Expect(!clock.EvaluateAttempt(start - .01d, early, late, out _, out _), "Clicks before scheduled music must be rejected.");
            Expect(!clock.EvaluateAttempt(start + 10d, early, late, out _, out _), "A non-looped track must reject clicks at its end.");
            Expect(clock.EvaluateAttempt(start + .9d,.1f,.1f,out _,out _), "200ms total window includes the -100ms boundary.");
            Expect(clock.EvaluateAttempt(start + 1.1d,.1f,.1f,out _,out _), "200ms total window includes the +100ms boundary.");
            Expect(!clock.EvaluateAttempt(start + .899d,.1f,.1f,out _,out _), "The -101ms attempt must fail.");
            Expect(!clock.EvaluateAttempt(start + 1.101d,.1f,.1f,out _,out _), "The +101ms attempt must fail.");
            clock.inputLatencySeconds = .04f;
            Expect(clock.EvaluateAttempt(start + 1.04d, early, late, out _, out error) && Mathf.Abs(error) < .00001f,
                "Positive latency must shift the accepted click later.");
            clock.inputLatencySeconds = -.04f;
            Expect(clock.EvaluateAttempt(start + .96d, early, late, out _, out error) && Mathf.Abs(error) < .00001f,
                "Negative latency must shift the accepted click earlier.");
            SetProperty(clock, "IsPlaying", false);
            Expect(!clock.EvaluateAttempt(start + 1d, early, late, out _, out _), "Stopped music must reject all timed shots.");

            clock = SeedClock(scope, scope.Track(.02f, 5f, 9.7f), loop: true);
            Expect(clock.EvaluateAttempt(start + 9.99d, early, late, out id, out error) && id == 3 && error < 0f,
                "An early first beat of the next loop must authorize the next loop's id.");
            clock = SeedClock(scope, scope.Track(.3f, 5f, 9.98f), loop: true);
            Expect(clock.EvaluateAttempt(start + 10.01d, early, late, out id, out error) && id == 2 && error > 0f,
                "The previous loop's last beat must keep its late window across the seam.");
            Expect(clock.EvaluateAttempt(start + 20.3d, early, late, out id, out _) && id == 6,
                "Beat ids must remain distinct over later loops.");
        }

        static void SetSongPosition(RhythmConductor clock, double seconds) => SetProperty(clock, "StartedDspTime", AudioSettings.dspTime - seconds);

        static void ResetGate(RhythmShotGate gate)
        {
            SetField(gate, "lastSession", gate.conductor.Session);
            SetField(gate, "consumedBeat", -1L);
            SetField(gate, "lastAttempt", AudioSettings.dspTime - 1d);
        }

        static void AllowQuietAttempt(RhythmShotGate gate) => SetField(gate, "lastAttempt", AudioSettings.dspTime - gate.spamQuietSeconds - .1d);

        static void CheckShotGate(TestScope scope)
        {
            RhythmGameFlow game = scope.Component<RhythmGameFlow>("Test game phase");
            SetField(game, "phase", RhythmGameFlow.GamePhase.BossFight);
            RhythmConductor clock = SeedClock(scope, scope.Track(1f, 2f));
            var gate = scope.Component<RhythmShotGate>("Test shot gate");
            gate.game = game; gate.conductor = clock;
            ResetGate(gate); SetSongPosition(clock, 1d);
            Expect(gate.TryAuthorize() && gate.LastAcceptedBeat == 0 && gate.AcceptedSession == clock.Session,
                "A quiet on-beat click must stamp the launch beat and session.");
            Expect(!gate.TryAuthorize() && gate.LastDecision == "Click spam lockout", "Rapid repeats must enter the quiet-time lockout.");
            AllowQuietAttempt(gate); SetSongPosition(clock, 1d);
            Expect(!gate.TryAuthorize() && gate.LastDecision == "This beat already attempted", "An accepted beat must not authorize twice, even after quiet time.");

            ResetGate(gate); SetSongPosition(clock, 1.15d);
            Expect(!gate.TryAuthorize() && gate.LastDecision == "Off beat", "A quiet off-beat click must fail.");
            AllowQuietAttempt(gate); SetSongPosition(clock, 1d);
            Expect(!gate.TryAuthorize() && gate.LastDecision == "This beat already attempted", "An off-beat attempt must consume that beat's opportunity.");

            ResetGate(gate); SetSongPosition(clock, 1d);
            Expect(!gate.TryAuthorize(false) && GetField<long>(gate, "consumedBeat") == -1L, "Reload clicks must be recorded without consuming a beat.");
            Expect(!gate.TryAuthorize(true) && gate.LastDecision == "Click spam lockout", "Reload spam must remain blocked immediately after becoming loaded.");
            AllowQuietAttempt(gate); SetSongPosition(clock, 1d);
            Expect(gate.TryAuthorize(true), "A reloaded weapon should authorize after a quiet interval.");

            SetProperty(clock, "Session", clock.Session + 1); SetSongPosition(clock, 1d);
            Expect(gate.TryAuthorize() && gate.AcceptedSession == clock.Session, "A new music session must clear the old beat consumption and lockout.");
            SetField(game, "phase", RhythmGameFlow.GamePhase.GoldenFish);
            SetProperty(clock, "IsPlaying", false); ResetGate(gate);
            Expect(gate.TryAuthorize() && gate.LastAcceptedBeat == -1, "Golden-fish free shots must carry no rhythm beat when music is stopped.");
            SetField(game, "phase", RhythmGameFlow.GamePhase.FinalCharge);
            Expect(!gate.TryAuthorize(), "The final charge must block shooting.");
        }

        static void CheckBossHitRejection(TestScope scope)
        {
            RhythmGameFlow game = scope.Component<RhythmGameFlow>("Test boss rewards");
            RhythmConductor clock = SeedClock(scope, scope.Track(1f, 2f));
            game.conductor = clock;
            SetField(game, "phase", RhythmGameFlow.GamePhase.BossFight);
            BreathHolding breath = scope.Component<BreathHolding>("Test unspent oxygen");
            game.breath = breath;
            SetField(breath, "air", .25f);
            int restoredEvents = 0;
            breath.onBreathRestored.AddListener(() => restoredEvents++);
            HarpoonProjectile arrow = scope.Component<HarpoonProjectile>("Test launch stamp");

            arrow.SetRhythmAuthorization(clock.Session - 1, 0L);
            game.OnNagaHit(arrow);
            Expect(breath.Air01 == .25f && restoredEvents == 0 && game.SuccessfulBossHits == 0,
                "A stale music-session arrow must not restore oxygen or reward a boss hit.");
            arrow.SetRhythmAuthorization(clock.Session, -1L);
            game.OnNagaHit(arrow);
            Expect(breath.Air01 == .25f && restoredEvents == 0, "A golden-fish/non-rhythm arrow must not restore oxygen.");
            arrow.SetRhythmAuthorization(clock.Session, 1L);
            GetField<HashSet<long>>(game, "rewardedBeats").Add(1L);
            game.OnNagaHit(arrow);
            Expect(breath.Air01 == .25f && restoredEvents == 0 && game.SuccessfulBossHits == 0,
                "An already rewarded launch beat must not restore oxygen a second time.");
            SetField(game, "phase", RhythmGameFlow.GamePhase.FinalCharge);
            arrow.SetRhythmAuthorization(clock.Session, 0L);
            game.OnNagaHit(arrow);
            Expect(breath.Air01 == .25f && restoredEvents == 0, "No arrow may restore oxygen after the finale begins.");
            // Only rejected paths are exercised here: an accepted hit creates a DOTween recovery tween.
            // Keeping that separate avoids creating the live editor's global DOTween manager during these checks.
        }

        sealed class TestScope : IDisposable
        {
            readonly Scene preview = EditorSceneManager.NewPreviewScene();
            readonly List<Object> data = new List<Object>();
            readonly AudioClip clip;

            public TestScope()
            {
                clip = AudioClip.Create("Rhythm regression (silent, 10 seconds)", 480000, 1, 48000, false);
                clip.hideFlags = HideFlags.HideAndDontSave;
                data.Add(clip);
            }

            public T Component<T>(string name) where T : Component
            {
                var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                go.SetActive(false);
                SceneManager.MoveGameObjectToScene(go, preview);
                return go.AddComponent<T>();
            }

            public RhythmTrack Track(params float[] times)
            {
                var track = ScriptableObject.CreateInstance<RhythmTrack>();
                track.hideFlags = HideFlags.HideAndDontSave;
                track.clip = clip; track.beatTimes = times;
                data.Add(track);
                return track;
            }

            public void Dispose()
            {
                EditorSceneManager.ClosePreviewScene(preview);
                for (int i = data.Count - 1; i >= 0; i--) if (data[i] != null) Object.DestroyImmediate(data[i]);
            }
        }
    }
}
