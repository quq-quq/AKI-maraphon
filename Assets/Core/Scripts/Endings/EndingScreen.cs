using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AKI.Endings
{
    /// <summary>
    /// The ending screen: the view goes black, then the subtitles of an <see cref="EndingData"/> fade in on it one
    /// after another. After the last one it stays, or waits for a key / a few seconds and reloads the scene, loads
    /// another one or quits
    /// (<see cref="EndingData.after"/>). Builds its own overlay canvas on top of everything and runs on unscaled time.
    /// When a scene is loaded after it, the black lives through the load and clears over the new scene (the menu).
    /// </summary>
    public class EndingScreen : MonoBehaviour
    {
        const int SortingOrder = 32000;            // above every other canvas
        const float LeaveFadeSeconds = 0.6f;       // the text fades out before the next scene
        const float ArriveFadeSeconds = 1.5f;      // the black clears over the next scene

        static EndingScreen current;

        public static bool IsShowing => current != null;
        /// <summary>The ending on the screen now (null when none is).</summary>
        public static EndingData Current => current != null ? current.ending : null;

        EndingData ending;
        CanvasGroup black;
        Text line;
        CanvasGroup words;
        CanvasGroup hint;

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

            RectTransform background = Panel("Black", transform, Vector2.zero, Vector2.one);
            background.gameObject.AddComponent<Image>().color = Color.black;
            black = background.gameObject.AddComponent<CanvasGroup>();
            black.alpha = 0f;

            Font font = ending.font != null ? ending.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            float side = (1f - ending.textWidth) * 0.5f;
            bool bottom = ending.placement == EndingData.Placement.Bottom;

            line = Label("Subtitle", background, new Vector2(side, bottom ? 0.09f : 0.12f),
                new Vector2(1f - side, bottom ? 0.4f : 0.88f), font, ending.fontSize, ending.textColor);
            line.alignment = bottom ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter;
            line.fontStyle = ending.fontStyle;
            line.lineSpacing = 1.2f;
            words = line.gameObject.AddComponent<CanvasGroup>();
            words.alpha = 0f;

            Color hintColor = ending.textColor;
            hintColor.a *= 0.55f;
            Text hintText = Label("Hint", background, new Vector2(0f, 0.02f), new Vector2(1f, 0.07f), font,
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
            yield return Fade(black, 0f, 1f, ending.fadeToBlackSeconds);
            if (ending.textDelaySeconds > 0f) yield return new WaitForSecondsRealtime(ending.textDelaySeconds);

            EndingData.Subtitle[] subtitles = ending.subtitles ?? new EndingData.Subtitle[0];
            for (int i = 0; i < subtitles.Length; i++)
            {
                line.text = subtitles[i].text;
                yield return Fade(words, 0f, 1f, ending.textFadeSeconds);
                if (subtitles[i].seconds > 0f) yield return new WaitForSecondsRealtime(subtitles[i].seconds);
                if (i < subtitles.Length - 1) yield return Fade(words, 1f, 0f, ending.textFadeSeconds);   // the last line stays
            }
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
            if (current == this) current = null;
        }
    }
}
