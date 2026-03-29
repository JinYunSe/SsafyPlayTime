using UnityEngine;
using RootMotion.Dynamics;
using System.Collections;
/// <summary>
/// PuppetMaster 상태 머신: Active↔Dead↔Frozen 전환을 관리한다.
/// 보고서 권장 패턴: 상태 전환, 죽음 처리, 리스폰(Teleport)
/// </summary>
public class PuppetLifecycleManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PuppetMaster puppetMaster;
    [SerializeField] BehaviourPuppet behaviourPuppet;

    [Header("Death Settings")]
    [SerializeField] float freezeAfterDeathTime = 5f;

    public bool IsDead => puppetMaster != null && puppetMaster.state == PuppetMaster.State.Dead;
    public bool IsFrozen => puppetMaster != null && puppetMaster.state == PuppetMaster.State.Frozen;

    public event System.Action OnDeath;

    void Awake()
    {
        if (puppetMaster == null)
            puppetMaster = GetComponentInChildren<PuppetMaster>();
        if (behaviourPuppet == null)
            behaviourPuppet = GetComponentInChildren<BehaviourPuppet>();
    }

    public void GoActive()
    {
        if (puppetMaster == null) return;
        puppetMaster.mode = PuppetMaster.Mode.Active;
        if (behaviourPuppet != null)
            behaviourPuppet.SetState(BehaviourPuppet.State.Puppet);
    }

    public void Die()
    {
        if (puppetMaster == null || IsDead) return;

        StopAllCoroutines();
        puppetMaster.state = PuppetMaster.State.Dead;
        puppetMaster.mode = PuppetMaster.Mode.Active;

        OnDeath?.Invoke();

        if (freezeAfterDeathTime > 0)
            StartCoroutine(FreezeAfterDelay());
    }

    IEnumerator FreezeAfterDelay()
    {
        yield return new WaitForSeconds(freezeAfterDeathTime);
        if (IsDead && puppetMaster != null)
        {
            puppetMaster.state = PuppetMaster.State.Frozen;
        }
    }
}
