using UnityEngine;

/// <summary>
/// Map 레이어를 카메라 cullingMask에서 항상 포함시켜 Map 오브젝트가
/// 모든 플레이어 화면에 항상 렌더링되도록 강제합니다.
/// Camera GameObject에 추가하세요.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MapLayerCullingEnforcer : MonoBehaviour
{
    [SerializeField] private string mapLayerName = "Map";

    private Camera _cam;
    private int _mapLayerMask;

    private void Awake()
    {
        _cam = GetComponent<Camera>();

        int layer = LayerMask.NameToLayer(mapLayerName);
        if (layer == -1)
        {
            Debug.LogError($"MapLayerCullingEnforcer: '{mapLayerName}' 레이어가 존재하지 않습니다. Project Settings > Tags and Layers에서 추가하세요.");
            enabled = false;
            return;
        }

        _mapLayerMask = 1 << layer;
    }

    private void LateUpdate()
    {
        _cam.cullingMask |= _mapLayerMask;
    }
}
