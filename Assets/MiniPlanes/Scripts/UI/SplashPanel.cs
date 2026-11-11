using System;
using System.Collections;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The opening screen: the game's logo fades in on a panel that lazily follows the head, then, a moment
    /// after it is fully shown, a Start button fades in beneath it. Poking Start fades both away, and the game
    /// opens the start menu. Everything visible is in the SplashPanel prefab, so the logo and button can be
    /// swapped or restyled freely.
    /// </summary>
    public class SplashPanel : MonoBehaviour
    {
        public const string StartId = "splash_start";

        public LazyFollow follow;
        public PokeCanvas poke;
        [Tooltip("Fades as one: the logo and anything else that should appear first.")]
        public CanvasGroup logo;
        [Tooltip("Fades in after the logo: the Start button.")]
        public CanvasGroup startButton;

        [Header("Timing, in seconds")]
        public float logoFadeSeconds = 1.4f;
        [Tooltip("Pause between the logo being fully shown and the Start button appearing.")]
        public float buttonDelaySeconds = 1.5f;
        public float buttonFadeSeconds = 0.5f;
        public float fadeOutSeconds = 0.7f;

        /// <summary>Start was poked and the splash has faded away.</summary>
        public event Action StartChosen;

        public bool IsOpen => poke != null && poke.gameObject.activeSelf;
        /// <summary>The logo has fully faded in.</summary>
        public bool LogoShown { get; private set; }
        /// <summary>The Start button is fully shown and can be poked.</summary>
        public bool ButtonShown { get; private set; }
        public bool Leaving { get; private set; }
        /// <summary>When the logo finished fading in, and when the button finished, in game seconds. For tests.</summary>
        public float LogoShownAt { get; private set; }
        public float ButtonShownAt { get; private set; }

        Coroutine _routine;

        void Awake()
        {
            if (poke != null) poke.Clicked += OnClicked;
            Close();
        }

        public void SetHead(Transform head)
        {
            if (follow != null) follow.head = head;
        }

        public void SetHands(HandSource[] hands)
        {
            if (poke != null) poke.hands = hands;
        }

        /// <summary>Opens the splash and starts the reveal: logo first, the Start button after.</summary>
        public void Show()
        {
            if (poke == null) return;
            poke.gameObject.SetActive(true);
            if (follow != null)
            {
                follow.enabled = true;
                follow.Snap();
            }
            var curved = poke.GetComponent<CurvedCanvas>();
            if (curved != null) curved.MarkDirty();
            LogoShown = ButtonShown = Leaving = false;
            SetAlpha(logo, 0f);
            SetAlpha(startButton, 0f);
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Reveal());
        }

        /// <summary>Hides the splash at once, without fading.</summary>
        public void Close()
        {
            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
            LogoShown = ButtonShown = Leaving = false;
            if (poke != null) poke.gameObject.SetActive(false);
        }

        /// <summary>The same as poking Start. Ignored until the button is shown.</summary>
        public void PressStart()
        {
            if (!ButtonShown || Leaving) return;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Leave());
        }

        public Vector3? ButtonPosition()
        {
            if (poke == null) return null;
            foreach (var b in poke.GetComponentsInChildren<PokeButton>(false))
                if (b.id == StartId) return poke.WorldCentre(b);
            return null;
        }

        void LateUpdate()
        {
            if (follow != null && poke != null) follow.Hold = poke.IsPressing;
        }

        void OnClicked(PokeButton button)
        {
            if (button != null && button.id == StartId) PressStart();
        }

        IEnumerator Reveal()
        {
            yield return Fade(logo, 0f, 1f, logoFadeSeconds);
            LogoShown = true;
            LogoShownAt = Time.time;
            float t = 0f;
            while (t < buttonDelaySeconds)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return Fade(startButton, 0f, 1f, buttonFadeSeconds);
            ButtonShown = true;
            ButtonShownAt = Time.time;
            _routine = null;
        }

        IEnumerator Leave()
        {
            Leaving = true;
            float fromLogo = logo != null ? logo.alpha : 1f;
            float fromButton = startButton != null ? startButton.alpha : 1f;
            float t = 0f;
            while (t < fadeOutSeconds)
            {
                t += Time.deltaTime;
                float k = Smooth(Mathf.Clamp01(t / Mathf.Max(0.01f, fadeOutSeconds)));
                SetAlpha(logo, Mathf.Lerp(fromLogo, 0f, k));
                SetAlpha(startButton, Mathf.Lerp(fromButton, 0f, k));
                yield return null;
            }
            Close();
            StartChosen?.Invoke();
        }

        static IEnumerator Fade(CanvasGroup group, float from, float to, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                SetAlpha(group, Mathf.Lerp(from, to, Smooth(Mathf.Clamp01(t / Mathf.Max(0.01f, seconds)))));
                yield return null;
            }
            SetAlpha(group, to);
        }

        static float Smooth(float k) => k * k * (3f - 2f * k);

        static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group != null) group.alpha = alpha;
        }
    }
}
