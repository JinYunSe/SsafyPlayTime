using UnityEngine;

/// <summary>
/// 전역 성능 매니저: LOD 업데이트, 거리 기반 Puppet 비활성화를 주기적으로 수행한다.
/// 씬에 하나만 존재해야 한다.
/// </summary>
public class PuppetPerformanceManager : MonoBehaviour
{
    [SerializeField] float lodCheckInterval = 0.5f;

    float _nextCheck;

    void Update()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + lodCheckInterval;

        Camera cam = Camera.main;
        if (cam != null)
            PuppetMasterLOD.UpdateAll(cam);
    }
}
