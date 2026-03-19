using UnityEditor;
using UnityEngine;

/// <summary>
/// CameraAndCharacter 프리팹 내 Camera 오브젝트에
/// MapLayerCullingEnforcer를 자동으로 추가합니다.
/// 메뉴: Tools > Setup > Add MapLayerCullingEnforcer to Puppet Prefabs
/// </summary>
public static class MapLayerCullingEnforcerSetup
{
    private static readonly string[] PrefabPaths =
    {
        "Assets/_Project/Prefabs/CameraAndCharacter/AIgCharacterAndCameraAndPuppet.prefab",
        "Assets/_Project/Prefabs/CameraAndCharacter/FitCharacterAndCameraAndPuppet.prefab",
        "Assets/_Project/Prefabs/CameraAndCharacter/SsatyCharacterAndCameraAndPuppet.prefab",
        "Assets/_Project/Prefabs/CameraAndCharacter/WiseCharacterAndCameraAndPuppet.prefab",
    };

    [MenuItem("Tools/Setup/Add MapLayerCullingEnforcer to Puppet Prefabs")]
    public static void AddEnforcerToPrefabs()
    {
        int addedCount = 0;
        int skippedCount = 0;

        foreach (var path in PrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[Setup] 프리팹을 찾을 수 없음: {path}");
                continue;
            }

            using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                var root = scope.prefabContentsRoot;
                var cameras = root.GetComponentsInChildren<Camera>(true);

                if (cameras.Length == 0)
                {
                    Debug.LogWarning($"[Setup] Camera 없음: {path}");
                    continue;
                }

                foreach (var cam in cameras)
                {
                    if (cam.GetComponent<MapLayerCullingEnforcer>() != null)
                    {
                        Debug.Log($"[Setup] 이미 존재, 스킵: {path} / {cam.gameObject.name}");
                        skippedCount++;
                        continue;
                    }

                    cam.gameObject.AddComponent<MapLayerCullingEnforcer>();
                    Debug.Log($"[Setup] 추가 완료: {path} / {cam.gameObject.name}");
                    addedCount++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "MapLayerCullingEnforcer Setup",
            $"완료!\n추가: {addedCount}개\n스킵(이미 존재): {skippedCount}개",
            "OK"
        );
    }
}
