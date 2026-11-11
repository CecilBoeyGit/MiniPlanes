using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// Shows small pictures inside a line of text. Write a key in braces, such as "{fighter}", and it is
    /// drawn as that icon, sized to the text and flowing with it. uGUI text can't hold images, so each key
    /// becomes a run of non-breaking spaces and an Image is laid over the gap once the text is laid out.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class InlineIcons : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("Written in the text as {key}.")]
            public string key;
            public Sprite sprite;
        }

        public Entry[] icons = new Entry[0];
        [Tooltip("Icon size as a share of the font size.")]
        public float iconScale = 1.5f;

        const char Gap = ' ';

        /// <summary>Icons placed by the last <see cref="SetText"/>.</summary>
        public int IconCount { get; private set; }
        /// <summary>The placed icons, in order, for tests.</summary>
        public IReadOnlyList<Image> Placed => _placed;

        Text _text;
        readonly List<Image> _pool = new List<Image>();
        readonly List<Image> _placed = new List<Image>();

        Text TextComponent => _text != null ? _text : _text = GetComponent<Text>();

        /// <summary>Sets the text, turning each known {key} into its icon. Unknown keys are left as written.</summary>
        public void SetText(string raw)
        {
            var text = TextComponent;
            raw = raw ?? "";
            var slots = new List<(int start, int count, Sprite sprite)>();
            var sb = new StringBuilder(raw.Length + 16);
            int gaps = GapsPerIcon();
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '{')
                {
                    int end = raw.IndexOf('}', i + 1);
                    var sprite = end > i ? Find(raw.Substring(i + 1, end - i - 1)) : null;
                    if (sprite != null)
                    {
                        slots.Add((sb.Length, gaps, sprite));
                        sb.Append(Gap, gaps);
                        i = end;
                        continue;
                    }
                }
                sb.Append(raw[i]);
            }
            text.text = sb.ToString();
            Place(slots);
        }

        Sprite Find(string key)
        {
            if (icons == null) return null;
            foreach (var e in icons)
                if (e != null && e.sprite != null && string.Equals(e.key, key, StringComparison.OrdinalIgnoreCase)) return e.sprite;
            return null;
        }

        float IconSize => TextComponent.fontSize * iconScale;

        int GapsPerIcon()
        {
            var text = TextComponent;
            float advance = text.fontSize * 0.25f;
            if (text.font != null)
            {
                text.font.RequestCharactersInTexture(Gap.ToString(), text.fontSize, text.fontStyle);
                if (text.font.GetCharacterInfo(Gap, out var info, text.fontSize, text.fontStyle) && info.advance > 0) advance = info.advance;
            }
            return Mathf.Max(1, Mathf.CeilToInt(IconSize * 1.05f / advance));
        }

        void Place(List<(int start, int count, Sprite sprite)> slots)
        {
            var text = TextComponent;
            foreach (var img in _placed) img.gameObject.SetActive(false);
            _placed.Clear();
            IconCount = 0;
            if (slots.Count == 0) return;

            var rt = text.rectTransform;
            var gen = new TextGenerator();
            gen.Populate(text.text, text.GetGenerationSettings(rt.rect.size));
            var chars = gen.characters;
            var lines = gen.lines;
            float ppu = Mathf.Max(0.0001f, text.pixelsPerUnit);
            float size = IconSize;

            for (int s = 0; s < slots.Count; s++)
            {
                var (start, count, sprite) = slots[s];
                int last = start + count - 1;
                if (last >= chars.Count) break;
                var first = chars[start];
                var end = chars[last];
                float x = (first.cursorPos.x + end.cursorPos.x + end.charWidth) * 0.5f / ppu;
                int line = 0;
                for (int l = 0; l < lines.Count; l++)
                    if (lines[l].startCharIdx <= start) line = l;
                float y = (lines.Count > 0 ? lines[line].topY - lines[line].height * 0.5f : first.cursorPos.y) / ppu;

                var img = Take(s);
                img.sprite = sprite;
                img.preserveAspect = true;
                var irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = rt.pivot;
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.sizeDelta = new Vector2(size, size);
                irt.anchoredPosition = new Vector2(x, y);
                img.gameObject.SetActive(true);
                _placed.Add(img);
                IconCount++;
            }
            CurvedCanvas.MarkDirty(transform);
        }

        Image Take(int index)
        {
            while (_pool.Count <= index)
            {
                var go = new GameObject("Icon" + _pool.Count, typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                go.AddComponent<CurvedGraphic>();
                _pool.Add(img);
            }
            return _pool[index];
        }
    }
}
