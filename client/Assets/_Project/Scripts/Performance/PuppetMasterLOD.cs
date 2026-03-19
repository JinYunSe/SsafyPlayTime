using System.Collections.Generic;
using RootMotion.Dynamics;
using UnityEngine;

/// <summary>
/// 카메라로부터의 거리에 따라 PuppetMaster를 비활성화/활성화한다.
/// 비활성화 시 Root Rigidbody를 kinematic으로 바꿔 낙하를 막는다.
/// </summary>
public class PuppetMasterLOD : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] float activeDistance = 20f;
    [SerializeField] float deactivateDistance = 25f;

    [Header("Safety")]
    [SerializeField] float minY = -10f;

    static readonly List<PuppetMasterLOD> All = new List<PuppetMasterLOD>();

    PuppetMaster _pm;
    Transform _root;
    Rigidbody _rootRb;
    PlayerStats _playerStats;
    bool _isDeactivated;
    bool _wasKinematic;
    Vector3 _lastSafePosition;

    void Awake()
    {
        _pm = GetComponentInChildren<PuppetMaster>();
        _root = transform;
        _rootRb = GetComponent<Rigidbody>();
        _playerStats = GetComponent<PlayerStats>();
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void LateUpdate()
    {
        if (_playerStats != null && _playerStats.IsDead)
            return;

        // 살아 있는 상태에서만 마지막 안전 위치로 복구한다.
        if (_root.position.y < minY)
        {
            if (_rootRb != null)
            {
                _rootRb.velocity = Vector3.zero;
                _rootRb.angularVelocity = Vector3.zero;
            }

            _root.position = _lastSafePosition;

            if (_pm != null)
                _pm.Teleport(_lastSafePosition, _root.rotation, true);
        }
        else if (_root.position.y > minY + 1f)
        {
            _lastSafePosition = _root.position;
        }
    }

    public static void UpdateAll(Camera cam)
    {
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;

        for (int i = 0; i < All.Count; i++)
        {
            var lod = All[i];
            if (lod._pm == null) continue;

            float dist = Vector3.Distance(camPos, lod._root.position);

            if (lod._isDeactivated && dist < lod.activeDistance)
                lod.Activate();
            else if (!lod._isDeactivated && dist > lod.deactivateDistance)
                lod.Deactivate();
        }
    }

    void Activate()
    {
        if (_pm == null || !_isDeactivated) return;

        if (_rootRb != null)
            _rootRb.isKinematic = _wasKinematic;

        _pm.mode = PuppetMaster.Mode.Active;
        _isDeactivated = false;
    }

    void Deactivate()
    {
        if (_pm == null || _isDeactivated) return;

        if (_rootRb != null)
        {
            _wasKinematic = _rootRb.isKinematic;
            _rootRb.isKinematic = true;
        }

        _pm.mode = PuppetMaster.Mode.Disabled;
        _isDeactivated = true;
    }
}
