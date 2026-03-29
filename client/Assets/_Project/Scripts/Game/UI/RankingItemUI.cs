using TMPro;
using UnityEngine;

// 순위 항목 하나를 표현하는 UI 컴포넌트.
// LauncherScene gameEndPanel 하위의 rankingContainer에 배치해 rankText, nicknameText를 연결한다.
public class RankingItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text nicknameText;
    public void SetData(int rank, string nickname, int characterTypeIndex = -1)
    {
        if (rankText != null)
            rankText.text = $"{rank}등";

        if (nicknameText != null)
            nicknameText.text = nickname;
    }
}
