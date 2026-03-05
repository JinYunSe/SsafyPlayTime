using Fusion;
using UnityEngine;

// Fusion 입력 파이프라인용 입력 데이터 구조체.
// 각 클라이언트가 OnInput 콜백에서 채워 서버로 전송하며,
// 서버는 FixedUpdateNetwork에서 GetInput<PlayerNetworkInput>()으로 꺼내 물리에 적용한다.
public struct PlayerNetworkInput : INetworkInput
{
    // WASD / 조이스틱 이동 입력 (-1 ~ 1 범위의 X, Y 축)
    public Vector2 Move;

    // 점프 키(Space) 입력 여부. Space를 누르고 있는 동안 true
    public NetworkBool Jump;
}
