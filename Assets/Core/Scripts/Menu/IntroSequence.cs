using System;
using System.Collections;
using System.Collections.Generic;
using AKI.UI;
using Core.Scripts.Sound;
using UnityEngine;
using UnityEngine.UI;

namespace AKI.Menu
{
    /// <summary>
    /// The legend, told in the middle of the grandfather's jump. The view goes black while the camera flies into his
    /// head, the game stops (time and every sound of it) and the legend plays on the black: two pictures that fade in
    /// and out and slowly come closer, the grandfather's voice, a soft ambient and the lines of the voice as subtitles
    /// at the bottom, one sentence at a time. Then the game goes on from the same frame and the black clears.
    /// Started by <see cref="MainMenuController"/>. Builds its own overlay canvas and runs on unscaled time.
    /// Pictures and the voice are loaded from Resources/Intro by the names in <see cref="Settings"/>.
    /// </summary>
    public class IntroSequence : MonoBehaviour
    {
        const int SortingOrder = 31000;     // above the game's canvases, below the ending screen
        const float BlackOverscan = 200f;   // the black reaches this far past every edge: no gap can show the game

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
                new Slide { image = "Intro/legend_2", from = 17.4f, to = -1f, fill = true, size = 1f, focus = new Vector2(0.5f, 0.5f) }
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

            [Header("Timing (seconds)")]
            [Tooltip("Silence on the black, before the legend and after it.")]
            [Min(0f)] public float blackSeconds = 0.8f;
            [Tooltip("The last picture stays this long after the voice has ended.")]
            [Min(0f)] public float tailSeconds = 1f;
            [Tooltip("The black clears over the game, which goes on from where it stopped.")]
            [Min(0f)] public float resumeFadeSeconds = 1.5f;
        }

        static IntroSequence current;

        public static bool IsRunning => current != null;

        Settings settings;
        CanvasGroup black;
        CanvasGroup[] slideGroups;
        RectTransform[] slideRects;
        Texture2D[] slideTextures;
        Text line;
        CanvasGroup words;
        AudioSource voice;
        AudioSource ambient;
        readonly List<AudioSource> heldSources = new List<AudioSource>();
        readonly List<float> heldVolumes = new List<float>();
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
                var image = rect.gameObject.AddComponent<RawImage>();
                image.texture = slideTextures[i];
                image.raycastTarget = false;
                if (!slides[i].fill)
                {
                    // black closes in on the picture's edges, so it melts into the dark instead of ending in a frame
                    var feather = Panel("Feather", rect, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                    feather.texture = FeatherTexture();
                    feather.raycastTarget = false;
                }
                slideGroups[i] =rect.gameObject.AddComponent<CanvasGroup>();
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

            voice = Source(false);
            voice.volume = settings.voiceVolume;
            ambient = Source(true);
            ambient.volume = 0f;
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
            yield return FadeAmbient(1f);
            yield return new WaitForSecondsRealtime(settings.blackSeconds);

            // the game goes on from the frame it stopped at
            Unfreeze();
            for (float t = 0f; t < settings.resumeFadeSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Ramp(t, settings.resumeFadeSeconds);
                black.alpha = 1f - k;
                SetGameSound(k);
                yield return null;
            }
            SetGameSound(1f);
            Destroy(gameObject);
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
                if (slide.fill) Cover(slideRects[i].GetComponent<RawImage>(), slideTextures[i], slide.focus, zoom);
                else Hang(slideRects[i], slideTextures[i], slide.size * zoom);
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

        // black at the edges, clear inside: laid over a picture it softens the edges
        static Texture2D FeatherTexture()
        {
            const int Size = 64;
            const float Edge = 0.16f;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float fx = x / (Size - 1f), fy = y / (Size - 1f);
                    float inside = Mathf.Min(Mathf.Min(fx, 1f - fx), Mathf.Min(fy, 1f - fy)) / Edge;
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, 1f - Mathf.SmoothStep(0f, 1f, inside)));
                }
            texture.Apply(false, true);
            return texture;
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
