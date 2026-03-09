/*
 * 파일 개요:
 * - ItemIds 스크립트가 들어 있는 파일이다.
 * - Common 계층에서 아이템 시스템 전반이 공유하는 모델, 상수, 인터페이스를 정의한다.
 * - 이 파일이 바뀌면 Character, World, Runtime 전부에 영향이 갈 수 있으므로 하위 호환성을 우선 확인한다.
 */
namespace SSAFYPlayTime.Gameplay.Items
{
    public static class ItemIds
    {
        public const string BlackholeBomb = "ITEM_BLACKHOLE_BOMB";
        public const string Growth = "ITEM_GROWTH";
        public const string Shrink = "ITEM_SHRINK";
        public const string Americano = "ITEM_AMERICANO";
        public const string Flamethrower = "ITEM_FLAMETHROWER";
        public const string Invisibility = "ITEM_INVISIBILITY";
        public const string WaterMelonSword = "ITEM_WATERMELON_SWORD";
        public const string OfficeTool = WaterMelonSword;
        public const string SatelliteStrike = "ITEM_SATELLITE_STRIKE";
    }
}

