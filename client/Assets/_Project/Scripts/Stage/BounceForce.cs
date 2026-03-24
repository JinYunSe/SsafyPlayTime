using UnityEngine;

public class BounceForce : MonoBehaviour
{
    [SerializeField] private float bounceForce = 15.0f;
    [SerializeField, Range(0f, 0.5f)] private float verticalLift = 0.14f;
    [SerializeField, Range(0f, 1f)] private float recoveringBounceScale = 0.45f;

    private void OnCollisionEnter(Collision collision)
    {
        var networkPlayer = collision.transform.root.GetComponent<NetworkPlayer>();
        var playerRb = networkPlayer != null
            ? collision.transform.root.GetComponent<Rigidbody>()
            : collision.rigidbody != null
                ? collision.rigidbody
                : collision.gameObject.GetComponent<Rigidbody>();
        if (networkPlayer != null && networkPlayer.IsStunned)
        {
            var velocity = playerRb != null ? playerRb.velocity : Vector3.zero;
            networkPlayer.TraceStunForceEvent(
                "BounceForce",
                playerRb,
                Vector3.zero,
                ForceMode.Impulse,
                velocity,
                velocity,
                false,
                "skipped=stunned");
            return;
        }
        if (playerRb == null)
            return;

        var forceScale = 1f;
        if (networkPlayer != null && networkPlayer.IsRecovering)
            forceScale = recoveringBounceScale;

        var planarPush = Vector3.ProjectOnPlane(collision.transform.position - transform.position, Vector3.up);
        if (planarPush.sqrMagnitude <= 0.0001f)
            planarPush = transform.forward;

        var pushDir = (planarPush.normalized + Vector3.up * verticalLift).normalized;
        var appliedForce = pushDir * bounceForce * forceScale;
        var velocityBefore = playerRb.velocity;
        playerRb.AddForce(appliedForce, ForceMode.Impulse);

        /*if (networkPlayer != null)
        {
            if (networkPlayer.HasStateAuthority)
            {
                playerRb.AddForce(appliedForce, ForceMode.Impulse);
            }
            else
            {
                networkPlayer.RPC_ApplyBounceForce(appliedForce);
            }
        }*/

        networkPlayer?.TraceStunForceEvent(
            "BounceForce",
            playerRb,
            appliedForce,
            ForceMode.Impulse,
            velocityBefore,
            playerRb.velocity,
            true,
            $"recoveringScale={forceScale:F2}");
    }
}
