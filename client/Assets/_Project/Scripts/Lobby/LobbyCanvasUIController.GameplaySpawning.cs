using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

namespace SSAFYPlayTime
{
    // GameScene에서의 캐릭터 스폰을 담당하는 파셜 클래스.
    // 서버(IsServer)에서만 동작하며 각 플레이어에게 선택한 캐릭터를 Fusion으로 생성한다.
    public sealed partial class LobbyCanvasUIController
    {
        [Header("Gameplay Spawning")]
        // 캐릭터 인덱스 → 프리팹 매핑 에셋. 프리팹 교체 시 이 에셋만 수정하면 된다.
        [SerializeField] private CharacterRegistry characterRegistry;

        // SpawnPointGroup이 없거나 포인트가 고갈됐을 때 사용할 폴백 스폰 위치 목록.
        // 인스펙터에서 씬의 Transform을 직접 지정한다.
        [SerializeField] private Transform[] fallbackSpawnPoints;

        // 씬 전환 후 DontDestroyOnLoad 처리됐는지 여부 (중복 호출 방지)
        private bool _isPersistentAcrossScenes;

        // 스폰된 캐릭터 NetworkObject를 PlayerId 키로 관리 (퇴장 시 Despawn에 사용)
        private readonly Dictionary<int, NetworkObject> _spawnedGameplayNetworkCharacters = new();
        private readonly Dictionary<int, int> _spawnedCharacterIndexByPlayerId = new();
        private readonly HashSet<int> _deadGameplayPlayerIds = new();
        private readonly Dictionary<int, NetworkPlayer> _trackedGameplayPlayersByPlayerId = new();

        // 씬에서 찾아둔 SpawnPointGroup 캐시 (OnSceneLoadStart 시 null 초기화)
        private SpawnPointGroup _cachedSpawnPointGroup;
        private bool _gameplaySceneSpawnBootstrapComplete;

        // 호스트 마이그레이션 직전에 캡처한 각 플레이어의 캐릭터 위치/회전 (구 PlayerId 키).
        // 재접속 후 PlayerId가 유지되면 직접 조회한다.
        // 새 방장의 PlayerId가 바뀐 경우 OnHostMigration에서 RemapMigrationEntry로 미리 키를 교체한다.
        private readonly Dictionary<int, (Vector3 position, Quaternion rotation)> _migratedPositionsByOldPlayerId = new();
        private readonly Dictionary<string, (Vector3 position, Quaternion rotation)> _migratedPositionsByClientId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SeaLevelController.MigrationState> _migratedSeaLevelsByPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DeathZoneRoot.MigrationState> _migratedDeathZonesByPath = new(StringComparer.Ordinal);

        // 마이그레이션 직전 clientId→구PlayerId 역방향 조회 테이블.
        // OnHostMigration 시점에는 일부 플레이어만 재접속 완료됐으므로,
        // 이후 OnPlayerJoined에서 각 플레이어가 재접속할 때마다 이 테이블로 위치·캐릭터 데이터를 리매핑한다.
        private readonly Dictionary<string, int> _migrationOldPlayerIdByClientId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _migrationReadyStateByClientId = new(StringComparer.OrdinalIgnoreCase);

        // 호스트 마이그레이션 진행 중 여부. true이면 OnSceneLoadStart에서 마이그레이션 데이터를 지우지 않는다.
        private bool _isMigrating;

        // 마이그레이션 진행 중(_isMigrating) 수신된 캐릭터 선택을 임시 보관한다.
        // _isMigrating = false 직후 FlushPendingMigrationSpawns()에서 일괄 처리한다.
        private readonly Dictionary<PlayerRef, int> _pendingCharacterSelectionsWhileMigrating = new();

        // DontDestroyOnLoad를 한 번만 호출하도록 보장한다.
        private void EnsurePersistentAcrossScenes()
        {
            if (_isPersistentAcrossScenes)
            {
                return;
            }

            var persistentTarget = transform.root != null ? transform.root.gameObject : gameObject;
            DontDestroyOnLoad(persistentTarget);
            _isPersistentAcrossScenes = true;
        }

