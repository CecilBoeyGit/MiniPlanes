using UnityEngine;

namespace MiniPlanes
{
    /// <summary>Applies a <see cref="PlanePalette"/> to a plane model through the Matte shader's recolour inputs.</summary>
    public static class PlanePaint
    {
        static readonly int HueShiftId = Shader.PropertyToID("_HueShift");
        static readonly int SaturationId = Shader.PropertyToID("_SatScale");
        static readonly int BrightnessId = Shader.PropertyToID("_ValScale");
        static readonly int HueSourceId = Shader.PropertyToID("_HueSource");
        static MaterialPropertyBlock _block;

        public static void Apply(GameObject model, PlanePalette palette)
        {
            if (model == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Silhouette") continue;
                r.GetPropertyBlock(_block);
                _block.SetFloat(HueShiftId, palette != null ? palette.hueShift : 0f);
                _block.SetFloat(SaturationId, palette != null ? palette.saturation : 1f);
                _block.SetFloat(BrightnessId, palette != null ? palette.brightness : 1f);
                _block.SetFloat(HueSourceId, palette != null ? palette.onlyHue : -1f);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
