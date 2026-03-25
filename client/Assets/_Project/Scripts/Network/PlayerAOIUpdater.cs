using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Network
{
    /// <summary>
    /// 서버(호스트)에서 매 프레임 각 플레이어의 Area Of Interest를 갱신한다.
    /// 플레이어 위치를 기준으로 반경 내 NetworkObject만 해당 클라이언트에 복제된다.
    ///
    /// [요구 조건]
    /// - NetworkProjectConfig > ReplicationFeatures = SchedulingAndInterestManagement
    /// - 복제 필터링 대상 NetworkObject의 ObjectInterest = AreaOfInterest 설정 필요
    ///   (설정하지 않으면 AOI 등록만 되고 실제 필터링은 미적용)
    ///
    /// [자동 부트스트랩]
    /// RuntimeInitializeOnLoadMethod로 씬 로드 시 자동 생성되므로 별도 씬 배치 불필요.
    /// </summary>
    public class PlayerAOIUpdater : MonoBehaviour
    {
        [Tooltip("각 플레이어의 관심 반경 (m). 이 범위 밖의 AreaOfInterest 오브젝트는 해당 클라이언트에 복제되지 않는다.")]
        [SerializeField] private float interestRadius = 80f;

        // AOI는 10Hz(0.1초마다)로만 갱신한다.
        // 플레이어 이동 속도 대비 80m 반경은 충분히 여유가 있어 매 프레임 갱신이 불필요하다.
        private const float UpdateInterval = 0.1f;
        private float _nextUpdateTime;
        private NetworkRunner _runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (FindAnyObjectByType<PlayerAOIUpdater>() != null)
                return;

            var go = new GameObject("[PlayerAOIUpdater]");
            DontDestroyOnLoad(go);
            go.AddComponent<PlayerAOIUpdater>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextUpdateTime)
                return;
            _nextUpdateTime = Time.unscaledTime + UpdateInterval;

            // Runner가 없거나 세션 종료 시 재탐색
            if (_runner == null || !_runner.IsRunning)
                _runner = FindAnyObjectByType<NetworkRunner>();

            // 서버(호스트)에서만 AOI를 관리한다
            if (_runner == null || !_runner.IsRunning || !_runner.IsServer)
                return;

            foreach (var player in _runner.ActivePlayers)
            {
                if (!_runner.TryGetPlayerObject(player, out var playerObj))
                    continue;

                _runner.ClearPlayerAreaOfInterest(player);
                _runner.AddPlayerAreaOfInterest(player, playerObj.transform.position, interestRadius);
            }
        }
    }
}