        // 현재 활성 씬 이름이 gameplaySceneName과 일치하는지 확인한다.
        private bool IsActiveGameplayScene()
        {
            if (string.IsNullOrWhiteSpace(gameplaySceneName))
            {
                return false;
            }

            var activeScene = SceneManager.GetActiveScene();
            var requested = System.IO.Path.GetFileNameWithoutExtension(gameplaySceneName.Trim());
            return string.Equals(activeScene.name, requested, StringComparison.OrdinalIgnoreCase);
        }

        // characterIndex(0~3)에 해당하는 게임플레이 캐릭터 프리팹을 반환한다.
        private GameObject GetGameplayCharacterPrefabByIndex(int characterIndex)
        {
            if (characterRegistry == null)
            {
                Debug.LogError("[Lobby] CharacterRegistry is not assigned. Assign it in the inspector.");
                return null;
            }

            var idx = SanitizeCharacterIndexOrNone(characterIndex);
            if (idx == (int)CharacterKind.Random)
            {
                // ResolveRandomCharacterSelections()에서 사전 해결되지 않은 경우의 폴백
                idx = UnityEngine.Random.Range(0, 4);
            }

            return characterRegistry.GetPrefabByIndex(idx);
        }

        // Random을 선택한 플레이어들에게 다른 플레이어와 중복되지 않는 캐릭터를 배정한다.
        // 스폰 전에 서버에서 한 번 호출한다.
        private void ResolveRandomCharacterSelections()
        {
            var takenIndices = new HashSet<int>();
            var randomPlayerIds = new List<int>();

            foreach (var kvp in _selectedCharacterIndexByPlayerId)
            {
                var charIdx = SanitizeCharacterIndexOrNone(kvp.Value);
                if (charIdx == (int)CharacterKind.Random || charIdx < 0)
                {
                    randomPlayerIds.Add(kvp.Key);
                }
                else
                {
                    takenIndices.Add(charIdx);
                }
            }

            if (randomPlayerIds.Count == 0)
            {
                return;
            }

            var available = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                if (!takenIndices.Contains(i))
                {
                    available.Add(i);
                }
            }

            // Fisher-Yates 셔플
            for (var i = available.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (available[i], available[j]) = (available[j], available[i]);
            }

            for (var i = 0; i < randomPlayerIds.Count; i++)
            {
                var playerId = randomPlayerIds[i];
                var resolvedIdx = i < available.Count ? available[i] : available[UnityEngine.Random.Range(0, available.Count)];
                _selectedCharacterIndexByPlayerId[playerId] = resolvedIdx;
                Debug.Log($"[Lobby] Resolved Random → {(CharacterKind)resolvedIdx} for player={playerId}");
            }
        }

        // 현재 접속 중인 실제 플레이어를 PlayerId 오름차순으로 정렬해 반환한다.
        private List<PlayerRef> GetOrderedActivePlayers()
        {
            if (_runner == null || !_runner.IsRunning)
            {
                return new List<PlayerRef>();
            }

            return _runner.ActivePlayers
                .Where(p => p.IsRealPlayer)
                .OrderBy(p => p.PlayerId)
                .ToList();
        }

