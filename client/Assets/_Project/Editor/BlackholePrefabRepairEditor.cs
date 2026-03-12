using UnityEditor;
using UnityEngine;

namespace SSAFYPlayTime.EditorTools
{
    /// <summary>
    /// 한국어: 블랙홀 프리팹의 머티리얼 override를 Unity 직렬화 기준으로 다시 저장한다.
    /// </summary>
    public static class BlackholePrefabRepairEditor
    {
        private const string ProjectBlackholePrefabPath = "Assets/_Project/Prefabs/Items/BlackholeBomb.prefab";
        private const string ResourceBlackholePrefabPath = "Assets/Resources/_Project/Prefabs/Items/BlackholeBomb.prefab";
        private const string SourceBlackholePrefabPath = "Assets/Polygon Arsenal/Prefabs/Interactive/BlackHole/Mega/MegaBlackHolePurple.prefab";
        private const string ResourceSourceBlackholePrefabPath = "Assets/Resources/Polygon Arsenal/Prefabs/Interactive/BlackHole/Mega/MegaBlackHolePurple.prefab";

        private const string ShellMaterialPath = "Assets/_Project/Materials/ItemBlackholeShell.mat";
        private const string OuterMaterialPath = "Assets/_Project/Materials/BlackholeFx_Outer_URP.mat";
        private const string GlowMaterialPath = "Assets/_Project/Materials/BlackholeFx_Glow_URP.mat";
        private const string SpriteMaterialPath = "Assets/_Project/Materials/BlackholeFx_Sprite_URP.mat";
        private const string SolidMaterialPath = "Assets/_Project/Materials/BlackholeFx_Solid_URP.mat";

        [MenuItem("Tools/Item/Repair Blackhole Prefab Visuals")]
        public static void RepairBlackholePrefabs()
        {
            var shell = LoadRequiredMaterial(ShellMaterialPath);
            var outer = LoadRequiredMaterial(OuterMaterialPath);
            var glow = LoadRequiredMaterial(GlowMaterialPath);
            var sprite = LoadRequiredMaterial(SpriteMaterialPath);
            var solid = LoadRequiredMaterial(SolidMaterialPath);
            if (shell == null || outer == null || glow == null || sprite == null || solid == null)
            {
                return;
            }

            RepairSourceFxPrefab(SourceBlackholePrefabPath, outer, glow, sprite, solid);
            RepairSourceFxPrefab(ResourceSourceBlackholePrefabPath, outer, glow, sprite, solid);
            RepairItemPrefab(ProjectBlackholePrefabPath, shell, outer, glow, sprite, solid);
            RepairItemPrefab(ResourceBlackholePrefabPath, shell, outer, glow, sprite, solid);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ItemRuntime][Editor] 블랙홀 프리팹 머티리얼 수리 완료");
        }

        private static Material LoadRequiredMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Debug.LogError($"[ItemRuntime][Editor] 블랙홀 머티리얼 누락: {path}");
            }

            return material;
        }

        private static void RepairItemPrefab(string prefabPath, Material shell, Material outer, Material glow, Material sprite, Material solid)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[ItemRuntime][Editor] 블랙홀 프리팹 로드 실패: {prefabPath}");
                return;
            }

            try
            {
                var rootRenderer = root.GetComponent<MeshRenderer>();
                if (rootRenderer != null)
                {
                    rootRenderer.sharedMaterials = new[] { shell };
                }

                var authoring = root.GetComponent<SSAFYPlayTime.Gameplay.Items.ItemBlackholeVisualAuthoring>();
                if (authoring != null)
                {
                    var serializedObject = new SerializedObject(authoring);
                    serializedObject.FindProperty("shellMaterial").objectReferenceValue = shell;
                    serializedObject.FindProperty("outerLayerMaterial").objectReferenceValue = outer;
                    serializedObject.FindProperty("glowMaterial").objectReferenceValue = glow;
                    serializedObject.FindProperty("spriteMaterial").objectReferenceValue = sprite;
                    serializedObject.FindProperty("solidMaterial").objectReferenceValue = solid;
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(authoring);
                }

                ApplyFxBindings(root.transform.Find("Item_BlackholeFx"), outer, glow, sprite, solid);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RepairSourceFxPrefab(string prefabPath, Material outer, Material glow, Material sprite, Material solid)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[ItemRuntime][Editor] 블랙홀 FX 프리팹 로드 실패: {prefabPath}");
                return;
            }

            try
            {
                ApplyFxBindings(root.transform, outer, glow, sprite, solid);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ApplyFxBindings(Transform effectRoot, Material outer, Material glow, Material sprite, Material solid)
        {
            if (effectRoot == null)
            {
                return;
            }

            ApplyRendererMaterials(effectRoot.GetComponent<Renderer>(), new[] { glow });

            foreach (var renderer in effectRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                switch (renderer.gameObject.name)
                {
                    case "OuterLayer":
                        ApplyRendererMaterials(renderer, new[] { outer });
                        break;
                    case "Glow":
                        ApplyRendererMaterials(renderer, new[] { glow });
                        break;
                    case "Particles":
                    case "Circling":
                    case "Trails":
                        ApplyRendererMaterials(renderer, new[] { sprite, solid });
                        break;
                }
            }
        }

        private static void ApplyRendererMaterials(Renderer renderer, Material[] materials)
        {
            if (renderer == null || materials == null || materials.Length == 0)
            {
                return;
            }

            renderer.sharedMaterials = materials;

            if (renderer is ParticleSystemRenderer particleRenderer && materials.Length > 1)
            {
                particleRenderer.trailMaterial = materials[^1];
            }

            EditorUtility.SetDirty(renderer);
        }
    }
}
