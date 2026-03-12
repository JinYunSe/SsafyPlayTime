using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public class GhostCube : NetworkBehaviour
    {
        [Header("Settings")]
        [Tooltip("생성 후 자동 파괴될 때까지의 시간 (초)")]
        public float lifeTime = 5f;

        [Networked]
        private TickTimer LifeTimer { get; set; }

        public override void Spawned()
        {
            // StateAuthority(서버/호스트)에서만 타이머 세팅
            if (HasStateAuthority)
            {
                LifeTimer = TickTimer.CreateFromSeconds(Runner, lifeTime);
            }
        }

        public override void FixedUpdateNetwork()
        {
            // 서버/호스트에서 수명이 다하면 제거
            if (HasStateAuthority)
            {
                if (LifeTimer.Expired(Runner))
                {
                    Runner.Despawn(Object);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // 충돌 처리용 로그. 실제 로직에서는 
            // 캐릭터 밀침(Rigidbody에 폭발력 가하기 등)이 포함될 수 있습니다.
            if (HasStateAuthority)
            {
                // Debug.Log($"GhostCube가 {collision.gameObject.name}와(과) 충돌했습니다.");
            }
        }
    }
}
