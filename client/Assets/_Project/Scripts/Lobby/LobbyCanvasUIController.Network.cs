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
        // Fusion 로비에서 방 목록이 갱신될 때 호출. UI에 반영할 RoomSnapshot 목록을 재구성한다.
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

        // 플레이어가 방에 입장했을 때 호출. 서버는 참가자를 등록하고 방장을 지정한다.
        // GameScene이 활성화된 상태라면 즉시 캐릭터 스폰도 수행한다.
        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner != _runner)
            {
                return;
            }

            if (runner.IsServer)
            {
                // 연결 토큰(닉네임)을 이용해 입장한 플레이어를 참가자 목록에 등록한다.
                TryRegisterParticipantFromToken(runner, player);

                // 로컬 플레이어(방장 본인)가 토큰 없이 입장한 경우 닉네임으로 직접 등록한다.
                if (player == runner.LocalPlayer && !_roomParticipantsByPlayerId.ContainsKey(player.PlayerId))
                {
                    RegisterParticipant(player, _nickname);
                }

                // 방장이 아직 지정되지 않은 경우 현재 로컬 플레이어를 방장으로 설정한다.
                // (방 생성 직후 첫 OnPlayerJoined 또는 호스트 마이그레이션 직후에 해당)
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
                // 클라이언트는 자신이 입장했을 때만 로컬 정보를 등록한다.
                // 다른 플레이어 정보는 방장이 보내는 Roster를 통해 동기화된다.
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

        // 플레이어가 방을 나갔을 때 호출. 스폰된 캐릭터를 제거하고 참가자 목록을 정리한다.
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
            _spawnedCharacterIndexByPlayerId.Remove(player.PlayerId);

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

            // ShutdownRunnerAsync 또는 방 생성/입장 처리 중에 발생한 종료는
            // 상위 흐름에서 이미 처리 중이므로 여기서 추가 복구를 하지 않는다.
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

                // 모든 플레이어의 캐릭터 선택을 캡처한다.
                // ShutdownRunnerAsync가 dict를 Clear하므로 shutdown 전에 전체를 저장해야
                // 방장 이전 후 각 플레이어가 선택했던 캐릭터가 유지된다.
                var savedAllCharacterIndices = _selectedCharacterIndexByPlayerId
                    .Where(kvp => SanitizeCharacterIndexOrNone(kvp.Value) >= 0)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var savedNicknamesByOldPlayerId = _roomParticipantsByPlayerId.Values
                    .Where(p => p != null && p.PlayerId > 0 && !string.IsNullOrWhiteSpace(p.Nickname))
                    .ToDictionary(p => p.PlayerId, p => SanitizeNameToken(p.Nickname));

                if (priorityIndex > 0)
                {
                    // 낮은 ID 플레이어가 먼저 StartGame 을 호출하도록 대기
                    await Task.Delay(priorityIndex * 350);
                }

                // GameScene에서 마이그레이션 발생 시 ShutdownRunnerAsync 전에 위치를 캡처한다.
                // NetworkObject가 아직 살아있는 상태에서 읽어야 정확한 위치를 얻을 수 있다.
                if (IsActiveGameplayScene())
                {
                    _isMigrating = true;
                    CaptureCharacterStatesForMigration();
                }

                // StartGame 후 새 PlayerId를 알 수 있으므로 로컬 플레이어의 구 PlayerId를 미리 저장한다.
                var localOldPlayerId = runner.LocalPlayer.PlayerId;

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

                // 재연결 후, RegisterParticipant 호출 전에 모든 플레이어의 캐릭터 선택을 복원한다.
                // → 서버: RegisterParticipant 가 복원된 인덱스를 읽어 roster 에 올바르게 포함시킴.
                // → 클라이언트: OnPlayerJoined 타이밍과 무관하게 dict 에 값이 있어 RegisterParticipant 가 읽어감.
                foreach (var kvp in savedAllCharacterIndices)
                {
                    _selectedCharacterIndexByPlayerId[kvp.Key] = kvp.Value;
                }

                RemapMigrationEntriesByNickname(_runner, savedNicknamesByOldPlayerId);

                // 새 방장의 PlayerId가 바뀐 경우 위치·캐릭터 선택 테이블의 키를 새 PlayerId로 교체한다.
                // 닉네임을 사용하지 않으므로 닉네임 중복에 완전히 안전하다.
                if (IsActiveGameplayScene())
                {
                    RemapMigrationEntry(localOldPlayerId, _runner.LocalPlayer.PlayerId);
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

                if (IsActiveGameplayScene())
                {
                    // ── GameScene 방장 이전 처리 ──────────────────────────────────────
                    // StartGame 완료 이후에는 OnSceneLoadStart가 재발동하지 않으므로
                    // 마이그레이션 데이터 보호가 더 이상 필요하지 않다.
                    // 여기서 해제해야 TrySpawnGameplayNetworkCharacter의 _isMigrating 가드를 통과할 수 있다.
                    _isMigrating = false;

                    // 서버(새 방장): TrySpawnGameplayNetworkCharactersForAllPlayers()로 자신을 먼저 스폰한다.
                    // 다른 클라이언트는 아직 재접속 전이므로 여기서는 스폰하지 않는다.
                    if (_runner.IsServer)
                    {
                        TrySpawnGameplayNetworkCharactersForAllPlayers();
                    }

                    // 모든 플레이어(서버·클라이언트 공통)가 자신의 캐릭터 선택을 재전송한다.
                    // PlayerId가 바뀌더라도 각자가 직접 알리므로 새 방장이 항상 올바른 데이터를 갖는다.
                    // - 서버(새 방장): SetLocalPlayerSelectedCharacter → BroadcastPlayerRoster
                    // - 클라이언트: SetLocalPlayerSelectedCharacter → SendCharacterSelectionToHost
                    //   → 새 방장의 OnReliableDataReceived에서 수신 후 TrySpawnGameplayNetworkCharacter 호출
                    // 연쇄 마이그레이션도 동일한 흐름으로 처리된다.
                    // _localSelectedCharacterIndex가 미설정(-1)인 경우 savedCharacterIndex → Ssaty 순으로 폴백해
                    // 항상 유효한 캐릭터를 전송함으로써 스폰 누락을 방지한다.
                    if (_runner.LocalPlayer.IsRealPlayer)
                    {
                        var charToSend = _localSelectedCharacterIndex >= 0
                            ? _localSelectedCharacterIndex
                            : savedCharacterIndex >= 0
                                ? savedCharacterIndex
                                : (int)CharacterKind.Ssaty;
                        SetLocalPlayerSelectedCharacter(charToSend);
                    }
                }
                else
                {
                    // ── LauncherScene(로비) 방장 이전 처리 ───────────────────────────
                    // 기존 방 패널로 복귀해 대기 상태를 유지한다.
                    ShowRoomPanel();
                    UpdateRoomPanel();

                    // 클라이언트는 새 방장에게 캐릭터 선택을 재전송해야 roster 에 반영됨.
                    // 서버는 이미 RegisterParticipant + BroadcastPlayerRoster 로 처리됨.
                    // savedAllCharacterIndices 에서 로컬 플레이어 인덱스를 읽어 재전송한다.
                    if (!_runner.IsServer && _runner.LocalPlayer.IsRealPlayer)
                    {
                        var localIndex = savedAllCharacterIndices.TryGetValue(_runner.LocalPlayer.PlayerId, out var li)
                            ? li
                            : savedCharacterIndex;
                        if (localIndex >= 0)
                        {
                            SetLocalPlayerSelectedCharacter(localIndex);
                        }
                    }
                }
            }
            finally
            {
                _isProcessing = false;
                _isMigrating = false;
                // 마이그레이션 중 수신됐으나 _isMigrating 가드로 보류된 캐릭터 선택을 처리한다.
                FlushPendingMigrationSpawns();
            }
        }

        // ─── 좌클릭 꾹 vs 연타 판별용 필드 ───
        private void RemapMigrationEntriesByNickname(NetworkRunner runner, Dictionary<int, string> oldNicknamesByPlayerId)
        {
            if (runner == null || oldNicknamesByPlayerId == null || oldNicknamesByPlayerId.Count == 0)
            {
                return;
            }

            var newPlayerIdByNickname = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var active in runner.ActivePlayers.Where(p => p.IsRealPlayer).OrderBy(p => p.PlayerId))
            {
                var nickname = DecodeConnectionToken(runner.GetPlayerConnectionToken(active));
                if (string.IsNullOrWhiteSpace(nickname) && active == runner.LocalPlayer)
                {
                    nickname = _nickname;
                }

                nickname = SanitizeNameToken(nickname);
                if (string.IsNullOrWhiteSpace(nickname))
                {
                    continue;
                }

                if (!newPlayerIdByNickname.ContainsKey(nickname))
                {
                    newPlayerIdByNickname[nickname] = active.PlayerId;
                }
            }

            foreach (var kvp in oldNicknamesByPlayerId)
            {
                var oldPlayerId = kvp.Key;
                var nickname = kvp.Value;
                if (oldPlayerId <= 0 || string.IsNullOrWhiteSpace(nickname))
                {
                    continue;
                }

                if (!newPlayerIdByNickname.TryGetValue(nickname, out var newPlayerId))
                {
                    continue;
                }

                if (oldPlayerId != newPlayerId)
                {
                    RemapMigrationEntry(oldPlayerId, newPlayerId);
                }
            }
        }

        private bool _netLeftMouseDown;
        private float _netLeftMouseDownTime;
        private bool _netLeftMouseConsumedAsGrab;
        private const float NET_GRAB_HOLD_THRESHOLD = 0.15f;

        // 매 네트워크 틱마다 로컬 플레이어의 입력을 수집해 Fusion에 전달한다.
        // 좌클릭 꾹(0.15초 이상) = GrabHold, 좌클릭 짧게 = Punch
        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
            // 좌클릭 상태 추적
            if (Input.GetMouseButtonDown(0))
            {
                _netLeftMouseDown = true;
                _netLeftMouseDownTime = Time.time;
                _netLeftMouseConsumedAsGrab = false;
            }

            if (Input.GetMouseButton(0) && _netLeftMouseDown)
            {
                if (Time.time - _netLeftMouseDownTime >= NET_GRAB_HOLD_THRESHOLD)
                    _netLeftMouseConsumedAsGrab = true;
            }

            bool isPunch = false;
            if (Input.GetMouseButtonUp(0))
            {
                if (!_netLeftMouseConsumedAsGrab)
                    isPunch = true;
                _netLeftMouseDown = false;
            }

            bool isGrabHold = _netLeftMouseDown && _netLeftMouseConsumedAsGrab;

            input.Set(new PlayerNetworkInput
            {
                Move = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical")),
                Jump = Input.GetKey(KeyCode.Space),
                Punch = isPunch,
                Drop = Input.GetKeyDown(KeyCode.F),
                Throw = Input.GetMouseButtonDown(1),
                GrabHold = isGrabHold,
                Headbutt = Input.GetMouseButtonDown(2),
                Sprint = Input.GetKey(KeyCode.LeftShift)
            });
        }
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

            // 수신 키에 따라 처리 분기:
            // PlayerRosterReliableKey       → 방장이 보낸 전체 참가자 목록 동기화 (클라이언트 수신)
            // CharacterSelectionReliableKey → 클라이언트가 보낸 캐릭터 선택 처리 (방장 수신)
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

                        // GameScene에서는 클라이언트가 재접속 후 캐릭터 선택을 재전송하므로
                        // 수신 즉시 스폰을 시도한다.
                        // 마이그레이션 진행 중(_isMigrating)이면 TrySpawnGameplayNetworkCharacter가
                        // 가드로 막히므로 버퍼에 보관했다가 마이그레이션 완료 후 일괄 처리한다.
                        if (IsActiveGameplayScene())
                        {
                            if (_isMigrating)
                            {
                                _pendingCharacterSelectionsWhileMigrating[player] = normalized;
                                Debug.Log($"[Lobby] Buffered character selection during migration. player={player.PlayerId}, charIdx={normalized}");
                            }
                            else
                            {
                                TrySpawnGameplayNetworkCharacter(player);
                            }
                        }
                    }
                }

                if (roomPanel.activeSelf)
                {
                    UpdateRoomPanel();
                }
            }
        }

        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

        // 씬 전환이 시작될 때 호출. 스폰 목록과 SpawnPointGroup 캐시를 초기화한다.
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
        {
            if (runner != _runner)
            {
                return;
            }

            _spawnedGameplayNetworkCharacters.Clear();
            _spawnedCharacterIndexByPlayerId.Clear();
            _cachedSpawnPointGroup = null;

            // 마이그레이션 중에는 캡처해둔 위치 테이블을 지우지 않는다.
            // StartGame(HostMigrationToken) 과정에서 OnSceneLoadStart 가 발동할 수 있으나
            // 이 데이터는 재스폰에 반드시 필요하므로 _isMigrating 플래그로 보호한다.
            if (!_isMigrating)
            {
                _migratedPositionsByOldPlayerId.Clear();
            }
        }

        // 씬 전환이 완료됐을 때 호출. GameScene이면 로비 UI를 숨기고 서버에서 캐릭터를 스폰한다.
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
