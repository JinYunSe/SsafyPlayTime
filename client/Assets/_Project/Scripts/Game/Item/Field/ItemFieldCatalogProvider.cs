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
