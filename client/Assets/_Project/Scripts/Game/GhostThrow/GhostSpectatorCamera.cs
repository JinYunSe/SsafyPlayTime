using System.Linq;
using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    public class GhostSpectatorCamera : MonoBehaviour
    {
        [Header("Camera Settings")]
        [Tooltip("Minimum zoom distance")]
        public float minZoom = 15f;
        [Tooltip("Distance limiter for zoom calculation")]
        public float zoomLimiter = 50f;
        [Tooltip("Base offset from the center of all players")]
        public Vector3 offset = new Vector3(0, 5, -15);
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

            // Find all active NetworkPlayers in the scene and filter out dead ones (camerashift logic)
            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            var alivePlayers = allPlayers.Where(p => 
            {
                if (p == null || !p.gameObject.activeInHierarchy) return false;
                PlayerStats stats = p.GetComponent<PlayerStats>();
                return stats != null && stats.currentHealth > 0;
            }).ToArray();
            
            if (alivePlayers.Length == 0) return;

            // Calculate Bounds bounding all players
            Bounds bounds = new Bounds(alivePlayers[0].transform.position, Vector3.zero);
            for (int i = 1; i < alivePlayers.Length; i++)
            {
                bounds.Encapsulate(alivePlayers[i].transform.position);
            }

            Vector3 center = bounds.center;
            
            // To ensure all players are visible, we use the camera's Field of View.
            // We find the bounding sphere radius of the players.
            float radius = bounds.extents.magnitude;
            
            // Add a little padding so they don't touch the screen edges
            float padding = 1.5f;
            radius *= padding;
            
            // Calculate distance required to fit this radius using basic trigonometry:
            // distance = radius / sin(fov/2)
            // We use the smaller of the vertical or horizontal FOV to be safe.
            float fov = _cam.fieldOfView;
            float aspect = _cam.aspect;
            
            // The vertical FOV is _cam.fieldOfView, horizontal is calculated below:
            float radVFOV = fov * Mathf.Deg2Rad;
            float radHFOV = 2 * Mathf.Atan(Mathf.Tan(radVFOV / 2) * aspect);
            
            // Use the smaller FOV angle (usually vertical on widescreen)
            float minFovContext = Mathf.Min(radVFOV, radHFOV);
            
            // Calculate required distance backwards along the camera's look vector
            float requiredDistance = radius / Mathf.Sin(minFovContext / 2f);
            
            // Ensure we don't zoom in *too* much if they are super close
            float minDistance = offset.magnitude;
            requiredDistance = Mathf.Max(minDistance, requiredDistance);
            
            // The original offset gives us the "direction" vector of the camera.
            // We normalize it and multiply by the required distance.
            Vector3 newOffset = offset.normalized * requiredDistance;
            
            Vector3 targetPosition = center + newOffset;
            
            // Smoothly move the camera to the target position
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSpeed);
            
            // Look at the center of the players
            Quaternion targetRotation = Quaternion.LookRotation(center - transform.position);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * moveSpeed);
        }
    }
}
