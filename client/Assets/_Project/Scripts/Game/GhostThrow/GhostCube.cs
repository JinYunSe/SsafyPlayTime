using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public class GhostCube : NetworkBehaviour
    {
        [Header("Life Settings")]
        [Tooltip("생성 후 자동 파괴될 때까지의 최대 시간 (초)")]
        public float lifeTime = 10f;

        [Header("Explosion Settings")]
        [Tooltip("폭발 범위 (반지름, 미터)")]
        public float explosionRadius = 5f;
        [Tooltip("넉백 폭발력")]
        public float explosionForce = 20f;
        [Tooltip("위쪽 방향 가중치 (높을수록 더 위로 날아감)")]
        public float upwardModifier = 3f;
        [Tooltip("맵/바닥에 닿은 후 몇 초 뒤에 폭발할지")]
        public float groundExplosionDelay = 2f;
        [Tooltip("폭발 이펙트 프리팹 (추후 연결용)")]
        public GameObject explosionEffectPrefab;

        [Networked]
        private TickTimer LifeTimer { get; set; }

        // 이미 폭발했는지 여부 (중복 폭발 방지)
        private bool _hasExploded = false;

        // 바닥에 닿아 지연 폭발 카운트다운 중인지
        private bool _groundCountdownStarted = false;
        private float _groundCountdownEnd = 0f;

        // ─── Fusion 네트워크 수명 타이머 ───────────────────────
        public override void Spawned()
        {
            if (HasStateAuthority)
                LifeTimer = TickTimer.CreateFromSeconds(Runner, lifeTime);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || _hasExploded) return;

            // 최대 수명 초과 → 폭발
            if (LifeTimer.Expired(Runner))
            {
                TriggerExplode();
                return;
            }

            // 바닥 지연 카운트다운 완료 → 폭발
            if (_groundCountdownStarted && Time.time >= _groundCountdownEnd)
                TriggerExplode();
        }

        // ─── 오프라인 환경: MonoBehaviour Update로 지연 처리 ───
        private void Update()
        {
            if (Runner != null || _hasExploded) return;

            if (_groundCountdownStarted && Time.time >= _groundCountdownEnd)
                ExplodeLocal();
        }

        // ─── 충돌 판정 ────────────────────────────────────────
        private void OnCollisionEnter(Collision collision)
        {
            if (_hasExploded) return;
            if (_groundCountdownStarted) return; // 이미 카운트다운 중이면 무시

            bool hitsPlayer = IsPlayer(collision.gameObject);

            if (Runner == null)
            {
                // 오프라인 처리
                if (hitsPlayer)
                {
                    ExplodeLocal();
                }
                else
                {
                    StartGroundCountdownLocal();
                }
            }
            else
            {
                // 온라인: StateAuthority(서버/호스트)만 처리
                if (!HasStateAuthority) return;

                if (hitsPlayer)
                    TriggerExplode();
                else
                    StartGroundCountdown();
            }
        }

        // ─── 충돌 대상이 플레이어인지 판별 ────────────────────
        /// <summary>
        /// 충돌한 오브젝트(또는 부모)에 NetworkPlayer 컴포넌트가 있으면 플레이어로 판단.
        /// </summary>
        private bool IsPlayer(GameObject obj)
        {
            return obj.GetComponentInParent<NetworkPlayer>() != null;
        }

        // ─── 바닥 지연 카운트다운 ─────────────────────────────
        private void StartGroundCountdown()
        {
            _groundCountdownStarted = true;
            _groundCountdownEnd = Time.time + groundExplosionDelay;
            Debug.Log($"[GhostCube] 바닥 충돌 → {groundExplosionDelay}초 후 폭발 예정");
        }

        private void StartGroundCountdownLocal()
        {
            _groundCountdownStarted = true;
            _groundCountdownEnd = Time.time + groundExplosionDelay;
            Debug.Log($"[GhostCube][Local] 바닥 충돌 → {groundExplosionDelay}초 후 폭발 예정");
        }

        // ─── 폭발 진입점 ──────────────────────────────────────
        /// <summary>온라인(StateAuthority) 폭발.</summary>
        private void TriggerExplode()
        {
            if (_hasExploded) return;
            _hasExploded = true;

            ApplyExplosionKnockback(transform.position);
            SpawnExplosionEffect(transform.position);

            Runner.Despawn(Object);
        }

        /// <summary>오프라인/테스트 환경 폭발.</summary>
        private void ExplodeLocal()
        {
            if (_hasExploded) return;
            _hasExploded = true;

            ApplyExplosionKnockback(transform.position);
            SpawnExplosionEffect(transform.position);

            Destroy(gameObject);
        }

        // ─── 폭발 로직 ────────────────────────────────────────
        /// <summary>범위 내 모든 Rigidbody에 폭발력 + NetworkPlayer HP 데미지 적용.</summary>
        private void ApplyExplosionKnockback(Vector3 explosionPos)
        {
            Collider[] cols = Physics.OverlapSphere(explosionPos, explosionRadius);
            foreach (var col in cols)
            {
                if (col == null) continue;
                if (col.transform.IsChildOf(transform) || col.gameObject == gameObject) continue;

                Rigidbody rb = col.attachedRigidbody;
                if (rb == null)
                    rb = col.GetComponentInParent<Rigidbody>();

                if (rb == null || rb.isKinematic) continue;

                rb.AddExplosionForce(
                    explosionForce,
                    explosionPos,
                    explosionRadius,
                    upwardModifier,
                    ForceMode.VelocityChange
                );

                Debug.Log($"[GhostCube] 넉백: {rb.gameObject.name} (거리: {Vector3.Distance(explosionPos, rb.position):F1}m)");

                // ─── HP 데미지: 거리 감쇠 적용 ───────────────────────
                var np = col.GetComponentInParent<NetworkPlayer>();
                if (np != null)
                {
                    float dist = Vector3.Distance(explosionPos, col.transform.position);
                    float ratio = Mathf.Clamp01(1f - dist / explosionRadius); // 중심=1, 가장자리=0
                    float baseDmg = CombatSettings.Instance != null
                        ? CombatSettings.Instance.ghostBombHpDamage
                        : 30f;
                    float hpDmg = baseDmg * ratio;
                    if (hpDmg > 0.5f)
                    {
                        np.ApplyHpDamage(hpDmg);
                        Debug.Log($"[GhostCube] HP 데미지: {np.name} -{hpDmg:F1} (거리비율={ratio:F2})");
                    }
                }
            }
        }

        /// <summary>폭발 이펙트 생성. 프리팹 연결 시 자동 동작.</summary>
        private void SpawnExplosionEffect(Vector3 pos)
        {
            if (explosionEffectPrefab != null)
            {
                var effect = Instantiate(explosionEffectPrefab, pos, Quaternion.identity);
                var ps = effect.GetComponent<ParticleSystem>();
                float destroyDelay = ps != null
                    ? ps.main.duration + ps.main.startLifetime.constantMax
                    : 3f;
                Destroy(effect, destroyDelay);
            }

            Debug.Log($"[GhostCube] 폭발! 위치: {pos}, 반경: {explosionRadius}m");
        }

        // ─── 에디터 Gizmo ─────────────────────────────────────
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.25f);
            Gizmos.DrawSphere(transform.position, explosionRadius);
            Gizmos.color = new Color(1f, 0.3f, 0f, 1f);
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}
