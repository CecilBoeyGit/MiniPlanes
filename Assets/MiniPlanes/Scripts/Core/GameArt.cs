using UnityEngine;

namespace MiniPlanes
{
    /// <summary>
    /// One place for every material, model, sprite and font the game uses at runtime. Referencing them
    /// from an asset keeps them and their shaders in the Android build. Any model left empty falls back
    /// to the procedural mesh; any VFX sprite left empty simply does not draw.
    /// </summary>
    [CreateAssetMenu(fileName = "GameArt", menuName = "MiniPlanes/Game Art")]
    public class GameArt : ScriptableObject
    {
        [Header("Materials")]
        [Tooltip("Vertex-coloured models: the balloons and the procedural fallback.")]
        public Material planeMatte;
        [Tooltip("Textured aircraft. Each falls back to Plane Matte if empty.")]
        public Material playerMatte;
        public Material fighterMatte;
        public Material bomberMatte;
        public Material turretMatte;
        [Tooltip("The zeppelin's own textured material.")]
        public Material zeppelinMatte;
        public Material jammingTowerMatte;
        public Material handRock;
        public Material silhouette;
        public Material bubbleRing;
        public Material pathLine;
        public Material comicSprite;
        public Material shieldBubble;
        public Material projectile;
        [Tooltip("The turret's bullets.")]
        public Material turretShot;
        [Tooltip("The zeppelin's bullets.")]
        public Material bossBullet;
        [Tooltip("The streak behind a boosting plane.")]
        public Material boostTrail;
        [Tooltip("The see-through wall of a booster tube.")]
        public Material tubeGlass;
        [Tooltip("Refracting glass for the menu, pause and wrist panels and their buttons. Empty keeps them solid.")]
        public Material uiGlass;
        [Tooltip("The rounded panel shape the glass uses, with the edge distance in red.")]
        public Sprite uiGlassSprite;

        [Header("Models")]
        public GameObject playerPlane;
        public GameObject enemyFighter;
        public GameObject enemyBomber;
        public GameObject balloonShield;
        public GameObject balloonBomb;
        [Tooltip("The gold balloon that fits a turret. Empty uses the shield balloon, repainted gold.")]
        public GameObject balloonTurret;
        [Tooltip("The turret fitted to the plane.")]
        public GameObject turret;
        [Tooltip("The boss airship. Empty builds a simple stand-in from shapes.")]
        public GameObject zeppelin;
        [Tooltip("The jamming tower on the floating islands. Empty builds a simple stand-in from shapes.")]
        public GameObject jammingTower;

        [Header("World UI prefabs (edit these to restyle)")]
        [Tooltip("Floats above the blue shield balloon.")]
        public GameObject shieldIcon;
        [Tooltip("Floats above the red bomb balloon. Used only if the no-touch icon is missing.")]
        public GameObject bombIcon;
        [Tooltip("Above a bomb balloon before it flashes: don't touch it.")]
        public GameObject noTouchIcon;
        [Tooltip("Above a flashing bomb balloon: cup it with both hands. Animated.")]
        public GameObject cupIcon;
        [Tooltip("Floats above the gold turret balloon.")]
        public GameObject turretIcon;
        [Tooltip("Floats above the small heart balloon.")]
        public GameObject lifeIcon;
        [Tooltip("Floats above a jamming tower: a blocked signal.")]
        public GameObject jamIcon;
        [Tooltip("The drag handle on a booster tube.")]
        public GameObject dragCursor;
        [Tooltip("The pinch-and-drag target under each powerup balloon.")]
        public GameObject crosshair;
        [Tooltip("The fuel bar above the plane.")]
        public GameObject fuelGauge;
        [Tooltip("Pictures a tutorial's intro text can show: write {key} in the text, such as {fighter}.")]
        public InlineIcons.Entry[] captionIcons = new InlineIcons.Entry[0];

        [Header("Comic VFX")]
        public Texture2D ringOnset;
        public Texture2D ringPeak;
        public Texture2D ringBreakup;
        public Texture2D crashOnset;
        public Texture2D crashPeak;
        public Texture2D crashBreakup;
        public Texture2D flickOnset;
        public Texture2D flickPeak;
        public Texture2D flickBreakup;
        public Texture2D plus1;
        public Texture2D plus2;

        [Header("Fonts")]
        public Font hudFont;
        public Font headingFont;

        public Material PlayerMaterial => playerMatte != null ? playerMatte : planeMatte;
        public Material FighterMaterial => fighterMatte != null ? fighterMatte : planeMatte;
        public Material BomberMaterial => bomberMatte != null ? bomberMatte : planeMatte;
        public Material TurretMaterial => turretMatte != null ? turretMatte : planeMatte;
        public Material ZeppelinMaterial => zeppelinMatte != null ? zeppelinMatte : planeMatte;
        public Material JammingTowerMaterial => jammingTowerMatte != null ? jammingTowerMatte : planeMatte;
    }

    /// <summary>Attaches a normalised model under a parent. Shared by the plane, enemies and balloons.</summary>
    public static class ModelUtil
    {
        public enum FitAxis { Width, Height, Length }

        static readonly int UseVertexColorId = Shader.PropertyToID("_UseVertexColor");

        /// <summary>Instantiates the prefab centred on the parent and scaled so its fit axis has the given size.</summary>
        public static GameObject Attach(Transform parent, GameObject prefab, Material material, float size, FitAxis axis)
        {
            var go = Object.Instantiate(prefab, parent, false);
            go.name = prefab.name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Bounds bounds = MeasureInParent(go.transform, parent);
            float measured = axis == FitAxis.Width ? bounds.size.x : axis == FitAxis.Height ? bounds.size.y : bounds.size.z;
            float scale = measured > 1e-6f ? size / measured : 1f;
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = -bounds.center * scale;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return go;
        }

        static Bounds MeasureInParent(Transform root, Transform parent)
        {
            bool any = false;
            var result = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                Matrix4x4 m = parent.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 p = m.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        result = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        /// <summary>A primitive shape with a flat colour, for stand-ins and simple props. No collider.</summary>
        public static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale,
            Quaternion localRotation, Material material, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            var r = go.GetComponent<MeshRenderer>();
            if (material != null) r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetFloat(UseVertexColorId, 0f);
            block.SetColor("_BaseColor", color);
            r.SetPropertyBlock(block);
            return go;
        }

        /// <summary>A flat single-mesh fallback when no model asset exists.</summary>
        public static GameObject AttachProcedural(Transform parent, PlaneShapeParams shape, Material material)
        {
            var go = new GameObject("ProceduralModel");
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            filter.sharedMesh = ProceduralPlaneMesh.Build(shape);
            renderer.sharedMaterials = new[] { material, material };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetFloat(UseVertexColorId, 0f);
            block.SetColor("_BaseColor", shape.bodyColor);
            renderer.SetPropertyBlock(block, 0);
            block.SetColor("_BaseColor", shape.wingColor);
            renderer.SetPropertyBlock(block, 1);
            return go;
        }

        /// <summary>A material-only copy of the model's meshes, for the behind-the-hand reveal.</summary>
        public static void AddSilhouette(GameObject model, Material silhouette)
        {
            if (silhouette == null) return;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var copy = new GameObject("Silhouette");
                copy.transform.SetParent(mf.transform, false);
                copy.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = copy.AddComponent<MeshRenderer>();
                var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = silhouette;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        public static Mesh QuadMesh()
        {
            var mesh = new Mesh { name = "CentredQuad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
            });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
