using System;
using UnityEngine;

namespace AKI.Endings
{
    /// <summary>
    /// One ending of the game: a black screen with subtitles on it. Create one per ending
    /// (Create > AKI > Ending), write the lines and show it with <see cref="EndingScreen.Show"/>
    /// or from a UnityEvent through <see cref="EndingTrigger"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Ending_New", menuName = "AKI/Ending")]
    public class EndingData : ScriptableObject
    {
        public enum AfterEnding { StayOnScreen, ReloadScene, LoadScene, QuitGame }
        public enum Placement { Bottom, Center }

        [Serializable]
        public struct Subtitle
        {
            [Tooltip("The line. Several rows are fine; rich text tags (<b>, <i>, <color>) work too.")]
            [TextArea(2, 6)] public string text;
            [Tooltip("Seconds the line stays after fading in. For the last line: before a key is accepted (or before going on by itself).")]
            [Min(0f)] public float seconds;
        }

        [Header("Subtitles")]
        [Tooltip("Shown one after another; the last one stays on the screen.")]
        public Subtitle[] subtitles = { new Subtitle { text = "Конец.", seconds = 4f } };
        public Placement placement = Placement.Bottom;
        [Min(8)] public int fontSize = 40;
        public Color textColor = new Color(1f, 0.85f, 0.1f, 1f);
        public FontStyle fontStyle = FontStyle.Normal;
        [Tooltip("Empty = Unity's built-in font.")]
        public Font font;
        [Tooltip("Width of the subtitles, part of the screen width (0..1).")]
        [Range(0.2f, 1f)] public float textWidth = 0.8f;

        [Header("Timing (seconds, unscaled)")]
        [Tooltip("How long the screen takes to go black. 0 = it is black at once (e.g. the view was already cut to black).")]
        [Min(0f)] public float fadeToBlackSeconds = 3f;
        [Tooltip("Pause on the empty black screen before the first line.")]
        [Min(0f)] public float textDelaySeconds = 1f;
        [Tooltip("How long each line takes to fade in and out.")]
        [Min(0f)] public float textFadeSeconds = 0.6f;

        [Header("After the subtitles")]
        [Tooltip("What happens once the last line has been read.")]
        public AfterEnding after = AfterEnding.StayOnScreen;
        [Tooltip("Scene to load for Load Scene (it must be in the build settings).")]
        public string sceneName;
        [Tooltip("On: waits for any key or click. Off: goes on by itself after the last line's seconds.")]
        public bool waitForKey = true;
        [Tooltip("Small hint at the bottom once a key is accepted. Empty = no hint.")]
        public string continueHint = "Нажмите любую клавишу";
    }
}
