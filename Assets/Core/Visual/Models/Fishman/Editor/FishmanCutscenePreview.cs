using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Editor-only viewer. Does not create a controller, modify clips or touch the level.
public sealed class FishmanCutscenePreview : EditorWindow
{
    private const string Folder = "Assets/Core/Visual/Models/Fishman/";
    private PreviewRenderUtility preview;
    private GameObject instance;
    private Animator animator;
    private Transform boat;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable sitPlayable, divePlayable;
    private AnimationClip sit, dive;
    private Bounds framing;
    private Vector3 boatStart;
    private float boatDrift;
    private float time;
    [SerializeField] private float seatedHold = 1f;
    [SerializeField] private float crossfadeSeconds = .25f;
    private bool playing;
    private double lastTick;
    private Vector2 orbit = new Vector2(125f, 22f);
    private float zoom = 1f;
    private string error;

    public float TransitionDuration => sit != null && dive != null ? Mathf.Clamp(crossfadeSeconds, 0f, Mathf.Min(.6f, sit.length, dive.length)) : 0f;
    public float RunAccelerationDuration => TransitionDuration > .00001f ? .4f : 0f;
    public float TransitionStart => seatedHold + (sit != null ? sit.length : 0f) - TransitionDuration;
    public float Duration => seatedHold + (sit != null ? sit.length : 0f) + (dive != null ? dive.length : 0f) - TransitionDuration + RunAccelerationDuration * .5f;
    public float BoatDriftMeters => boatDrift;
    public float PreviewTime => time;
    public string Segment => time < seatedHold ? "Seated pose" : time < TransitionStart ? "Sit_Stand" : time < TransitionStart + TransitionDuration ? "Stand → Run (blend)" : "Run_To_Dive";

    [MenuItem("Tools/Fishman/Preview Full Cutscene")]
    public static FishmanCutscenePreview Open()
    {
        var window = GetWindow<FishmanCutscenePreview>();
        window.titleContent = new GUIContent("Fishman Cutscene");
        window.minSize = new Vector2(640f, 460f);
        window.Show();
        window.Focus();
        return window;
    }

    private void OnEnable()
    {
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        lastTick = EditorApplication.timeSinceStartup;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
        Cleanup();
    }

    private void Cleanup()
    {
        playing = false;
        if (graph.IsValid()) graph.Destroy();
        preview?.Cleanup();
        preview = null;
        instance = null;
        animator = null;
        boat = null;
    }

