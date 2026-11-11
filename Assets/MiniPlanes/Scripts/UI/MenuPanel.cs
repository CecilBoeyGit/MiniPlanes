using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MiniPlanes
{
    /// <summary>
    /// The main menu on a curved, lazily following panel, used by poking. Its pages are: Start (Tutorial
    /// list, Infinite, Hangar and coins), the level intro with its Start button, the result screen, and the
    /// Hangar for buying planes and choosing their paint. Every visible part lives in the MenuPanel prefab
    /// and is linked below, so it can be restyled freely as long as the links and button ids stay.
    /// </summary>
    public class MenuPanel : MonoBehaviour
    {
        [Serializable]
        public class LevelRow
        {
            public PokeButton button;
            public Text number;
            public Text name;
            public Text status;
        }

        [Serializable]
        public class PlaneRow
        {
            public PokeButton button;
            public Text name;
            public Text status;
        }

        [Header("Scene links (set by the scene builder)")]
        public GameArt art;
        public PlaneCatalog catalog;

        [Header("Panel")]
        public LazyFollow follow;
        public PokeCanvas poke;
        public GameObject startPage;
        public GameObject introPage;
        public GameObject resultPage;
        public GameObject hangarPage;

        [Header("Start page")]
        public Text startCoins;
        public PokeScrollArea levelList;
        public LevelRow[] levelRows = new LevelRow[0];
        public PokeButton infiniteButton;
        public Text infiniteLabel;
        public Text infiniteBest;
        public Text infiniteLock;
        public PokeButton hangarButton;

        [Header("Level intro")]
        public Text introNumber;
        public Text introTitle;
        public Text introCaption;
        [Tooltip("Draws {key} icons, such as {fighter}, in the intro text. Added automatically if missing.")]
        public InlineIcons introIcons;
        public Image[] introLives = new Image[0];
        public PokeButton introStart;
        public PokeButton introBack;

        [Header("Result")]
        public Text resultTitle;
        public Text resultDetail;
        public Text resultScore;
        public Text resultCoins;
        [Tooltip("The coins the player has now, shown on the result page.")]
        public Text resultWallet;
        public PokeButton nextButton;
        public Text nextLabel;
        [Tooltip("Infinite's game-over page: carry on for coins. Hidden on other results.")]
        public PokeButton respawnButton;
        public Text respawnLabel;
        [Tooltip("The respawn label when the player can't afford it.")]
        public Color respawnUnaffordableText = new Color(0.47f, 0.47f, 0.47f, 1f);

        [Header("Hangar")]
        public Text hangarCoins;
        public PlaneRow[] planeRows = new PlaneRow[0];
        [Tooltip("Where the spinning 3D preview of the plane floats.")]
        public Transform previewAnchor;
        [Tooltip("Wingspan of the preview model, in metres.")]
        public float previewWingspan = 0.11f;
        public float previewSpinDegPerSecond = 35f;
        public Text planeName;
        public Text planeInfo;
        public PokeButton planeAction;
        public Text planeActionLabel;
        [Tooltip("The paint panel to the right, shown for planes the player owns.")]
        public GameObject palettePanel;
        public PokeButton[] paletteButtons = new PokeButton[0];
        public Text paletteName;

        [Header("Colours")]
        public Color rowNormal = new Color(0.56f, 0.82f, 0.68f, 1f);
        public Color rowLocked = new Color(0.88f, 0.85f, 0.79f, 1f);
        public Color rowViewing = new Color(0.36f, 0.72f, 0.62f, 1f);
        public Color ink = new Color(0.25f, 0.2f, 0.17f, 1f);
        public Color muted = new Color(0.56f, 0.52f, 0.47f, 1f);
        public Color good = new Color(0.16f, 0.44f, 0.41f, 1f);
        public Color bad = new Color(0.8f, 0.36f, 0.28f, 1f);
        [Tooltip("The Buy button when the player can't afford the plane.")]
        public Color actionUnavailable = new Color(0.72f, 0.72f, 0.72f, 1f);
        public Color actionUnavailableText = new Color(0.47f, 0.47f, 0.47f, 1f);

        public event Action<int> LevelChosen;
        public event Action InfiniteChosen;
        public event Action RetryChosen;
        public event Action NextChosen;
        public event Action MenuChosen;
        public event Action StartChosen;
        public event Action BackChosen;
        public event Action RespawnChosen;
        /// <summary>The player changed which plane they fly, or its paint.</summary>
        public event Action PlaneChanged;

        public bool IsOpen => gameObject.activeSelf && poke != null && poke.gameObject.activeSelf;
        public bool IsStartPage => IsOpen && startPage != null && startPage.activeSelf;
        public bool IsIntroPage => IsOpen && introPage != null && introPage.activeSelf;
        public bool IsHangarPage => IsOpen && hangarPage != null && hangarPage.activeSelf;
        public int ViewingPlane => _viewing;

        IReadOnlyList<LevelConfig> _levels;
        int _viewing;
        GameObject _preview;
        readonly Dictionary<string, PokeButton> _byId = new Dictionary<string, PokeButton>();

        void Awake()
        {
            if (poke != null) poke.Clicked += OnClicked;
            if (introCaption != null && introIcons == null)
            {
                introIcons = introCaption.GetComponent<InlineIcons>();
                if (introIcons == null) introIcons = introCaption.gameObject.AddComponent<InlineIcons>();
            }
            if (introIcons != null && (introIcons.icons == null || introIcons.icons.Length == 0) && art != null)
                introIcons.icons = art.captionIcons;
            if (planeAction != null) planeAction.disabledColor = actionUnavailable;
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

        // ---------------------------------------------------------------- pages

        void Open(GameObject page, int inputDelayFrames)
        {
            if (poke != null) poke.gameObject.SetActive(true);
            foreach (var p in new[] { startPage, introPage, resultPage, hangarPage })
                if (p != null) p.SetActive(p == page);
            if (page != hangarPage) ClearPreview();
            if (follow != null)
            {
                follow.enabled = true;
                follow.Snap();
            }
            if (poke != null)
            {
                var curved = poke.GetComponent<CurvedCanvas>();
                if (curved != null) curved.MarkDirty();
                poke.BlockInput(inputDelayFrames);
            }
            IndexButtons();
        }

        public void ShowStart(IReadOnlyList<LevelConfig> levels)
        {
            _levels = levels;
            Open(startPage, 30);
            RefreshStart();
        }

        /// <summary>The page before a tutorial level: its name, what it teaches, lives, and Start.</summary>
        public void ShowIntro(int levelNumber, string title, string caption, int lives)
        {
            Open(introPage, 30);
            if (introNumber != null) introNumber.text = "LEVEL " + levelNumber;
            if (introTitle != null) introTitle.text = title;
            if (introIcons != null) introIcons.SetText(caption);
            else if (introCaption != null) introCaption.text = caption;
            for (int i = 0; i < introLives.Length; i++)
                if (introLives[i] != null) introLives[i].gameObject.SetActive(i < lives);
        }

        public void ShowResult(string title, string detail, string score, bool success, bool canContinue, string next, int coinsEarned = 0)
        {
            Open(resultPage, 50);
            if (resultTitle != null)
            {
                resultTitle.text = title;
                resultTitle.color = success ? good : bad;
            }
            if (resultDetail != null) resultDetail.text = detail;
            if (resultScore != null) resultScore.text = score;
            if (resultCoins != null) resultCoins.text = coinsEarned > 0 ? "+" + coinsEarned + " COINS" : "";
            if (resultWallet != null) resultWallet.text = Progression.Coins.ToString();
            if (nextButton != null) nextButton.gameObject.SetActive(canContinue);
            if (nextLabel != null) nextLabel.text = next;
            if (respawnButton != null) respawnButton.gameObject.SetActive(false);
        }

        /// <summary>Shows the Respawn button on the result page, with its price, greyed out if unaffordable.</summary>
        public void OfferRespawn(int cost, bool affordable)
        {
            if (respawnButton == null) return;
            if (nextButton != null) nextButton.gameObject.SetActive(false);
            respawnButton.gameObject.SetActive(true);
            respawnButton.disabledColor = actionUnavailable;
            respawnButton.Interactable = affordable;
            if (respawnLabel != null)
            {
                respawnLabel.text = "RESPAWN\n" + cost + " COINS";
                respawnLabel.color = affordable ? Color.white : respawnUnaffordableText;
            }
            if (resultPage != null) CurvedCanvas.MarkDirty(resultPage.transform);
        }

        /// <summary>Fades the open page in from clear, for example after the splash screen.</summary>
        public void FadeIn(float seconds)
        {
            if (poke == null) return;
            var group = poke.GetComponent<CanvasGroup>();
            if (group == null) group = poke.gameObject.AddComponent<CanvasGroup>();
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeRoutine(group, seconds));
        }

        /// <summary>How opaque the menu is while fading in: 1 when fully shown.</summary>
        public float Opacity
        {
            get
            {
                var group = poke != null ? poke.GetComponent<CanvasGroup>() : null;
                return group != null ? group.alpha : 1f;
            }
        }

        Coroutine _fade;

        System.Collections.IEnumerator FadeRoutine(CanvasGroup group, float seconds)
        {
            float t = 0f;
            group.alpha = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, seconds));
                group.alpha = k * k * (3f - 2f * k);
                yield return null;
            }
            group.alpha = 1f;
            _fade = null;
        }

        public void ShowHangar()
        {
            Open(hangarPage, 20);
            int selected = catalog != null ? catalog.IndexOf(Progression.SelectedPlane) : 0;
            ViewPlane(selected);
        }

        public void Close()
        {
            ClearPreview();
            if (poke != null) poke.gameObject.SetActive(false);
        }

        public void PlaceInFront()
        {
            if (follow != null) follow.Snap();
        }

        // ---------------------------------------------------------------- tests and lookups

        void IndexButtons()
        {
            _byId.Clear();
            if (poke == null) return;
            foreach (var b in poke.GetComponentsInChildren<PokeButton>(true))
                if (!string.IsNullOrEmpty(b.id)) _byId[b.id] = b;
        }

        /// <summary>World position of a visible button, for automated tests. Null if there is none.</summary>
        public Vector3? ButtonPosition(string id)
        {
            if (_byId.Count == 0) IndexButtons();
            if (!_byId.TryGetValue(id, out var b) || b == null || !b.isActiveAndEnabled) return null;
            return poke.WorldCentre(b);
        }

        public bool IsButtonInteractable(string id)
        {
            if (_byId.Count == 0) IndexButtons();
            return _byId.TryGetValue(id, out var b) && b != null && b.Interactable;
        }

        public float ScrollMax => levelList != null ? levelList.ScrollMax : 0f;

        public void SetScroll(float value)
        {
            if (levelList != null) levelList.SetScroll(value);
        }

        // ---------------------------------------------------------------- start page

        void RefreshStart()
        {
            SetText(startCoins, Progression.Coins.ToString());
            for (int i = 0; i < levelRows.Length; i++)
            {
                var row = levelRows[i];
                if (row == null || row.button == null) continue;
                bool unlocked = Progression.IsLevelUnlocked(i);
                bool passed = Progression.HasPassed(i) || (Progression.UnlockAllOverride && i + 1 < Progression.UnlockedLevels);
                string name = _levels != null && i < _levels.Count && _levels[i] != null ? _levels[i].levelName : "Level " + (i + 1);
                SetText(row.number, (i + 1).ToString());
                SetText(row.name, unlocked ? name : "Level " + (i + 1));
                if (row.name != null) row.name.color = unlocked ? ink : muted;
                SetText(row.status, !unlocked ? "LOCKED" : passed ? "PASSED" : "NEW");
                if (row.status != null) row.status.color = passed && unlocked ? good : muted;
                row.button.Interactable = unlocked;
            }

            bool infinite = Progression.InfiniteUnlocked;
            if (infiniteButton != null) infiniteButton.Interactable = infinite;
            SetText(infiniteLabel, infinite ? "PLAY" : "LOCKED");
            if (infiniteLabel != null) infiniteLabel.color = infinite ? Color.white : muted;
            SetText(infiniteBest, "BEST  " + Progression.HighScore);
            SetText(infiniteLock, infinite ? "Survive wave after wave." : "Pass all six tutorial levels to unlock.");

            if (levelList != null)
            {
                int newest = Mathf.Clamp(Progression.UnlockedLevels - 1, 0, Mathf.Max(0, levelRows.Length - 1));
                float rowStep = levelRows.Length > 1 && levelRows[1].button != null && levelRows[0].button != null
                    ? Mathf.Abs(levelRows[1].button.Rect.anchoredPosition.y - levelRows[0].button.Rect.anchoredPosition.y)
                    : 100f;
                levelList.SetScroll((newest - 1) * rowStep);
            }
        }

        // ---------------------------------------------------------------- hangar

        void ViewPlane(int index)
        {
            if (catalog == null || catalog.Count == 0) return;
            _viewing = Mathf.Clamp(index, 0, catalog.Count - 1);
            var def = catalog.Get(_viewing);
            bool owned = catalog.IsOwned(_viewing);
            bool flying = catalog.IndexOf(Progression.SelectedPlane) == _viewing;

            SetText(hangarCoins, Progression.Coins.ToString());
            for (int i = 0; i < planeRows.Length; i++)
            {
                var row = planeRows[i];
                if (row == null || row.button == null) continue;
                bool exists = i < catalog.Count;
                row.button.gameObject.SetActive(exists);
                if (!exists) continue;
                var p = catalog.Get(i);
                bool rowOwned = catalog.IsOwned(i);
                bool rowFlying = catalog.IndexOf(Progression.SelectedPlane) == i;
                SetText(row.name, p.displayName);
                SetText(row.status, rowFlying ? "FLYING" : rowOwned ? "OWNED" : p.price + " COINS");
                if (row.status != null) row.status.color = rowFlying ? good : rowOwned ? ink : muted;
                row.button.SetNormalColor(i == _viewing ? rowViewing : rowOwned ? rowNormal : rowLocked);
            }

            SetText(planeName, def.displayName);
            SetText(planeInfo, def.description);
            if (planeAction != null)
            {
                bool canAfford = Progression.Coins >= def.price;
                planeAction.Interactable = owned ? !flying : canAfford;
                SetText(planeActionLabel, owned ? (flying ? "FLYING" : "FLY THIS") : canAfford ? "BUY  " + def.price : "NEED " + def.price);
                if (planeActionLabel != null) planeActionLabel.color = planeAction.Interactable ? Color.white : actionUnavailableText;
            }

            if (palettePanel != null) palettePanel.SetActive(owned);
            int palette = Progression.PaletteFor(def.id);
            for (int i = 0; i < paletteButtons.Length; i++)
            {
                var b = paletteButtons[i];
                if (b == null) continue;
                bool exists = def.palettes != null && i < def.palettes.Length;
                b.gameObject.SetActive(exists);
                if (!exists) continue;
                b.SetNormalColor(def.palettes[i].swatch);
                b.pressedColor = def.palettes[i].swatch * 0.8f;
                b.transform.localScale = Vector3.one * (i == palette ? 1.18f : 1f);
            }
            SetText(paletteName, def.palettes != null && palette < def.palettes.Length ? def.palettes[palette].name : "");
            if (palettePanel != null) CurvedCanvas.MarkDirty(palettePanel.transform);

            // Locked planes show in their original paint: only the Buy button greys out.
            ShowPreview(def, owned ? palette : 0);
        }

        void ShowPreview(PlaneDefinition def, int palette)
        {
            ClearPreview();
            if (previewAnchor == null || def == null) return;
            var model = def.model != null ? def.model : art != null ? art.playerPlane : null;
            if (model == null) return;
            var holder = new GameObject("PlanePreview").transform;
            holder.SetParent(previewAnchor, false);
            var material = def.material != null ? def.material : art != null ? art.planeMatte : null;
            ModelUtil.Attach(holder, model, material, previewWingspan, ModelUtil.FitAxis.Width);
            _preview = holder.gameObject;
            if (palette >= 0 && def.palettes != null && palette < def.palettes.Length)
                PlanePaint.Apply(_preview, def.palettes[palette]);
        }

        void ClearPreview()
        {
            if (_preview != null) Destroy(_preview);
            _preview = null;
        }

        void Update()
        {
            if (follow != null && poke != null) follow.Hold = poke.IsPressing;
            if (_preview != null)
                _preview.transform.Rotate(0f, previewSpinDegPerSecond * Time.deltaTime, 0f, Space.Self);
        }

        void PlaneAction()
        {
            var def = catalog.Get(_viewing);
            if (!catalog.IsOwned(_viewing))
            {
                if (!Progression.SpendCoins(def.price)) return;
                Sfx.Play(SoundKind.Purchase);
                Progression.GrantPlane(def.id);
            }
            Progression.SelectedPlane = def.id;
            ViewPlane(_viewing);
            PlaneChanged?.Invoke();
        }

        void ChoosePalette(int index)
        {
            var def = catalog.Get(_viewing);
            if (!catalog.IsOwned(_viewing) || def.palettes == null || index >= def.palettes.Length) return;
            Progression.SetPalette(def.id, index);
            ViewPlane(_viewing);
            PlaneChanged?.Invoke();
        }

        // ---------------------------------------------------------------- clicks

        /// <summary>Acts on a button by id, as if it had been poked. Used by the poke input and by tests.</summary>
        public void Press(string id)
        {
            switch (id)
            {
                case "infinite": InfiniteChosen?.Invoke(); return;
                case "hangar": ShowHangar(); return;
                case "hangar_back": ShowStart(_levels); return;
                case "start": StartChosen?.Invoke(); return;
                case "back": BackChosen?.Invoke(); return;
                case "next": NextChosen?.Invoke(); return;
                case "respawn": RespawnChosen?.Invoke(); return;
                case "retry": RetryChosen?.Invoke(); return;
                case "menu": MenuChosen?.Invoke(); return;
                case "plane_action": PlaneAction(); return;
            }
            if (TryIndex(id, "level", out int level)) LevelChosen?.Invoke(level);
            else if (TryIndex(id, "plane", out int plane)) ViewPlane(plane);
            else if (TryIndex(id, "palette", out int palette)) ChoosePalette(palette);
        }

        void OnClicked(PokeButton button)
        {
            Press(button.id);
        }

        static bool TryIndex(string id, string prefix, out int index)
        {
            index = 0;
            return id != null && id.StartsWith(prefix) && int.TryParse(id.Substring(prefix.Length), out index);
        }

        static void SetText(Text t, string value)
        {
            if (t != null && t.text != value) t.text = value;
        }
    }
}
