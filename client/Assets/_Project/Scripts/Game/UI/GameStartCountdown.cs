using System.Collections;
using Fusion;
using UnityEngine;

// 게임 시작 카운트다운 (3 → 2 → 1 → Start!)
// StateAuthority(방장/호스트)에서만 카운트다운을 구동하고,
// [Networked] 값 변경을 Render()에서 감지해 모든 클라이언트 UI를 동기화한다.
public class GameStartCountdown : NetworkBehaviour
{
    // -1: 아직 시작 안 함 / 3,2,1: 카운트다운 / 0: Start! / -2: 종료
    [Networked] private int CountdownValue { get; set; } = -1;

    private int _prevCountdownValue = -1;

    // 모든 클라이언트에서 Render()로 동기화되는 입력 허용 플래그
    public static bool InputEnabled { get; private set; } = false;

    public override void Spawned()
    {
        InputEnabled = false;
        if (HasStateAuthority)
            StartCoroutine(RunCountdown());
    }

    public override void Render()
    {
        if (CountdownValue == _prevCountdownValue) return;
        _prevCountdownValue = CountdownValue;
        UpdateHUD();

        if (CountdownValue == 0)
            InputEnabled = true;
    }

    private IEnumerator RunCountdown()
    {
        yield return new WaitForSeconds(2f);

        CountdownValue = 3;
        yield return new WaitForSeconds(1f);

        CountdownValue = 2;
        yield return new WaitForSeconds(1f);

        CountdownValue = 1;
        yield return new WaitForSeconds(1f);

        CountdownValue = 0; // "Start!"
        yield return new WaitForSeconds(1.5f);

        CountdownValue = -2; // 숨김
    }

    private void UpdateHUD()
    {
        var hud = GameHUD.FindOrCreate();
        if (hud == null)
            return;

        switch (CountdownValue)
        {
            case 3:
                hud.HideLoadingAndShow("3", Color.white);
                break;
            case 2:
            case 1:
                hud.ShowWithPulse(CountdownValue.ToString(), Color.white);
                break;
            case 0:
                hud.ShowWithPulse("Start!", Color.green);
                break;
            default:
                hud.HideCountdown();
                break;
        }
    }
}
