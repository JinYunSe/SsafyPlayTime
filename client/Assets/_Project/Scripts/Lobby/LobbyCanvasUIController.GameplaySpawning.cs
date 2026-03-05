using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SSAFYPlayTime
{
    // GameScene에서의 캐릭터 스폰을 담당하는 파셜 클래스.
    // 서버(IsServer)에서만 동작하며 각 플레이어에게 선택한 캐릭터를 Fusion으로 생성한다.
    public sealed partial class LobbyCanvasUIController
    {
        [Header("Gameplay Spawning")]
        // 각 캐릭터 종류별 게임플레이용 프리팹 (NetworkObject 포함)
        [SerializeField] private GameObject aiJiGameplayCharacterPrefab;
        [SerializeField] private GameObject pitGameplayCharacterPrefab;
        [SerializeField] private GameObject seuTatiGameplayCharacterPrefab;
        [SerializeField] private GameObject waiJeuGameplayCharacterPrefab;

        // 씬 전환 후 DontDestroyOnLoad 처리됐는지 여부 (중복 호출 방지)
        private bool _isPersistentAcrossScenes;

        // 스폰된 캐릭터 NetworkObject를 PlayerId 키로 관리 (퇴장 시 Despawn에 사용)
        private readonly Dictionary<int, NetworkObject> _spawnedGameplayNetworkCharacters = new();

        // 씬에서 찾아둔 SpawnPointGroup 캐시 (OnSceneLoadStart 시 null 초기화)
        private SpawnPointGroup _cachedSpawnPointGroup;

        // 호스트 마이그레이션 직전에 캡처한 각 플레이어의 캐릭터 위치/회전 (구 PlayerId 키).
        // 재접속 후 PlayerId가 유지되면 직접 조회한다.
        // 새 방장의 PlayerId가 바뀐 경우 OnHostMigration에서 RemapMigrationEntry로 미리 키를 교체한다.
        private readonly Dictionary<int, (Vector3 position, Quaternion rotation)> _migratedPositionsByOldPlayerId = new();

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

            DontDestroyOnLoad(gameObject);
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
            return SanitizeCharacterIndexOrNone(characterIndex) switch
            {
                (int)CharacterKind.AiJi => aiJiGameplayCharacterPrefab,
                (int)CharacterKind.Pit => pitGameplayCharacterPrefab,
                (int)CharacterKind.SeuTati => seuTatiGameplayCharacterPrefab,
                (int)CharacterKind.WaiJeu => waiJeuGameplayCharacterPrefab,
                _ => null
            };
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

            foreach (var np in UnityEngine.Object.FindObjectsOfType<NetworkPlayer>())
            {
                var no = np.GetComponent<NetworkObject>();
                if (no == null || !no.InputAuthority.IsRealPlayer)
                {
                    continue;
                }

                var playerId = no.InputAuthority.PlayerId;
                _migratedPositionsByOldPlayerId[playerId] = (np.transform.position, np.transform.rotation);
                Debug.Log($"[Lobby] Captured migration state. player={playerId}, pos={np.transform.position}");
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

            if (_spawnedGameplayNetworkCharacters.ContainsKey(player.PlayerId))
            {
                return;
            }

            var selectedCharacter = _selectedCharacterIndexByPlayerId.TryGetValue(player.PlayerId, out var selected)
                ? SanitizeCharacterIndexOrNone(selected)
                : -1;
            if (selectedCharacter < 0)
            {
                selectedCharacter = (int)CharacterKind.AiJi;
            }

            var prefab = GetGameplayCharacterPrefabByIndex(selectedCharacter);
            if (prefab == null)
            {
                Debug.LogWarning($"[Lobby] Gameplay prefab is missing for player={player.PlayerId}, index={selectedCharacter}");
                return;
            }

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError($"[Lobby] {prefab.name} prefab is missing NetworkObject component. Add NetworkObject to character prefab for network spawn.");
                return;
            }

            // 마이그레이션 위치 복원: PlayerId로 직접 조회한다.
            // 새 방장의 PlayerId가 바뀐 경우 OnHostMigration에서 RemapMigrationEntry()로
            // 미리 키가 교체돼 있으므로 여기서는 항상 직접 조회만 수행한다.
            // 조회 실패 시 SpawnPointGroup에서 랜덤 배정한다.
            Vector3 spawnPosition;
            Quaternion spawnRotation;

            if (_migratedPositionsByOldPlayerId.TryGetValue(player.PlayerId, out var captured))
            {
                spawnPosition = captured.position;
                spawnRotation = captured.rotation;
                _migratedPositionsByOldPlayerId.Remove(player.PlayerId);
                Debug.Log($"[Lobby] Restoring position from migration. player={player.PlayerId}, pos={spawnPosition}");
            }
            else
            {
                var spawnGroup = GetOrFindSpawnPointGroup();
                if (spawnGroup == null || !spawnGroup.ClaimRandomPoint(out spawnPosition, out spawnRotation))
                {
                    spawnPosition = new Vector3(0f, 1f, 0f);
                    spawnRotation = Quaternion.identity;
                    Debug.LogWarning($"[Lobby] SpawnPointGroup not found or no available points. player={player.PlayerId}, using fallback position.");
                }
            }

            try
            {
                var spawned = _runner.Spawn(prefab, spawnPosition, spawnRotation, player);
                if (spawned == null)
                {
                    Debug.LogWarning($"[Lobby] Network spawn returned null. player={player.PlayerId}, prefab={prefab.name}");
                    return;
                }

                _spawnedGameplayNetworkCharacters[player.PlayerId] = spawned;
                Debug.Log($"[Lobby] Spawned network character. player={player.PlayerId}, prefab={prefab.name}");
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

        // 현재 접속된 모든 플레이어에 대해 캐릭터 스폰을 순차 호출한다.
        // OnSceneLoadDone 시점에 서버에서 한 번 호출된다.
        private void TrySpawnGameplayNetworkCharactersForAllPlayers()
        {
            if (_runner == null || !_runner.IsRunning || !_runner.IsServer || !IsActiveGameplayScene())
            {
                return;
            }

            foreach (var player in GetOrderedActivePlayers())
            {
                TrySpawnGameplayNetworkCharacter(player);
            }
        }
    }
}
