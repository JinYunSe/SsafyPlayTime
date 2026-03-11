using Fusion;
using UnityEngine;

// Fusion 입력 파이프라인용 입력 데이터 구조체.
// 각 클라이언트가 OnInput 콜백에서 채워 서버로 전송하며,
// 서버는 FixedUpdateNetwork에서 GetInput<PlayerNetworkInput>()으로 꺼내 물리에 적용한다.
public struct PlayerNetworkInput : INetworkInput
{
    // WASD / 조이스틱 이동 입력 (-1 ~ 1 범위의 X, Y 축)
    public Vector2 Move;
    public float CameraYaw;

    // 점프 키(Space)
    public NetworkBool Jump;

    // Q = 보유 아이템 사용 시도(없으면 펀치)
    public NetworkBool Punch;

    // F키 = 무기 떨구기 / 내려놓기
    public NetworkBool Drop;

    // 우클릭 클릭 = 던지기 / 어퍼컷
    public NetworkBool Throw;

    // 좌클릭 꾹 = 그랩 홀드
    public NetworkBool GrabHold;

    // 마우스 휠 클릭 = 박치기 (Phase C에서 구현)
    public NetworkBool Headbutt;

    // Shift 홀드 = 달리기 (Phase C에서 구현)
    public NetworkBool Sprint;
}
