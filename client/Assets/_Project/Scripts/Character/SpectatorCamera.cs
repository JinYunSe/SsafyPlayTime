using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class SpectatorCamera : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private List<Transform> targets = new List<Transform>();

    [Header("View")]
    [SerializeField] private Vector3 baseOffset = new Vector3(0f, 10f, -10f);
    [SerializeField] private float smooth = 6f;

    [Header("Auto Zoom")]
    [SerializeField] private float minHeight = 8f;
    [SerializeField] private float maxHeight = 22f;
    [SerializeField] private float zoomFactor = 0.6f;

    [Header("Fallback (no alive players)")]
    [SerializeField] private Vector3 mapCenter = Vector3.zero;

    private bool _enabledSpectator;

    public void EnableSpectator(bool enabled)
    {
        _enabledSpectator = enabled;
    }

    public void SetTargets(IEnumerable<Transform> newTargets)
    {
        targets = newTargets.Where(t => t != null).Distinct().ToList();
    }

    private void LateUpdate()
    {
        if (!_enabledSpectator)
            return;

        targets.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);
        if (targets.Count == 0)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(mapCenter - transform.position, Vector3.up), Time.deltaTime * smooth);
            return;
        }

        var center = GetCenter(targets);
        var maxDist = GetMaxDistanceFromCenter(targets, center);

        // 멀리 퍼질수록 카메라 높이를 올려서 모두 보이게
        var desiredHeight = Mathf.Clamp(minHeight + maxDist * zoomFactor, minHeight, maxHeight);

        var desiredPos = center + new Vector3(baseOffset.x, desiredHeight, baseOffset.z);
        transform.position = Vector3.Lerp(transform.position, desiredPos, Time.deltaTime * smooth);

        var desiredRot = Quaternion.LookRotation(center - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, Time.deltaTime * smooth);
    }

    private Vector3 GetCenter(List<Transform> list)
    {
        var b = new Bounds(list[0].position, Vector3.zero);
        for (int i = 1; i < list.Count; i++)
            b.Encapsulate(list[i].position);
        return b.center;
    }

    private float GetMaxDistanceFromCenter(List<Transform> list, Vector3 center)
    {
        float max = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            var d = Vector3.Distance(center, list[i].position);
            if (d > max)
                max = d;
        }
        return max;
    }
}
