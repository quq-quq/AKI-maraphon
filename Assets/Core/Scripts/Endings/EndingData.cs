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
            [Tooltip("All lines show together as one text. The lines' seconds add up to the least time it stays before a key is accepted (or before going on by itself); it always stays until the voices are over.")]
            [Min(0f)] public float seconds;
            [Tooltip("Voice of this line. The voices of all lines play one after another while the text is on the screen.")]
            public AudioClip voice;
        }

        [Header("Subtitles")]
        [Tooltip("Shown together as one text, one line under another.")]
        public Subtitle[] subtitles = { new Subtitle { text = "Конец.", seconds = 4f } };
        [Tooltip("Loudness of the lines' voices. They are not muffled under water.")]
        [Range(0f, 1f)] public float voiceVolume = 1f;
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
        [Tooltip("What happens once the last line has been read. Reload Scene = the game starts over from the menu.")]
        public AfterEnding after = AfterEnding.ReloadScene;
        [Tooltip("Scene to load for Load Scene (it must be in the build settings).")]
        public string sceneName;
        [Tooltip("On: waits for any key or click. Off: goes on by itself after the last line's seconds.")]
        public bool waitForKey = true;
        [Tooltip("Small hint at the bottom once a key is accepted. Empty = no hint.")]
        public string continueHint = "Нажмите любую клавишу";
    }
}
