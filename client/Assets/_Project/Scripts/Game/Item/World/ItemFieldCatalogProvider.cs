/*
 * 파일 개요:
 * - ItemFieldCatalogProvider 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 필드 시스템에서 사용할 아이템 카탈로그 캐시를 관리한다.
    /// </summary>
    public sealed class ItemFieldCatalogProvider
    {
        private ItemCatalog _cachedCatalog;

        public void Invalidate()
        {
            _cachedCatalog = null;
        }

        public bool TryGetCatalog(ItemCatalogLoadOptions options, out ItemCatalog catalog, out string error)
        {
            if (_cachedCatalog != null)
            {
                catalog = _cachedCatalog;
                error = string.Empty;
                return true;
            }

            if (!ItemCatalogLoader.TryLoadFromDisk(options, out catalog, out _, out error))
            {
                return false;
            }

            _cachedCatalog = catalog;
            return true;
        }
    }
}

