using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 풀에서 빌린 HitImpactVFX 인스턴스가 파티클 재생을 마치면
/// 자동으로 풀에 반환하는 컴포넌트.
/// NetworkPlayer.CombatFeel에서 인스턴스 생성 시 자동 추가된다.
/// </summary>
public sealed class HitVFXPoolHandle : MonoBehaviour
{
    internal Stack<GameObject> pool;
    private ParticleSystem _ps;

    private void Awake()
    {
        _ps = GetComponentInChildren<ParticleSystem>(true);
    }

    private void Update()
    {
        if (pool == null)
            return;

        if (_ps == null || !_ps.IsAlive(true))
            ReturnToPool();
    }

    internal void ReturnToPool()
    {
        pool?.Push(gameObject);
        pool = null;
        gameObject.SetActive(false);
    }
}
