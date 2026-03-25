using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

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
                // 연결 토큰(clientId, 닉네임)을 이용해 입장한 플레이어를 참가자 목록에 등록한다.
                TryRegisterParticipantFromToken(runner, player);
                TryApplyMigrationReadyStateOnJoin(runner, player);

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

            if (runner.IsServer)
            {
                // 마이그레이션 후 재접속하는 플레이어는 새 PlayerId가 할당된다.
                // 캐릭터 데이터가 구 PlayerId로 저장돼 있으므로 스폰·등록 전에 리매핑한다.
                // LauncherScene·GameScene 공통으로 필요하다.
                TryRemapMigrationEntryOnJoin(runner, player);

                if (IsActiveGameplayScene() && _gameplaySceneSpawnBootstrapComplete)
                {
                    TrySpawnGameplayNetworkCharacter(player);
                }
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
                    UntrackGameplayPlayer(player.PlayerId);
                    runner.Despawn(spawned);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Lobby] Failed to despawn player character. player={player.PlayerId}, error={e.Message}");
                }
            }
            UntrackGameplayPlayer(player.PlayerId);
            _spawnedGameplayNetworkCharacters.Remove(player.PlayerId);
            _spawnedCharacterIndexByPlayerId.Remove(player.PlayerId);
            _deadGameplayPlayerIds.Remove(player.PlayerId);

            if (player.IsRealPlayer)
            {
                _roomParticipantsByPlayerId.Remove(player.PlayerId);
                _selectedCharacterIndexByPlayerId.Remove(player.PlayerId);
                _readyStateByPlayerId.Remove(player.PlayerId);
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
            Debug.Log($"[Lobby] Runner shutdown: reason={shutdownReason}");

            // ShutdownRunnerAsync 또는 방 생성/입장 처리 중에 발생한 종료는
            // 상위 흐름에서 이미 처리 중이므로 여기서 추가 복구를 하지 않는다.
            if (_isShuttingDownRunner || _isProcessing)
            {
                Debug.Log("[Lobby] Shutdown in progress, skip recovery.");
                return;
            }

            // GameScene에서 방장 강제 종료 감지 → 즉시 게임 종료 처리
            // _pendingGameEndPanel이 true이면 RPC_BroadcastRankings가 이미 씬 전환을 시작한 것이므로
            // TriggerHostExitAndReturnToLobby를 발동하지 않는다.
            if (!_isShowingGameEndPanel && !_pendingGameEndPanel && IsActiveSceneNamed(gameplaySceneName))
            {
                Debug.Log("[Lobby] Host exited during gameplay → returning to lobby.");
                TriggerHostExitAndReturnToLobby();
                return;
            }

            CleanupRunnerAfterGameEndHostExit();
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

            // GameScene에서 서버 연결 끊김 = 방장 강제 종료 → 즉시 게임 종료 처리
            // _pendingGameEndPanel이 true이면 정상 게임 종료 흐름으로 씬 전환 중이므로 무시한다.
            if (!_isShowingGameEndPanel && !_pendingGameEndPanel && IsActiveSceneNamed(gameplaySceneName))
            {
                Debug.Log("[Lobby] Disconnected from host during gameplay → returning to lobby.");
                TriggerHostExitAndReturnToLobby();
                return;
            }

            CleanupRunnerAfterGameEndHostExit();
        }

        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            if (runner != _runner || !runner.IsServer)
            {
                return;
            }

            var tokenInfo = ParseConnectionToken(token);
            if (!string.IsNullOrEmpty(tokenInfo.Nickname))
            {
                Debug.Log($"[Lobby] Connect request from {request.RemoteAddress}: clientId={tokenInfo.ClientId}, {PlayerRosterLabel}={tokenInfo.Nickname}");
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

            if (_isShowingGameEndPanel && !_gameEndReturnTransitionStarted)
            {
                // 자동 방 입장이 완료된 새 방 세션에서 발생한 migration은 normal path로 처리한다.
                // 아직 자동 입장 전(이전 게임 세션)이거나 입장 중인 경우에만 runner를 종료한다.
                var autoJoinDone = _gameEndAutoRoomJoinTask?.IsCompletedSuccessfully == true;
                if (!autoJoinDone)
                {
                    Debug.Log("[Lobby] 게임 종료 화면 대기 중 stale runner 감지 - 즉시 종료.");
                    _ = ShutdownRunnerAsync();
                    return;
                }
                // autoJoinDone: 새 방 세션의 host migration → normal path로 fall-through
                Debug.Log("[Lobby] 게임 종료 화면의 새 방 세션에서 host migration 발생 - normal path 처리.");
            }

            if (_isProcessing)
            {
                // 이전 migration 처리 중 새 token이 도착한 경우 최신 token만 보관한다.
                // finally에서 _isProcessing = false 이후 재처리된다.
                Debug.Log("[Lobby] Host migration received while processing. Queuing latest token for retry.");
                _pendingMigrationToken = hostMigrationToken;
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
                // roster에 아직 등록되지 않은 플레이어(-1)는 최하위 우선순위로 처리한다.
                // -1 > 0 이 false이므로 딜레이 없이 즉시 StartGame을 시도하게 되어
                // PlayerId가 낮은 정상 후보와 race condition이 발생하는 것을 방지한다.
                if (priorityIndex < 0)
                    priorityIndex = orderedRemaining.Count;

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
                var savedClientIdsByOldPlayerId = _roomParticipantsByPlayerId.Values
                    .Where(p => p != null && p.PlayerId > 0 && !string.IsNullOrWhiteSpace(p.ClientId))
                    .ToDictionary(p => p.PlayerId, p => p.ClientId);
                var savedLocalIsReady = _localIsReady;
                _migrationReadyStateByClientId.Clear();
                foreach (var presence in _roomParticipantsByPlayerId.Values)
                {
                    if (presence == null || string.IsNullOrWhiteSpace(presence.ClientId))
                    {
                        continue;
                    }

                    var isReady = _readyStateByPlayerId.TryGetValue(presence.PlayerId, out var ready) && ready;
                    _migrationReadyStateByClientId[presence.ClientId] = isReady;
                }

                if (priorityIndex > 0)
                {
                    // 낮은 ID 플레이어가 먼저 StartGame 을 호출하도록 대기
                    await Task.Delay(priorityIndex * 350);
                }

                // clientId → 구PlayerId 역방향 테이블을 보존한다.
                // LauncherScene·GameScene 공통으로 필요하다.
                // OnHostMigration 완료 시점에는 아직 재접속하지 않은 플레이어가 있으므로
                // 이후 OnPlayerJoined에서 각 플레이어가 재접속할 때 TryRemapMigrationEntryOnJoin()으로
                // 캐릭터 데이터를 올바른 새 PlayerId로 리매핑하기 위해 필요하다.
                // stale key cleanup이 아직 재접속 중인 플레이어 데이터를 삭제하지 않도록 보호한다.
                // 나간 방장(_currentOwnerPlayerId)은 재접속하지 않으므로 제외한다.
                var leavingHostId = _currentOwnerPlayerId;
                _migrationOldPlayerIdByClientId.Clear();
                foreach (var kvp in savedClientIdsByOldPlayerId)
                {
                    if (kvp.Key != leavingHostId)
                        _migrationOldPlayerIdByClientId[kvp.Value] = kvp.Key;
                }

                // GameScene에서 방장이 나간 경우 → Migration 대신 게임 종료 처리
                // _isShowingGameEndPanel=true 또는 _pendingGameEndPanel=true이면 이미 처리 중이므로
                // Runner만 종료하고 즉시 반환한다.
                // _pendingGameEndPanel=true: RPC_BroadcastRankings가 이미 씬 전환을 시작한 상태 (정상 게임 종료 중)
                if (IsActiveGameplayScene())
                {
                    if (!_isShowingGameEndPanel && !_pendingGameEndPanel)
                    {
                        Debug.Log("[Lobby] Host exited during gameplay → returning to lobby.");
                        TriggerHostExitAndReturnToLobby();
                    }
                    else
                    {
                        Debug.Log("[Lobby] Host exited during gameplay, already handling end → shutdown runner only.");
                        _ = ShutdownRunnerAsync();
                    }
                    return;
                }

                // GameScene에서 마이그레이션 발생 시 ShutdownRunnerAsync 전에 위치를 캡처한다.
                // NetworkObject가 아직 살아있는 상태에서 읽어야 정확한 위치를 얻을 수 있다.
                if (IsActiveGameplayScene())
                {
                    _isMigrating = true;
                    CaptureCharacterStatesForMigration();
                    CaptureEnvironmentStatesForMigration();

                    // CaptureCharacterStatesForMigration()이 NetworkPlayer.CharacterTypeIndex를 읽어
                    // _selectedCharacterIndexByPlayerId를 실제 스폰 캐릭터 인덱스(0~3)로 갱신한다.
                    // savedAllCharacterIndices·savedCharacterIndex는 이 갱신 전에 캡처됐으므로
                    // ? 선택자의 경우 여전히 4(Random)를 갖는다.
                    // ShutdownRunnerAsync 이후 이 값으로 복원하면 재추첨이 발생하므로
                    // 갱신된 dict를 기반으로 재캡처한다.
                    savedAllCharacterIndices = _selectedCharacterIndexByPlayerId
                        .Where(kvp => SanitizeCharacterIndexOrNone(kvp.Value) >= 0)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    if (_selectedCharacterIndexByPlayerId.TryGetValue(localPlayerId, out var scResolved))
                    {
                        var resolvedIdx = SanitizeCharacterIndexOrNone(scResolved);
                        if (resolvedIdx >= 0)
                            savedCharacterIndex = resolvedIdx;
                    }
                }

                // StartGame 후 새 PlayerId를 알 수 있으므로 로컬 플레이어의 구 PlayerId를 미리 저장한다.
                var localOldPlayerId = runner.LocalPlayer.PlayerId;

                await ShutdownRunnerAsync();

                if (!TryCreateRunner(out var newRunner))
                {
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

                if (_runner.IsServer)
                {
                    foreach (var activePlayer in _runner.ActivePlayers.Where(p => p.IsRealPlayer))
                    {
                        TryApplyMigrationReadyStateOnJoin(_runner, activePlayer);
                    }
                }

                var migrationRemapMap = BuildMigrationRemapMap(_runner, savedClientIdsByOldPlayerId);
                if (IsActiveGameplayScene() &&
                    localOldPlayerId > 0 &&
                    _runner.LocalPlayer.IsRealPlayer &&
                    !migrationRemapMap.ContainsKey(localOldPlayerId))
                {
                    migrationRemapMap[localOldPlayerId] = _runner.LocalPlayer.PlayerId;
                }

                RemapMigrationEntries(migrationRemapMap);

                // 리매핑 후 더 이상 활성 플레이어가 아닌 구 PlayerId 항목을 정리한다.
                // 단, _migrationOldPlayerIdByClientId.Values에 있는 키는 아직 재접속 중인 플레이어이므로
                // 제거하지 않는다 — TryRemapMigrationEntryOnJoin에서 OnPlayerJoined 시 리매핑된다.
                var activePids = new HashSet<int>(_runner.ActivePlayers
                    .Where(p => p.IsRealPlayer)
                    .Select(p => p.PlayerId));
                var pendingOldPids = new HashSet<int>(_migrationOldPlayerIdByClientId.Values);
                var staleKeys = _selectedCharacterIndexByPlayerId.Keys
                    .Where(k => !activePids.Contains(k) && !pendingOldPids.Contains(k))
                    .ToList();
                foreach (var staleKey in staleKeys)
                {
                    _selectedCharacterIndexByPlayerId.Remove(staleKey);
                    Debug.LogWarning($"[Lobby] Removed stale character selection for departed/unresolved player={staleKey}");
                }

                if (_runner.IsServer && _runner.LocalPlayer.IsRealPlayer)
                {
                    // 새 방장은 준비 상태가 없으므로 리셋한다.
                    // 방장이었다가 다시 방장이 되는 경우도 동일하게 처리한다.
                    _localIsReady = false;
                    RegisterParticipant(_runner.LocalPlayer, _nickname);
                    _currentOwnerPlayerId = _runner.LocalPlayer.PlayerId;
                    _currentRoomOwner = _nickname;
                    _readyStateByPlayerId[_runner.LocalPlayer.PlayerId] = false;
                    if (_roomParticipantsByPlayerId.TryGetValue(_runner.LocalPlayer.PlayerId, out var hostPresence) && hostPresence != null)
                    {
                        hostPresence.IsReady = false;
                    }
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
                        RestoreEnvironmentStatesAfterMigration();
                        TrySpawnGameplayNetworkCharactersForAllPlayers();
                        _gameplaySceneSpawnBootstrapComplete = true;
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
                        // savedCharacterIndex를 우선한다: 마이그레이션 직전 서버 동기화된 값이므로
                        // _localSelectedCharacterIndex(UI 설정값)보다 신뢰도가 높다.
                        // savedCharacterIndex가 유효하지 않을 경우에만 _localSelectedCharacterIndex를 사용한다.
                        var charToSend = savedCharacterIndex >= 0
                            ? savedCharacterIndex
                            : _localSelectedCharacterIndex >= 0
                                ? _localSelectedCharacterIndex
                                : (int)CharacterKind.Ssaty;
                        _localSelectedCharacterIndex = charToSend;
                        SetLocalPlayerSelectedCharacter(charToSend);
                    }
                }
                else
                {
                    // ── LauncherScene(로비) 방장 이전 처리 ───────────────────────────
                    // 기존 방 패널로 복귀해 대기 상태를 유지한다.
                    // 게임 종료 화면 표시 중(_isShowingGameEndPanel)이면 패널을 표시하지 않는다.
                    // 유저가 방 버튼을 눌렀을 때 ReturnToRoomFromGameEnd 흐름에서 ShowRoomPanel이 호출된다.
                    if (!_isShowingGameEndPanel)
                    {
                        ShowRoomPanel();
                        UpdateRoomPanel();
                    }

                    // 클라이언트는 새 방장에게 캐릭터 선택과 준비 상태를 재전송해야 roster 에 반영됨.
                    // 서버(새 방장)는 RegisterParticipant + BroadcastPlayerRoster 로 처리되며
                    // 방장의 준비 상태는 불필요하므로 재전송하지 않는다.
                    if (!_runner.IsServer && _runner.LocalPlayer.IsRealPlayer)
                    {
                        _localIsReady = savedLocalIsReady;
                        var localIndex = savedAllCharacterIndices.TryGetValue(_runner.LocalPlayer.PlayerId, out var li)
                            ? li
                            : savedCharacterIndex;
                        if (localIndex >= 0)
                        {
                            SetLocalPlayerSelectedCharacter(localIndex);
                        }

                        // 준비 상태 재전송: 마이그레이션 전 상태를 새 방장에게 다시 동기화한다.
                        SendReadyStateToHost(_localIsReady);
                    }
                }
            }
            finally
            {
                _isProcessing = false;
                _isMigrating = false;
                // 마이그레이션 중 수신됐으나 _isMigrating 가드로 보류된 캐릭터 선택을 처리한다.
                FlushPendingMigrationSpawns();

                // 처리 중 도착한 migration token이 있으면 지금 처리한다.
                // token이 만료됐거나 세션이 이미 변경된 경우 StartGame이 실패하며
                // 해당 경고 로그로 추적할 수 있다.
                if (_pendingMigrationToken != null)
                {
                    var token = _pendingMigrationToken;
                    _pendingMigrationToken = null;
                    Debug.Log("[Lobby] Processing deferred host migration token.");
                    ((INetworkRunnerCallbacks)this).OnHostMigration(_runner, token);
                }
            }
        }

        // ─── 좌클릭 꾹 vs 연타 판별용 필드 ───
        private Dictionary<int, int> BuildMigrationRemapMap(NetworkRunner runner, Dictionary<int, string> oldClientIdsByPlayerId)
        {
            if (runner == null || oldClientIdsByPlayerId == null || oldClientIdsByPlayerId.Count == 0)
            {
                return new Dictionary<int, int>();
            }

            var newPlayerIdByClientId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var active in runner.ActivePlayers.Where(p => p.IsRealPlayer).OrderBy(p => p.PlayerId))
            {
                var clientId = DecodeConnectionTokenClientId(runner.GetPlayerConnectionToken(active));
                if (string.IsNullOrWhiteSpace(clientId) && active == runner.LocalPlayer)
                {
                    EnsureLocalClientId();
                    clientId = _localClientId;
                }

                if (string.IsNullOrWhiteSpace(clientId))
                {
                    continue;
                }

                if (!newPlayerIdByClientId.ContainsKey(clientId))
                {
                    newPlayerIdByClientId[clientId] = active.PlayerId;
                }
                else
                {
                    Debug.LogWarning($"[Lobby] ClientId collision during migration remap: '{clientId}' (PlayerId={active.PlayerId}).");
                }
            }

            var oldToNewPlayerIds = new Dictionary<int, int>();
            foreach (var kvp in oldClientIdsByPlayerId)
            {
                var oldPlayerId = kvp.Key;
                var clientId = kvp.Value;
                if (oldPlayerId <= 0 || string.IsNullOrWhiteSpace(clientId))
                {
                    continue;
                }

                if (!newPlayerIdByClientId.TryGetValue(clientId, out var newPlayerId))
                {
                    continue;
                }

                oldToNewPlayerIds[oldPlayerId] = newPlayerId;
            }

            return oldToNewPlayerIds;
        }

        // GameEndScene에서 순위 UI 표시를 위해 PlayerId로 닉네임을 조회한다.
        public string GetParticipantNickname(int playerId)
        {
            if (_roomParticipantsByPlayerId.TryGetValue(playerId, out var p) && p != null && !string.IsNullOrEmpty(p.Nickname))
                return p.Nickname;
            // 퇴장 플레이어는 _roomParticipantsByPlayerId에서 제거됐으므로 별도 캐시로 폴백한다.
            if (_cachedNicknamesByPlayerId.TryGetValue(playerId, out var cached) && !string.IsNullOrEmpty(cached))
                return cached;
            return $"Player{playerId}";
        }

        private bool _netLeftMouseDown;
        private float _netLeftMouseDownTime;
        private bool _netLeftMouseConsumedAsGrab;
        private bool _netRightMouseDown;
        private float _netRightMouseDownTime;
        private bool _netRightMouseConsumedAsGrab;
        private bool _netPunchQueued;
        private bool _netThrowQueued;
        private bool _netJumpQueued;
        private bool _netDropQueued;
        private bool _netHeadbuttQueued;
        private Vector2 _netMoveInput;
        private Vector2 _netMoveInputRaw;
        private float _netCameraYaw;
        private bool _netSprintHeld;
        private const float NET_GRAB_HOLD_THRESHOLD = 0.15f;
        private float _lastMoveSyncInputLogAt = float.NegativeInfinity;
        private float _lastMoveSyncCaptureLogAt = float.NegativeInfinity;
        private const float MOVE_SYNC_INPUT_LOG_INTERVAL = 0.12f;

        private void CaptureNetworkInputState()
        {
            if (_runner == null || !_runner.IsRunning || !GameStartCountdown.InputEnabled)
            {
                ResetLatchedNetworkInputState();
                TraceMoveSyncCapture("Reset");
                return;
            }

            if (IsGhostThrowInputModeActive())
            {
                ResetLatchedNetworkInputState();
                return;
            }

            _netMoveInputRaw = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            _netMoveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            // Camera.main이 null이면 직전 유효 yaw를 그대로 유지 (0으로 리셋하면 이동 방향이 북쪽으로 고정됨)
            if (Camera.main != null)
                _netCameraYaw = Camera.main.transform.eulerAngles.y;
            _netSprintHeld = Input.GetKey(KeyCode.LeftShift);

            if (Input.GetMouseButtonDown(0))
            {
                _netLeftMouseDown = true;
                _netLeftMouseDownTime = Time.time;
                _netLeftMouseConsumedAsGrab = false;
            }

            if (_netLeftMouseDown && Input.GetMouseButton(0) &&
                Time.time - _netLeftMouseDownTime >= NET_GRAB_HOLD_THRESHOLD)
            {
                _netLeftMouseConsumedAsGrab = true;
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (_netLeftMouseDown &&
                    !_netLeftMouseConsumedAsGrab &&
                    Time.time - _netLeftMouseDownTime < NET_GRAB_HOLD_THRESHOLD)
                {
                    _netPunchQueued = true;
                }

                _netLeftMouseDown = false;
                _netLeftMouseConsumedAsGrab = false;
            }

            _netRightMouseDown = false;
            _netRightMouseDownTime = 0f;
            _netRightMouseConsumedAsGrab = false;

            // 우클릭 = 던지기 (잡고 있을 때)
            if (Input.GetMouseButtonDown(1))
                _netThrowQueued = true;

            if (Input.GetKeyDown(KeyCode.Space))
                _netJumpQueued = true;

            if (Input.GetKeyDown(KeyCode.F))
                _netDropQueued = true;

            if (Input.GetMouseButtonDown(2))
                _netHeadbuttQueued = true;

            TraceMoveSyncCapture("Capture");
        }

        private void ResetLatchedNetworkInputState()
        {
            _netLeftMouseDown = false;
            _netLeftMouseConsumedAsGrab = false;
            _netRightMouseDown = false;
            _netRightMouseConsumedAsGrab = false;
            _netPunchQueued = false;
            _netThrowQueued = false;
            _netJumpQueued = false;
            _netDropQueued = false;
            _netHeadbuttQueued = false;
            _netMoveInput = Vector2.zero;
            _netMoveInputRaw = Vector2.zero;
            _netCameraYaw = 0f;
            _netSprintHeld = false;
        }

        private static bool ConsumeLatchedNetworkFlag(ref bool queued)
        {
            var value = queued;
            queued = false;
            return value;
        }

        private void TraceMoveSyncInput(NetworkRunner runner, in PlayerNetworkInput payload)
        {
            if (!Application.isPlaying || !MoveSyncDiagnostics.Enabled)
                return;

            var forceLog =
                (bool)payload.Jump ||
                (bool)payload.Punch ||
                (bool)payload.Throw ||
                (bool)payload.Drop ||
                (bool)payload.Headbutt;

            var now = Time.unscaledTime;
            if (!forceLog && now - _lastMoveSyncInputLogAt < MOVE_SYNC_INPUT_LOG_INTERVAL)
                return;

            _lastMoveSyncInputLogAt = now;

            var tick = runner != null ? runner.Tick.Raw : -1;
            var playerId = runner != null && runner.LocalPlayer.IsRealPlayer ? runner.LocalPlayer.PlayerId : -1;
            MoveSyncDiagnostics.Emit(
                $"[MoveDiag:OnInput] role=LocalInput playerId={playerId} tick={tick} source=LobbyCanvasUIController.OnInput " +
                $"move={MoveSyncDiagnostics.FormatVector2(payload.Move)} camYaw={payload.CameraYaw:F1} " +
                $"jump={((bool)payload.Jump ? 1 : 0)} sprint={((bool)payload.Sprint ? 1 : 0)} " +
                $"punch={((bool)payload.Punch ? 1 : 0)} throw={((bool)payload.Throw ? 1 : 0)} " +
                $"drop={((bool)payload.Drop ? 1 : 0)} headbutt={((bool)payload.Headbutt ? 1 : 0)} " +
                $"leftGrab={((bool)payload.LeftGrabHold ? 1 : 0)} rightGrab={((bool)payload.RightGrabHold ? 1 : 0)}",
                this);
        }

        private void TraceMoveSyncCapture(string source)
        {
            if (!Application.isPlaying || !MoveSyncDiagnostics.Enabled || _runner == null || !_runner.IsRunning || !_runner.IsServer)
                return;

            var forceLog =
                _netPunchQueued ||
                _netThrowQueued ||
                _netJumpQueued ||
                _netDropQueued ||
                _netHeadbuttQueued;

            var now = Time.unscaledTime;
            if (!forceLog && now - _lastMoveSyncCaptureLogAt < MOVE_SYNC_INPUT_LOG_INTERVAL)
                return;

            _lastMoveSyncCaptureLogAt = now;

            var playerId = _runner.LocalPlayer.IsRealPlayer ? _runner.LocalPlayer.PlayerId : -1;
            MoveSyncDiagnostics.Emit(
                $"[MoveDiag:Capture] role=HostCapture playerId={playerId} tick={_runner.Tick.Raw} source={source} " +
                $"inputEnabled={(GameStartCountdown.InputEnabled ? 1 : 0)} focused={(Application.isFocused ? 1 : 0)} " +
                $"cursorLocked={(Cursor.lockState == CursorLockMode.Locked ? 1 : 0)} cameraMain={(Camera.main != null ? 1 : 0)} " +
                $"moveRaw={MoveSyncDiagnostics.FormatVector2(_netMoveInputRaw)} move={MoveSyncDiagnostics.FormatVector2(_netMoveInput)} " +
                $"camYaw={_netCameraYaw:F1} sprint={(_netSprintHeld ? 1 : 0)} " +
                $"leftGrab={(_netLeftMouseDown ? 1 : 0)} rightGrab={(_netRightMouseDown ? 1 : 0)} " +
                $"punchQ={(_netPunchQueued ? 1 : 0)} throwQ={(_netThrowQueued ? 1 : 0)} jumpQ={(_netJumpQueued ? 1 : 0)} " +
                $"dropQ={(_netDropQueued ? 1 : 0)} headbuttQ={(_netHeadbuttQueued ? 1 : 0)}",
                this);
        }

        private bool IsGhostThrowInputModeActive()
        {
            var ghostManagers = UnityEngine.Object.FindObjectsByType<SSAFYPlayTime.Game.GhostThrow.GhostThrowManager>(FindObjectsSortMode.None);
            for (var i = 0; i < ghostManagers.Length; i++)
            {
                var manager = ghostManagers[i];
                if (manager != null && manager.IsGhostThrowEnabled)
                    return true;
            }

            return false;
        }

        // 매 네트워크 틱마다 로컬 플레이어의 입력을 수집해 Fusion에 전달한다.
        // 좌클릭 짧게 = 아이템 사용(Punch), 좌클릭 꾹(0.15초+) = 왼손 그랩
        // 우클릭 짧게 = 던지기, 우클릭 꾹(0.15초+) = 오른손 그랩
        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (!GameStartCountdown.InputEnabled) return;

            var latchedLeftGrabHold = _netLeftMouseDown && _netLeftMouseConsumedAsGrab;
            var latchedRightGrabHold = _netRightMouseDown && _netRightMouseConsumedAsGrab;
            var payload = new PlayerNetworkInput
            {
                Move = _netMoveInput,
                CameraYaw = _netCameraYaw,
                Jump = ConsumeLatchedNetworkFlag(ref _netJumpQueued),
                Punch = ConsumeLatchedNetworkFlag(ref _netPunchQueued),
                PrimaryUseHold = _netLeftMouseDown,
                Drop = ConsumeLatchedNetworkFlag(ref _netDropQueued),
                Throw = ConsumeLatchedNetworkFlag(ref _netThrowQueued),
                LeftGrabHold = latchedLeftGrabHold,
                RightGrabHold = false,
                Headbutt = ConsumeLatchedNetworkFlag(ref _netHeadbuttQueued),
                Sprint = _netSprintHeld
            };
            input.Set(payload);
            TraceMoveSyncInput(runner, payload);
            return;

            bool isPunch = false;
            bool isThrow = false;


            // 좌클릭 상태 추적 (왼손 그랩)
            if (Input.GetMouseButtonDown(0))
            {
                _netLeftMouseDown = true;
                _netLeftMouseDownTime = Time.time;
                _netLeftMouseConsumedAsGrab = false;
            }

            if (runner == null && Input.GetMouseButton(0) && _netLeftMouseDown)
            {
                if (Time.time - _netLeftMouseDownTime >= NET_GRAB_HOLD_THRESHOLD)
                    _netLeftMouseConsumedAsGrab = true;
            }

            if (runner == null && Input.GetMouseButtonUp(0))
            {
                if (!_netLeftMouseConsumedAsGrab && Time.time - _netLeftMouseDownTime < NET_GRAB_HOLD_THRESHOLD)
                    isPunch = true;

                _netLeftMouseDown = false;
            }

            // 우클릭 상태 추적 (오른손 그랩)
            if (runner == null && Input.GetMouseButtonDown(1))
            {
                _netRightMouseDown = true;
                _netRightMouseDownTime = Time.time;
                _netRightMouseConsumedAsGrab = false;
            }

            if (runner == null && Input.GetMouseButton(1) && _netRightMouseDown)
            {
                if (Time.time - _netRightMouseDownTime >= NET_GRAB_HOLD_THRESHOLD)
                    _netRightMouseConsumedAsGrab = true;
            }

            if (runner == null && Input.GetMouseButtonUp(1))
            {
                if (!_netRightMouseConsumedAsGrab && Time.time - _netRightMouseDownTime < NET_GRAB_HOLD_THRESHOLD)
                    isThrow = true;

                _netRightMouseDown = false;
            }

            bool isLeftGrabHold = _netLeftMouseDown && _netLeftMouseConsumedAsGrab;
            bool isRightGrabHold = _netRightMouseDown && _netRightMouseConsumedAsGrab;

            input.Set(new PlayerNetworkInput
            {
                Move = _netMoveInput,
                CameraYaw = _netCameraYaw,
                Jump = ConsumeLatchedNetworkFlag(ref _netJumpQueued),
                Punch = ConsumeLatchedNetworkFlag(ref _netPunchQueued),
                Drop = ConsumeLatchedNetworkFlag(ref _netDropQueued),
                Throw = ConsumeLatchedNetworkFlag(ref _netThrowQueued),
                LeftGrabHold = isLeftGrabHold,
                RightGrabHold = isRightGrabHold,
                Headbutt = ConsumeLatchedNetworkFlag(ref _netHeadbuttQueued),
                Sprint = _netSprintHeld
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

            if (key == ReadyStateReliableKey && _runner != null && _runner.IsRunning && _runner.IsServer)
            {
                if (player.IsRealPlayer && (payload == "1" || payload == "0"))
                {
                    var isReady = payload == "1";
                    _readyStateByPlayerId[player.PlayerId] = isReady;
                    if (_roomParticipantsByPlayerId.TryGetValue(player.PlayerId, out var readyPresence) && readyPresence != null)
                    {
                        readyPresence.IsReady = isReady;
                    }

                    BroadcastPlayerRoster();
                    Debug.Log($"[Lobby] Player ready state updated: player={player.PlayerId}, isReady={isReady}");
                }

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

                return;
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
            UntrackAllGameplayPlayers();
            _cachedSpawnPointGroup = null;
            _gameplaySceneSpawnBootstrapComplete = false;

            // 마이그레이션 중에는 캡처해둔 위치 테이블을 지우지 않는다.
            // StartGame(HostMigrationToken) 과정에서 OnSceneLoadStart 가 발동할 수 있으나
            // 이 데이터는 재스폰에 반드시 필요하므로 _isMigrating 플래그로 보호한다.
            //
            // 사망 상태(_deadGameplayPlayerIds)도 마이그레이션 중에는 보존한다.
            // migration 완료 후 RemapMigrationEntries()에서 새 PlayerId로 리매핑된다.
            //
            // 환경 마이그레이션 데이터(_migratedSeaLevelsByPath, _migratedDeathZonesByPath)는
            // _isMigrating = false 이후에도 씬 리로드가 일어날 수 있으므로
            // 데이터가 남아있는 한 OnSceneLoadDone에서 복원할 수 있도록 지우지 않는다.
            // RestoreEnvironmentStatesAfterMigration()이 직접 초기화한다.
            if (!_isMigrating)
            {
                _deadGameplayPlayerIds.Clear();
                _migratedPositionsByOldPlayerId.Clear();
                _migratedPositionsByClientId.Clear();
                _migrationOldPlayerIdByClientId.Clear();
                _migrationReadyStateByClientId.Clear();
                // 환경 상태 데이터는 복원 완료 전까지 보존한다.
                // 미복원 데이터가 남아있으면 OnSceneLoadDone에서 복원할 수 있도록 유지한다.
                if (_migratedSeaLevelsByPath.Count == 0 && _migratedDeathZonesByPath.Count == 0)
                {
                    _migratedSeaLevelsByPath.Clear();
                    _migratedDeathZonesByPath.Clear();
                }
            }
        }

        // 씬 전환이 완료됐을 때 호출.
        // GameScene이면 로비 UI를 숨기고 서버에서 캐릭터를 스폰한다.
        // LauncherScene으로 복귀(게임 종료 후)이면 게임 종료 패널을 표시하고 방 세션 자동 입장을 시작한다.
        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
        {
            if (runner != _runner)
            {
                return;
            }

            EnsureLauncherBackgroundMusic();
            RefreshLauncherBackgroundMusicState();

            if (IsActiveGameplayScene())
            {
                _isShowingGameEndPanel = false;
                PlayBackgroundMusicClip(GetGameplayBackgroundMusicClip());

                if (nicknamePanel != null) nicknamePanel.SetActive(false);
                if (lobbyPanel != null) lobbyPanel.SetActive(false);
                if (roomPanel != null) roomPanel.SetActive(false);
                if (createRoomModal != null) createRoomModal.SetActive(false);
                if (passwordModal != null) passwordModal.SetActive(false);
                if (gamePanel != null) gamePanel.SetActive(true);

                // 게임씬 전환 시 로비 UI 캐릭터 미리보기 전부 숨김
                HideAllCharacterSlots();

                // GameHUD 인스턴스 생성 (모든 클라이언트에서 실행)
                if (gameHUDPrefab != null && FindObjectOfType<GameHUD>() == null)
                    Instantiate(gameHUDPrefab);

                if (runner.IsServer)
                {
                    // 씬 리로드 후 마이그레이션 환경 상태 복원.
                    // StartGame(HostMigrationToken) 이후 씬이 새로 로드된 경우,
                    // 마이그레이션 핸들러보다 OnSceneLoadDone이 먼저 실행되므로 여기서도 복원한다.
                    // 마이그레이션 데이터가 없으면 no-op.
                    RestoreEnvironmentStatesAfterMigration();
                    TrySpawnGameplayNetworkCharactersForAllPlayers();
                    _gameplaySceneSpawnBootstrapComplete = true;
                }
            }
            else
            {
                // LauncherScene 전환 완료.
                // GameScene 전환 시 LobbyCharacterRuntimeRoot의 캐릭터 오브젝트들이 파괴됐으므로
                // _characterSlotsInitialized를 리셋해 재초기화를 허용한다.
                ResetCharacterSlotState();

                // GameResultData에 결과가 있으면 게임 종료 후 복귀한 것 → 게임 종료 패널 표시.
                // [흐름] 게임 종료 패널을 보는 동안 백그라운드에서 자동으로
                // 이전 세션 종료 → 순위 기반 딜레이(0~450ms) → 새 방 세션 AutoHostOrClient 입장.
                // 버튼 클릭 시점에는 이미 방에 입장한 상태이므로 버튼 처리가 즉시 이뤄진다.
                if (_pendingGameEndPanel)
                {
                    _pendingGameEndPanel = false;
                    Debug.Log("[Lobby] 게임 종료 후 LauncherScene 전환 완료 - 게임 종료 패널 표시.");
                    ShowGameEndPanel();
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
