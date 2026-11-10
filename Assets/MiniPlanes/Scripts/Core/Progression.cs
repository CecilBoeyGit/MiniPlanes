using UnityEngine;

namespace MiniPlanes
{
    /// <summary>Saved progress: level unlocks, the Infinite high score, coins, owned planes and paint.</summary>
    public static class Progression
    {
        /// <summary>Tutorial levels in the list, the zeppelin level included.</summary>
        public const int TutorialLevelCount = 7;
        /// <summary>Levels to pass for Infinite. The zeppelin level after them is optional.</summary>
        public const int LevelsForInfinite = 6;

        const string UnlockedKey = "miniplanes.unlocked";
        const string HighScoreKey = "miniplanes.highscore";
        const string CoinsKey = "miniplanes.coins";
        const string OwnedKeyPrefix = "miniplanes.owned.";
        const string SelectedPlaneKey = "miniplanes.plane";
        const string PaletteKeyPrefix = "miniplanes.palette.";

        /// <summary>Set from GameManager for playtesting: treats every level as unlocked, without saving it.</summary>
        public static bool UnlockAllOverride;
        /// <summary>Set from GameManager for playtesting: treats every plane as owned, without saving it.</summary>
        public static bool AllPlanesOverride;

        /// <summary>Number of tutorial levels the player can open, at least 1.</summary>
        public static int UnlockedLevels
        {
            get
            {
                if (UnlockAllOverride) return TutorialLevelCount;
                return Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 1), 1, TutorialLevelCount);
            }
        }

        /// <summary>Tutorial levels fully passed. Infinite opens when all of them are.</summary>
        public static bool InfiniteUnlocked
        {
            get
            {
                if (UnlockAllOverride) return true;
                return PlayerPrefs.GetInt(UnlockedKey, 1) > LevelsForInfinite;
            }
        }

        public static int HighScore => PlayerPrefs.GetInt(HighScoreKey, 0);

        public static bool IsLevelUnlocked(int zeroBasedLevel)
        {
            return zeroBasedLevel < UnlockedLevels;
        }

        /// <summary>Whether this tutorial level has ever been passed. Ignores the playtest override.</summary>
        public static bool HasPassed(int zeroBasedLevel)
        {
            return PlayerPrefs.GetInt(UnlockedKey, 1) > zeroBasedLevel + 1;
        }

        /// <summary>Records a pass of a tutorial level, unlocking the next level or Infinite.</summary>
        public static void MarkPassed(int zeroBasedLevel)
        {
            int unlock = zeroBasedLevel + 2;
            if (unlock > PlayerPrefs.GetInt(UnlockedKey, 1))
            {
                PlayerPrefs.SetInt(UnlockedKey, unlock);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Returns true when this is a new best.</summary>
        public static bool ReportScore(int score)
        {
            if (score <= HighScore) return false;
            PlayerPrefs.SetInt(HighScoreKey, score);
            PlayerPrefs.Save();
            return true;
        }

        // ---------------------------------------------------------------- coins and planes

        public static int Coins => PlayerPrefs.GetInt(CoinsKey, 0);

        public static void AddCoins(int amount)
        {
            if (amount == 0) return;
            PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, Coins + amount));
            PlayerPrefs.Save();
        }

        /// <summary>Takes coins if there are enough. Returns false and takes nothing otherwise.</summary>
        public static bool SpendCoins(int amount)
        {
            if (amount > Coins) return false;
            AddCoins(-amount);
            return true;
        }

        public static bool OwnsPlane(string id)
        {
            return AllPlanesOverride || PlayerPrefs.GetInt(OwnedKeyPrefix + id, 0) == 1;
        }

        public static void GrantPlane(string id)
        {
            PlayerPrefs.SetInt(OwnedKeyPrefix + id, 1);
            PlayerPrefs.Save();
        }

        /// <summary>The id of the plane the player flies. Empty means the catalog's first plane.</summary>
        public static string SelectedPlane
        {
            get => PlayerPrefs.GetString(SelectedPlaneKey, "");
            set
            {
                PlayerPrefs.SetString(SelectedPlaneKey, value ?? "");
                PlayerPrefs.Save();
            }
        }

        public static int PaletteFor(string planeId)
        {
            return PlayerPrefs.GetInt(PaletteKeyPrefix + planeId, 0);
        }

        public static void SetPalette(string planeId, int palette)
        {
            PlayerPrefs.SetInt(PaletteKeyPrefix + planeId, palette);
            PlayerPrefs.Save();
        }

        /// <summary>Wipes unlocks, the high score, coins, owned planes and paint.</summary>
        public static void ResetAll(PlaneCatalog catalog = null)
        {
            PlayerPrefs.DeleteKey(UnlockedKey);
            PlayerPrefs.DeleteKey(HighScoreKey);
            PlayerPrefs.DeleteKey(CoinsKey);
            PlayerPrefs.DeleteKey(SelectedPlaneKey);
            if (catalog != null)
            {
                foreach (var p in catalog.planes)
                {
                    PlayerPrefs.DeleteKey(OwnedKeyPrefix + p.id);
                    PlayerPrefs.DeleteKey(PaletteKeyPrefix + p.id);
                }
            }
            PlayerPrefs.Save();
        }
    }
}
