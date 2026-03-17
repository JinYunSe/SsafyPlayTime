using System.Collections.Generic;
using UnityEngine;
using RootMotion.FinalIK;
using RootMotion.Dynamics;

/// <summary>
/// Finds the nearest opponent and sets it as the LookAtIK target.
/// Works with PuppetMaster via OnWrite callback (IK after physics).
/// </summary>
public class LookAtTargetTracker : MonoBehaviour
{
    // Static registry — no per-frame FindObjectsOfType
    static readonly List<PuppetMaster> AllPuppets = new List<PuppetMaster>();

    public static void Register(PuppetMaster pm) { if (!AllPuppets.Contains(pm)) AllPuppets.Add(pm); }
    public static void Unregister(PuppetMaster pm) { AllPuppets.Remove(pm); }

    [Header("References")]
    [SerializeField] PuppetMaster puppetMaster;
    [SerializeField] LookAtIK lookAtIK;

    [Header("Settings")]
    [Tooltip("0으로 설정하면 LookAt 비활성화")]
    [SerializeField] float maxLookDistance = 0f;
    [SerializeField] float weightBlendSpeed = 3f;
    [SerializeField] float lookAtHeightOffset = 0.8f;
    [SerializeField] float scanInterval = 0.2f;

    Transform _currentTarget;
    float _targetWeight;
    Transform _ikTarget;
    float _nextScanTime;

    void Start()
    {
        if (lookAtIK == null) lookAtIK = GetComponentInChildren<LookAtIK>();
        if (puppetMaster == null) puppetMaster = GetComponentInParent<PuppetMaster>();
        if (puppetMaster == null)
        {
            Transform root = transform.root;
            puppetMaster = root.GetComponentInChildren<PuppetMaster>();
        }

        if (lookAtIK == null || puppetMaster == null)
        {
            enabled = false;
            return;
        }

        Register(puppetMaster);

        var targetGO = new GameObject("LookAtTarget");
        targetGO.transform.SetParent(transform);
        _ikTarget = targetGO.transform;

        lookAtIK.solver.target = _ikTarget;
        lookAtIK.solver.SetIKPositionWeight(0f);
        lookAtIK.enabled = false;

        puppetMaster.OnWrite += OnPuppetMasterWrite;
    }

    void Update()
    {
        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + scanInterval;
            FindNearestOpponent();
        }

        float desired = _currentTarget != null ? 1f : 0f;
        _targetWeight = Mathf.MoveTowards(_targetWeight, desired, weightBlendSpeed * Time.deltaTime);
    }

    void OnPuppetMasterWrite()
    {
        if (!enabled) return;

        if (_currentTarget != null)
            _ikTarget.position = _currentTarget.position + Vector3.up * lookAtHeightOffset;

        lookAtIK.solver.SetIKPositionWeight(_targetWeight);
        lookAtIK.solver.Update();
    }

    void FindNearestOpponent()
    {
        float bestDist = maxLookDistance;
        Transform best = null;
        Vector3 myPos = transform.root.position;

        for (int i = AllPuppets.Count - 1; i >= 0; i--)
        {
            var pm = AllPuppets[i];
            if (pm == null) { AllPuppets.RemoveAt(i); continue; }
            if (pm == puppetMaster) continue;
            if (pm.transform.parent == null) continue;

            float dist = Vector3.Distance(myPos, pm.transform.parent.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = pm.transform.parent;
            }
        }

        _currentTarget = best;
    }

    void OnDestroy()
    {
        if (puppetMaster != null)
        {
            puppetMaster.OnWrite -= OnPuppetMasterWrite;
            Unregister(puppetMaster);
        }
    }
}