        // 호스트 마이그레이션 직전 (ShutdownRunnerAsync 전)에 호출한다.
        // 씬의 NetworkPlayer를 직접 탐색해 InputAuthority 기준으로 위치/회전을 캡처한다.
        // _spawnedGameplayNetworkCharacters는 서버에서만 채워지므로
        // 클라이언트가 새 방장이 되는 경우에도 올바르게 동작하도록 씬 탐색 방식을 사용한다.
        internal void CaptureCharacterStatesForMigration()
        {
            _migratedPositionsByOldPlayerId.Clear();
            _migratedPositionsByClientId.Clear();

            foreach (var np in UnityEngine.Object.FindObjectsOfType<NetworkPlayer>())
            {
                var no = np.GetComponent<NetworkObject>();
                if (no == null)
                {
                    Debug.LogWarning($"[Lobby] Skip migration capture: NetworkPlayer '{np.name}' has no NetworkObject.");
                    continue;
                }

                if (!no.InputAuthority.IsRealPlayer)
                {
                    Debug.LogWarning($"[Lobby] Skip migration capture: '{np.name}' inputAuthority={no.InputAuthority}, stateAuthority={no.StateAuthority}");
                    continue;
                }

                var playerId = no.InputAuthority.PlayerId;
                _migratedPositionsByOldPlayerId[playerId] = (np.transform.position, np.transform.rotation);
                Debug.Log($"[Lobby] Captured migration state. object={np.name}, player={playerId}, inputAuthority={no.InputAuthority}, stateAuthority={no.StateAuthority}, pos={np.transform.position}");

                // 실제 스폰된 캐릭터 타입을 Networked 속성에서 읽어 캐릭터 선택 테이블을 덮어쓴다.
                // ? 선택자는 _selectedCharacterIndexByPlayerId에 4(Random)가 남아있을 수 있으나
                // CharacterTypeIndex에는 onBeforeSpawned에서 확정된 실제 인덱스(0~3)가 저장돼 있다.
                // roster 수신 여부와 무관하므로 레이스 컨디션을 근본적으로 해소한다.
                var resolvedCharIdx = np.CharacterTypeIndex;
                if (resolvedCharIdx >= 0 && resolvedCharIdx < (int)CharacterKind.Random)
                {
                    _selectedCharacterIndexByPlayerId[playerId] = resolvedCharIdx;
                    Debug.Log($"[Lobby] Captured resolved character type from NetworkPlayer. player={playerId}, charIdx={resolvedCharIdx}");
                }

                if (_roomParticipantsByPlayerId.TryGetValue(playerId, out var presence) &&
                    presence != null &&
                    !string.IsNullOrWhiteSpace(presence.ClientId))
                {
                    _migratedPositionsByClientId[presence.ClientId] = (np.transform.position, np.transform.rotation);
                    Debug.Log($"[Lobby] Captured migration state by clientId. object={np.name}, clientId={presence.ClientId}, nickname={presence.Nickname}, player={playerId}, pos={np.transform.position}");
                }
                else
                {
                    Debug.LogWarning($"[Lobby] Missing participant presence during migration capture. object={np.name}, player={playerId}");
                }
            }
        }

        internal void CaptureEnvironmentStatesForMigration()
        {
            _migratedSeaLevelsByPath.Clear();

            foreach (var seaLevel in UnityEngine.Object.FindObjectsOfType<SeaLevelController>(true))
            {
                if (seaLevel == null)
                {
                    continue;
                }

                var key = seaLevel.GetMigrationKey();
                if (string.IsNullOrWhiteSpace(key))
                {
                    Debug.LogWarning("[Lobby] Skip sea level migration capture: empty hierarchy path.");
                    continue;
                }

                _migratedSeaLevelsByPath[key] = seaLevel.CaptureMigrationState();
                Debug.Log($"[Lobby] Captured sea level migration state. key={key}, y={_migratedSeaLevelsByPath[key].WaterLevelY:F3}");
            }

            _migratedDeathZonesByPath.Clear();

            foreach (var deathZone in UnityEngine.Object.FindObjectsOfType<DeathZoneRoot>(true))
            {
                if (deathZone == null)
                {
                    continue;
                }

                var key = deathZone.GetMigrationKey();
                if (string.IsNullOrWhiteSpace(key))
                {
                    Debug.LogWarning("[Lobby] Skip death zone migration capture: empty hierarchy path.");
                    continue;
                }

                _migratedDeathZonesByPath[key] = deathZone.CaptureMigrationState();
                Debug.Log($"[Lobby] Captured death zone migration state. key={key}, scaleY={_migratedDeathZonesByPath[key].ScaleY:F3}");
            }
        }

        internal void RestoreEnvironmentStatesAfterMigration()
        {
            if (_migratedSeaLevelsByPath.Count > 0)
            {
                foreach (var seaLevel in UnityEngine.Object.FindObjectsOfType<SeaLevelController>(true))
                {
                    if (seaLevel == null)
                    {
                        continue;
                    }

                    var key = seaLevel.GetMigrationKey();
                    if (!_migratedSeaLevelsByPath.TryGetValue(key, out var state))
                    {
                        continue;
                    }

                    seaLevel.RestoreMigrationState(state);
                    Debug.Log($"[Lobby] Restored sea level migration state. key={key}, y={state.WaterLevelY:F3}");
                }

                _migratedSeaLevelsByPath.Clear();
            }

            if (_migratedDeathZonesByPath.Count > 0)
            {
                foreach (var deathZone in UnityEngine.Object.FindObjectsOfType<DeathZoneRoot>(true))
                {
                    if (deathZone == null)
                    {
                        continue;
                    }

                    var key = deathZone.GetMigrationKey();
                    if (!_migratedDeathZonesByPath.TryGetValue(key, out var state))
                    {
                        continue;
                    }

                    deathZone.RestoreMigrationState(state);
                    Debug.Log($"[Lobby] Restored death zone migration state. key={key}, scaleY={state.ScaleY:F3}");
                }

                _migratedDeathZonesByPath.Clear();
            }
        }

