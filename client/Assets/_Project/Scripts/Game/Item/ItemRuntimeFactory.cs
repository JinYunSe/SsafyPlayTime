using System.Collections.Generic;

namespace SSAFYPlayTime.Gameplay.Items
{
    public static class ItemRuntimeFactory
    {
        public static bool TryCreateDefault(
            IItemRuntimeBridge bridge,
            out ItemRuntimeController controller,
            out IReadOnlyList<string> warnings,
            out string error)
        {
            controller = null;
            warnings = null;
            error = string.Empty;

            var options = ItemCatalogLoader.CreateDefaultOptions();
            if (!ItemCatalogLoader.TryLoadFromDisk(options, out var catalog, out warnings, out error))
            {
                return false;
            }

            controller = new ItemRuntimeController(catalog, bridge);
            return true;
        }
    }
}
