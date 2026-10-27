using System;
using System.Collections;
using System.Collections.Generic;
using AKI.UI;
using Core.Scripts.Sound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AKI.Menu
{
    /// <summary>
    /// The legend, told in the middle of the grandfather's jump. The view goes black while the camera flies into his
    /// head, the game stops (time and every sound of it) and the legend plays on the black: two pictures that fade in
    /// and out and slowly come closer, the grandfather's voice, a soft ambient and the lines of the voice as subtitles
    /// at the bottom, one sentence at a time. Then the controls show on the black; as their captions go, the game goes
    /// on from the same frame, the black clears and the bare keyboard and mouse stay over the game for a while.
    /// Started by <see cref="MainMenuController"/>. Builds its own overlay canvas and runs on unscaled time.
    /// Pictures and the voice are loaded from Resources/Intro by the names in <see cref="Settings"/>.
    /// Once it has been seen in this run of the game, holding any key skips the rest of it (a ring at the top right
    /// fills while the key is held).
    /// </summary>
    public class IntroSequence : MonoBehaviour
    {
        const int SortingOrder = 31000;     // above the game's canvases, below the ending screen
        const float BlackOverscan = 200f;   // the black reaches this far past every edge: no gap can show the game
        const float FeatherEdge = 0.16f;    // a hanging picture melts into the dark over this share of its size

        [Serializable]
        public struct Cue
        {
            [Tooltip("The sentence, shown while it is spoken.")]
            [TextArea(1, 4)] public string text;
            [Tooltip("Seconds from the start of the voice.")]
            [Min(0f)] public float time;
        }

        [Serializable]
        public struct Slide
        {
            [Tooltip("Picture in Resources (path without extension).")]
            public string image;
            [Tooltip("Seconds from the start of the voice when the picture starts to fade in.")]
            [Min(0f)] public float from;
            [Tooltip("Seconds from the start of the voice when it is gone again. Below 0 = it stays to the end.")]
            public float to;
            [Tooltip("On: the picture fills the whole screen. Off: it hangs in the middle of the black with soft edges.")]
            public bool fill;
            [Tooltip("Share of the screen height the picture takes when it doesn't fill the screen.")]
            [Range(0.2f, 1f)] public float size;
            [Tooltip("Which part of the picture stays when it fills a screen of another shape (0..1 from the left, from the top).")]
            public Vector2 focus;
        }

        [Serializable]
        public class Settings
        {
            [Tooltip("Off = the dive goes on without the legend.")]
            public bool enabled = true;

            [Header("Sound")]
            [Tooltip("The grandfather's voice (Resources path without extension).")]
            public string voice = "Intro/legend_voice";
            [Range(0f, 1f)] public float voiceVolume = 1f;
            [Tooltip("Looped under the voice: the water ambient of the game's sound config.")]
            [Range(0f, 1f)] public float ambientVolume = 0.4f;

            [Header("Pictures and lines")]
            public Slide[] slides =
            {
                new Slide { image = "Intro/legend_1", from = 0f, to = 17.4f, fill = false, size = 0.62f, focus = new Vector2(0.5f, 0.3f) },
                new Slide { image = "Intro/legend_2", from = 17.4f, to = -1f, fill = false, size = 0.62f, focus = new Vector2(0.5f, 0.5f) }
            };
            public Cue[] cues =
            {
                new Cue { time = 0f, text = "Существует легенда о богине южного моря, Ньяи Роро Кидул." },
                new Cue { time = 7f, text = "Она была прекрасной принцессой, но трагически погибла на дне моря." },
                new Cue { time = 13f, text = "Море исцелило её и сделало богиней." },
                new Cue { time = 18f, text = "У Ньяи Роро Кидул есть священные существа в море, которым нельзя причинять вред." }
            };

            [Header("Look")]
            [Tooltip("Seconds a picture takes to fade in and out.")]
            [Min(0f)] public float imageFadeSeconds = 1.2f;
            [Tooltip("How quickly a picture comes closer (share of its size per second).")]
            [Min(0f)] public float zoomPerSecond = 0.008f;
            [Tooltip("Seconds a sentence takes to fade in and out.")]
            [Min(0f)] public float textFadeSeconds = 0.6f;
            [Min(8)] public int fontSize = 44;
            public Color textColour = new Color(1f, 0.93f, 0.75f, 1f);

            [Header("Controls (after the legend: on the black, then over the game)")]
            [Tooltip("Off = the game goes on right after the legend.")]
            public bool showControls = true;
            [Tooltip("Keyboard and mouse with arrows and captions (Resources path without extension).")]
            public string controlsLabelled = "Intro/controls_full";
            [Tooltip("The same drawing without arrows and captions: it stays when they fade away.")]
            public string controlsPlain = "Intro/controls_clean";
            [Tooltip("Share of the screen height the drawing takes.")]
            [Range(0.3f, 1f)] public float controlsHeight = 0.7f;
            [Tooltip("Seconds the drawing takes to fade in and out.")]
            [Min(0f)] public float controlsFadeSeconds = 1.2f;
            [Tooltip("Seconds the arrows and captions stay.")]
            [Min(0f)] public float controlsLabelSeconds = 4f;
            [Tooltip("Seconds the arrows and captions take to go (the black clears and the game goes on meanwhile).")]
            [Min(0f)] public float controlsLabelFadeSeconds = 1f;
            [Tooltip("Seconds the bare keyboard and mouse hang over the game.")]
            [Min(0f)] public float controlsPlainSeconds = 5f;

            [Header("Timing (seconds)")]
            [Tooltip("Silence on the black, before the legend and after it.")]
            [Min(0f)] public float blackSeconds = 0.8f;
            [Tooltip("The last picture stays this long after the voice has ended.")]
            [Min(0f)] public float tailSeconds = 1f;
            [Tooltip("The black clears over the game, which goes on from where it stopped (as the controls' captions go).")]
            [Min(0f)] public float resumeFadeSeconds = 1.5f;

            [Header("Skipping (once the legend has been seen in this run of the game)")]
            [Tooltip("The line at the top right while the legend can be skipped.")]
            public string skipText = "Зажмите любую кнопку, чтобы пропустить...";
            [Tooltip("Seconds a key has to be held to skip the legend and the controls.")]
            [Min(0.1f)] public float skipHoldSeconds = 1.5f;
            [Min(8)] public int skipFontSize = 30;
        }

        static IntroSequence current;
        static bool seen;   // the legend has played through once in this run of the game: it may be skipped now

        /// <summary>Forgets that the legend has been seen (a new run of the game: <see cref="GameReset"/>).</summary>
        // statics outlive play mode in the editor when the domain isn't reloaded
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetSession() => seen = false;

        public static bool IsRunning => current != null;

        /// <summary>The game is stopped for the legend: nothing of it may count time meanwhile.</summary>
        public static bool IsHoldingGame => current != null && current.frozen;

        Settings settings;
        CanvasGroup black;
        CanvasGroup[] slideGroups;
        RectTransform[] slideRects;
        Texture2D[] slideTextures;
        RawImage[] slideImages;
        RectMask2D[] slideMasks;
        Text line;
        CanvasGroup words;
        CanvasGroup controlsPlain;
        CanvasGroup controlsLabelled;
        AudioSource voice;
        AudioSource ambient;
        readonly List<AudioSource> heldSources = new List<AudioSource>();
        readonly List<float> heldVolumes = new List<float>();
        HoldPrompt skip;
        float skipHeld;
        bool skipping;
        float timeScaleBefore = 1f;
        bool frozen;

        /// <summary>
        /// Starts the legend: the view goes black over <paramref name="fadeSeconds"/> (the camera is flying into the
        /// grandfather's head meanwhile), then the game stops for the legend. Ignored while one is running.
        /// </summary>
        public static void Begin(Settings settings, float fadeSeconds)
        {
            if (current != null || settings == null || !settings.enabled) return;
            var go = new GameObject("IntroSequence");
            current = go.AddComponent<IntroSequence>();
            current.settings = settings;
            current.Build();
            current.StartCoroutine(current.Run(Mathf.Max(0.05f, fadeSeconds)));
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

            RectTransform background = Panel("Black", transform, Vector2.zero, Vector2.one);
            background.offsetMin = -Vector2.one * BlackOverscan;
            background.offsetMax = Vector2.one * BlackOverscan;
            background.gameObject.AddComponent<Image>().color = Color.black;
            black = background.gameObject.AddComponent<CanvasGroup>();
            black.alpha = 0f;
            black.blocksRaycasts = false;

            Slide[] slides = settings.slides ?? new Slide[0];
            slideGroups = new CanvasGroup[slides.Length];
            slideRects = new RectTransform[slides.Length];
            slideTextures = new Texture2D[slides.Length];
            slideImages = new RawImage[slides.Length];
            slideMasks = new RectMask2D[slides.Length];
            for (int i = 0; i < slides.Length; i++)
            {
                slideTextures[i] = Resources.Load<Texture2D>(slides[i].image);
                if (slideTextures[i] == null)
                {
                    Debug.LogWarning("IntroSequence: no picture '" + slides[i].image + "' in Resources.", this);
                    continue;
                }
                RectTransform rect = Panel("Slide " + i, transform, Vector2.zero, Vector2.one);
                slideRects[i] = rect;
                // the picture's own alpha falls off at its edges (a soft mask, not black laid over it), so the edges
                // stay just as soft while it fades in and out
                if (!slides[i].fill) slideMasks[i] = rect.gameObject.AddComponent<RectMask2D>();
                var image = Panel("Picture", rect, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                image.texture = slideTextures[i];
                image.raycastTarget = false;
                slideImages[i] = image;
                slideGroups[i] = rect.gameObject.AddComponent<CanvasGroup>();
                slideGroups[i].alpha = 0f;
            }

            // a soft shade under the sentences, so they read on any picture
            RectTransform shade = Panel("Shade", transform, Vector2.zero, new Vector2(1f, 0.4f));
            RawImage shadeImage = shade.gameObject.AddComponent<RawImage>();
            shadeImage.texture = ShadeTexture();
            shadeImage.raycastTarget = false;
            words = shade.gameObject.AddComponent<CanvasGroup>();
            words.alpha = 0f;

            line = Label("Subtitle", shade, new Vector2(0.1f, 0.22f), new Vector2(0.9f, 0.9f), GameFont.Get(),
                settings.fontSize, settings.textColour);
            line.alignment = TextAnchor.LowerCenter;
            line.lineSpacing = 1.15f;
            line.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);

            if (settings.showControls) BuildControls();
            if (seen) BuildSkip();

            voice = Source(false);
            voice.volume = settings.voiceVolume;
            ambient = Source(true);
            ambient.volume = 0f;
        }

        // The drawing sits in the middle of the screen, the plain one under the labelled one: when the labelled one
        // fades away, the bare keyboard and mouse are what is left.
        void BuildControls()
        {
            controlsPlain = ControlsImage("Controls plain", settings.controlsPlain);
            controlsLabelled = ControlsImage("Controls labelled", settings.controlsLabelled);
        }

        // Top right: the line, and a ring beside it that fills while a key is held.
        void BuildSkip() => skip = new HoldPrompt(transform, settings.skipText, settings.skipFontSize, settings.textColour);

        CanvasGroup ControlsImage(string name, string path)
        {
            var texture = Resources.Load<Texture2D>(path);
            if (texture == null)
            {
                Debug.LogWarning("IntroSequence: no picture '" + path + "' in Resources.", this);
                return null;
            }
            float margin = (1f - settings.controlsHeight) * 0.5f;
            RectTransform area = Panel(name, transform, new Vector2(0.1f, margin), new Vector2(0.9f, 1f - margin));
            var image = area.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            var fit = area.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = texture.width / Mathf.Max(1f, (float)texture.height);
            var group = area.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            return group;
        }

        AudioSource Source(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.bypassListenerEffects = true;   // not under water
            source.ignoreListenerPause = true;     // plays while the game's sound is held
            return source;
        }

        static Texture2D ShadeTexture()
        {
            const int Height = 64;
            var texture = new Texture2D(1, Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < Height; y++)
            {
                float up = y / (Height - 1f);   // 0 at the bottom edge
                texture.SetPixel(0, y, new Color(0f, 0f, 0f, 0.9f * Mathf.Pow(1f - up, 1.3f)));
            }
            texture.Apply(false, true);
            return texture;
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

        static Text Label(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Font font, int size, Color colour)
        {
            var text = Panel(name, parent, anchorMin, anchorMax).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = colour;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        // ------------------------------------------------------------------ the sequence

        IEnumerator Run(float fadeSeconds)
        {
            // the view goes black and the game's sound goes with it
            HoldGameSound();
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Ramp(t, fadeSeconds);
                black.alpha = k;
                SetGameSound(1f - k);
                yield return null;
            }
            black.alpha = 1f;
            SetGameSound(0f);

            Freeze();
            yield return new WaitForSecondsRealtime(settings.blackSeconds);
            yield return Legend();
            if (settings.showControls) yield return Controls();   // it lets the game go on halfway through
            else
            {
                yield return FadeAmbient(1f);
                yield return new WaitForSecondsRealtime(settings.blackSeconds);
                // the game goes on from the frame it stopped at
                Unfreeze();
                seen = true;
                yield return Fade(ResumeStep, settings.resumeFadeSeconds);
            }
            Destroy(gameObject);
        }

        // the black clears over the game and its sound comes back: 0 -> 1
        void ResumeStep(float k)
        {
            black.alpha = 1f - k;
            SetGameSound(k);
        }

        IEnumerator Legend()
        {
            AudioClip clip = Resources.Load<AudioClip>(settings.voice);
            AudioClip bed = SoundManager.Instance != null && SoundManager.Instance.Config != null
                && SoundManager.Instance.Config.WaterAmbient != null ? SoundManager.Instance.Config.WaterAmbient.Clip : null;
            Cue[] cues = settings.cues ?? new Cue[0];
            float lastCue = cues.Length > 0 ? cues[cues.Length - 1].time : 0f;
            float voiceEnd = clip != null ? clip.length : lastCue + 6f;
            float total = voiceEnd + settings.tailSeconds;

            if (clip != null)
            {
                voice.clip = clip;
                voice.Play();
            }
            else Debug.LogWarning("IntroSequence: no voice '" + settings.voice + "' in Resources.", this);
            if (bed != null)
            {
                ambient.clip = bed;
                ambient.Play();
            }

            int shown = -1;
            for (float t = 0f; t < total; t += Time.unscaledDeltaTime)
            {
                UpdateSlides(t, total);
                shown = UpdateLine(cues, shown, t, total);
                ambient.volume = settings.ambientVolume * Ramp(t, 2f);
                yield return null;
            }
            for (int i = 0; i < slideGroups.Length; i++)
                if (slideGroups[i] != null) slideGroups[i].alpha = 0f;
            words.alpha = 0f;
            voice.Stop();
        }

        // Keyboard and mouse come out of the dark with their arrows and captions. As those go, so does the black: the
        // game goes on and the bare keyboard and mouse hang over it for a few seconds before they too fade away.
        IEnumerator Controls()
        {
            // the labelled drawing comes first; the plain one takes its place (the keyboard and mouse stay put)
            CanvasGroup first = controlsLabelled != null ? controlsLabelled : controlsPlain;
            yield return Fade(k =>
            {
                if (first != null) first.alpha = k;
            }, settings.controlsFadeSeconds);
            if (settings.controlsLabelSeconds > 0f) yield return new WaitForSecondsRealtime(settings.controlsLabelSeconds);

            // the arrows and captions go and the black goes with them: the game goes on under the bare drawing
            StartCoroutine(FadeAmbient(settings.resumeFadeSeconds));
            Unfreeze();
            seen = true;
            float labelFade = settings.controlsLabelFadeSeconds, resumeFade = settings.resumeFadeSeconds;
            float span = Mathf.Max(labelFade, resumeFade);
            for (float t = 0f; t < span; t += Time.unscaledDeltaTime)
            {
                float k = Ramp(t, labelFade);
                if (first != null) first.alpha = 1f - k;
                if (controlsPlain != null && controlsPlain != first) controlsPlain.alpha = k;
                ResumeStep(Ramp(t, resumeFade));
                yield return null;
            }
            if (first != null && first != controlsPlain) first.alpha = 0f;
            if (controlsPlain != null) controlsPlain.alpha = 1f;
            ResumeStep(1f);
            if (settings.controlsPlainSeconds > 0f) yield return new WaitForSecondsRealtime(settings.controlsPlainSeconds);
            yield return Fade(k =>
            {
                if (controlsPlain != null) controlsPlain.alpha = 1f - k;
            }, settings.controlsFadeSeconds);
        }

        // calls step with 0 -> 1 (eased) over the seconds
        static IEnumerator Fade(Action<float> step, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                step(Ramp(t, seconds));
                yield return null;
            }
            step(1f);
        }

        IEnumerator FadeAmbient(float seconds)
        {
            float from = ambient.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                ambient.volume = from * (1f - Ramp(t, seconds));
                yield return null;
            }
            ambient.Stop();
        }

        void UpdateSlides(float t, float total)
        {
            float fade = settings.imageFadeSeconds;
            for (int i = 0; i < slideGroups.Length; i++)
            {
                if (slideGroups[i] == null) continue;
                Slide slide = settings.slides[i];
                float end = slide.to < 0f ? total : Mathf.Min(slide.to, total);
                slideGroups[i].alpha = Ramp(t - slide.from, fade) * Ramp(end - t, fade);
                float zoom = 1f + settings.zoomPerSecond * Mathf.Max(0f, t - slide.from);
                if (slide.fill) Cover(slideImages[i], slideTextures[i], slide.focus, zoom);
                else
                {
                    Hang(slideRects[i], slideTextures[i], slide.size * zoom);
                    Rect area = slideRects[i].rect;
                    slideMasks[i].softness = new Vector2Int(Mathf.RoundToInt(area.width * FeatherEdge),
                        Mathf.RoundToInt(area.height * FeatherEdge));
                }
            }
        }

        // The picture keeps its shape and hangs a little above the middle, leaving the bottom to the sentences.
        static void Hang(RectTransform rect, Texture texture, float size)
        {
            float screen = Screen.width / Mathf.Max(1f, (float)Screen.height);
            float picture = texture.width / Mathf.Max(1f, (float)texture.height);
            float height = size;
            float width = height * picture / screen;
            const float CentreY = 0.58f;
            rect.anchorMin = new Vector2(0.5f - width * 0.5f, CentreY - height * 0.5f);
            rect.anchorMax = new Vector2(0.5f + width * 0.5f, CentreY + height * 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // the sentence being spoken: in as it starts, out before the next one (or the end) takes its place
        int UpdateLine(Cue[] cues, int shown, float t, float total)
        {
            int index = -1;
            for (int i = 0; i < cues.Length; i++)
                if (cues[i].time <= t) index = i;
            if (index < 0)
            {
                words.alpha = 0f;
                return shown;
            }
            if (index != shown) line.text = cues[index].text;
            float next = index + 1 < cues.Length ? cues[index + 1].time : total;
            words.alpha = Ramp(t - cues[index].time, settings.textFadeSeconds) * Ramp(next - t, settings.textFadeSeconds);
            return index;
        }

        // The picture fills the screen; what doesn't fit of it is cropped around the focus. A zoom above 1 shows less of it.
        static void Cover(RawImage image, Texture texture, Vector2 focus, float zoom)
        {
            float screen = Screen.width / Mathf.Max(1f, (float)Screen.height);
            float picture = texture.width / Mathf.Max(1f, (float)texture.height);
            float width = picture > screen ? screen / picture : 1f;
            float height = picture > screen ? 1f : picture / screen;
            width /= zoom;
            height /= zoom;
            image.uvRect = new Rect((1f - width) * Mathf.Clamp01(focus.x), (1f - height) * (1f - Mathf.Clamp01(focus.y)), width, height);
        }

        // 0 -> 1 over the seconds, eased; a negative time is still 0
        static float Ramp(float since, float seconds)
        {
            if (seconds <= 0f) return since >= 0f ? 1f : 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(since / seconds));
        }

        // ------------------------------------------------------------------ skipping

        // While the game is held (the legend and the controls on the black) a held key fills the ring; let go and
        // it drains. Full: the rest is skipped.
        void Update()
        {
            if (skip == null || skipping) return;
            float dt = Time.unscaledDeltaTime;
            // the reset's line takes the same corner: this one makes way for it
            float shown = frozen && !GameReset.IsShowing ? 1f : 0f;
            skip.Group.alpha = Mathf.MoveTowards(skip.Group.alpha, shown, dt / 0.15f);
            if (!frozen)
            {
                skipHeld = 0f;
                skip.Fill = 0f;
                return;
            }
            skipHeld = AnyKeyHeld() ? skipHeld + dt : Mathf.Max(0f, skipHeld - dt * 2f);
            skip.Fill = skipHeld / settings.skipHoldSeconds;
            if (skipHeld >= settings.skipHoldSeconds) StartSkip();
        }

        // Backspace is the game reset's (GameReset): holding it fills that ring, not this one
        static bool AnyKeyHeld()
        {
            if (Keyboard.current != null && Keyboard.current.backspaceKey.isPressed) return false;
            return (Keyboard.current != null && Keyboard.current.anyKey.isPressed)
                || (Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed
                    || Mouse.current.middleButton.isPressed));
        }

        void StartSkip()
        {
            skipping = true;
            StopAllCoroutines();
            voice.Stop();
            StartCoroutine(SkipOut());
        }

        // everything on the canvas fades away with the black; the game and its sound come back
        IEnumerator SkipOut()
        {
            var groups = new List<CanvasGroup>(slideGroups) { words, controlsPlain, controlsLabelled, skip?.Group };
            groups.RemoveAll(g => g == null);
            var from = new List<float>();
            foreach (CanvasGroup g in groups) from.Add(g.alpha);
            float blackFrom = black.alpha, ambientFrom = ambient.volume;
            Unfreeze();
            yield return Fade(k =>
            {
                for (int i = 0; i < groups.Count; i++) groups[i].alpha = from[i] * (1f - k);
                black.alpha = blackFrom * (1f - k);
                ambient.volume = ambientFrom * (1f - k);
                SetGameSound(k);
            }, settings.resumeFadeSeconds);
            ambient.Stop();
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ holding the game

        // The game stops (time) and so does all of its sound. Sounds that play now fade with the view first.
        void HoldGameSound()
        {
            foreach (AudioSource source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (source == voice || source == ambient || !source.isPlaying) continue;
                heldSources.Add(source);
                heldVolumes.Add(source.volume);
            }
        }

        void SetGameSound(float k)
        {
            for (int i = 0; i < heldSources.Count; i++)
                if (heldSources[i] != null) heldSources[i].volume = heldVolumes[i] * k;
        }

        void Freeze()
        {
            if (frozen) return;
            timeScaleBefore = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            frozen = true;
        }

        void Unfreeze()
        {
            if (!frozen) return;
            Time.timeScale = timeScaleBefore;
            AudioListener.pause = false;
            frozen = false;
        }

        void OnDestroy()
        {
            // never leave the game stopped or silent, whatever ended the sequence
            Unfreeze();
            SetGameSound(1f);
            if (current == this) current = null;
        }
    }
}
