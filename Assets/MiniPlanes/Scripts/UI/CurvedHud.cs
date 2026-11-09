using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// Lazy-follow cockpit HUD on a curved canvas: flight progress, score, level, lives and coins, plus a
    /// caption line for wave numbers and countdowns. Everything visible is in the CockpitHUD prefab and
    /// linked below, so it can be restyled freely.
    /// </summary>
    public class CurvedHud : MonoBehaviour
    {
        [Header("Panel")]
        public LazyFollow follow;
        [Tooltip("The part that shows during a flight. Hidden in menus.")]
        public GameObject strip;

        [Header("Readouts")]
        [Tooltip("A Filled, horizontal Image.")]
        public Image progressFill;
        public Text score;
        public Text level;
        public Text coins;
        [Tooltip("One image per life, left to right.")]
        public Image[] lives = new Image[0];
        public Color lifeLostColor = new Color(1f, 1f, 1f, 0.18f);

        [Header("Caption")]
        public GameObject captionPlate;
        public Text caption;

        public float Progress { get; private set; }
        public string ScoreText => score != null ? score.text : "";
        public string LevelText => level != null ? level.text : "";
        public string CaptionText => caption != null ? caption.text : "";
        public string CoinsText => coins != null ? coins.text : "";
        public int LivesShown { get; private set; }

        Color _lifeColor = Color.white;
        float _captionUntil = float.PositiveInfinity;

        void Awake()
        {
            if (lives.Length > 0 && lives[0] != null) _lifeColor = lives[0].color;
            SetVisible(false);
            SetCaption("");
        }

        public void SetHead(Transform head)
        {
            if (follow != null) follow.head = head;
        }

        public void SetVisible(bool visible)
        {
            if (strip != null && strip.activeSelf != visible)
            {
                strip.SetActive(visible);
                if (visible) Rebend(strip);
            }
            if (visible && follow != null) follow.Snap();
        }

        public void SetProgress(float value)
        {
            Progress = Mathf.Clamp01(value);
            if (progressFill != null && !Mathf.Approximately(progressFill.fillAmount, Progress))
                progressFill.fillAmount = Progress;
        }

        public void SetScore(int value)
        {
            SetText(score, value.ToString());
        }

        public void SetLevelLabel(string label)
        {
            SetText(level, label);
        }

        public void SetCoins(int value)
        {
            SetText(coins, value.ToString());
        }

        /// <summary>Shows how many of the lives are left. Extra life images beyond the total are hidden.</summary>
        public void SetLives(int left, int total)
        {
            LivesShown = left;
            for (int i = 0; i < lives.Length; i++)
            {
                if (lives[i] == null) continue;
                lives[i].gameObject.SetActive(i < total);
                lives[i].color = i < left ? _lifeColor : lifeLostColor;
            }
        }

        /// <summary>Shows a transient line. Zero seconds means until replaced or cleared.</summary>
        public void SetCaption(string text, float seconds = 0f)
        {
            bool show = !string.IsNullOrEmpty(text);
            SetText(caption, text ?? "");
            if (captionPlate != null && captionPlate.activeSelf != show)
            {
                captionPlate.SetActive(show);
                if (show) Rebend(captionPlate);
            }
            _captionUntil = show && seconds > 0f ? Time.time + seconds : float.PositiveInfinity;
        }

        void LateUpdate()
        {
            if (Time.time > _captionUntil) SetCaption("");
        }

        static void Rebend(GameObject part)
        {
            CurvedCanvas.MarkDirty(part.transform);
        }

        static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
        }
    }
}
