using UnityEngine;
using Fusion;

namespace ithappy
{
    public class OscillateRotation : NetworkBehaviour
    {
        public Vector3 rotationAxis = Vector3.up;
        public float rotationAngle = 45f;
        public float duration = 2f;
        public bool useRandomDelay = false;
        public float maxRandomDelay = 1f;

        // 동기화할 네트워크 변수들
        [Networked] private Quaternion NetRotation { get; set; }
        [Networked] private float timeElapsed { get; set; }
        [Networked] private NetworkBool isReversing { get; set; }
        [Networked] private float randomDelay { get; set; }

        private Quaternion startRotation;

        public override void Spawned()
        {
            startRotation = transform.rotation;

            // 호스트(서버)에서만 랜덤 딜레이를 결정합니다.
            if (HasStateAuthority && useRandomDelay)
            {
                randomDelay = Random.Range(0f, maxRandomDelay);
            }
        }

        // Update 대신 FixedUpdateNetwork를 사용합니다.
        public override void FixedUpdateNetwork()
        {
            // 호스트(State Authority)만 회전 로직을 계산합니다.
            if (HasStateAuthority)
            {
                UpdateHammerLogic();
                // 계산된 회전값을 네트워크 변수에 저장
                NetRotation = transform.rotation;
            }
        }

        private void UpdateHammerLogic()
        {
            if (timeElapsed < randomDelay)
            {
                timeElapsed += Runner.DeltaTime;
                return;
            }

            float progress = (timeElapsed - randomDelay) / (duration / 2f);
            progress = Mathf.Clamp01(progress);
            progress = EaseInOut(progress);

            float currentAngle = rotationAngle * (isReversing ? (1 - progress) : progress);
            transform.rotation = startRotation * Quaternion.AngleAxis(currentAngle, rotationAxis);

            timeElapsed += Runner.DeltaTime;

            if (timeElapsed >= duration / 2f + randomDelay)
            {
                timeElapsed = randomDelay;
                isReversing = !isReversing;
            }
        }

        public override void Render()
        {
            // 클라이언트는 호스트가 계산한 NetRotation을 부드럽게 따라갑니다.
            if (!HasStateAuthority)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, NetRotation, Time.deltaTime * 20f);
            }
        }

        private float EaseInOut(float t)
        {
            return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
        }
    }
}
