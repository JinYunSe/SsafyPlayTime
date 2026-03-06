using UnityEngine;

public class CameraRig : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Transform target;

    // 월드 기준 오프셋
    [SerializeField] private Vector3 offset = new Vector3(0f, 6f, -10f);
    [SerializeField] private float followSmooth = 12f;

    [Header("Look")]
    [SerializeField] private float lookSmooth = 16f;
    [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1.2f, 0f);

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // 월드 기준으로 따라가기
        var desiredPos = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * followSmooth);

        // 플레이어를 바라보기
        var lookPos = target.position + lookAtOffset;
        var desiredRot = Quaternion.LookRotation(lookPos - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, Time.deltaTime * lookSmooth);
    }
}
