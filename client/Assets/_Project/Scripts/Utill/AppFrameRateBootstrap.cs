using UnityEngine;

/// <summary>
/// 앱 시작 시 가장 먼저 실행되어 프레임레이트를 설정한다.
/// Fusion 64Hz 틱과 보간이 부드럽게 동작하려면 렌더 프레임이 충분히 높아야 한다.
/// vSyncCount는 QualitySettings에서 0으로 설정돼 있어야 이 값이 유효하다.
/// </summary>
public static class AppFrameRateBootstrap
{
    // Fusion 틱레이트(64Hz)의 2배 → 보간 alpha가 항상 0~1 사이에서 충분히 샘플링됨
    private const int TargetFrameRate = 128;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        Application.targetFrameRate = TargetFrameRate;
    }
}
