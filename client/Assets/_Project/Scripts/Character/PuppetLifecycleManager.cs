using System.Collections;
using UnityEngine;
using RootMotion.Dynamics;

/// <summary>
/// PuppetMaster 상태 머신: Active↔Dead↔Frozen 전환을 관리한다.
/// 보고서 권장 패턴: 상태 전환, 죽음 처리, 리스폰(Teleport)
/// </summary>
public class PuppetLifecycleManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PuppetMaster puppetMaster;
    [SerializeField] BehaviourPuppet behaviourPuppet;

    [Header("Respawn Settings")]
    [SerializeField] float respawnDelay = 3f;
    [SerializeField] Transform respawnPoint;

    [Header("Death Settings")]
    [SerializeField] float freezeAfterDeathTime = 5f;

    public bool IsDead => puppetMaster != null && puppetMaster.state == PuppetMaster.State.Dead;
    public bool IsFrozen => puppetMaster != null && puppetMaster.state == PuppetMaster.State.Frozen;

    public event System.Action OnDeath;
    public event System.Action OnRespawn;

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

    public void Respawn()
    {
        if (puppetMaster == null) return;
        StartCoroutine(RespawnCoroutine());
    }

    public void Respawn(Vector3 position, Quaternion rotation)
    {
        if (puppetMaster == null) return;
        StartCoroutine(RespawnCoroutine(position, rotation));
    }

    IEnumerator RespawnCoroutine()
    {
        Vector3 pos = respawnPoint != null ? respawnPoint.position : Vector3.up * 2f;
        Quaternion rot = respawnPoint != null ? respawnPoint.rotation : Quaternion.identity;
        yield return RespawnCoroutine(pos, rot);
    }

    IEnumerator RespawnCoroutine(Vector3 pos, Quaternion rot)
    {
        puppetMaster.Resurrect();
        if (behaviourPuppet != null)
            behaviourPuppet.SetState(BehaviourPuppet.State.Puppet);

        yield return null;
        yield return new WaitForFixedUpdate();

        puppetMaster.Teleport(pos, rot, true);

        yield return null;
        puppetMaster.mode = PuppetMaster.Mode.Active;

        OnRespawn?.Invoke();
    }

    public void DieAndAutoRespawn()
    {
        Die();
        StartCoroutine(AutoRespawnCoroutine());
    }

    IEnumerator AutoRespawnCoroutine()
    {
        yield return new WaitForSeconds(respawnDelay);
        Respawn();
    }
}
