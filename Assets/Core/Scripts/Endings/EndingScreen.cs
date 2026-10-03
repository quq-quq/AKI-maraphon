using System.Collections;
using System.Collections.Generic;
using AKI.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AKI.Endings
{
    /// <summary>
    /// The ending screen: the view goes black and every sound of the game fades out with it, then the subtitles of an
    /// <see cref="EndingData"/> fade in on it as one text while their voices play one after another. Then it stays,
    /// or waits for a key / a few seconds and reloads the scene, loads another one or quits
    /// (<see cref="EndingData.after"/>). Builds its own overlay canvas on top of everything and runs on unscaled time.
    /// When a scene is loaded after it, the black lives through the load and clears over the new scene (the menu).
    /// </summary>
    public class EndingScreen : MonoBehaviour
    {
        const int SortingOrder = 32000;            // above every other canvas
        const float LeaveFadeSeconds = 0.6f;       // the text fades out before the next scene
        const float ArriveFadeSeconds = 1.5f;      // the black clears over the next scene
        const float BlackOverscan = 200f;          // the black reaches this far past every edge: no gap can show the game

        static EndingScreen current;

        public static bool IsShowing => current != null;
        /// <summary>The ending on the screen now (null when none is).</summary>
        public static EndingData Current => current != null ? current.ending : null;

        EndingData ending;
        CanvasGroup black;
        Text line;
        CanvasGroup words;
        CanvasGroup hint;
        AudioSource voice;
        bool silencing;
        readonly Dictionary<AudioSource, float> silenced = new Dictionary<AudioSource, float>();

        /// <summary>Shows <paramref name="ending"/>. Ignored while another ending is on the screen.</summary>
        public static void Show(EndingData ending)
        {
            if (ending == null)
            {
                Debug.LogWarning("EndingScreen: no ending to show.");
                return;
            }
            if (current != null) return;
            var go = new GameObject("EndingScreen (" + ending.name + ")");
            current = go.AddComponent<EndingScreen>();
            current.ending = ending;
            current.Build();
            current.StartCoroutine(current.Run());
        }

        /// <summary>Takes the ending screen away at once (the game shows again).</summary>
        public static void Hide()
        {
            if (current != null) Destroy(current.gameObject);
        }

        // ------------------------------------------------------------------ the canvas

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();   // the black takes the clicks, nothing under it reacts

            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            voice.bypassListenerEffects = true;   // the narrator is not under water
            voice.ignoreListenerPause = true;
            voice.volume = ending.voiceVolume;

            RectTransform background = Panel("Black", transform, Vector2.zero, Vector2.one);
            background.offsetMin = -Vector2.one * BlackOverscan;
            background.offsetMax = Vector2.one * BlackOverscan;
            background.gameObject.AddComponent<Image>().color = Color.black;
            black = background.gameObject.AddComponent<CanvasGroup>();
            black.alpha = 0f;

            Font font = ending.font != null ? ending.font : GameFont.Get();
            float side = (1f - ending.textWidth) * 0.5f;
            bool bottom = ending.placement == EndingData.Placement.Bottom;

            // on the canvas, not on the black: the black reaches past the screen edges, the text must not
            line = Label("Subtitle", transform, new Vector2(side, bottom ? 0.09f : 0.12f),
                new Vector2(1f - side, bottom ? 0.4f : 0.88f), font, ending.fontSize, ending.textColor);
            line.alignment = bottom ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter;
            line.fontStyle = ending.fontStyle;
            line.lineSpacing = 1.2f;
            words = line.gameObject.AddComponent<CanvasGroup>();
            words.alpha = 0f;

            Color hintColor = ending.textColor;
            hintColor.a *= 0.55f;
            Text hintText = Label("Hint", transform, new Vector2(0f, 0.02f), new Vector2(1f, 0.07f), font,
                Mathf.Max(8, Mathf.RoundToInt(ending.fontSize * 0.5f)), hintColor);
            hintText.text = ending.continueHint;
            hint = hintText.gameObject.AddComponent<CanvasGroup>();
            hint.alpha = 0f;
        }

        static RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        static Text Label(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Font font, int size, Color color)
        {
            var text = Panel(name, parent, anchorMin, anchorMax).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        // ------------------------------------------------------------------ the sequence

        IEnumerator Run()
        {
            StartCoroutine(Silence(ending.fadeToBlackSeconds));
            yield return Fade(black, 0f, 1f, ending.fadeToBlackSeconds);
            if (ending.textDelaySeconds > 0f) yield return new WaitForSecondsRealtime(ending.textDelaySeconds);

            // all the lines as one text, shown once; their voices play one after another under it
            EndingData.Subtitle[] subtitles = ending.subtitles ?? new EndingData.Subtitle[0];
            var text = new System.Text.StringBuilder();
            foreach (EndingData.Subtitle subtitle in subtitles)
            {
                if (string.IsNullOrEmpty(subtitle.text)) continue;
                if (text.Length > 0) text.Append('\n');
                text.Append(subtitle.text);
            }
            line.text = text.ToString();
            yield return Fade(words, 0f, 1f, ending.textFadeSeconds);
            // the voices start right away, back to back; the lines' seconds add up to the least time the text stays
            float least = 0f, spoken = 0f;
            foreach (EndingData.Subtitle subtitle in subtitles)
            {
                least += subtitle.seconds;
                if (subtitle.voice == null) continue;
                voice.clip = subtitle.voice;
                voice.Play();
                yield return new WaitForSecondsRealtime(subtitle.voice.length);
                spoken += subtitle.voice.length;
            }
            if (least > spoken) yield return new WaitForSecondsRealtime(least - spoken);
            if (ending.after == EndingData.AfterEnding.StayOnScreen) yield break;

            if (ending.waitForKey)
            {
                Coroutine showHint = string.IsNullOrEmpty(ending.continueHint) ? null : StartCoroutine(Fade(hint, 0f, 1f, 0.8f));
                yield return null;   // a key held from the game doesn't count
                while (!AnyKeyPressed()) yield return null;
                if (showHint != null) StopCoroutine(showHint);
            }

            hint.alpha = 0f;
            yield return Fade(words, 1f, 0f, LeaveFadeSeconds);
            yield return Leave();
        }

        // Every sound but the ending's voice fades out with the view and stays silent while the ending is on the
        // screen (sounds that start meanwhile too). Stops when the next scene loads: its sounds start fresh.
        IEnumerator Silence(float seconds)
        {
            silencing = true;
            for (float t = 0f; silencing; t += Time.unscaledDeltaTime)
            {
                float k = seconds > 0f ? 1f - Mathf.SmoothStep(0f, 1f, t / seconds) : 0f;
                foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                {
                    if (source == voice) continue;
                    if (!silenced.TryGetValue(source, out float volume)) silenced[source] = volume = source.volume;
                    source.volume = volume * k;
                }
                yield return null;
            }
        }

        static IEnumerator Fade(CanvasGroup group, float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                yield return null;
            }
            group.alpha = to;
        }

        static bool AnyKeyPressed()
        {
            return (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
        }

        IEnumerator Leave()
        {
            switch (ending.after)
            {
                case EndingData.AfterEnding.ReloadScene:
                    yield return Arrive(Load(SceneManager.GetActiveScene()));
                    break;

                case EndingData.AfterEnding.LoadScene:
                    if (string.IsNullOrEmpty(ending.sceneName))
                    {
                        Debug.LogWarning("EndingScreen: '" + ending.name + "' has no scene to load.", ending);
                        yield break;
                    }
                    yield return Arrive(SceneManager.LoadSceneAsync(ending.sceneName));
                    break;

                case EndingData.AfterEnding.QuitGame:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
            }
        }

        // the next scene starts from scratch behind the black, then the black clears
        IEnumerator Arrive(AsyncOperation load)
        {
            silencing = false;
            DontDestroyOnLoad(gameObject);
            while (load != null && !load.isDone) yield return null;
            yield return null;   // let the new scene set itself up behind the black
            yield return Fade(black, 1f, 0f, ArriveFadeSeconds);
            Destroy(gameObject);
        }

        static AsyncOperation Load(Scene scene)
        {
            if (scene.buildIndex >= 0) return SceneManager.LoadSceneAsync(scene.buildIndex);
#if UNITY_EDITOR
            // a test scene that isn't in the build settings
            return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            return SceneManager.LoadSceneAsync(scene.name);
#endif
        }

        void OnDestroy()
        {
            // sounds that outlived the ending (Hide, or ones kept through the load) get their volume back
            foreach (KeyValuePair<AudioSource, float> pair in silenced)
                if (pair.Key != null) pair.Key.volume = pair.Value;
            if (current == this) current = null;
        }
    }
}
