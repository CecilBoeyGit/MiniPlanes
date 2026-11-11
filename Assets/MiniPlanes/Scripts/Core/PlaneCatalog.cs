using System;
using UnityEngine;

namespace MiniPlanes
{
    /// <summary>A paint scheme: a hue shift and saturation and brightness scales applied to the plane's colourful parts.</summary>
    [Serializable]
    public class PlanePalette
    {
        public string name = "Original";
        [Tooltip("The colour of this palette's button in the Hangar.")]
        public Color swatch = Color.white;
        [Range(-0.5f, 0.5f), Tooltip("Hue shift, as a fraction of the colour wheel.")]
        public float hueShift;
        [Range(0f, 2f)] public float saturation = 1f;
        [Range(0f, 2f)] public float brightness = 1f;
        [Tooltip("Only repaint colours near this hue (0 to 1). -1 repaints every colourful part.")]
        public float onlyHue = -1f;
    }

    /// <summary>What a plane does on its own, beyond flying. A plane can have several.</summary>
    [Flags]
    public enum PlanePerk
    {
        [Tooltip("Nothing extra.")] None = 0,
        [Tooltip("Fits its own turret when the first enemy plane of each wave shows up.")] WaveTurret = 1,
        [Tooltip("Boosts for a few seconds at the start of each wave.")] WaveBoost = 2,
        [Tooltip("Every turret it fits comes as a pair, one each side of turretMount.")] TwinTurrets = 4
    }

    /// <summary>One plane the player can fly.</summary>
    [Serializable]
    public class PlaneDefinition
    {
        [Tooltip("Saved with the player's progress. Don't change it once players have bought the plane.")]
        public string id = "plane";
        public string displayName = "Plane";
        [TextArea] public string description = "";
        public GameObject model;
        [Tooltip("The plane's own textured material.")]
        public Material material;
        [Tooltip("Coins to unlock. 0 means owned from the start.")]
        public int price;
        public PlanePerk perk = PlanePerk.None;
        public bool Has(PlanePerk p) => (perk & p) != 0;
        [Tooltip("For the WaveBoost perk: seconds of boost at the start of each wave.")]
        public float perkBoostSeconds = 3f;
        [Tooltip("For the WaveBoost perk: speed multiplier while boosting.")]
        public float perkBoostMultiplier = 1.6f;
        [Tooltip("Where the turret sits, in wingspans from the model's centre: x right, y up, z forward. With twin turrets, the second sits mirrored across the centre line.")]
        public Vector3 turretMount = new Vector3(0f, 0.16f, -0.08f);
        [Tooltip("The plane's colour palettes. The first is the original.")]
        public PlanePalette[] palettes = new PlanePalette[0];
    }

    /// <summary>
    /// Every plane the Hangar offers, in order. The first is the default plane. Edit prices, names,
    /// palettes and turret mounts here: Assets/MiniPlanes/Levels/Planes.asset.
    /// </summary>
    [CreateAssetMenu(fileName = "Planes", menuName = "MiniPlanes/Plane Catalog")]
    public class PlaneCatalog : ScriptableObject
    {
        public PlaneDefinition[] planes = new PlaneDefinition[0];
        [HideInInspector, Tooltip("Lets the scene builder add new fields' starter values once, keeping your edits.")]
        public int version;

        public int Count => planes != null ? planes.Length : 0;

        public PlaneDefinition Get(int index)
        {
            if (Count == 0) return null;
            return planes[Mathf.Clamp(index, 0, Count - 1)];
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < Count; i++) if (planes[i].id == id) return i;
            return 0;
        }

        public bool IsOwned(int index)
        {
            var p = Get(index);
            return p != null && (p.price <= 0 || Progression.OwnsPlane(p.id));
        }

        public PlaneDefinition Selected => Get(IndexOf(Progression.SelectedPlane));
    }
}
