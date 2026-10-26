using System.Collections;
using AKI.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AKI.Rhythm
{
    /// <summary>
    /// The hint that teaches the rhythm: a line at the top of the screen, right under the beat pulse of
    /// <see cref="RhythmBeatFX"/>, once the first beat has sounded. It goes when the player has shot on the beat a few
    /// times or after a while, and is not shown again in this run of the game. Started by
    /// <see cref="RhythmGameFlow"/> when fishing begins; builds its own overlay canvas.
    /// </summary>
    public sealed class ShootHint : MonoBehaviour
    {
        const int SortingOrder = 90;     // above the game's canvases, below the intro and the endings
        const float FadeSeconds = 0.6f;

        static ShootHint current;
        static bool done;                // shown once: not again after passing out or a restart

        RhythmConductor conductor;
        int hitsNeeded;
        float seconds;
        int hits;
        CanvasGroup group;

        /// <summary>Starts the hint unless it is on the screen already or has been shown before.</summary>
        public static void Show(RhythmConductor conductor, string text, int hitsNeeded, float seconds)
        {
            if (done || current != null || conductor == null || string.IsNullOrEmpty(text)) return;
            var go = new GameObject("Shoot Hint");
            current = go.AddComponent<ShootHint>();
            current.conductor = conductor;
            current.hitsNeeded = Mathf.Max(1, hitsNeeded);
            current.seconds = seconds;
            current.Build(text);
            current.StartCoroutine(current.Run());
        }

        void Build(string text)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var label = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
            RectTransform rect = label.rectTransform;
            rect.SetParent(transform, false);
            // the pulse runs along the top ~10% of the screen: the line sits right under it
            rect.anchorMin = new Vector2(0.15f, 0.8f);
            rect.anchorMax = new Vector2(0.85f, 0.89f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            label.font = GameFont.Get();
            label.fontSize = 46;
            label.color = new Color(1f, 0.93f, 0.75f, 0.95f);
            label.alignment = TextAnchor.UpperCenter;
            label.text = text;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        }

        void OnEnable()
        {
            RhythmShotGate.OnBeatShot += OnBeatShot;
            RhythmGameFlow.FishingEnded += Finish;
        }

        void OnDisable()
        {
            RhythmShotGate.OnBeatShot -= OnBeatShot;
            RhythmGameFlow.FishingEnded -= Finish;
        }

        void OnBeatShot() => hits++;

        IEnumerator Run()
        {
            // nothing to shoot to before the first beat of the music
            while (!conductor.IsPlaying || conductor.CurrentBeat < 0) yield return null;
            yield return Fade(0f, 1f);
            float shown = 0f;
            while (hits < hitsNeeded && shown < seconds && conductor.IsPlaying)
            {
                shown += Time.unscaledDeltaTime;
                yield return null;
            }
            done = true;
            yield return Fade(1f, 0f);
            Destroy(gameObject);
        }

        IEnumerator Fade(float from, float to)
        {
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / FadeSeconds));
                yield return null;
            }
            group.alpha = to;
        }

        // the music is over (the golden fish): the hint goes at once
        void Finish()
        {
            done = true;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (current == this) current = null;
        }
    }
}
