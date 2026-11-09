using System;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// The pause menu: Resume, Restart and Main Menu on a curved panel that lazily follows the player's
    /// head, used by poking. The game clock is stopped while it is open, so it follows on real time. Its
    /// look is in the PausePanel prefab; the buttons are found by their ids "resume", "restart", "mainmenu".
    /// </summary>
    public class PausePanel : MonoBehaviour
    {
        public LazyFollow follow;
        public PokeCanvas poke;
        [Tooltip("Frames after opening before a press counts.")]
        public int inputDelayFrames = 18;

        public event Action ResumeChosen;
        public event Action RestartChosen;
        public event Action MainMenuChosen;

        public bool IsOpen => poke != null && poke.gameObject.activeSelf;

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

        public void Open()
        {
            if (poke == null) return;
            poke.gameObject.SetActive(true);
            if (follow != null)
            {
                follow.unscaledTime = true;
                follow.Snap();
            }
            var curved = poke.GetComponent<CurvedCanvas>();
            if (curved != null) curved.MarkDirty();
            poke.BlockInput(inputDelayFrames);
        }

        public void Close()
        {
            if (poke != null) poke.gameObject.SetActive(false);
        }

        /// <summary>World position of a button's centre, for automated tests. Null if there is none.</summary>
        public Vector3? ButtonPosition(string id)
        {
            if (poke == null) return null;
            foreach (var b in poke.GetComponentsInChildren<PokeButton>(false))
                if (b.id == id) return poke.WorldCentre(b);
            return null;
        }

        void LateUpdate()
        {
            if (follow != null && poke != null) follow.Hold = poke.IsPressing;
        }

        void OnClicked(PokeButton button)
        {
            switch (button.id)
            {
                case "resume": ResumeChosen?.Invoke(); break;
                case "restart": RestartChosen?.Invoke(); break;
                case "mainmenu": MainMenuChosen?.Invoke(); break;
            }
        }
    }
}
