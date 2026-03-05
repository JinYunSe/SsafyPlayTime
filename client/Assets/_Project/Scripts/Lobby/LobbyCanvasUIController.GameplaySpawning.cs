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

        // 씬에서 SpawnPointGroup을 찾아 캐시한다. 이미 캐시됐으면 재사용한다.
        private SpawnPointGroup GetOrFindSpawnPointGroup()
        {
            if (_cachedSpawnPointGroup == null)
                _cachedSpawnPointGroup = UnityEngine.Object.FindObjectOfType<SpawnPointGroup>();
            return _cachedSpawnPointGroup;
        }

        // 특정 플레이어의 캐릭터를 SpawnPointGroup 랜덤 포인트에 서버에서 스폰한다.
        // 이미 스폰됐거나 서버가 아닌 경우 즉시 반환한다.
        private void TrySpawnGameplayNetworkCharacter(PlayerRef player)
        {
            if (_runner == null || !_runner.IsRunning || !_runner.IsServer || !player.IsRealPlayer)
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

            var spawnGroup = GetOrFindSpawnPointGroup();
            Vector3 spawnPosition;
            Quaternion spawnRotation;
            if (spawnGroup == null || !spawnGroup.ClaimRandomPoint(out spawnPosition, out spawnRotation))
            {
                spawnPosition = new Vector3(0f, 1f, 0f);
                spawnRotation = Quaternion.identity;
                Debug.LogWarning($"[Lobby] SpawnPointGroup not found or no available points. player={player.PlayerId}, using fallback position.");
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
