/*
 * 파일 개요:
 * - ItemGameplayRunner.Input 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Core 계층에서 ItemGameplayRunner의 생명주기, 입력, 이벤트 연결 같은 중심 흐름을 관리한다.
 * - 테스트용 실행 경로를 정리하는 파일이므로, 실게임 로직과 분리된 검증 허브라는 성격을 유지해야 한다.
 */
namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void Update()
        {
            TickAudioListenerGuard();
            UpdateFlamethrowerVisualFollow();
            TickLocalDebugController();
        }
    }
}

