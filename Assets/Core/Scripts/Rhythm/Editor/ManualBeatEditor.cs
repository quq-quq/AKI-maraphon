using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AKI.Rhythm.Editor
{
    /// <summary>Standalone authoring tool. Does not modify scene or conductor bindings.</summary>
    public sealed class ManualBeatEditor : EditorWindow
    {
        const string Folder = "Assets/Core/Data/Rhythm/Manual";
        const string AudioFolder = "Assets/Core/Audio/Background/";
        [SerializeField] RhythmTrack track;
        [SerializeField] float cursor;
        [SerializeField] float viewStart;
        [SerializeField] float visibleSeconds = 20;
        [SerializeField] float tapCorrectionMs;
        [SerializeField] bool follow = true;
        [SerializeField] bool auditionMarkers;
        [SerializeField] float auditionSeconds = 2;
        int selected = -1;
        bool selectedAccent;
        bool playing;
        bool dragging;
        float previewEnd = -1;
        Vector2 listScroll;
        float[] waveform;
        string error;
        readonly HashSet<KeyCode> heldKeys = new HashSet<KeyCode>();
        static readonly Type AudioUtil = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Color BeatColor = new Color(.3f, .85f, 1);
        static readonly Color AccentColor = new Color(1, .65f, .2f);

        [MenuItem("AKI/Rhythm/Manual Beat Editor")]
        public static void Open()
        {
            var window = GetWindow<ManualBeatEditor>("Manual Beats");
            window.minSize = new Vector2(740, 530);
            if (window.track == null) window.LoadTrack(GetOrCreateTrack("FishingGamelan"));
            window.Show();
        }

        public static RhythmTrack GetOrCreateTrack(string song)
        {
            if (song != "FishingGamelan" && song != "NagaGamelan")
                throw new ArgumentException("Unknown song", nameof(song));
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + song + ".mp3");
            if (clip == null) throw new InvalidOperationException("Audio clip not found: " + song);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Core/Data/Rhythm", "Manual");
            string path = Folder + "/" + song + "_Manual.asset";
            var asset = AssetDatabase.LoadAssetAtPath<RhythmTrack>(path);
            if (asset != null) return asset;
            asset = CreateInstance<RhythmTrack>();
            asset.clip = clip;
            asset.analysisNotes = "Manual annotation. Beat = harpoon/light pulse; Accent = current particle shake. Times in seconds from AudioClip start. Empty until annotated in AKI/Rhythm/Manual Beat Editor.";
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        void OnEnable()
        {
            EditorApplication.update += Tick;
            Undo.undoRedoPerformed += AfterUndo;
            if (track != null) ReadWaveform();
        }

        void OnDisable()
        {
            Stop();
            EditorApplication.update -= Tick;
            Undo.undoRedoPerformed -= AfterUndo;
        }

        void OnLostFocus() { heldKeys.Clear(); }
        void AfterUndo() { selected = -1; Repaint(); }

        static object AudioCall(string name, params object[] args)
        {
            if (AudioUtil == null) throw new InvalidOperationException("Unity audio preview API unavailable.");
            var method = AudioUtil.GetMethods(Flags).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
            if (method == null) throw new MissingMethodException("AudioUtil." + name);
            return method.Invoke(null, args);
        }

        float Duration => track != null && track.clip != null ? (float)track.Duration : 0;
        float[] SelectedTimes => selectedAccent ? track.accentTimes : track.beatTimes;
        bool HasSelection => track != null && selected >= 0 && selected < SelectedTimes.Length;

        void LoadTrack(RhythmTrack next)
        {
            Stop();
            track = next;
            selected = -1;
            cursor = viewStart = 0;
            listScroll = Vector2.zero;
            ReadWaveform();
            Repaint();
        }

        void ReadWaveform()
        {
            waveform = null;
            error = null;
            if (track == null || track.clip == null) return;
            try
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(track.clip)) as AudioImporter;
                if (importer != null) waveform = AudioCall("GetMinMaxData", importer) as float[];
            }
            catch (Exception ex) { error = "Waveform: " + ex.GetBaseException().Message; }
        }

        public void PlayFromCursor()
        {
            if (track == null || track.clip == null || Duration <= 0 || EditorApplication.isPlaying) return;
            try
            {
                AudioCall("StopAllPreviewClips");
                int sample = Mathf.Clamp(Mathf.RoundToInt(cursor * track.clip.frequency), 0, track.clip.samples - 1);
                AudioCall("PlayPreviewClip", track.clip, sample, false);
                playing = true;
                error = null;
            }
            catch (Exception ex) { error = "Audio preview: " + ex.GetBaseException().Message; playing = false; }
        }

        void Stop()
        {
            if (playing)
            {
                try { cursor = ReadPosition(); AudioCall("StopAllPreviewClips"); }
                catch { /* Preview can be unavailable during domain reload. */ }
            }
            playing = false;
            previewEnd = -1;
        }

        float ReadPosition()
        {
            return Mathf.Clamp((int)AudioCall("GetPreviewClipSamplePosition") / (float)track.clip.frequency, 0, Duration);
        }

        void Tick()
        {
            if (!playing || track == null || track.clip == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Stop(); return; }
            try
            {
                if (!(bool)AudioCall("IsPreviewClipPlaying")) { playing = false; previewEnd = -1; Repaint(); return; }
                cursor = ReadPosition();
                if (previewEnd >= 0 && cursor >= previewEnd) { Stop(); Repaint(); return; }
                if (follow && (cursor > viewStart + visibleSeconds * .9f || cursor < viewStart))
                    viewStart = Mathf.Clamp(cursor - visibleSeconds * .15f, 0, Mathf.Max(0, Duration - visibleSeconds));
                Repaint();
            }
            catch (Exception ex) { error = ex.GetBaseException().Message; playing = false; }
        }

        void Seek(float time)
        {
            bool resume = playing;
            Stop();
            cursor = Mathf.Clamp(time, 0, Mathf.Max(0, Duration - .001f));
            if (resume) PlayFromCursor();
            Repaint();
        }

        void Changed(string label)
        {
            Undo.RecordObject(track, label);
            EditorUtility.SetDirty(track);
        }

        public void AddMarker(bool accent, float seconds)
        {
            if (track == null || track.clip == null || Duration <= 0) return;
            seconds = Mathf.Clamp(seconds, 0, Duration - .001f);
            float[] times = accent ? track.accentTimes : track.beatTimes;
            if (times.Any(t => Mathf.Abs(t - seconds) < .001f)) return;
            Changed(accent ? "Add accent" : "Add beat");
            var values = times.ToList();
            int insertion = values.FindIndex(t => t > seconds);
            if (insertion < 0) insertion = values.Count;
            values.Insert(insertion, seconds);
            if (accent)
            {
                var strengths = track.accentStrengths.ToList();
                strengths.Insert(insertion, 1);
                track.accentTimes = values.ToArray();
                track.accentStrengths = strengths.ToArray();
            }
            else track.beatTimes = values.ToArray();
            selectedAccent = accent;
            selected = insertion;
            Repaint();
        }

        void MarkNow(bool accent)
        {
            // Read audio sample position in the input event, not the previous repaint.
            float time = playing ? ReadPosition() : cursor;
            AddMarker(accent, time - (playing ? tapCorrectionMs * .001f : 0));
        }

        void DeleteSelected()
        {
            if (!HasSelection) return;
            Changed("Delete marker");
            if (selectedAccent)
            {
                track.accentTimes = track.accentTimes.Where((t, i) => i != selected).ToArray();
                track.accentStrengths = track.accentStrengths.Where((t, i) => i != selected).ToArray();
            }
            else track.beatTimes = track.beatTimes.Where((t, i) => i != selected).ToArray();
            selected = -1;
        }

        void SortMarkers()
        {
            float remembered = HasSelection ? SelectedTimes[selected] : -1;
            track.beatTimes = track.beatTimes.OrderBy(t => t).ToArray();
            var order = Enumerable.Range(0, track.accentTimes.Length).OrderBy(i => track.accentTimes[i]).ToArray();
            track.accentStrengths = order.Select(i => track.accentStrengths[i]).ToArray();
            track.accentTimes = order.Select(i => track.accentTimes[i]).ToArray();
            if (remembered >= 0) selected = Array.IndexOf(SelectedTimes, remembered);
        }

        void MoveSelected(float time, bool recordUndo = true)
        {
            if (!HasSelection) return;
            if (recordUndo) Changed("Move marker");
            float next = Mathf.Clamp(time, 0, Duration - .001f);
            if (SelectedTimes.Where((t, i) => i != selected).Any(t => Mathf.Abs(t - next) < .001f)) return;
            SelectedTimes[selected] = next;
            if (!dragging) SortMarkers();
            Repaint();
        }

        void HandleKeys()
        {
            var e = Event.current;
            if (e.type == EventType.KeyUp) { heldKeys.Remove(e.keyCode); return; }
            if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField || track == null || track.clip == null) return;
            if (e.control || e.command || e.alt) return;
            if (e.keyCode != KeyCode.M && e.keyCode != KeyCode.A && e.keyCode != KeyCode.Space &&
                e.keyCode != KeyCode.Delete && e.keyCode != KeyCode.Backspace && e.keyCode != KeyCode.LeftArrow && e.keyCode != KeyCode.RightArrow) return;
            if (!heldKeys.Add(e.keyCode)) { e.Use(); return; }
            if (e.keyCode == KeyCode.Space) { if (playing) Stop(); else PlayFromCursor(); }
            else if (e.keyCode == KeyCode.M) MarkNow(false);
            else if (e.keyCode == KeyCode.A) MarkNow(true);
            else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) DeleteSelected();
            else
            {
                float step = (e.shift ? .001f : .01f) * (e.keyCode == KeyCode.LeftArrow ? -1 : 1);
                if (HasSelection) MoveSelected(SelectedTimes[selected] + step);
                else Seek(cursor + step);
            }
            e.Use();
            Repaint();
        }

        void OnGUI()
        {
            HandleKeys();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("FishingGamelan")) LoadTrack(GetOrCreateTrack("FishingGamelan"));
                if (GUILayout.Button("NagaGamelan")) LoadTrack(GetOrCreateTrack("NagaGamelan"));
                if (GUILayout.Button("Save", GUILayout.Width(75)) && track != null)
                { SortMarkers(); EditorUtility.SetDirty(track); AssetDatabase.SaveAssetIfDirty(track); }
            }
            var next = (RhythmTrack)EditorGUILayout.ObjectField("Annotation asset", track, typeof(RhythmTrack), false);
            if (next != track) LoadTrack(next);
            if (track == null || track.clip == null) { EditorGUILayout.HelpBox("Choose an annotation asset with an AudioClip.", MessageType.Info); return; }
            EditorGUILayout.LabelField("Audio", track.clip.name + "   |   " + Duration.ToString("F3") + " s   |   Beats " + track.BeatCount + " / Accents " + track.accentTimes.Length);
            EditorGUILayout.HelpBox("Space: play/stop   M: beat   A: accent   Click waveform: seek\nDrag marker: move   Delete: remove   Arrows: ±10 ms (Shift: ±1 ms)   Ctrl+Z: undo\nBeat = shot/light pulse; Accent = particle shake. Save writes only this annotation asset.", MessageType.Info);
            if (EditorApplication.isPlaying) EditorGUILayout.HelpBox("Leave Play Mode to listen and annotate.", MessageType.Warning);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                {
                    if (GUILayout.Button(playing ? "Stop" : "Play", GUILayout.Width(65))) { if (playing) Stop(); else PlayFromCursor(); }
                }
                if (GUILayout.Button("Start", GUILayout.Width(55))) { Seek(0); viewStart = 0; }
                float position = EditorGUILayout.FloatField(cursor, GUILayout.Width(80));
                if (Mathf.Abs(position - cursor) > .0001f) Seek(position);
                GUILayout.Label("seconds", GUILayout.Width(55));
                if (GUILayout.Button("+ Beat (M)")) MarkNow(false);
                if (GUILayout.Button("+ Accent (A)")) MarkNow(true);
                follow = GUILayout.Toggle(follow, "Follow", GUILayout.Width(70));
            }
            tapCorrectionMs = EditorGUILayout.Slider(new GUIContent("Tap correction (ms)", "Positive value places LIVE taps earlier to compensate your reaction/audio output delay. Does not move existing markers."), tapCorrectionMs, -300, 300);
            visibleSeconds = EditorGUILayout.Slider("Visible seconds", visibleSeconds, Mathf.Min(1, Duration), Mathf.Max(1, Duration));
            viewStart = EditorGUILayout.Slider("View start", viewStart, 0, Mathf.Max(0, Duration - visibleSeconds));
            DrawTimeline(GUILayoutUtility.GetRect(10, 180, GUILayout.ExpandWidth(true)));
            DrawSelection();
            using (new EditorGUILayout.HorizontalScope())
            {
                auditionMarkers = GUILayout.Toggle(auditionMarkers, "Audition when selecting a row");
                auditionSeconds = EditorGUILayout.Slider("Excerpt seconds", auditionSeconds, .5f, 5);
            }
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            var rows = track.beatTimes.Select((t, i) => new { time = t, index = i, accent = false })
                .Concat(track.accentTimes.Select((t, i) => new { time = t, index = i, accent = true })).OrderBy(row => row.time);
            foreach (var row in rows)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.color = row.accent ? AccentColor : BeatColor;
                    if (GUILayout.Button((selected == row.index && selectedAccent == row.accent ? "▶ " : "") +
                        (row.accent ? "Accent " : "Beat ") + (row.index + 1), GUILayout.Width(125)))
                    {
                        selected = row.index; selectedAccent = row.accent;
                        Seek(Mathf.Max(0, row.time - .5f));
                        viewStart = Mathf.Clamp(row.time - visibleSeconds * .5f, 0, Mathf.Max(0, Duration - visibleSeconds));
                        if (auditionMarkers && !EditorApplication.isPlaying) { PlayFromCursor(); previewEnd = Mathf.Min(Duration, cursor + auditionSeconds); }
                    }
                    GUI.color = Color.white;
                    GUILayout.Label(row.time.ToString("F3") + " s");
                    if (row.accent) GUILayout.Label("Strength " + track.accentStrengths[row.index].ToString("F2"));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawSelection()
        {
            if (!HasSelection) { EditorGUILayout.LabelField("Select a marker to edit its exact time."); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(selectedAccent ? "Selected accent" : "Selected beat", GUILayout.Width(115));
                float time = EditorGUILayout.FloatField(SelectedTimes[selected], GUILayout.Width(95));
                if (Mathf.Abs(time - SelectedTimes[selected]) > .0001f) MoveSelected(time);
                if (selectedAccent)
                {
                    float strength = EditorGUILayout.Slider("Strength", track.accentStrengths[selected], 0, 1);
                    if (!Mathf.Approximately(strength, track.accentStrengths[selected])) { Changed("Accent strength"); track.accentStrengths[selected] = strength; }
                }
                if (GUILayout.Button("Delete", GUILayout.Width(65))) DeleteSelected();
            }
        }

        float TimeAt(Rect r, float x) => Mathf.Clamp(viewStart + (x - r.x) / r.width * visibleSeconds, 0, Duration - .001f);
        float XAt(Rect r, float time) => r.x + (time - viewStart) / visibleSeconds * r.width;

        void DrawTimeline(Rect r)
        {
            EditorGUI.DrawRect(r, new Color(.07f, .09f, .12f));
            if (Event.current.type == EventType.Repaint)
            {
                int channels = Mathf.Max(1, track.clip.channels);
                int bins = waveform == null ? 0 : waveform.Length / (channels * 2);
                if (bins > 0)
                {
                    int columns = Mathf.CeilToInt(r.width);
                    for (int x = 0; x < columns; x++)
                    {
                        int lo = Mathf.Clamp(Mathf.FloorToInt((viewStart + x / r.width * visibleSeconds) / Duration * bins), 0, bins - 1);
                        int hi = Mathf.Clamp(Mathf.CeilToInt((viewStart + (x + 1) / r.width * visibleSeconds) / Duration * bins), lo + 1, bins);
                        float peak = 0;
                        for (int b = lo; b < hi; b++)
                            for (int c = 0; c < channels * 2; c++) peak = Mathf.Max(peak, Mathf.Abs(waveform[b * channels * 2 + c]));
                        float height = Mathf.Clamp01(peak) * (r.height - 45);
                        EditorGUI.DrawRect(new Rect(r.x + x, r.center.y - height * .5f, 1, Mathf.Max(1, height)), new Color(.37f, .46f, .53f));
                    }
                }
                else GUI.Label(new Rect(r.x + 10, r.y + 25, r.width - 20, 25), "Waveform unavailable; audio and marker editing remain available.");
                float grid = visibleSeconds > 45 ? 10 : visibleSeconds > 12 ? 5 : visibleSeconds > 4 ? 1 : .25f;
                for (float t = Mathf.Ceil(viewStart / grid) * grid; t <= viewStart + visibleSeconds; t += grid)
                {
                    float x = XAt(r, t);
                    EditorGUI.DrawRect(new Rect(x, r.y, 1, r.height), new Color(1, 1, 1, .09f));
                    GUI.Label(new Rect(x + 3, r.y + 2, 65, 20), t.ToString("F2"));
                }
                DrawMarkers(r, track.beatTimes, false);
                DrawMarkers(r, track.accentTimes, true);
                if (cursor >= viewStart && cursor <= viewStart + visibleSeconds)
                    EditorGUI.DrawRect(new Rect(XAt(r, cursor), r.y, 2, r.height), Color.white);
            }
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                GUI.FocusControl(null);
                bool laneAccent = e.mousePosition.y > r.yMax - 40;
                var times = laneAccent ? track.accentTimes : track.beatTimes;
                int nearest = -1; float distance = 8;
                for (int i = 0; i < times.Length; i++)
                {
                    float d = Mathf.Abs(XAt(r, times[i]) - e.mousePosition.x);
                    if (d < distance) { distance = d; nearest = i; }
                }
                if (nearest >= 0)
                {
                    selected = nearest; selectedAccent = laneAccent; dragging = true;
                    Changed("Drag marker");
                }
                else if (e.shift || e.alt) AddMarker(e.alt, TimeAt(r, e.mousePosition.x));
                else { selected = -1; Seek(TimeAt(r, e.mousePosition.x)); }
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && dragging)
            { MoveSelected(TimeAt(r, e.mousePosition.x), false); e.Use(); }
            else if (e.type == EventType.MouseUp && dragging)
            { dragging = false; SortMarkers(); e.Use(); Repaint(); }
        }

        void DrawMarkers(Rect r, float[] times, bool accent)
        {
            for (int i = 0; i < times.Length; i++)
            {
                if (times[i] < viewStart || times[i] > viewStart + visibleSeconds) continue;
                bool active = selected == i && selectedAccent == accent;
                float x = XAt(r, times[i]);
                float y = accent ? r.yMax - 38 : r.y + 22;
                float h = accent ? 35 : r.height - 63;
                EditorGUI.DrawRect(new Rect(x, y, active ? 3 : 1, h), active ? Color.magenta : accent ? AccentColor : BeatColor);
                GUI.Label(new Rect(x + 2, accent ? r.yMax - 23 : r.y + 20, 35, 20), accent ? "A" : "M");
            }
        }
    }
}
