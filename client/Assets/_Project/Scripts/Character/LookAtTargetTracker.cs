using System.Collections.Generic;
using RootMotion.Dynamics;
using RootMotion.FinalIK;
using UnityEngine;

public class LookAtTargetTracker : MonoBehaviour
{
    private static readonly List<PuppetMaster> AllPuppets = new();

    public static void Register(PuppetMaster pm)
    {
        if (!AllPuppets.Contains(pm))
            AllPuppets.Add(pm);
    }

    public static void Unregister(PuppetMaster pm)
    {
        AllPuppets.Remove(pm);
    }

    [Header("References")]
    [SerializeField] private PuppetMaster puppetMaster;
    [SerializeField] private LookAtIK lookAtIK;

    [Header("Settings")]
    [Tooltip("Set to 0 to disable look-at.")]
    [SerializeField] private float maxLookDistance = 5.5f;
    [SerializeField] private float weightBlendSpeed = 2.2f;
    [SerializeField] private float lookAtHeightOffset = 0.8f;
    [SerializeField] private float scanInterval = 0.2f;

    private Transform _currentTarget;
    private float _targetWeight;
    private Transform _ikTarget;
    private float _nextScanTime;

    private void Start()
    {
        if (lookAtIK == null)
            lookAtIK = GetComponentInChildren<LookAtIK>();
        if (puppetMaster == null)
            puppetMaster = GetComponentInParent<PuppetMaster>();
        if (puppetMaster == null)
        {
            var root = transform.root;
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

    private void Update()
    {
        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + scanInterval;
            FindNearestOpponent();
        }

        var desired = _currentTarget != null ? 1f : 0f;
        _targetWeight = Mathf.MoveTowards(_targetWeight, desired, weightBlendSpeed * Time.deltaTime);
    }

    private void OnPuppetMasterWrite()
    {
        if (!enabled)
            return;

        if (_currentTarget != null)
            _ikTarget.position = _currentTarget.position + Vector3.up * lookAtHeightOffset;

        lookAtIK.solver.SetIKPositionWeight(_targetWeight);
        lookAtIK.solver.Update();
    }

    private void FindNearestOpponent()
    {
        if (maxLookDistance <= 0f)
        {
            _currentTarget = null;
            return;
        }

        var bestDist = maxLookDistance;
        Transform best = null;
        var myPos = transform.root.position;

        for (var i = AllPuppets.Count - 1; i >= 0; i--)
        {
            var pm = AllPuppets[i];
            if (pm == null)
            {
                AllPuppets.RemoveAt(i);
                continue;
            }

            if (pm == puppetMaster || pm.transform.parent == null)
                continue;

            var dist = Vector3.Distance(myPos, pm.transform.parent.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = pm.transform.parent;
            }
        }

        _currentTarget = best;
    }

    private void OnDestroy()
    {
        if (puppetMaster != null)
        {
            puppetMaster.OnWrite -= OnPuppetMasterWrite;
            Unregister(puppetMaster);
        }
    }
}
