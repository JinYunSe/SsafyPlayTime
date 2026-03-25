using System.Collections;
using System.Collections.Generic;

using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Stage
{
    /// <summary>
    /// obstacle_4_002(해머 헤드)에 부착한다.
    /// IsTrigger = true 인 SphereCollider(또는 임의 Collider)가 같은 오브젝트에 있어야 한다.
    ///
    /// [Unity 에디터 설정]
    /// 1) obstacle_4_002 선택 → Add Component → SphereCollider
    /// 2) SphereCollider의 Is Trigger = true, Radius를 해머 머리 크기에 맞게 조절
    /// 3) 같은 오브젝트에 이 스크립트 추가
    /// </summary>
    public class HammerKnockback : NetworkBehaviour
    {
        [Header("Force")]
        [SerializeField] private float knockbackForce = 28f;
        [SerializeField, Range(0f, 1f)] private float verticalLift = 0.35f;
        [SerializeField, Range(0f, 1f)] private float stunnedKnockbackScale = 0.35f;
        [SerializeField, Range(0f, 1f)] private float recoveringKnockbackScale = 0.45f;

        [Header("Stun")]
        [Tooltip("스턴 대미지. knockout threshold(30)를 넘어야 래그돌로 전환되어 groundStickForce가 해제된다.")]
        [SerializeField] private float stunDamage = 35f;
        [SerializeField] private float healthDamage = 0f;
        [Tooltip("스턴이 적용되기 전까지의 대기 시간 (캐릭터가 clamp 없이 날아가는 시간")]
        [SerializeField] private float stunDelay = 0.15f;

        [Header("Tumble")]
        // [SerializeField, Range(0f, 0.3f)] private float yawTorqueScale = 0.1f;
        [SerializeField, Range(0f, 0.3f)] private float yawTorqueScale = 0f;

        [Header("Detection")]
        [Tooltip("한 번 맞은 뒤 재발동 방지 쿨타임(초)")]
        [SerializeField] private float hitCooldown = 0.8f;

        private Vector3 _prevPosition;
        private Vector3 _velocity;
        private readonly Dictionary<NetworkPlayer, float> _cooldowns = new();

        public override void Spawned()
        {
            _prevPosition = transform.position;
        }

        /*private void Update()
        {
            if (Time.deltaTime > 0f)
                _velocity = (transform.position - _prevPosition) / Time.deltaTime;
            _prevPosition = transform.position;
        }*/

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            _velocity = (transform.position - _prevPosition) / Runner.DeltaTime;
            _prevPosition = transform.position;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!HasStateAuthority)
                return;

            TryHit(other);
        }

        // 플레이어가 트리거 안에 이미 있는 상태에서 해머가 스윙해도 감지
        private void OnTriggerStay(Collider other)
        {
            if (!HasStateAuthority) return;
            TryHit(other);
        }

        private void TryHit(Collider other)
        {
            // 1. Fusion의 NetworkObject 컴포넌트를 통해 권한 체크
            var networkObject = GetComponentInParent<NetworkObject>();
            if (networkObject != null && !networkObject.HasStateAuthority)
                return;

            var networkPlayer = other.transform.root.GetComponent<NetworkPlayer>();
            var playerRb = networkPlayer != null
                ? other.transform.root.GetComponent<Rigidbody>()
                : other.attachedRigidbody != null
                    ? other.attachedRigidbody
                    : other.GetComponent<Rigidbody>();

            if (playerRb == null)
                return;

            // 쿨타임 체크
            if (networkPlayer != null)
            {
                if (_cooldowns.TryGetValue(networkPlayer, out var nextAllowed) && Runner.SimulationTime < nextAllowed)
                    return;
                _cooldowns[networkPlayer] = Runner.SimulationTime + hitCooldown;
            }

            ApplyKnockback(networkPlayer, playerRb, other.transform.root.position);
        }

        private void ApplyKnockback(NetworkPlayer networkPlayer, Rigidbody playerRb, Vector3 playerRootPos)
        {
            // 해머 이동 방향 → 팅겨나갈 방향
            var planarVelocity = Vector3.ProjectOnPlane(_velocity, Vector3.up);
            Vector3 pushDir;
            if (planarVelocity.sqrMagnitude > 0.01f)
            {
                pushDir = (planarVelocity.normalized + Vector3.up * verticalLift).normalized;
            }
            else
            {
                // 해머가 거의 정지 상태면 플레이어 바깥 방향으로
                var away = playerRootPos - transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = transform.forward;
                pushDir = (away.normalized + Vector3.up * verticalLift).normalized;
            }

            var forceScale = (networkPlayer != null && networkPlayer.IsRecovering)
                ? recoveringKnockbackScale
                : 1f;

            StartCoroutine(DelayedStunRoutine(networkPlayer, healthDamage, stunDamage));

            // 1) 래그돌 전환
            // networkPlayer?.ApplyCombinedDamage(healthDamage, stunDamage, "HammerKnockback");
            networkPlayer?.ApplyCombinedDamage(healthDamage, stunDamage, "HammerKnockback", 0f, 0f, 1f, deferStunEntryDamping: true, null);

            // 이동
            if (networkPlayer != null)
            {
                playerRb.transform.position += Vector3.up * 1.0f + pushDir * 0.1f;
            }

            var appliedForce = pushDir * knockbackForce * forceScale;
            var velocityBefore = playerRb.velocity;

            // 2) velocity 직접 지정 → 해머 진행 방향으로 즉각 날아감
            //    AddForce 대신 직접 설정하면 groundStickForce·물리 틱 타이밍 문제와 무관하게 확실히 동작
            playerRb.velocity = pushDir * knockbackForce * forceScale;

            // 3) yaw 토크 → 스핀 (몸이 비틀리는 느낌)
            if (yawTorqueScale > 0f)
            {
                var yawSign = Mathf.Sign(Vector3.Cross(playerRb.transform.forward, pushDir).y);
                playerRb.AddTorque(
                    Vector3.up * yawSign * knockbackForce * yawTorqueScale * forceScale,
                    ForceMode.Impulse);
            }

            networkPlayer?.TraceStunForceEvent(
                "HammerKnockback",
                playerRb,
                playerRb.velocity,
                ForceMode.VelocityChange,
                velocityBefore,
                playerRb.velocity,
                true,
                $"hammerSpeed={_velocity.magnitude:F2} forceScale={forceScale:F2}");
        }

        /// <summary>
        /// 지정된 시간만큼 대기한 후 스턴 및 대미지를 적용합니다.
        /// </summary>
        private IEnumerator DelayedStunRoutine(NetworkPlayer player, float hDamage, float sDamage)
        {
            if (player == null)
                yield break;

            // 0.15초 동안 대기 (이 기간 동안 플레이어는 정상 상태라 강한 속도를 유지)
            yield return new WaitForSeconds(stunDelay);

            // 대기 후 플레이어가 여전히 존재하고 스턴 상태가 아니라면 스턴 적용
            if (player != null)
            {
                player.ApplyCombinedDamage(hDamage, sDamage, "HammerKnockback", 0f, 0f, 1f, deferStunEntryDamping: true, null);
            }
        }
    }
}
