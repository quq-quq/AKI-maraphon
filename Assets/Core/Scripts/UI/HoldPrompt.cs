using UnityEngine;
using UnityEngine.UI;

namespace AKI.UI
{
    /// <summary>
    /// "Hold a key to..." in the top right corner of a canvas: a line of text and, to its right, a thin ring that
    /// fills clockwise while the key is held (<see cref="Fill"/>). Used by the legend's skip and the game reset.
    /// </summary>
    public sealed class HoldPrompt
    {
        const float Ring = 46f, Margin = 48f, Gap = 18f;

        static Sprite ringSprite;

        /// <summary>Show / hide it (0..1).</summary>
        public readonly CanvasGroup Group;
        readonly Image ring;

        public HoldPrompt(Transform canvas, string text, int fontSize, Color colour)
        {
            RectTransform area = Panel("Hold prompt", canvas, Vector2.one, Vector2.one);
            area.pivot = Vector2.one;
            area.sizeDelta = new Vector2(1100f, Ring);
            area.anchoredPosition = new Vector2(-Margin, -Margin);
            Group = area.gameObject.AddComponent<CanvasGroup>();
            Group.alpha = 0f;
            Group.blocksRaycasts = false;
            Group.interactable = false;

            if (ringSprite == null) ringSprite = RingSprite();
            RectTransform track = Panel("Ring track", area, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            track.pivot = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(Ring, Ring);
            Image trackImage = track.gameObject.AddComponent<Image>();
            trackImage.sprite = ringSprite;
            trackImage.color = new Color(colour.r, colour.g, colour.b, 0.25f);
            trackImage.raycastTarget = false;

            ring = Panel("Ring", track, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            ring.sprite = ringSprite;
            ring.color = colour;
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = true;
            ring.fillAmount = 0f;
            ring.raycastTarget = false;

            RectTransform labelRect = Panel("Line", area, Vector2.zero, Vector2.one);
            labelRect.offsetMax = new Vector2(-(Ring + Gap), 0f);
            Text label = labelRect.gameObject.AddComponent<Text>();
            label.font = GameFont.Get();
            label.fontSize = fontSize;
            label.color = colour;
            label.alignment = TextAnchor.MiddleRight;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = text;
            label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -2f);
        }

        /// <summary>How full the ring is (0..1).</summary>
        public float Fill
        {
            get => ring.fillAmount;
            set => ring.fillAmount = Mathf.Clamp01(value);
        }

        static RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        // a thin white ring with smooth edges, filled radially by the Image
        static Sprite RingSprite()
        {
            const int Size = 128;
            const float Outer = 0.5f, Inner = 0.4f;   // share of the size
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            const float Pixel = 1f / Size;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = Vector2.Distance(new Vector2((x + 0.5f) / Size, (y + 0.5f) / Size), new Vector2(0.5f, 0.5f));
                    float a = Mathf.Clamp01((Outer - r) / Pixel) * Mathf.Clamp01((r - Inner) / Pixel);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
