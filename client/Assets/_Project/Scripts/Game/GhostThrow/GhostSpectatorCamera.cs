using System.Linq;
using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    public class GhostSpectatorCamera : MonoBehaviour
    {
        [Header("Camera Settings")]
        [Tooltip("Minimum top-down height")]
        public float minZoom = 15f;
        [Tooltip("Maximum top-down height")]
        public float zoomLimiter = 50f;
        [Tooltip("Horizontal framing offset from the center of all players")]
        public Vector3 offset = new Vector3(0, 0, -3f);
        [Tooltip("Additional height multiplier based on alive player spread")]
        public float topDownHeightMultiplier = 2.2f;
        public float moveSpeed = 5f;

        private Camera _cam;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null)
            {
                Debug.LogError("GhostSpectatorCamera requires a Camera component!");
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) return;

            var aliveTargets = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None)
                .Where(IsAlivePlayer)
                .Select(ResolveFocusTarget)
                .Where(t => t != null)
                .ToArray();

            if (aliveTargets.Length == 0)
                return;

            Bounds bounds = new Bounds(aliveTargets[0].position, Vector3.zero);
            for (int i = 1; i < aliveTargets.Length; i++)
            {
                bounds.Encapsulate(aliveTargets[i].position);
            }

            Vector3 center = bounds.center;
            float radius = bounds.extents.magnitude;
            float paddedRadius = radius * 1.35f;
            float requiredHeight = Mathf.Clamp(paddedRadius * topDownHeightMultiplier, minZoom, zoomLimiter);

            Vector3 horizontalOffset = new Vector3(offset.x, 0f, offset.z);
            Vector3 targetPosition = center + Vector3.up * requiredHeight + horizontalOffset;

            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSpeed);
            Quaternion targetRotation = Quaternion.LookRotation(center - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * moveSpeed);
        }

        private static bool IsAlivePlayer(NetworkPlayer player)
        {
            return player != null &&
                   player.gameObject.activeInHierarchy &&
                   !player.IsDeadNetworked;
        }

        private static Transform ResolveFocusTarget(NetworkPlayer player)
        {
            if (player == null)
                return null;

            var followTarget = player.GetCameraFollowTarget();
            return followTarget != null ? followTarget : player.transform;
        }
    }
}
