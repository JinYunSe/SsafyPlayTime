using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    public class GhostThrowManager : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool enableOnlyAfterDeath = true;
        [SerializeField] private string localPlayerTag = "Player";
        [SerializeField] private Transform manualLocalTarget;

        [Header("Settings")]
        public float cooldown = 1f;
        public float throwForce = 15f;
        public float spawnForwardOffset = 2f;
        [Tooltip("스폰 위치의 타겟 기준 높이 (m) — 너무 높으면 카메라 위로 올라감")]
        public float spawnHeight = 3f;
        [Tooltip("스폰 위치에서 포물선 정점까지 추가 높이 (m) — 클수록 체공 시간이 길어져 느리게 날아감")]
        public float arcHeight = 20f;
        [Tooltip("수평 최대 속도 (m/s) — 거리가 멀어도 이 속도를 넘지 않도록 arcHeight를 자동으로 높임")]
        public float maxHorizontalSpeed = 25f;
        [Tooltip("타겟에서 카메라 방향으로 스폰 위치를 얼마나 오프셋할지 (m)")]
        public float spawnLaunchOffset = 8f;

        [Header("Ghost Throw Spawn Point")]
        [Tooltip("사망 후 폭탄/바나나가 발사될 고정 위치. 지정하면 항상 이 위치에서 발사됨.\n비워두면 카메라 방향 자동 계산 방식으로 폴백.")]
        [SerializeField] private Transform ghostThrowSpawnPoint;
        public LayerMask hitLayer = ~0;

        [Header("Bomb Prefabs")]
        public GameObject cubePrefabOffline;
        public NetworkPrefabRef cubePrefabOnline;
        [SerializeField] private GameObject cubePrefabOnlineObject;

        [Header("Banana Prefabs")]
        public GameObject bananaPrefabOffline;
        public NetworkPrefabRef bananaPrefabOnline;
        [SerializeField] private GameObject bananaPrefabOnlineObject;

        private float _lastBombThrowTime = -100f;
        private float _lastBananaThrowTime = -100f;
        private PlayerStats _localPlayerStats;
        private NetworkPlayer _localNetworkPlayer;
        private bool _isGhostThrowEnabled;
        private bool _hasLoggedMissingLocalPlayer;
        private bool controlEnabled = true;
        private bool enableOutOfBoundsKillCheck;

        public bool IsGhostThrowEnabled => _isGhostThrowEnabled;

        private void Start()
        {
            BindLocalPlayer();
            _isGhostThrowEnabled = !enableOnlyAfterDeath;
        }

        private void OnDestroy()
        {
            if (_localPlayerStats != null)
                _localPlayerStats.OnDied -= HandleLocalPlayerDied;
        }

        public void SetGhostControlEnabled(bool enabled)
        {
            controlEnabled = enabled;
        }

        public void SetEnableOutOfBoundsKillCheck(bool enabled)
        {
            enableOutOfBoundsKillCheck = enabled;
        }

        /// <summary>
        /// 고스트 모드 진입 시 spawnPoint를 null로 설정해 카메라 기반 발사 위치를 사용하게 한다.
        /// (캐릭터가 0,0,0에 고정된 후에도 카메라 시점에서 발사됨)
        /// </summary>
        public void SetGhostThrowSpawnPoint(Transform point) => ghostThrowSpawnPoint = point;

        private void Update()
        {
            if (_localPlayerStats == null && _localNetworkPlayer == null)
                BindLocalPlayer();

            RefreshGhostThrowStateFromLocalPlayer();

            if (!_isGhostThrowEnabled)
                return;

            // ghostThrowSpawnPoint가 없는 매니저는, 동일 씬에 스폰 포인트가 지정된
            // 다른 활성 매니저가 있으면 입력을 양보한다. (중복 투척 방지)
            if (!ShouldHandleInput())
                return;

            if (Input.GetMouseButtonDown(0))
                TryThrow(isBanana: false);

            if (Input.GetMouseButtonDown(1))
                TryThrow(isBanana: true);
        }

        private bool ShouldHandleInput()
        {
            // 스폰 포인트가 지정된 매니저는 항상 우선권을 가진다
            if (ghostThrowSpawnPoint != null)
                return true;

            // 스폰 포인트가 없는 경우: 이미 활성화된 다른 매니저 중
            // ghostThrowSpawnPoint가 있는 것이 있으면 그쪽에 양보
            var allManagers = FindObjectsByType<GhostThrowManager>(FindObjectsSortMode.None);
            for (var i = 0; i < allManagers.Length; i++)
            {
                var other = allManagers[i];
                if (other == null || other == this) continue;
                if (other.isActiveAndEnabled && other._isGhostThrowEnabled && other.ghostThrowSpawnPoint != null)
                    return false;
            }

            return true;
        }

        private void BindLocalPlayer()
        {
            var localPlayer = ResolveLocalPlayerObject();

            if (localPlayer == null)
            {
                if (!_hasLoggedMissingLocalPlayer)
                {
                    Debug.LogWarning($"GhostThrowManager: local player not found. tag={localPlayerTag}");
                    _hasLoggedMissingLocalPlayer = true;
                }
                return;
            }

            _hasLoggedMissingLocalPlayer = false;
            _localPlayerStats = localPlayer.GetComponent<PlayerStats>();
            if (_localPlayerStats == null)
                _localPlayerStats = localPlayer.GetComponentInChildren<PlayerStats>(true);
            if (_localPlayerStats == null)
                _localPlayerStats = localPlayer.GetComponentInParent<PlayerStats>();

            _localNetworkPlayer = localPlayer.GetComponent<NetworkPlayer>();
            if (_localNetworkPlayer == null)
                _localNetworkPlayer = localPlayer.GetComponentInChildren<NetworkPlayer>(true);
            if (_localNetworkPlayer == null)
                _localNetworkPlayer = localPlayer.GetComponentInParent<NetworkPlayer>();

            if (_localPlayerStats == null && _localNetworkPlayer == null)
            {
                Debug.LogWarning($"GhostThrowManager: PlayerStats and NetworkPlayer missing on {localPlayer.name}");
                return;
            }

            if (_localPlayerStats != null)
            {
                _localPlayerStats.OnDied -= HandleLocalPlayerDied;
                _localPlayerStats.OnDied += HandleLocalPlayerDied;
            }

            if ((_localPlayerStats != null && _localPlayerStats.IsDead) ||
                (_localNetworkPlayer != null && _localNetworkPlayer.IsDeadState))
                _isGhostThrowEnabled = true;
        }

        private void HandleLocalPlayerDied(PlayerStats deadPlayer)
        {
            ForceEnableGhostThrow($"dead player {deadPlayer.name}");
        }

        public void ForceEnableGhostThrow(string reason = null)
        {
            _isGhostThrowEnabled = true;
            Debug.Log($"GhostThrowManager: ghost throw enabled{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})")}");
        }

        private void RefreshGhostThrowStateFromLocalPlayer()
        {
            if (_isGhostThrowEnabled || !enableOnlyAfterDeath)
                return;

            if (_localPlayerStats != null && _localPlayerStats.IsDead)
            {
                ForceEnableGhostThrow($"PlayerStats death state on {_localPlayerStats.name}");
                return;
            }

            if (_localNetworkPlayer != null && _localNetworkPlayer.IsDeadState)
            {
                ForceEnableGhostThrow($"NetworkPlayer death state on {_localNetworkPlayer.name}");
                return;
            }

            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            for (var i = 0; i < allPlayers.Length; i++)
            {
                var player = allPlayers[i];
                if (player == null)
                    continue;

                var networkObject = player.GetComponent<NetworkObject>();
                if (networkObject == null || !networkObject.HasInputAuthority)
                    continue;

                if (!player.IsDeadState)
                    continue;

                ForceEnableGhostThrow($"NetworkPlayer death state on {player.name}");
                return;
            }
        }

        private void TryThrow(bool isBanana)
        {
            ref var lastThrowTime = ref (isBanana ? ref _lastBananaThrowTime : ref _lastBombThrowTime);
            if (Time.time < lastThrowTime + cooldown)
            {
                var remain = lastThrowTime + cooldown - Time.time;
                Debug.Log($"GhostThrow [{(isBanana ? "banana" : "bomb")}]: cooldown {remain:F1}s");
                return;
            }

            if (Camera.main == null)
            {
                Debug.LogWarning("GhostThrowManager: main camera not found.");
                return;
            }

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var targetPoint = Physics.Raycast(ray, out RaycastHit hit, 1000f, hitLayer)
                ? hit.point
                : ray.GetPoint(100f);

            lastThrowTime = Time.time;

            // 스폰 위치: 고정 스폰 포인트가 있으면 그것을, 없으면 카메라→타겟 방향으로
            // spawnLaunchOffset(또는 타겟까지 거리의 절반) 앞 지점에서 발사.
            // 이렇게 하면 포물선 시작점과 도착점 모두 카메라 시야에 잡힘.
            Vector3 spawnPos;
            if (ghostThrowSpawnPoint != null)
            {
                spawnPos = ghostThrowSpawnPoint.position;
            }
            else
            {
                var camPos = Camera.main.transform.position;
                var camToTarget = targetPoint - camPos;
                var distToTarget = camToTarget.magnitude;
                var dirToTarget = distToTarget > 0.001f
                    ? camToTarget / distToTarget
                    : Camera.main.transform.forward;
                // 타겟까지 거리의 절반을 넘지 않도록 clamp → 스폰이 타겟을 넘어가지 않음
                var spawnDist = Mathf.Min(spawnLaunchOffset, distToTarget * 0.5f);
                spawnPos = camPos + dirToTarget * spawnDist;
            }
            var initialVelocity = CalculateParabolicVelocity(spawnPos, targetPoint);

            var runner = FindAnyObjectByType<NetworkRunner>();
            if (runner != null && runner.IsRunning)
            {
                // ── 온라인 경로 ──────────────────────────────────────────────
                // InputAuthority(클라이언트 포함)는 RPC로 호스트에 위임.
                // StateAuthority(호스트)는 직접 스폰.
                // 어느 쪽이든 SpawnOffline으로 폴백하지 않는다:
                //   SpawnOffline은 로컬 오브젝트이므로 호스트에만 보이는 버그를 유발한다.
                var localNetworkPlayer = ResolveLocalNetworkPlayer();
                if (localNetworkPlayer != null && localNetworkPlayer.TryRequestGhostThrow(isBanana, spawnPos, initialVelocity))
                {
                    Debug.Log($"GhostThrow [Request]: requested {(isBanana ? "banana" : "bomb")} at {spawnPos}");
                    return;
                }

                // RPC 실패(localNetworkPlayer가 없거나 InputAuthority 아님)인 경우
                // 호스트라면 직접 스폰 시도
                if (runner.IsServer)
                {
                    SpawnOnline(runner, isBanana, spawnPos, initialVelocity);
                }
                else
                {
                    Debug.LogError("GhostThrow: 온라인 환경이지만 localNetworkPlayer를 찾을 수 없어 투척 실패. " +
                                   "NetworkPlayer가 올바르게 연결됐는지 확인하세요.");
                }

                // 온라인 환경에서는 반드시 여기서 종료 (SpawnOffline 폴백 없음)
                return;
            }

            // ── 오프라인(로컬 테스트) 환경 전용 ─────────────────────────────
            SpawnOffline(isBanana, spawnPos, initialVelocity);
        }

        private Vector3 CalculateParabolicVelocity(Vector3 from, Vector3 to)
        {
            float g = Physics.gravity.y; // 음수 (예: -9.81)
            float dy = to.y - from.y;
            float dx = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));

            // ① 수평 속도 제한: 멀수록 arcHeight를 자동으로 높여 최대 수평속도를 보장
            //    T_level ≈ 2*sqrt(2h/|g|)  →  h_min = |g|*dx² / (8*Vmax²)
            float vMaxSq = maxHorizontalSpeed * maxHorizontalSpeed;
            float minArcForSpeed = vMaxSq > 0f ? (-g) * dx * dx / (8f * vMaxSq) : 0f;

            // ② 카메라 가시성 제한: frustum top plane 기준으로 정점이 화면 안에 들어오도록 arcHeight를 클램프
            float maxArcForVisibility = ComputeMaxVisibleArcHeight(from, to);

            // ③ 최종 적용: 속도 제약(하한) 우선, 가능하면 가시성 범위(상한) 안으로 제한
            float effectiveArcHeight = Mathf.Max(minArcForSpeed, Mathf.Min(maxArcForVisibility, arcHeight));

            // 원하는 호 높이에서 초기 수직 속도 계산
            float vy0 = Mathf.Sqrt(Mathf.Max(0f, -2f * g * effectiveArcHeight));

            // 0.5*g*T^2 + vy0*T - dy = 0 풀기
            float discriminant = vy0 * vy0 + 2f * g * dy;
            if (discriminant < 0f) discriminant = 0f;
            float T = (-vy0 - Mathf.Sqrt(discriminant)) / g;
            if (T <= 0.01f) T = 0.5f; // 폴백

            return new Vector3(
                (to.x - from.x) / T,
                vy0,
                (to.z - from.z) / T
            );
        }

        /// <summary>
        /// 카메라 frustum의 top plane 기준으로, 포물선 정점 위치에서 허용되는
        /// 최대 worldspace Y를 역산해 arcHeight 상한을 반환한다.
        /// 정점은 수평 경로 중간 지점 근처에서 발생한다고 근사한다.
        /// </summary>
        private float ComputeMaxVisibleArcHeight(Vector3 spawnPos, Vector3 targetPos)
        {
            var cam = Camera.main;
            if (cam == null)
                return arcHeight;

            // 포물선 정점의 수평 위치 = 스폰과 타겟의 중간점
            var peakXZ = (spawnPos + targetPos) * 0.5f;

            // Unity frustum planes 순서: 0=Left 1=Right 2=Bottom 3=Top 4=Near 5=Far
            // 각 평면의 normal은 frustum 내부를 향함 → 내부 점은 dot(n, p) + d >= 0
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var top = planes[3];

            // top plane 방정식을 Y에 대해 풀기:
            //   top.normal.x*x + top.normal.y*Y + top.normal.z*z + top.distance >= 0
            //   → Y <= -(top.normal.x*x + top.normal.z*z + top.distance) / top.normal.y
            //      (top.normal.y < 0 이므로 부등호 방향 주의 — 이미 위 식에 반영됨)
            if (Mathf.Abs(top.normal.y) < 0.001f)
                return arcHeight; // top plane이 수평 → Y 제약 없음

            float maxWorldY = -(top.normal.x * peakXZ.x
                              + top.normal.z * peakXZ.z
                              + top.distance) / top.normal.y;

            // arcHeight는 스폰 위치 기준 상대 높이
            float maxArc = maxWorldY - spawnPos.y;

            // 너무 낮으면 1m 보장 (속도 제약이 별도로 하한을 맞춤)
            return Mathf.Max(1f, maxArc);
        }

        internal bool TrySpawnOnlineFromRequest(bool isBanana, Vector3 spawnPos, Vector3 velocity)
        {
            var runner = FindAnyObjectByType<NetworkRunner>();
            if (runner == null || !runner.IsRunning || !runner.IsServer)
                return false;

            return SpawnOnline(runner, isBanana, spawnPos, velocity);
        }

        private bool SpawnOnline(NetworkRunner runner, bool isBanana, Vector3 spawnPos, Vector3 velocity)
        {
            var prefabRef = isBanana ? bananaPrefabOnline : cubePrefabOnline;
            var prefabObject = isBanana ? bananaPrefabOnlineObject : cubePrefabOnlineObject;
            var label = isBanana ? "banana" : "bomb";

            var spawnRot = velocity.sqrMagnitude > 0.001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;

            // onBeforeSpawned: [Networked] 초기 속도를 저장 → 모든 클라이언트의 Spawned()에서 읽어 Rigidbody에 적용.
            // 이 콜백이 없으면 클라이언트는 속도 0으로 시작해 폭탄이 수직 낙하하며 보이지 않는다.
            var capturedVelocity = velocity;
            void OnBeforeSpawned(NetworkRunner r, NetworkObject no)
            {
                if (isBanana)
                {
                    var bp = no.GetComponent<BananaPeel>();
                    if (bp != null) bp.NetworkedInitialVelocity = capturedVelocity;
                }
                else
                {
                    var gc = no.GetComponent<GhostCube>();
                    if (gc != null) gc.NetworkedInitialVelocity = capturedVelocity;
                }
            }

            // ── NetworkPrefabRef 우선 사용 ──────────────────────────────────
            // Fusion이 prefab GUID로 클라이언트에게 스폰 메시지를 전달하므로
            // 모든 클라이언트에서 동일한 오브젝트가 생성된다 (진정한 네트워크 복제).
            // prefabObject(GameObject) 경로는 Fusion 테이블 미등록 시 로컬 생성만 되므로 폴백으로만 사용.
            if (prefabRef.IsValid)
            {
                var spawnedObj = runner.Spawn(prefabRef, spawnPos, spawnRot,
                    onBeforeSpawned: OnBeforeSpawned);
                if (spawnedObj == null)
                {
                    Debug.LogError($"GhostThrowManager [Online/prefabRef]: {label} 스폰 실패. " +
                                   $"Fusion > Network Project Config > Rebuild Object Table 을 실행했는지 확인하세요.");
                    return false;
                }

                NotifySpectatorCameraToTrack(spawnedObj.transform);
                Debug.Log($"GhostThrow [Online/prefabRef]: threw {label} at {spawnPos}");
                return true;
            }

            // ── 폴백: GameObject 경로 ────────────────────────────────────────
            // NetworkPrefabRef가 없을 때만 사용. Fusion NetworkPrefabTable에 등록돼 있어야 복제된다.
            if (prefabObject != null)
            {
                var spawnedByObject = runner.Spawn(prefabObject, spawnPos, spawnRot,
                    onBeforeSpawned: OnBeforeSpawned);
                if (spawnedByObject == null)
                {
                    Debug.LogError($"GhostThrowManager [Online/prefabObject]: {label} 스폰 실패. " +
                                   $"Inspector의 cubePrefabOnline(NetworkPrefabRef) 필드를 설정하고 " +
                                   $"Fusion > Network Project Config > Rebuild Object Table 을 실행하세요.");
                    return false;
                }

                NotifySpectatorCameraToTrack(spawnedByObject.transform);
                Debug.Log($"GhostThrow [Online/prefabObject]: threw {label} at {spawnPos}");
                return true;
            }

            Debug.LogError($"GhostThrowManager [Online]: {label} 프리팹이 지정되지 않았습니다. " +
                           $"Inspector에서 cubePrefabOnline(NetworkPrefabRef) 또는 cubePrefabOnlineObject(GameObject)를 설정하세요.");
            return false;
        }

        private void SpawnOffline(bool isBanana, Vector3 spawnPos, Vector3 velocity)
        {
            var prefab = isBanana ? bananaPrefabOffline : cubePrefabOffline;
            var label = isBanana ? "banana" : "bomb";

            if (prefab == null)
            {
                Debug.LogWarning($"GhostThrowManager [Offline]: {label} prefab is not assigned.");
                return;
            }

            var spawnRot = velocity.sqrMagnitude > 0.001f ? Quaternion.LookRotation(velocity) : Quaternion.identity;
            var spawnedObj = Instantiate(prefab, spawnPos, spawnRot);
            ApplyThrowVelocity(spawnedObj, velocity);
            NotifySpectatorCameraToTrack(spawnedObj.transform);
            Debug.Log($"GhostThrow [Offline]: threw {label} at {spawnPos}");
        }

        private void NotifySpectatorCameraToTrack(Transform projectile)
        {
            // GhostThrowManager와 같은 오브젝트에 붙은 카메라 우선 탐색,
            // 없으면 씬 전체에서 찾음
            var spectatorCam = GetComponent<GhostSpectatorCamera>();
            if (spectatorCam == null)
                spectatorCam = FindAnyObjectByType<GhostSpectatorCamera>();

            spectatorCam?.TrackProjectile(projectile);
        }

        private static void ApplyThrowVelocity(GameObject obj, Vector3 velocity)
        {
            var rb = obj.GetComponent<Rigidbody>();
            if (rb == null)
                return;

            // Bomb 프리팹의 ItemFieldDrop.Awake()가 drag를 0.15f로 덮어쓰므로
            // 포물선 계산(drag=0 가정)과 실제 비행이 일치하도록 명시적으로 초기화한다.
            rb.drag = 0f;
            rb.angularDrag = 0.05f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.velocity = velocity;
        }

        private NetworkPlayer ResolveLocalNetworkPlayer()
        {
            if (_localPlayerStats != null)
            {
                var player = _localPlayerStats.GetComponent<NetworkPlayer>();
                if (player == null)
                    player = _localPlayerStats.GetComponentInParent<NetworkPlayer>();
                if (player == null)
                    player = _localPlayerStats.GetComponentInChildren<NetworkPlayer>(true);
                if (player != null)
                    return player;
            }

            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            for (var i = 0; i < allPlayers.Length; i++)
            {
                var player = allPlayers[i];
                if (player == null)
                    continue;

                var networkObject = player.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.HasInputAuthority)
                    return player;
            }

            return null;
        }

        private GameObject ResolveLocalPlayerObject()
        {
            if (manualLocalTarget != null)
                return manualLocalTarget.gameObject;

            var localNetworkPlayer = ResolveLocalNetworkPlayerFromScene();
            if (localNetworkPlayer != null)
                return localNetworkPlayer.gameObject;

            var localStats = ResolveLocalPlayerStatsFromScene();
            if (localStats != null)
                return localStats.gameObject;

            return GameObject.FindGameObjectWithTag(localPlayerTag);
        }

        private static NetworkPlayer ResolveLocalNetworkPlayerFromScene()
        {
            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            for (var i = 0; i < allPlayers.Length; i++)
            {
                var player = allPlayers[i];
                if (player == null)
                    continue;

                var networkObject = player.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.HasInputAuthority)
                    return player;
            }

            return null;
        }

        private static PlayerStats ResolveLocalPlayerStatsFromScene()
        {
            var allPlayerStats = FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);
            for (var i = 0; i < allPlayerStats.Length; i++)
            {
                var stats = allPlayerStats[i];
                if (stats == null)
                    continue;

                var netObj = stats.GetComponent<NetworkObject>();
                if (netObj == null)
                    netObj = stats.GetComponentInParent<NetworkObject>();
                if (netObj == null)
                    netObj = stats.GetComponentInChildren<NetworkObject>(true);

                if (netObj != null && netObj.HasInputAuthority)
                    return stats;
            }

            return null;
        }

        public static GhostThrowManager FindManagerForPlayer(NetworkPlayer player)
        {
            if (player != null)
            {
                var childManager = player.GetComponentInChildren<GhostThrowManager>(true);
                if (childManager != null)
                    return childManager;
            }

            var managers = FindObjectsByType<GhostThrowManager>(FindObjectsSortMode.None);
            GhostThrowManager firstAvailable = null;

            for (var i = 0; i < managers.Length; i++)
            {
                var manager = managers[i];
                if (manager == null)
                    continue;

                firstAvailable ??= manager;

                if (manager._isGhostThrowEnabled || !manager.enableOnlyAfterDeath)
                    return manager;
            }

            return firstAvailable;
        }
    }
}
