using UnityEngine;

namespace AKI.UI
{
    /// <summary>
    /// The font of every text the game draws itself (ending subtitles, hints): Kurale, a storybook serif with
    /// Cyrillic (SIL Open Font License, see Resources/Fonts/Kurale-OFL.txt). Loaded from Resources, so no scene or
    /// asset has to reference it; Unity's built-in font if it is missing.
    /// </summary>
    public static class GameFont
    {
        const string Path = "Fonts/Kurale-Regular";

        static Font font;

        public static Font Get()
        {
            if (font == null) font = Resources.Load<Font>(Path);
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }
}
