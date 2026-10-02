using UnityEngine;

namespace AKI.Endings
{
    /// <summary>
    /// Shows an ending from the scene: hook <see cref="Show"/> (or <see cref="ShowEnding"/> with another ending)
    /// to any UnityEvent, e.g. RhythmGameFlow's On Ended, or tick Show On Start to look at a text right away.
    /// </summary>
    public class EndingTrigger : MonoBehaviour
    {
        [Tooltip("The ending Show() puts on the screen.")]
        public EndingData ending;
        [Tooltip("Show the ending as soon as the scene starts (to check how a text looks).")]
        public bool showOnStart;

        void Start()
        {
            if (showOnStart) Show();
        }

        /// <summary>Shows <see cref="ending"/>.</summary>
        [ContextMenu("Show ending")]
        public void Show()
        {
            if (Application.isPlaying) EndingScreen.Show(ending);
        }

        /// <summary>Shows <paramref name="other"/> instead (one trigger for several events).</summary>
        public void ShowEnding(EndingData other)
        {
            if (Application.isPlaying) EndingScreen.Show(other);
        }
    }
}
