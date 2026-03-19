using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    /// <summary>
    /// 바나나 껍질 아이템.
    /// - 던져서 바닥에 착지하면 제자리 고정(Kinematic).
    /// - NetworkPlayer가 밟으면 랜덤 방향으로 미끄러져 넘어짐.
    /// - 밟힌 후 또는 수명이 다하면 자동 제거.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BananaPeel : NetworkBehaviour
    {
        [Header("Life Settings")]
        [Tooltip("바닥에 착지한 뒤 사라지기까지의 시간 (초)")]
        public float lifeAfterLanding = 15f;

        [Header("Slip Settings")]
        [Tooltip("미끄러지는 힘의 세기")]
        public float slipForce = 12f;
        [Tooltip("위로 튀어 오르는 힘")]
        public float slipUpForce = 5f;
        [Tooltip("밟힌 후 껍질이 사라지기까지 딜레이 (초)")]
        public float despawnDelay = 0.3f;

        // ─── 내부 상태 ──────────────────────────────────────
        private bool _isLanded   = false;
        private bool _isConsumed = false;
        private float _landedTime = 0f;
        private Rigidbody _rb;
        private Collider _col;

        // ─── 온라인 수명 타이머 ──────────────────────────────
        [Networked] private TickTimer LifeTimer { get; set; }

        /// <summary>
        /// 호스트가 onBeforeSpawned에서 설정 → 클라이언트 Spawned()에서 읽어 Rigidbody에 적용.
        /// NetworkRigidbody 없이도 초기 탄도를 모든 클라이언트에 동기화한다.
        /// </summary>
        [Networked]
        public Vector3 NetworkedInitialVelocity { get; set; }

        private void Awake()
        {
            _rb  = GetComponent<Rigidbody>();
            _col = GetComponent<Collider>();
        }

        public override void Spawned()
        {
            _rb  = GetComponent<Rigidbody>();
            _col = GetComponent<Collider>();
            // 충분히 긴 최대 수명 (착지 후 LifeTimer 재설정)
            if (HasStateAuthority)
                LifeTimer = TickTimer.CreateFromSeconds(Runner, lifeAfterLanding + 30f);

            // 모든 클라이언트: 호스트가 onBeforeSpawned에서 저장한 초기 속도를 적용.
            // NetworkRigidbody 없이 탄도를 동기화하는 핵심 처리.
            if (NetworkedInitialVelocity.sqrMagnitude > 0.001f && _rb != null)
            {
                _rb.drag = 0f;
                _rb.angularDrag = 0.05f;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _rb.velocity = NetworkedInitialVelocity;
            }
        }

        // ─── 오프라인 수명 체크 ──────────────────────────────
        private void Update()
        {
            // 오프라인 전용 (온라인은 FixedUpdateNetwork에서 처리)
            if (Runner != null || _isConsumed) return;

            if (_isLanded && Time.time - _landedTime >= lifeAfterLanding)
                Destroy(gameObject);
        }

        // ─── 온라인 수명 체크 ────────────────────────────────
        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || _isConsumed) return;

            if (_isLanded && LifeTimer.Expired(Runner))
                TriggerDespawn();
        }

        // ─── 충돌 판정 (비행 중) ─────────────────────────────
        private void OnCollisionEnter(Collision collision)
        {
            if (_isConsumed || _isLanded) return;

            // 다른 BananaPeel 위에 착지하려는 경우 → 무시(통과)
            // isTrigger로 변환된 착지 바나나는 이미 물리 충돌이 없으므로
            // 여기에 오는 BananaPeel은 아직 비행 중인 것 → 착지 처리 건너뜀
            if (IsBananaPeel(collision.gameObject)) return;

            if (IsPlayer(collision.gameObject))
                ApplySlip(collision.gameObject); // 비행 중 캐릭터에 직접 맞은 경우
            else
                Land();                          // 바닥/맵 충돌 → 착지 고정
        }

        // ─── Trigger 판정 (착지 후 플레이어 밟기 감지) ────────
        private void OnTriggerEnter(Collider other)
        {
            if (_isConsumed || !_isLanded) return;

            // 다른 바나나는 무시 (통과)
            if (IsBananaPeel(other.gameObject)) return;

            if (IsPlayer(other.gameObject))
                ApplySlip(other.gameObject);
        }

        // ─── 착지 처리 ──────────────────────────────────────
        private void Land()
        {
            _isLanded   = true;
            _landedTime = Time.time;

            if (_rb != null)
            {
                _rb.velocity        = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic     = true;
            }

            // Collider를 Trigger로 전환 → 이 위에 던진 바나나가 통과해 진짜 바닥에 착지
            if (_col != null)
                _col.isTrigger = true;

            // 온라인: 착지 시점부터 수명 타이머 재설정
            if (Runner != null && HasStateAuthority)
                LifeTimer = TickTimer.CreateFromSeconds(Runner, lifeAfterLanding);

            Debug.Log("[BananaPeel] 착지 완료 (Trigger 전환). 플레이어를 기다리는 중...");
        }

        // ─── 슬립(미끄러짐) 적용 ────────────────────────────
        private void ApplySlip(GameObject playerObj)
        {
            if (_isConsumed) return;

            // 온라인: StateAuthority(서버/호스트)만 처리
            if (Runner != null && !HasStateAuthority) return;

            var networkPlayer = playerObj.GetComponentInParent<NetworkPlayer>();
            if (networkPlayer != null && (networkPlayer.IsStunned || networkPlayer.IsRecovering))
            {
                var skippedRb = networkPlayer.transform.root.GetComponent<Rigidbody>();
                var velocity = skippedRb != null ? skippedRb.velocity : Vector3.zero;
                networkPlayer.TraceStunForceEvent(
                    "BananaPeel",
                    skippedRb,
                    Vector3.zero,
                    ForceMode.VelocityChange,
                    velocity,
                    velocity,
                    false,
                    "skipped=stunned_or_recovering");
                return;
            }

            _isConsumed = true;

            Rigidbody playerRb = networkPlayer != null
                ? networkPlayer.transform.root.GetComponent<Rigidbody>()
                : playerObj.GetComponentInParent<Rigidbody>();
            if (playerRb != null && !playerRb.isKinematic)
            {
                Vector3 randomHorizontal = new Vector3(
                    Random.Range(-1f, 1f),
                    0f,
                    Random.Range(-1f, 1f)
                ).normalized;

                Vector3 slipVelocity = randomHorizontal * slipForce + Vector3.up * slipUpForce;
                var velocityBefore = playerRb.velocity;
                playerRb.AddForce(slipVelocity, ForceMode.VelocityChange);
                networkPlayer?.TraceStunForceEvent(
                    "BananaPeel",
                    playerRb,
                    slipVelocity,
                    ForceMode.VelocityChange,
                    velocityBefore,
                    playerRb.velocity,
                    true,
                    "applied");

                Debug.Log($"[BananaPeel] {playerRb.gameObject.name} 이(가) 바나나를 밟아 미끄러졌습니다!");
            }

            // ─── HP 데미지 ────────────────────────────────────────
            var np = playerObj.GetComponentInParent<NetworkPlayer>();
            if (np != null)
            {
                float dmg = CombatSettings.Instance != null
                    ? CombatSettings.Instance.bananaHpDamage
                    : 10f;
                np.ApplyHealthDamage(dmg);
                Debug.Log($"[BananaPeel] HP 데미지: {np.name} -{dmg}");
            }

            if (Runner != null)
                Invoke(nameof(TriggerDespawn), despawnDelay);
            else
                Invoke(nameof(DestroySelf), despawnDelay);
        }

        // ─── 제거 처리 ──────────────────────────────────────
        private void TriggerDespawn()
        {
            if (Runner == null || !Object.IsValid) return;
            Runner.Despawn(Object);
        }

        private void DestroySelf() => Destroy(gameObject);

        // ─── 헬퍼 ───────────────────────────────────────────
        private bool IsPlayer(GameObject obj)
            => obj.GetComponentInParent<NetworkPlayer>() != null;

        private bool IsBananaPeel(GameObject obj)
            => obj.GetComponentInParent<BananaPeel>() != null;

        // ─── 에디터 Gizmo ────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0f, 0.4f);
            Gizmos.DrawSphere(transform.position, 0.6f);
            Gizmos.color = new Color(1f, 0.9f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, 0.6f);
        }
    }
}
