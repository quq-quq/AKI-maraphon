using UnityEditor;
using UnityEngine;

namespace AKI.Rhythm.Editor
{
    [CustomEditor(typeof(RhythmTrack))]
    sealed class RhythmTrackInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var track = (RhythmTrack)target;
            EditorGUILayout.HelpBox("Recorded beats are the authoritative rhythm. Reference BPM is only an average; these recordings change tempo. Times are measured from AudioClip start, not from entering water.", MessageType.Info);
            EditorGUILayout.LabelField("Measured pulses", track.BeatCount.ToString());
            EditorGUILayout.LabelField("Strong accents", track.accentTimes.Length.ToString());
            EditorGUILayout.LabelField("Duration", track.Duration.ToString("F3") + " s");
            DrawDefaultInspector();
            if (!track.IsValid(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            Rect timeline = GUILayoutUtility.GetRect(10, 55, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(timeline, new Color(.08f,.1f,.14f));
            if (track.Duration <= 0d) return;
            foreach (float time in track.beatTimes)
                EditorGUI.DrawRect(new Rect(timeline.x+(float)(time/track.Duration)*timeline.width,timeline.y+5,1,timeline.height-10),new Color(.4f,.85f,1f,.75f));
            for (int i = 0; i < track.accentTimes.Length; i++)
                EditorGUI.DrawRect(new Rect(timeline.x+(float)(track.accentTimes[i]/track.Duration)*timeline.width,timeline.yMax-15,1,10*track.accentStrengths[i]),new Color(1f,.65f,.2f));
        }
    }
    [CustomEditor(typeof(RhythmGameFlow))]
    sealed class RhythmFlowInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var game = (RhythmGameFlow)target;
            if (!Application.isPlaying) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live phase", game.Phase.ToString());
            EditorGUILayout.LabelField("Successful boss hits", game.SuccessfulBossHits.ToString());
            if (game.conductor != null)
            {
                EditorGUILayout.LabelField("Music position", game.conductor.ClipSeconds.ToString("F3") + " s");
                EditorGUILayout.LabelField("Beat", game.conductor.CurrentBeat.ToString());
            }
            if (game.ShotGate != null)
            {
                EditorGUILayout.LabelField("Last click", game.ShotGate.LastDecision);
                EditorGUILayout.LabelField("Timing error", (game.ShotGate.LastTimingError*1000f).ToString("F1")+" ms");
            }
            if (GUILayout.Button("Debug: spawn golden fish")) game.SpawnGoldenFish();
            if (GUILayout.Button("Debug: begin boss")) game.BeginBossFight();
            if (GUILayout.Button("Restart")) game.RestartGame();
            Repaint();
        }
    }
}