        // 새 방장의 PlayerId가 바뀐 경우, 위치와 캐릭터 선택 테이블의 키를 교체한다.
        // 닉네임 의존 없이 PlayerId만으로 리매핑하므로 닉네임 중복에 완전히 안전하다.
        internal void RemapMigrationEntry(int oldPlayerId, int newPlayerId)
        {
            if (oldPlayerId == newPlayerId)
            {
                return;
            }

            if (_migratedPositionsByOldPlayerId.TryGetValue(oldPlayerId, out var pos))
            {
                _migratedPositionsByOldPlayerId[newPlayerId] = pos;
                _migratedPositionsByOldPlayerId.Remove(oldPlayerId);
                Debug.Log($"[Lobby] Remapped migration position. oldPlayer={oldPlayerId} → newPlayer={newPlayerId}");
            }

            if (_selectedCharacterIndexByPlayerId.TryGetValue(oldPlayerId, out var charIdx))
            {
                _selectedCharacterIndexByPlayerId[newPlayerId] = charIdx;
                _selectedCharacterIndexByPlayerId.Remove(oldPlayerId);
                Debug.Log($"[Lobby] Remapped character selection. oldPlayer={oldPlayerId} → newPlayer={newPlayerId}, charIdx={charIdx}");
            }
        }

        internal void RemapMigrationEntries(IReadOnlyDictionary<int, int> oldToNewPlayerIds)
        {
            if (oldToNewPlayerIds == null || oldToNewPlayerIds.Count == 0)
            {
                return;
            }

            var migratedPositionsSnapshot = new Dictionary<int, (Vector3 position, Quaternion rotation)>(_migratedPositionsByOldPlayerId);
            var selectedCharacterSnapshot = new Dictionary<int, int>(_selectedCharacterIndexByPlayerId);

            _migratedPositionsByOldPlayerId.Clear();
            foreach (var kvp in migratedPositionsSnapshot)
            {
                var finalPlayerId = oldToNewPlayerIds.TryGetValue(kvp.Key, out var remappedPlayerId)
                    ? remappedPlayerId
                    : kvp.Key;
                _migratedPositionsByOldPlayerId[finalPlayerId] = kvp.Value;
            }

            _selectedCharacterIndexByPlayerId.Clear();
            foreach (var kvp in selectedCharacterSnapshot)
            {
                var finalPlayerId = oldToNewPlayerIds.TryGetValue(kvp.Key, out var remappedPlayerId)
                    ? remappedPlayerId
                    : kvp.Key;
                _selectedCharacterIndexByPlayerId[finalPlayerId] = kvp.Value;
            }

            foreach (var kvp in oldToNewPlayerIds)
            {
                if (kvp.Key == kvp.Value)
                {
                    continue;
                }

                Debug.Log($"[Lobby] Batch remapped migration entry. oldPlayer={kvp.Key} → newPlayer={kvp.Value}");
            }
        }

