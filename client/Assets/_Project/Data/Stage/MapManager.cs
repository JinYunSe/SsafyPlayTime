using System.Collections;
using System.Collections.Generic;

using Fusion;
using UnityEngine;

public class MapManager : NetworkBehaviour
{
    public MapData mapData;

    // 네트워크를 통해 동기화되는 경과 시간
    [Networked] private TickTimer GimmickTimer { get; set; }
    [Networked] private float ElapsedTime { get; set; }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority) // 서버(호스트)만 계산
        {
            ElapsedTime += Runner.DeltaTime;
        }

        // 리스트를 돌며 각 기믹 실행
        foreach (var gimmick in mapData.Gimmicks)
        {
            if (ElapsedTime >= gimmick.startDelay)
            {
                ExecuteGimmick(gimmick);
            }
        }
    }

    private void ExecuteGimmick(MapGimmickData data)
    {
        if (data is SeaLevelData seaData)
        {
            // 1. 씬에서 해수면을 제어하는 컨트롤러를 찾습니다.
            // (성능을 위해 씬 시작 시 미리 찾아두는 것을 더 권장합니다)
            SeaLevelController controller = FindObjectOfType<SeaLevelController>();

            if (controller != null)
            {
                // 2. 컨트롤러에 SO 데이터를 전달합니다.
                // SeaLevelController에 public SeaLevelData seaLevelData 변수가 있어야 합니다.
                controller.seaLevelData = seaData;

                // 3. (옵션) 기존 컨트롤러가 자동으로 시작되지 않는다면 여기서 활성화 신호를 줍니다.
                // controller.enabled = true; 
            }
        }
        // else if (data is JoyconGimmickData joyData) { ... }
    }
}
