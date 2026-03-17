using UnityEngine;
using RootMotion.Dynamics;
using RootMotion.FinalIK;

/// <summary>
/// GrounderIK를 PuppetMaster.OnWrite 콜백에서 구동한다.
/// (물리 시뮬 후 시각 보정 — 발 위치를 지면에 맞춤)
/// </summary>
public class GrounderIKPuppetHandler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PuppetMaster puppetMaster;
    [SerializeField] GrounderIK grounderIK;

    void Start()
    {
        if (puppetMaster == null)
            puppetMaster = GetComponentInParent<PuppetMaster>();
        if (puppetMaster == null)
        {
            Transform root = transform.root;
            puppetMaster = root.GetComponentInChildren<PuppetMaster>();
        }

        if (grounderIK == null)
            grounderIK = GetComponent<GrounderIK>();

        if (puppetMaster == null || grounderIK == null)
        {
            enabled = false;
            return;
        }

        // Disable auto-update; PuppetMaster controls timing
        grounderIK.enabled = false;

        puppetMaster.OnWrite += OnPuppetMasterWrite;
    }

    void OnPuppetMasterWrite()
    {
        if (!enabled || grounderIK == null) return;
        grounderIK.solver.Update();
    }

    void OnDestroy()
    {
        if (puppetMaster != null)
            puppetMaster.OnWrite -= OnPuppetMasterWrite;
    }
}
