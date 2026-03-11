using UnityEngine;

namespace SSAFYPlayTime
{
    // 캐릭터 인덱스(CharacterKind)와 게임플레이용 NetworkObject 프리팹의 매핑을 관리하는 ScriptableObject.
    // 프리팹이 교체되더라도 이 에셋만 수정하면 되며, 나머지 게임 로직(마이그레이션, 동기화 등)은 변경 불필요.
    [CreateAssetMenu(fileName = "CharacterRegistry", menuName = "SSAFYPlayTime/Character Registry")]
    public sealed class CharacterRegistry : ScriptableObject
    {
        [Header("Gameplay Prefabs (NetworkObject 포함)")]
        [SerializeField] private GameObject stattyPrefab;
        [SerializeField] private GameObject alGPrefab;
        [SerializeField] private GameObject fitPrefab;
        [SerializeField] private GameObject wisePrefab;

        // characterIndex(0~3)에 해당하는 게임플레이 프리팹을 반환한다. 범위 밖이면 null.
        public GameObject GetPrefabByIndex(int characterIndex)
        {
            return characterIndex switch
            {
                0 => stattyPrefab,
                1 => alGPrefab,
                2 => fitPrefab,
                3 => wisePrefab,
                _ => null
            };
        }
    }
}
