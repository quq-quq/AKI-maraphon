using System.Collections;
using AKI.Water;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AKI.Player
{
    /// <summary>
    /// Passing out and coming to: the view slowly goes black, the scene reloads, then the eyes open - the lids part
    /// with a heavy blink, and the view is gaussian-blurred at first and comes into focus over a few seconds.
    /// Drawn by the water lens pass (<see cref="WaterLensFeature"/>). Lives through the reload and removes itself after.
    /// </summary>
    public class Blackout : MonoBehaviour
    {
        static readonly int FadeId = Shader.PropertyToID("_ScreenFade");
        static readonly int BlurId = Shader.PropertyToID("_WakeBlur");
        static readonly int LidsId = Shader.PropertyToID("_EyeClosed");

        // eyelids while coming to (time in s -> 1 shut .. 0 open): crack open, sink back once, then open up
        static readonly AnimationCurve Lids = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.45f, 0.45f), new Keyframe(0.75f, 0.8f), new Keyframe(1.6f, 0f));

        static Blackout running;

        public static bool IsRunning => running != null;

        /// <summary>
        /// Fades to black over <paramref name="fadeSeconds"/>, reloads the active scene and comes to over
        /// <paramref name="wakeSeconds"/>. Ignored while one is already running.
        /// </summary>
        public static void ReloadScene(float fadeSeconds = 1.5f, float wakeSeconds = 2.5f)
        {
            if (running != null) return;
            var go = new GameObject("Blackout");
            DontDestroyOnLoad(go);
            running = go.AddComponent<Blackout>();
            running.StartCoroutine(running.Run(Mathf.Max(0.05f, fadeSeconds), Mathf.Max(0.5f, wakeSeconds)));
        }

        IEnumerator Run(float fadeSeconds, float wakeSeconds)
        {
            WaterLensFeature.ScreenActive = true;

            // passing out: darker and darker, a little out of focus
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / fadeSeconds)
            {
                float k = t * t;
                Push(k, 0.5f * k, 0f);
                yield return null;
            }
            Push(1f, 0.5f, 1f);
            yield return new WaitForSecondsRealtime(0.4f);

            yield return LoadActiveScene();
            yield return null;   // let the new scene set itself up behind the black

            // coming to: lids open, the blur clears
            for (float t = 0f; t < wakeSeconds; t += Time.unscaledDeltaTime)
            {
                float k = t / wakeSeconds;
                float fade = 1f - Mathf.Clamp01(t / 0.35f);                        // the lids take over from the black
                float blur = Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, k), 1.3f);   // focus comes back slowly
                Push(fade, blur, Lids.Evaluate(t));
                yield return null;
            }
            Finish();
        }

        static IEnumerator LoadActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            AsyncOperation load;
            if (scene.buildIndex >= 0) load = SceneManager.LoadSceneAsync(scene.buildIndex);
            else
            {
#if UNITY_EDITOR
                // a test scene that isn't in the build settings
                load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
                load = SceneManager.LoadSceneAsync(scene.name);
#endif
            }
            while (load != null && !load.isDone) yield return null;
        }

        static void Push(float fade, float blur, float lids)
        {
            Shader.SetGlobalFloat(FadeId, fade);
            Shader.SetGlobalFloat(BlurId, blur);
            Shader.SetGlobalFloat(LidsId, lids);
        }

        void Finish()
        {
            Push(0f, 0f, 0f);
            WaterLensFeature.ScreenActive = false;
            if (running == this) running = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (running != this) return;
            Push(0f, 0f, 0f);
            WaterLensFeature.ScreenActive = false;
            running = null;
        }
    }
}
