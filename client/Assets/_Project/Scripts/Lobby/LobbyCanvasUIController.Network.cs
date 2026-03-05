using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace SSAFYPlayTime
{
    public sealed partial class LobbyCanvasUIController
    {
        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            _isInLobby = true;
            _lastSessionListUpdatedAtUtc = DateTime.UtcNow;
            _roomSnapshots.Clear();
            foreach (var session in sessionList)
            {
                if (!session.IsValid || !session.IsVisible)
                {
                    continue;
                }

                var snapshot = BuildSnapshot(session);
                if (string.IsNullOrWhiteSpace(snapshot.Name))
                {
                    continue;
                }

                _roomSnapshots.Add(snapshot);
            }

            if (_isNicknameConfirmed && lobbyPanel.activeSelf)
            {
                RefreshRoomList();
            }

            SetLobbyStatus(string.Format(statusRoomsUpdatedFormat, _roomSnapshots.Count));
            LogRoomSnapshotSummary();
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner != _runner)
            {
                return;
            }

            if (runner.IsServer)
            {
                TryRegisterParticipantFromToken(runner, player);

                if (player == runner.LocalPlayer && !_roomParticipantsByPlayerId.ContainsKey(player.PlayerId))
                {
                    RegisterParticipant(player, _nickname);
                }

                if (_currentOwnerPlayerId <= 0 && runner.LocalPlayer.IsRealPlayer)
                {
                    _currentOwnerPlayerId = runner.LocalPlayer.PlayerId;
                    _currentRoomOwner = _nickname;
                    UpdateOwnerSessionProperty();
                }

                BroadcastPlayerRoster();
            }
            else if (player == runner.LocalPlayer)
            {
                RegisterParticipant(player, _nickname);
            }

            if (runner.IsServer && IsActiveGameplayScene())
            {
                TrySpawnGameplayNetworkCharacter(player);
            }

            if (roomPanel.activeSelf)
            {
                UpdateRoomPanel();
            }
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (runner != _runner)
            {
                return;
            }

            if (runner.IsServer && _spawnedGameplayNetworkCharacters.TryGetValue(player.PlayerId, out var spawned) && spawned != null)
            {
                try
                {
                    runner.Despawn(spawned);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Lobby] Failed to despawn player character. player={player.PlayerId}, error={e.Message}");
                }
            }
            _spawnedGameplayNetworkCharacters.Remove(player.PlayerId);

            if (player.IsRealPlayer)
            {
                _roomParticipantsByPlayerId.Remove(player.PlayerId);
                _selectedCharacterIndexByPlayerId.Remove(player.PlayerId);
            }

            if (runner.IsServer)
            {
                RecalculateOwnerAfterLeave();
                BroadcastPlayerRoster();
            }

            if (roomPanel.activeSelf)
            {
                UpdateRoomPanel();
            }
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (runner != _runner)
            {
                Debug.Log("[Lobby] Ignored shutdown callback from stale runner.");
                return;
            }

            _isInLobby = false;
            if (lobbyPanel.activeSelf)
            {
                SetLobbyStatus(string.Format(statusDisconnectedFormat, shutdownReason));
            }
            Debug.Log($"[Lobby] Runner shutdown: reason={shutdownReason}");

            if (_isShuttingDownRunner || _isProcessing)
            {
                Debug.Log("[Lobby] Shutdown in progress, skip recovery.");
                return;
            }

        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            if (runner != _runner)
            {
                Debug.Log("[Lobby] Ignored disconnect callback from stale runner.");
                return;
            }

            _isInLobby = false;

            if (_isShuttingDownRunner || _isProcessing)
            {
                Debug.Log("[Lobby] Shutdown in progress, skip disconnect recovery.");
                return;
            }

        }

        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            if (runner != _runner || !runner.IsServer)
            {
                return;
            }

            var nickname = DecodeConnectionToken(token);
            if (!string.IsNullOrEmpty(nickname))
            {
                Debug.Log($"[Lobby] Connect request from {request.RemoteAddress}: {PlayerRosterLabel}={nickname}");
            }
        }

        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

        async void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
            if (runner != _runner)
            {
                return;
            }

            if (_isProcessing)
            {
                return;
            }

            _isProcessing = true;
            try
            {
                Debug.Log("[Lobby] Host migration started.");

                // 가장 낮은 PlayerId(먼저 입장한 유저)가 새 방장이 되도록 우선순위 딜레이 적용.
                // 현재 방장(_currentOwnerPlayerId)은 떠난 상태이므로 제외하고 정렬.
                var localPlayerId = runner.LocalPlayer.PlayerId;
                var orderedRemaining = _roomParticipantsByPlayerId.Keys
                    .Where(id => id != _currentOwnerPlayerId)
                    .OrderBy(id => id)
                    .ToList();
                var priorityIndex = orderedRemaining.IndexOf(localPlayerId);

                // ShutdownRunnerAsync 가 _selectedCharacterIndexByPlayerId 를 Clear 하므로
                // 재연결 후 복원할 수 있도록 미리 캡처해 둠 (async 로컬 변수는 await 를 넘어 유지됨)
                var savedCharacterIndex = _selectedCharacterIndexByPlayerId.TryGetValue(localPlayerId, out var sc)
                    ? SanitizeCharacterIndexOrNone(sc)
                    : -1;

                if (priorityIndex > 0)
                {
                    // 낮은 ID 플레이어가 먼저 StartGame 을 호출하도록 대기
                    await Task.Delay(priorityIndex * 350);
                }

                await ShutdownRunnerAsync();

                if (!TryCreateRunner(out var newRunner))
                {
                    SetLobbyStatus(statusHostMigrationInitFailed);
                    return;
                }

                _runner = newRunner;
                var appSettings = GetOrCreatePhotonSettings();
                var result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = hostMigrationToken.GameMode,
                    SessionName = _currentRoomName,
                    SceneManager = GetOrAddSceneManager(),
                    // 재연결 시 닉네임 토큰을 포함해야 새 방장이 각 플레이어의 닉네임을 식별할 수 있음
                    ConnectionToken = BuildConnectionToken(_nickname),
                    CustomLobbyName = SharedLobbyName,
                    CustomPhotonAppSettings = appSettings,
                    HostMigrationToken = hostMigrationToken
                });

                if (!result.Ok)
                {
                    SetLobbyStatus(string.Format(statusHostMigrationFailedFormat, result.ShutdownReason));
                    Debug.LogWarning($"[Lobby] Host migration failed: {result.ShutdownReason}");
                    return;
                }

                // 재연결 후, RegisterParticipant 호출 전에 dict 를 복원해 둠.
                // → 서버: RegisterParticipant 가 저장된 인덱스를 읽어 roster 에 포함시킴.
                // → 클라이언트: OnPlayerJoined 타이밍과 무관하게 dict 에 값이 있어 RegisterParticipant 가 읽어감.
                if (savedCharacterIndex >= 0 && _runner.LocalPlayer.IsRealPlayer)
                {
                    _selectedCharacterIndexByPlayerId[_runner.LocalPlayer.PlayerId] = savedCharacterIndex;
                }

                if (_runner.IsServer && _runner.LocalPlayer.IsRealPlayer)
                {
                    RegisterParticipant(_runner.LocalPlayer, _nickname);
                    _currentOwnerPlayerId = _runner.LocalPlayer.PlayerId;
                    _currentRoomOwner = _nickname;
                    UpdateOwnerSessionProperty();
                    BroadcastPlayerRoster();
                }

                Debug.Log("[Lobby] Host migration completed.");
                ShowRoomPanel();
                UpdateRoomPanel();

                // 클라이언트는 새 방장에게 캐릭터 선택을 재전송해야 roster 에 반영됨.
                // 서버는 이미 RegisterParticipant + BroadcastPlayerRoster 로 처리됨.
                if (!_runner.IsServer && savedCharacterIndex >= 0 && _runner.LocalPlayer.IsRealPlayer)
                {
                    SetLocalPlayerSelectedCharacter(savedCharacterIndex);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
            if (runner != _runner)
            {
                return;
            }

            string payload;
            try
            {
                payload = data.Array == null
                    ? string.Empty
                    : Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);
            }
            catch
            {
                payload = string.Empty;
            }

            if (key == PlayerRosterReliableKey)
            {
                ApplyRosterPayload(payload);

                if (roomPanel.activeSelf)
                {
                    UpdateRoomPanel();
                }
                return;
            }

            if (key == CharacterSelectionReliableKey && _runner != null && _runner.IsRunning && _runner.IsServer)
            {
                if (player.IsRealPlayer && int.TryParse(payload, out var selected))
                {
                    var normalized = SanitizeCharacterIndexOrNone(selected);
                    if (normalized >= 0)
                    {
                        _selectedCharacterIndexByPlayerId[player.PlayerId] = normalized;
                        if (_roomParticipantsByPlayerId.TryGetValue(player.PlayerId, out var presence) && presence != null)
                        {
                            presence.CharacterIndex = normalized;
                        }

                        BroadcastPlayerRoster();
                    }
                }

                if (roomPanel.activeSelf)
                {
                    UpdateRoomPanel();
                }
            }
        }

        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
        {
            if (runner != _runner)
            {
                return;
            }

            _spawnedGameplayNetworkCharacters.Clear();
        }

        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
        {
            if (runner != _runner)
            {
                return;
            }

            if (IsActiveGameplayScene())
            {
                if (nicknamePanel != null) nicknamePanel.SetActive(false);
                if (lobbyPanel != null) lobbyPanel.SetActive(false);
                if (roomPanel != null) roomPanel.SetActive(false);
                if (createRoomModal != null) createRoomModal.SetActive(false);
                if (passwordModal != null) passwordModal.SetActive(false);

                // 게임씬 전환 시 로비 UI 캐릭터 미리보기 전부 숨김
                HideAllCharacterSlots();

                if (runner.IsServer)
                {
                    TrySpawnGameplayNetworkCharactersForAllPlayers();
                }
            }
        }

        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        private void LogRoomSnapshotSummary()
        {
            var summary = _roomSnapshots.Count == 0
                ? "-"
                : string.Join(", ", _roomSnapshots.Select(r => $"{r.Name}({r.PlayerCount}/{MaxPlayers})"));
            Debug.Log($"[Lobby] Session list updated: count={_roomSnapshots.Count}, rooms={summary}, updatedAtUtc={_lastSessionListUpdatedAtUtc:O}");
        }
    }
}