    private void Initialize()
    {
        Cleanup();
        error = null;
        try
        {
            sit = AssetDatabase.LoadAllAssetsAtPath(Folder + "Fishman_Cutscene.fbx").OfType<AnimationClip>().First(c => c.name == "Sit_Stand");
            dive = AssetDatabase.LoadAllAssetsAtPath(Folder + "Fishman_Cutscene.fbx").OfType<AnimationClip>().First(c => c.name == "Run_To_Dive");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Prefabs/Fishman_Cutscene.prefab");
            if (prefab == null) throw new InvalidOperationException("Fishman_Cutscene prefab not found.");
            preview = new PreviewRenderUtility();
            instance = preview.InstantiatePrefabInScene(prefab);
            animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) throw new InvalidOperationException("A valid Humanoid Animator is required.");
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.updateWhenOffscreen = true;
            boat = instance.transform.Find("Boat");
            if (boat == null) throw new InvalidOperationException("Independent Boat object not found.");
            boatStart = boat.position;
            boatDrift = 0f;
            graph = PlayableGraph.Create("Fishman_Editor_Cutscene_Preview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            sitPlayable = AnimationClipPlayable.Create(graph, sit);
            divePlayable = AnimationClipPlayable.Create(graph, dive);
            sitPlayable.SetApplyFootIK(false);
            divePlayable.SetApplyFootIK(false);
            sitPlayable.SetSpeed(0);
            divePlayable.SetSpeed(0);
            graph.Connect(sitPlayable, 0, mixer, 0);
            graph.Connect(divePlayable, 0, mixer, 1);
            var output = AnimationPlayableOutput.Create(graph, "Actor only", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
            preview.camera.orthographic = true;
            preview.camera.nearClipPlane = .01f;
            preview.camera.farClipPlane = 100f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.13f, .15f, .18f, 1f);
            preview.lights[0].intensity = 1.5f;
            preview.lights[0].transform.rotation = Quaternion.Euler(35, 120, 0);
            preview.lights[1].intensity = 1f;
            preview.ambientColor = new Color(.6f, .6f, .6f);
            framing = boat.GetComponent<Renderer>().bounds;
            foreach (float t in new[] { 0f, TransitionStart, TransitionStart + TransitionDuration, TransitionStart + dive.length * .5f, Duration })
            {
                Sample(t);
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var baked = new Mesh();
                    try
                    {
                        skin.BakeMesh(baked);
                        var bounds = baked.bounds;
                        for (int i = 0; i < 8; i++)
                        {
                            var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                            framing.Encapsulate(skin.transform.TransformPoint(corner));
                        }
                    }
                    finally { DestroyImmediate(baked); }
                }
            }
            time = 0f;
            Sample(time);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Cleanup();
            Debug.LogException(exception);
        }
    }

    public void Seek(float seconds)
    {
        if (preview == null) Initialize();
        if (preview == null) return;
        playing = false;
        time = Mathf.Clamp(seconds, 0f, Duration);
        Sample(time);
        Repaint();
    }

    private void Sample(float seconds)
    {
        if (!graph.IsValid()) return;
        // Overlap the tail of standing with the beginning of running. SmoothStep
        // starts and finishes with zero weight derivative, avoiding a pose pop.
        float runWeight = TransitionDuration > .00001f
            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TransitionStart, TransitionStart + TransitionDuration, seconds))
            : seconds >= TransitionStart ? 1f : 0f;
        mixer.SetInputWeight(0, 1f - runWeight);
        mixer.SetInputWeight(1, runWeight);
        sitPlayable.SetTime(Mathf.Clamp(seconds - seatedHold, 0f, sit.length));
        float elapsed = Mathf.Max(0f, seconds - TransitionStart);
        float runTime = elapsed;
        // Integrate a SmoothStep speed ramp (0 → original speed). This avoids
        // accelerating the baked trajectory twice through both time and weight.
        if (RunAccelerationDuration > .00001f)
        {
            float u = Mathf.Clamp01(elapsed / RunAccelerationDuration);
            runTime = elapsed < RunAccelerationDuration
                ? RunAccelerationDuration * (u * u * u - .5f * u * u * u * u)
                : elapsed - RunAccelerationDuration * .5f;
        }
        divePlayable.SetTime(Mathf.Clamp(runTime, 0f, dive.length));
        graph.Evaluate(0f);
        boatDrift = Mathf.Max(boatDrift, Vector3.Distance(boatStart, boat.position));
    }

    private void Tick()
    {
        var now = EditorApplication.timeSinceStartup;
        if (playing && preview != null)
        {
            time = Mathf.Min(Duration, time + (float)Math.Min(now - lastTick, .1));
            Sample(time);
            if (time >= Duration) playing = false;
            Repaint();
        }
        lastTick = now;
    }

    private void OnGUI()
    {
        if (preview == null && error == null) Initialize();
        if (error != null)
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("Reload")) Initialize();
            return;
        }
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button(playing ? "Pause" : "Play", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                if (time >= Duration) time = 0f;
                playing = !playing;
                lastTick = EditorApplication.timeSinceStartup;
            }
            if (GUILayout.Button("Restart", EditorStyles.toolbarButton, GUILayout.Width(65))) Seek(0f);
            GUILayout.Label($"{Segment}   {time:F2} / {Duration:F2} s", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload assets", EditorStyles.toolbarButton, GUILayout.Width(90))) Initialize();
        }
        EditorGUI.BeginChangeCheck();
        float selectedTime = EditorGUILayout.Slider("Sequence time", time, 0f, Duration);
        if (EditorGUI.EndChangeCheck()) Seek(selectedTime);
        EditorGUI.BeginChangeCheck();
        seatedHold = EditorGUILayout.Slider("Seated hold (preview only)", seatedHold, 0f, 3f);
        if (EditorGUI.EndChangeCheck()) Seek(time);
        EditorGUI.BeginChangeCheck();
        crossfadeSeconds = EditorGUILayout.Slider("Stand → Run transition (s)", crossfadeSeconds, .1f, .5f);
        if (EditorGUI.EndChangeCheck()) Seek(time);
        EditorGUILayout.HelpBox("Seated first frame → Sit_Stand → smooth Stand/Run blend + 0.4 s acceleration → Run_To_Dive. Original clips unchanged; water/swimmer handoff not included. This transition is in the editor preview, not an Animator Controller. Drag to orbit; scroll to zoom.", MessageType.Info);
        var rect = GUILayoutUtility.GetRect(100f, 100f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        var evt = Event.current;
        if (rect.Contains(evt.mousePosition))
        {
            if (evt.type == EventType.MouseDrag && evt.button == 0)
            {
                orbit.x += evt.delta.x * .5f;
                orbit.y = Mathf.Clamp(orbit.y + evt.delta.y * .5f, -75f, 75f);
                evt.Use(); Repaint();
            }
            else if (evt.type == EventType.ScrollWheel)
            {
                zoom = Mathf.Clamp(zoom * Mathf.Exp(evt.delta.y * .05f), .35f, 2.5f);
                evt.Use(); Repaint();
            }
        }
        if (evt.type == EventType.Repaint && rect.width > 0f && rect.height > 0f)
        {
            Sample(time);
            var rotation = Quaternion.Euler(orbit.y, orbit.x, 0f);
            preview.camera.transform.position = framing.center + rotation * new Vector3(0f, 0f, -12f);
            preview.camera.transform.rotation = rotation;
            preview.camera.orthographicSize = Mathf.Max(2f, framing.extents.magnitude) * zoom * Mathf.Max(1f, rect.height / rect.width);
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render(true, false);
            var texture = preview.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }
        GUILayout.Label($"Boat displacement: {boatDrift * 1000f:F3} mm  •  Editor preview only — scene and Animator Controller unchanged.", EditorStyles.miniLabel);
    }
}
