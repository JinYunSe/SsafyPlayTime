# 시스템 코드 리포트 (자동 생성)

- 생성 시각: 2026-03-03 17:44:34
- 대상 루트: `Assets/_Project/Scripts`
- 파일 수: 24

## 파일 목록
- `Assets/_Project/Scripts/Character/GroundProbe.cs` (22 lines)
- `Assets/_Project/Scripts/Character/PlayerInputSnapshot.cs` (16 lines)
- `Assets/_Project/Scripts/Character/PlayerMotorConfig.cs` (25 lines)
- `Assets/_Project/Scripts/Character/PlayerMotorState.cs` (12 lines)
- `Assets/_Project/Scripts/Character/PlayerMotorStateMachine.cs` (34 lines)
- `Assets/_Project/Scripts/Character/SyncPhysicsObject.cs` (35 lines)
- `Assets/_Project/Scripts/Editor/CharacterAnimationSetup.cs` (366 lines)
- `Assets/_Project/Scripts/Editor/FontImportPostprocessor.cs` (28 lines)
- `Assets/_Project/Scripts/Editor/MissingScriptScanner.cs` (126 lines)
- `Assets/_Project/Scripts/Editor/SystemReportBuilder.cs` (64 lines)
- `Assets/_Project/Scripts/Lobby/Editor/LobbyCanvasBuilder.cs` (433 lines)
- `Assets/_Project/Scripts/Lobby/FusionLobbyService.cs` (335 lines)
- `Assets/_Project/Scripts/Lobby/ILobbyService.cs` (24 lines)
- `Assets/_Project/Scripts/Lobby/LobbyCanvasUIController.cs` (502 lines)
- `Assets/_Project/Scripts/Lobby/LobbyRoom.cs` (41 lines)
- `Assets/_Project/Scripts/Lobby/LobbyRoomInfo.cs` (17 lines)
- `Assets/_Project/Scripts/Lobby/LobbyViewState.cs` (14 lines)
- `Assets/_Project/Scripts/Network/NetworkPlayer.cs` (82 lines)
- `Assets/_Project/Scripts/Session/SessionManager.cs` (31 lines)
- `Assets/_Project/Scripts/Utill/RuntimeLogOverlay.cs` (317 lines)
- `Assets/_Project/Scripts/Utill/TmpFontFallbackBootstrap.cs` (130 lines)
- `Assets/_Project/Scripts/Utill/UiKoreanFontBootstrap.cs` (127 lines)
- `Assets/_Project/Scripts/Utils/ConfigurableJointExtensions.cs` (62 lines)
- `Assets/_Project/Scripts/Utils/IgnoreCollision.cs` (22 lines)

## 클래스/인터페이스 키워드 스캔
### `Assets/_Project/Scripts/Character/GroundProbe.cs`
- `public sealed class GroundProbe`
### `Assets/_Project/Scripts/Character/PlayerMotorConfig.cs`
- `public sealed class PlayerMotorConfig : ScriptableObject`
### `Assets/_Project/Scripts/Character/PlayerMotorState.cs`
- `public enum PlayerMotorState`
### `Assets/_Project/Scripts/Character/PlayerMotorStateMachine.cs`
- `public sealed class PlayerMotorStateMachine`
### `Assets/_Project/Scripts/Character/SyncPhysicsObject.cs`
- `public class SyncPhysicsObject : MonoBehaviour`
### `Assets/_Project/Scripts/Editor/CharacterAnimationSetup.cs`
- `public class CharacterAnimationSetup : EditorWindow`
### `Assets/_Project/Scripts/Editor/FontImportPostprocessor.cs`
- `public sealed class FontImportPostprocessor : AssetPostprocessor`
### `Assets/_Project/Scripts/Editor/MissingScriptScanner.cs`
- `public static class MissingScriptScanner`
### `Assets/_Project/Scripts/Editor/SystemReportBuilder.cs`
- `public static class SystemReportBuilder`
- `if (trimmed.Contains(" class ") || trimmed.StartsWith("class ")`
- `|| trimmed.Contains(" interface ") || trimmed.StartsWith("interface ")`
- `|| trimmed.Contains(" enum ") || trimmed.StartsWith("enum "))`
### `Assets/_Project/Scripts/Lobby/Editor/LobbyCanvasBuilder.cs`
- `public static class LobbyCanvasBuilder`
### `Assets/_Project/Scripts/Lobby/FusionLobbyService.cs`
- `public sealed class FusionLobbyService : ILobbyService, INetworkRunnerCallbacks`
### `Assets/_Project/Scripts/Lobby/ILobbyService.cs`
- `public interface ILobbyService : IDisposable`
### `Assets/_Project/Scripts/Lobby/LobbyCanvasUIController.cs`
- `public sealed class LobbyCanvasUIController : MonoBehaviour`
### `Assets/_Project/Scripts/Lobby/LobbyRoom.cs`
- `public sealed class LobbyRoom`
### `Assets/_Project/Scripts/Lobby/LobbyRoomInfo.cs`
- `public sealed class LobbyRoomInfo`
### `Assets/_Project/Scripts/Lobby/LobbyViewState.cs`
- `public sealed class LobbyViewState`
### `Assets/_Project/Scripts/Network/NetworkPlayer.cs`
- `public sealed class NetworkPlayer : MonoBehaviour`
### `Assets/_Project/Scripts/Session/SessionManager.cs`
- `public sealed class SessionManager : MonoBehaviour`
### `Assets/_Project/Scripts/Utill/RuntimeLogOverlay.cs`
- `public sealed class RuntimeLogOverlay : MonoBehaviour`
### `Assets/_Project/Scripts/Utill/TmpFontFallbackBootstrap.cs`
- `public static class TmpFontFallbackBootstrap`
### `Assets/_Project/Scripts/Utill/UiKoreanFontBootstrap.cs`
- `public static class UiKoreanFontBootstrap`
- `private sealed class UiKoreanFontBootstrapDriver : MonoBehaviour`
### `Assets/_Project/Scripts/Utils/ConfigurableJointExtensions.cs`
- `public static class ConfigurableJointExtensions`
### `Assets/_Project/Scripts/Utils/IgnoreCollision.cs`
- `public class IgnoreCollision : MonoBehaviour`