        // GameScene에서 플레이어가 재접속할 때 호출된다.
        // _migrationOldPlayerIdByClientId를 이용해 해당 플레이어의 위치·캐릭터 데이터를
        // 구 PlayerId에서 새 PlayerId로 즉시 리매핑한다.
        // OnHostMigration 완료 시점에는 일부 클라이언트만 재접속 완료되므로
        // 나머지 플레이어는 OnPlayerJoined에서 이 메서드를 통해 개별 리매핑한다.
        internal void TryRemapMigrationEntryOnJoin(NetworkRunner runner, PlayerRef player)
        {
            if (_migrationOldPlayerIdByClientId.Count == 0)
            {
                return;
            }

            var clientId = DecodeConnectionTokenClientId(runner.GetPlayerConnectionToken(player));
            if (string.IsNullOrWhiteSpace(clientId) && player == runner.LocalPlayer)
            {
                EnsureLocalClientId();
                clientId = _localClientId;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            if (!_migrationOldPlayerIdByClientId.TryGetValue(clientId, out var oldPlayerId))
            {
                return;
            }

            _migrationOldPlayerIdByClientId.Remove(clientId);
            RemapMigrationEntry(oldPlayerId, player.PlayerId);
        }

        internal void TryApplyMigrationReadyStateOnJoin(NetworkRunner runner, PlayerRef player)
        {
            var clientId = DecodeConnectionTokenClientId(runner.GetPlayerConnectionToken(player));
            if (string.IsNullOrWhiteSpace(clientId) && player == runner.LocalPlayer)
            {
                EnsureLocalClientId();
                clientId = _localClientId;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                return;
            }

            if (!_migrationReadyStateByClientId.TryGetValue(clientId, out var isReady))
            {
                return;
            }

            _readyStateByPlayerId[player.PlayerId] = isReady;
            if (_roomParticipantsByPlayerId.TryGetValue(player.PlayerId, out var presence) && presence != null)
            {
                presence.IsReady = isReady;
            }
        }

        private string GetMigrationClientId(NetworkRunner runner, PlayerRef player)
        {
            var clientId = DecodeConnectionTokenClientId(runner.GetPlayerConnectionToken(player));
            if (string.IsNullOrWhiteSpace(clientId) && player == runner.LocalPlayer)
            {
                EnsureLocalClientId();
                clientId = _localClientId;
            }

            return clientId;
        }

        // 씬에서 SpawnPointGroup을 찾아 캐시한다. 이미 캐시됐으면 재사용한다.
        private SpawnPointGroup GetOrFindSpawnPointGroup()
        {
            if (_cachedSpawnPointGroup == null)
                _cachedSpawnPointGroup = UnityEngine.Object.FindObjectOfType<SpawnPointGroup>();
            return _cachedSpawnPointGroup;
        }

        // 특정 플레이어의 캐릭터를 SpawnPointGroup 랜덤 포인트에 서버에서 스폰한다.
        // 이미 스폰됐거나 서버가 아닌 경우 즉시 반환한다.
        // 호스트 마이그레이션 진행 중(_isMigrating)에는 스폰을 건너뛴다.
        // 마이그레이션 완료 후 OnHostMigration에서 TrySpawnGameplayNetworkCharactersForAllPlayers()가
        // 복원된 캐릭터 선택 데이터로 올바르게 재스폰을 처리한다.
        private void TrySpawnGameplayNetworkCharacter(PlayerRef player)
        {
            if (_runner == null || !_runner.IsRunning || !_runner.IsServer || !player.IsRealPlayer)
            {
                return;
            }

            if (_isMigrating)
            {
                return;
            }

            if (_deadGameplayPlayerIds.Contains(player.PlayerId))
            {
                Debug.Log($"[Lobby] Skip gameplay spawn for dead player={player.PlayerId}.");
                return;
            }

            var selectedCharacter = _selectedCharacterIndexByPlayerId.TryGetValue(player.PlayerId, out var selected)
                ? SanitizeCharacterIndexOrNone(selected)
                : -1;

            // Random(?) 또는 미선택 상태이면 다른 플레이어와 중복되지 않도록 확정한다.
            // TrySpawnGameplayNetworkCharactersForAllPlayers()를 거치지 않고
            // 개별 호출되는 경우(늦은 재접속, 마이그레이션 후 개별 스폰 등)에도
            // 이미 배정된 캐릭터를 고려해 중복 없이 배정할 수 있도록 여기서도 호출한다.
            if (selectedCharacter < 0 || selectedCharacter == (int)CharacterKind.Random)
            {
                ResolveRandomCharacterSelections();
                selectedCharacter = _selectedCharacterIndexByPlayerId.TryGetValue(player.PlayerId, out var resolved)
                    ? SanitizeCharacterIndexOrNone(resolved)
                    : -1;
            }

            // 씬 전환 직후에는 roster/selection 동기화가 아직 안 끝났을 수 있다.
            // 이 시점에 기본 캐릭터로 폴백하면 잘못된 추가 스폰이 생기므로 확정될 때까지 보류한다.
            if (selectedCharacter < 0)
            {
                Debug.LogWarning($"[Lobby] Deferred gameplay spawn until character selection is resolved. player={player.PlayerId}");
                return;
            }

            if (_spawnedGameplayNetworkCharacters.TryGetValue(player.PlayerId, out var existingSpawned))
            {
                if (_spawnedCharacterIndexByPlayerId.TryGetValue(player.PlayerId, out var existingCharacterIndex) &&
                    existingCharacterIndex == selectedCharacter)
                {
                    return;
                }

                if (existingSpawned != null)
                {
                    UntrackGameplayPlayer(player.PlayerId);
                    // 캐릭터 종류 변경으로 재스폰하는 경우, 현재 위치를 보존한다.
                    // 마이그레이션 위치가 이미 소비된 경우에도 재스폰 위치가 유지된다.
                    if (!_migratedPositionsByOldPlayerId.ContainsKey(player.PlayerId))
                    {
                        _migratedPositionsByOldPlayerId[player.PlayerId] =
                            (existingSpawned.transform.position, existingSpawned.transform.rotation);
                    }

                    try
                    {
                        _runner.Despawn(existingSpawned);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Lobby] Failed to replace character spawn. player={player.PlayerId}, error={e.Message}");
                        return;
                    }
                }

                _spawnedGameplayNetworkCharacters.Remove(player.PlayerId);
                _spawnedCharacterIndexByPlayerId.Remove(player.PlayerId);
            }

            var prefab = GetGameplayCharacterPrefabByIndex(selectedCharacter);
            if (prefab == null)
            {
                Debug.LogWarning($"[Lobby] Gameplay prefab is missing for player={player.PlayerId}, index={selectedCharacter}");
                return;
            }

            // Fusion이 네트워크로 스폰하려면 프리팹에 NetworkObject 컴포넌트가 반드시 있어야 한다.
            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"[Lobby] {prefab.name} prefab is missing NetworkObject component. Add NetworkObject to character prefab for network spawn.");
                return;
            }

            // 마이그레이션 위치 복원: PlayerId로 직접 조회한다.
            // 새 방장의 PlayerId가 바뀐 경우 OnHostMigration에서 RemapMigrationEntry()로
            // 미리 키가 교체돼 있으므로 여기서는 항상 직접 조회만 수행한다.
            // 조회 실패 시 SpawnPointGroup에서 랜덤 배정한다.
            Vector3 spawnPosition = new Vector3(0f, 1f, 0f);
            Quaternion spawnRotation = Quaternion.identity;

            var useMigratedPosition = false;
            var hasCapturedMigrationState = false;
            var capturedMigrationState = default((Vector3 position, Quaternion rotation));
            var capturedMigrationClientId = GetMigrationClientId(_runner, player);
            Debug.Log($"[Lobby] Resolving spawn restore. player={player.PlayerId}, clientId={capturedMigrationClientId}, selectedCharacter={selectedCharacter}");
            if (!string.IsNullOrWhiteSpace(capturedMigrationClientId) &&
                _migratedPositionsByClientId.TryGetValue(capturedMigrationClientId, out var capturedByClientId))
            {
                hasCapturedMigrationState = true;
                capturedMigrationState = capturedByClientId;
                if (capturedByClientId.position.y > -50f && capturedByClientId.position.magnitude < 10000f)
                {
                    spawnPosition = capturedByClientId.position;
                    spawnRotation = capturedByClientId.rotation;
                    useMigratedPosition = true;
                    Debug.Log($"[Lobby] Restoring position from clientId migration. player={player.PlayerId}, clientId={capturedMigrationClientId}, pos={spawnPosition}");
                }
                else
                {
                    Debug.LogWarning($"[Lobby] Discarding out-of-bounds clientId migration position {capturedByClientId.position} for player={player.PlayerId}, clientId={capturedMigrationClientId}. Using spawn point instead.");
                }
            }
            else if (_migratedPositionsByOldPlayerId.TryGetValue(player.PlayerId, out var captured))
            {
                hasCapturedMigrationState = true;
                capturedMigrationState = captured;
                // Y < -50 이거나 원점 기준 10000 초과 시 월드 밖으로 떨어진 것으로 간주해 폐기한다.
                if (captured.position.y > -50f && captured.position.magnitude < 10000f)
                {
                    spawnPosition = captured.position;
                    spawnRotation = captured.rotation;
                    useMigratedPosition = true;
                    Debug.Log($"[Lobby] Restoring position from migration. player={player.PlayerId}, pos={spawnPosition}");
                }
                else
                {
                    Debug.LogWarning($"[Lobby] Discarding out-of-bounds migration position {captured.position} for player={player.PlayerId}. Using spawn point instead.");
                }
            }
            else
            {
                Debug.LogWarning($"[Lobby] No migration state found for player={player.PlayerId}, clientId={capturedMigrationClientId}. Falling back to spawn points.");
            }

            if (!useMigratedPosition)
            {
                var spawnGroup = GetOrFindSpawnPointGroup();
                if (spawnGroup == null || !spawnGroup.ClaimRandomPoint(out spawnPosition, out spawnRotation))
                {
                    if (fallbackSpawnPoints != null && fallbackSpawnPoints.Length > 0)
                    {
                        var fp = fallbackSpawnPoints[Random.Range(0, fallbackSpawnPoints.Length)];
                        if (fp != null)
                        {
                            spawnPosition = fp.position;
                            spawnRotation = fp.rotation;
                            Debug.LogWarning($"[Lobby] SpawnPointGroup unavailable. player={player.PlayerId}, using inspector fallback point '{fp.name}'.");
                        }
                        else
                        {
                            Debug.LogWarning($"[Lobby] Fallback spawn point is null. player={player.PlayerId}, using default position.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Lobby] SpawnPointGroup not found and no fallback points set. player={player.PlayerId}, using default position.");
                    }
                }
            }

            try
            {
                // onBeforeSpawned에서 확정된 캐릭터 인덱스를 NetworkPlayer에 기록한다.
                // ? 선택자도 실제 배정값으로 저장되므로, 마이그레이션 캡처 시
                // roster 수신 여부와 무관하게 외형을 보존할 수 있다.
                var charIndexToStore = selectedCharacter;
                var spawned = _runner.Spawn(prefab, spawnPosition, spawnRotation, player,
                    onBeforeSpawned: (_, obj) =>
                    {
                        var np = obj.GetComponent<NetworkPlayer>();
                        if (np != null)
                            np.CharacterTypeIndex = charIndexToStore;
                    });
                if (spawned == null)
                {
                    Debug.LogWarning($"[Lobby] Network spawn returned null. player={player.PlayerId}, prefab={prefab.name}");
                    return;
                }

                TrackGameplayPlayer(player.PlayerId, spawned);

                if (hasCapturedMigrationState &&
                    !string.IsNullOrWhiteSpace(capturedMigrationClientId) &&
                    _migratedPositionsByClientId.TryGetValue(capturedMigrationClientId, out var pendingCapturedByClientId) &&
                    pendingCapturedByClientId.position == capturedMigrationState.position &&
                    pendingCapturedByClientId.rotation == capturedMigrationState.rotation)
                {
                    _migratedPositionsByClientId.Remove(capturedMigrationClientId);
                }

                if (hasCapturedMigrationState &&
                    _migratedPositionsByOldPlayerId.TryGetValue(player.PlayerId, out var pendingCaptured) &&
                    pendingCaptured.position == capturedMigrationState.position &&
                    pendingCaptured.rotation == capturedMigrationState.rotation)
                {
                    _migratedPositionsByOldPlayerId.Remove(player.PlayerId);
                }

                _spawnedGameplayNetworkCharacters[player.PlayerId] = spawned;
                _spawnedCharacterIndexByPlayerId[player.PlayerId] = selectedCharacter;
                Debug.Log($"[Lobby] Spawned network character. player={player.PlayerId}, clientId={capturedMigrationClientId}, prefab={prefab.name}, pos={spawnPosition}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Lobby] Failed to spawn network character. player={player.PlayerId}, prefab={prefab.name}, error={e.Message}");
            }
        }

        // 마이그레이션 중 수신했으나 _isMigrating 가드로 보류된 캐릭터 선택을 일괄 처리한다.
        // OnHostMigration의 finally에서 _isMigrating = false 직후 호출한다.
        internal void FlushPendingMigrationSpawns()
        {
            if (_pendingCharacterSelectionsWhileMigrating.Count == 0)
            {
                return;
            }

            if (!IsActiveGameplayScene() || _runner == null || !_runner.IsRunning || !_runner.IsServer)
            {
                Debug.LogWarning($"[Lobby] Discarding {_pendingCharacterSelectionsWhileMigrating.Count} pending migration spawn(s): conditions not met (scene={IsActiveGameplayScene()}, runnerRunning={_runner != null && _runner.IsRunning}, isServer={_runner != null && _runner.IsServer}).");
                _pendingCharacterSelectionsWhileMigrating.Clear();
                return;
            }

            foreach (var kvp in _pendingCharacterSelectionsWhileMigrating)
            {
                var player = kvp.Key;
                var charIdx = kvp.Value;
                _selectedCharacterIndexByPlayerId[player.PlayerId] = charIdx;
                if (_roomParticipantsByPlayerId.TryGetValue(player.PlayerId, out var presence) && presence != null)
                {
                    presence.CharacterIndex = charIdx;
                }
                Debug.Log($"[Lobby] Flushing pending migration spawn. player={player.PlayerId}, charIdx={charIdx}");
                TrySpawnGameplayNetworkCharacter(player);
            }
            _pendingCharacterSelectionsWhileMigrating.Clear();
        }

        private void TrackGameplayPlayer(int playerId, NetworkObject spawnedObject)
        {
            if (spawnedObject == null)
                return;

            UntrackGameplayPlayer(playerId);

            var networkPlayer = spawnedObject.GetComponent<NetworkPlayer>();
            if (networkPlayer == null)
                return;

            networkPlayer.OnNetworkPlayerDied -= HandleTrackedGameplayPlayerDied;
            networkPlayer.OnNetworkPlayerDied += HandleTrackedGameplayPlayerDied;
            _trackedGameplayPlayersByPlayerId[playerId] = networkPlayer;

            if (networkPlayer.IsDeadState)
                _deadGameplayPlayerIds.Add(playerId);
        }

        private void UntrackGameplayPlayer(int playerId)
        {
            if (!_trackedGameplayPlayersByPlayerId.TryGetValue(playerId, out var trackedPlayer) || trackedPlayer == null)
            {
                _trackedGameplayPlayersByPlayerId.Remove(playerId);
                return;
            }

            trackedPlayer.OnNetworkPlayerDied -= HandleTrackedGameplayPlayerDied;
            _trackedGameplayPlayersByPlayerId.Remove(playerId);
        }

        private void UntrackAllGameplayPlayers()
        {
            foreach (var playerId in _trackedGameplayPlayersByPlayerId.Keys.ToArray())
                UntrackGameplayPlayer(playerId);
        }

        private void HandleTrackedGameplayPlayerDied(NetworkPlayer deadPlayer)
        {
            if (deadPlayer == null)
                return;

            var networkObject = deadPlayer.GetComponent<NetworkObject>();
            if (networkObject == null || !networkObject.InputAuthority.IsRealPlayer)
                return;

            var playerId = networkObject.InputAuthority.PlayerId;
            _deadGameplayPlayerIds.Add(playerId);
            Debug.Log($"[Lobby] Marked gameplay player as dead. player={playerId}, object={deadPlayer.name}");
        }

        // 현재 접속된 모든 플레이어에 대해 캐릭터 스폰을 순차 호출한다.
        // OnSceneLoadDone 시점에 서버에서 한 번 호출된다.
        private void TrySpawnGameplayNetworkCharactersForAllPlayers()
        {
            if (_runner == null || !_runner.IsRunning || !_runner.IsServer || !IsActiveGameplayScene())
            {
                return;
            }

            _gameplaySceneSpawnBootstrapComplete = false;
            ResolveRandomCharacterSelections();

            // ? 선택을 확정된 캐릭터 인덱스로 클라이언트에 동기화한다.
            // ResolveRandomCharacterSelections()가 _selectedCharacterIndexByPlayerId를 갱신했으므로
            // 이 시점에 Roster를 브로드캐스트하면 클라이언트의 _selectedCharacterIndexByPlayerId에도
            // 확정값이 저장된다. 이로써 호스트 마이그레이션 시 savedAllCharacterIndices가
            // ? 대신 실제 캐릭터 인덱스를 캡처하게 되어 재추첨이 발생하지 않는다.
            BroadcastPlayerRoster();

            foreach (var player in GetOrderedActivePlayers())
            {
                TrySpawnGameplayNetworkCharacter(player);
            }

            _gameplaySceneSpawnBootstrapComplete = true;
        }
    }
}
