using UnityEngine;

public class CameraRig : MonoBehaviour
{
    [Header("Follow")]
    [SerializeField] private Transform target;
    [SerializeField] private string autoFindPlayerTag = "Player";
    [SerializeField] private bool autoFindTargetWhenNull = true;

    [Header("Orbit")]
    [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 1.2f, 0f);
    [SerializeField] private float distance = 8f;
    [SerializeField] private float height = 2.2f;
    [SerializeField] private float yaw = 180f;
    [SerializeField] private float pitch = 15f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 70f;
    [SerializeField] private float mouseSensitivity = 2.2f;
    [SerializeField] private bool invertY;
    [SerializeField] private bool lockCursor = true;

    [Header("Smoothing")]
    [SerializeField] private float followSmooth = 14f;
    [SerializeField] private float rotateSmooth = 18f;

    private float _currentYaw;
    private float _currentPitch;
    private bool _initializedAngles;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        InitializeAngles(force: true);
    }

    private void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        TryAutoFindTarget();
        InitializeAngles(force: false);
    }

    private void LateUpdate()
    {
        TryAutoFindTarget();

        if (target == null)
            return;

        var lookX = Input.GetAxis("Mouse X");
        var lookY = Input.GetAxis("Mouse Y");

        _currentYaw += lookX * mouseSensitivity;
        _currentPitch += (invertY ? lookY : -lookY) * mouseSensitivity;
        _currentPitch = Mathf.Clamp(_currentPitch, minPitch, maxPitch);

        var pivot = target.position + pivotOffset;
        var desiredRot = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        var desiredPos = pivot + desiredRot * new Vector3(0f, height, -distance);

        transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * followSmooth);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, Time.deltaTime * rotateSmooth);
    }

    private void TryAutoFindTarget()
    {
        if (!autoFindTargetWhenNull || target != null)
            return;

        var tagged = GameObject.FindGameObjectWithTag(autoFindPlayerTag);
        if (tagged != null)
        {
            SetTarget(tagged.transform);
            return;
        }

        var networkPlayer = FindObjectOfType<NetworkPlayer>();
        if (networkPlayer != null)
            SetTarget(networkPlayer.transform);
    }

    private void InitializeAngles(bool force)
    {
        if (target == null)
            return;

        if (!force && _initializedAngles)
            return;

        _currentYaw = yaw;
        _currentPitch = pitch;
        _initializedAngles = true;
    }
}
