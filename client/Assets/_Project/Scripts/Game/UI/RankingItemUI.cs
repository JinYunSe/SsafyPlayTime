using TMPro;
using UnityEngine;

// 순위 항목 하나를 표현하는 UI 컴포넌트.
// GameEndScene의 RankingItemPrefab에 붙여서 rankText, nicknameText를 연결한다.
public class RankingItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text nicknameText;

    public void SetData(int rank, string nickname)
    {
        if (rankText != null)
            rankText.text = $"{rank}등";

        if (nicknameText != null)
            nicknameText.text = nickname;
    }
}
