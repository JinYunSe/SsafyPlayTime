using UnityEngine;

// 물 트리거 영역. MeshCollider(IsTrigger=true)와 함께 water_001에 부착한다.
// 플레이어가 진입/이탈할 때 NetworkPlayer에 수영 상태를 알린다.
// 수면 Y는 이 오브젝트의 position.y를 기준으로 한다(SeaLevelController가 이동시킴).
public class WaterZone : MonoBehaviour
{
    // 수면으로 판정할 플레이어 Y의 허용 오차 (Inspector에서 덮어쓸 수 있음)
    [SerializeField] private float surfaceThreshold = 0.8f;

    /// <summary>현재 수면 높이 (SeaLevelController가 이 오브젝트를 이동시키므로 항상 최신값)</summary>
    public float SurfaceY => transform.position.y;

    /// <summary>playerY가 수면 근처에 있는지 여부 → 점프 허용 판정에 사용</summary>
    public bool IsNearSurface(float playerY) => playerY >= SurfaceY - surfaceThreshold;

    private void OnTriggerEnter(Collider other)
    {
        var player = other.transform.root.GetComponent<NetworkPlayer>();
        if (player != null)
            player.SetInWater(true, this);
    }

    private void OnTriggerExit(Collider other)
    {
        var player = other.transform.root.GetComponent<NetworkPlayer>();
        if (player != null)
            player.SetInWater(false, null);
    }
}
