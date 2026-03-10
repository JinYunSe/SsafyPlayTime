using Fusion;
using UnityEngine;

/// <summary>
/// 호스트 마이그레이션 시 씬에 배치된 NetworkObject GameObjects를
/// runner 종료 시 파괴하지 않는 NetworkObjectProvider.
///
/// 기본 NetworkObjectProviderDefault.DestroySceneObject()는 runner.Shutdown() 시
/// 씬 오브젝트(물, DeathZone 등)의 GameObject를 Destroy()한다.
/// 이로 인해 새 runner가 IsSceneTakeOverEnabled=true로 씬을 테이크오버할 때
/// 오브젝트가 없어 영구 소실된다.
///
/// 이 Provider는 씬 오브젝트를 파괴하지 않고 보존하여,
/// 새 runner가 씬을 테이크오버 시 동일 오브젝트를 재등록할 수 있도록 한다.
/// </summary>
public class MigrationAwareNetworkObjectProvider : NetworkObjectProviderDefault
{
    protected override void DestroySceneObject(
        NetworkRunner runner,
        NetworkSceneObjectId sceneObjectId,
        NetworkObject instance)
    {
        // 씬에 배치된 오브젝트는 파괴하지 않는다.
        // 호스트 마이그레이션으로 runner가 종료돼도 GameObject가 씬에 남아있어야
        // 새 runner의 씬 테이크오버(IsSceneTakeOverEnabled) 시 재등록된다.
        // 재등록 후 Spawned()에서 _localScaleY / _localPhaseStartY를 기반으로
        // 올바른 상태가 복원된다.
        Debug.Log($"[MigrationAwareNetworkObjectProvider] Preserving scene object '{instance.name}' (id={sceneObjectId}) for host migration.");
    }
}
