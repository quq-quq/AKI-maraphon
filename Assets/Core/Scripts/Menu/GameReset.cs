using System.Collections;
using AKI.Endings;
using AKI.Player;
using AKI.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AKI.Menu
{
    /// <summary>
    /// Holding Backspace for a few seconds starts the game over. While it is held a line and a filling ring show in the
    /// top right corner (let go and the ring drains away); when it is full the view and the sound fade out, the scene
    /// loads afresh and the main menu is there as at the start of the game: nothing of this session is remembered (the
    /// legend can't be skipped, he doesn't come to in the boat). Installs itself when the game starts and lives through
    /// scene loads; runs on unscaled time, so it works while the legend holds the game.
    /// </summary>
    public sealed class GameReset : MonoBehaviour
    {
        const int SortingOrder = 32700;       // above every other canvas, the ending screen too
        const float HoldSeconds = 3f;
        const float LeaveFadeSeconds = 0.6f;  // the view and the sound go
        const float ArriveFadeSeconds = 1f;   // the black clears over the menu
        const float BlackOverscan = 200f;     // the black reaches this far past every edge: no gap can show the game
        const string Line = "Перезагрузка игры...";
        static readonly Color Colour = new Color(1f, 0.93f, 0.75f, 1f);   // the colour of the legend's lines
        const int FontSize = 30;

        static GameReset instance;

        /// <summary>Its line is on the screen (Backspace held) or the game is being reset: other prompts in that corner make way.</summary>
        public static bool IsShowing => instance != null && (instance.resetting || instance.prompt.Group.alpha > 0f);

        HoldPrompt prompt;
        CanvasGroup black;
        float held;
        bool resetting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (instance != null) return;
            var go = new GameObject("Game Reset");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameReset>();
            instance.Build();
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var background = (RectTransform)new GameObject("Black", typeof(RectTransform)).transform;
            background.SetParent(transform, false);
            background.anchorMin = Vector2.zero;
            background.anchorMax = Vector2.one;
            background.offsetMin = -Vector2.one * BlackOverscan;
            background.offsetMax = Vector2.one * BlackOverscan;
            Image image = background.gameObject.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            black = background.gameObject.AddComponent<CanvasGroup>();
            black.alpha = 0f;
            black.blocksRaycasts = false;

            prompt = new HoldPrompt(transform, Line, FontSize, Colour);
        }

        void Update()
        {
            if (resetting) return;
            float dt = Time.unscaledDeltaTime;
            bool down = Keyboard.current != null && Keyboard.current.backspaceKey.isPressed;
            held = down ? held + dt : Mathf.Max(0f, held - dt * 2f);
            prompt.Fill = held / HoldSeconds;
            prompt.Group.alpha = Mathf.MoveTowards(prompt.Group.alpha, down || held > 0f ? 1f : 0f, dt / 0.25f);
            if (held >= HoldSeconds) StartCoroutine(Reset());
        }

        IEnumerator Reset()
        {
            resetting = true;
            float volume = AudioListener.volume;
            for (float t = 0f; t < LeaveFadeSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / LeaveFadeSeconds);
                black.alpha = k;
                prompt.Group.alpha = 1f - k;
                AudioListener.volume = volume * (1f - k);
                yield return null;
            }
            black.alpha = 1f;
            prompt.Group.alpha = 0f;
            prompt.Fill = 0f;
            AudioListener.volume = 0f;

            ForgetSession();
            yield return LoadActiveScene();
            yield return null;   // let the new scene set itself up behind the black
            AudioListener.volume = volume;

            for (float t = 0f; t < ArriveFadeSeconds; t += Time.unscaledDeltaTime)
            {
                black.alpha = 1f - Mathf.SmoothStep(0f, 1f, t / ArriveFadeSeconds);
                yield return null;
            }
            black.alpha = 0f;
            held = 0f;
            resetting = false;
        }

        // Everything this run of the game remembers goes, as if it had only just started.
        static void ForgetSession()
        {
            // what can outlive the scene or hold the game: an ending on the screen, passing out, the legend
            EndingScreen.Hide();
            foreach (Blackout blackout in FindObjectsByType<Blackout>(FindObjectsSortMode.None)) Destroy(blackout.gameObject);
            foreach (IntroSequence intro in FindObjectsByType<IntroSequence>(FindObjectsSortMode.None)) Destroy(intro.gameObject);
            Time.timeScale = 1f;
            AudioListener.pause = false;

            IntroSequence.ResetSession();              // the legend can't be skipped until it is seen again
            MainMenuController.RespawnInBoat = false;  // the menu, not coming to in the boat
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

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
