using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SSAFYPlayTime
{
    public sealed partial class LobbyCanvasUIController
    {
        [Header("Gameplay Spawning")]
        [SerializeField] private GameObject aiJiGameplayCharacterPrefab;
        [SerializeField] private GameObject pitGameplayCharacterPrefab;
        [SerializeField] private GameObject seuTatiGameplayCharacterPrefab;
        [SerializeField] private GameObject waiJeuGameplayCharacterPrefab;
        [SerializeField] private string gameplaySpawnPointName = "PlayerSpawnPoint";
        [SerializeField] private Vector3 gameplayFallbackSpawnPosition = new Vector3(0f, 1f, 0f);

        private bool _isPersistentAcrossScenes;
        private readonly Dictionary<int, NetworkObject> _spawnedGameplayNetworkCharacters = new();

        private void EnsurePersistentAcrossScenes()
        {
            if (_isPersistentAcrossScenes)
            {
                return;
            }

            DontDestroyOnLoad(gameObject);
            _isPersistentAcrossScenes = true;
        }

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

        private Vector3 ResolveSpawnPositionForPlayer(int playerId)
        {
            var spawnPosition = gameplayFallbackSpawnPosition;
            if (!string.IsNullOrWhiteSpace(gameplaySpawnPointName))
            {
                var spawnPoint = GameObject.Find(gameplaySpawnPointName);
                if (spawnPoint != null)
                {
                    spawnPosition = spawnPoint.transform.position;
                }
            }

            var orderedPlayers = GetOrderedActivePlayers();
            var slotIndex = orderedPlayers.FindIndex(p => p.PlayerId == playerId);
            if (slotIndex < 0)
            {
                slotIndex = 0;
            }

            // Place up to 4 players in a horizontal line around the spawn center.
            var lateralOffset = (slotIndex - 1.5f) * 2.2f;
            return spawnPosition + new Vector3(lateralOffset, 0f, 0f);
        }

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

            var spawnPosition = ResolveSpawnPositionForPlayer(player.PlayerId);
            try
            {
                var spawned = _runner.Spawn(prefab, spawnPosition, Quaternion.identity, player);
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
