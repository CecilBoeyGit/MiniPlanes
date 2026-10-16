using UnityEngine;

namespace MiniPlanes
{
    /// <summary>Tunables for one difficulty level. Create via Assets > Create > MiniPlanes > Level Config.</summary>
    [CreateAssetMenu(fileName = "Level", menuName = "MiniPlanes/Level Config")]
    public class LevelConfig : ScriptableObject
    {
        [Header("Identity")]
        public string levelName = "Level 1";
        [Tooltip("Points the player must score on this level before advancing.")]
        public int pointsToAdvance = 3;

        [Header("Mini plane")]
        [Tooltip("Flight speed in metres per second.")]
        public float planeSpeed = 0.35f;

        [Header("Flight path (relative to the player's head)")]
        [Tooltip("How many full laps around the player the hidden route makes before leaving.")]
        public float laps = 3f;
        [Tooltip("Angular spacing between waypoints, in degrees. Smaller is smoother, larger gives sharper turns.")]
        public float waypointSpacingDeg = 32f;
        [Tooltip("Chance per waypoint that the plane turns back on itself for a few waypoints, making a loop.")]
        public float reversalChance = 0.05f;
        public float minRadius = 0.38f;
        public float maxRadius = 0.65f;
        [Tooltip("Vertical offset range relative to eye height, in metres.")]
        public float minHeight = -0.35f;
        public float maxHeight = 0.08f;
        [Tooltip("Random azimuth jitter per waypoint, in degrees.")]
        public float azimuthJitterDeg = 8f;

        [Header("Enemies")]
        public int maxEnemies = 0;
        public float enemySpawnInterval = 6f;
        public float enemySpeed = 0.35f;
        public float enemyFireInterval = 2.5f;
        public float projectileSpeed = 0.9f;
        [Tooltip("Enemies open fire only when closer than this to the mini plane, in metres.")]
        public float enemyFireRange = 0.6f;

        /// <summary>Fills in sensible defaults for a 1-based level index.</summary>
        public void ApplyDefaults(int level)
        {
            levelName = "Level " + level;
            pointsToAdvance = 3;
            planeSpeed = 0.35f + 0.06f * (level - 1);
            laps = 3f + 0.5f * (level - 1);
            waypointSpacingDeg = Mathf.Max(32f - 2f * (level - 1), 24f);
            reversalChance = 0.04f + 0.03f * (level - 1);
            minRadius = 0.38f;
            maxRadius = 0.65f;
            minHeight = -0.35f;
            maxHeight = 0.08f;
            azimuthJitterDeg = 6f + 3f * (level - 1);
            maxEnemies = level <= 2 ? 0 : Mathf.Min(level - 2, 3);
            enemySpawnInterval = Mathf.Max(7f - level, 3f);
            enemySpeed = 0.3f + 0.06f * (level - 1);
            enemyFireInterval = Mathf.Max(3.2f - 0.3f * (level - 1), 1.4f);
            projectileSpeed = 0.8f + 0.1f * (level - 1);
            enemyFireRange = 0.6f;
        }
    }
}
