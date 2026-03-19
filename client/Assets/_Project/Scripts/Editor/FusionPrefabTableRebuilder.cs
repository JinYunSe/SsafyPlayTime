#if UNITY_EDITOR
using Fusion.Editor;
using UnityEditor;
using UnityEngine;

namespace SSAFYPlayTime.Editor
{
    public static class FusionPrefabTableRebuilder
    {
        [MenuItem("Window/SSAFYPlayTime/Fusion Prefab Table 재빌드")]
        public static void RebuildPrefabTable()
        {
            NetworkProjectConfigUtilities.RebuildPrefabTable();
            Debug.Log("[SSAFYPlayTime] Fusion Prefab Table 재빌드 완료.");
        }
    }
}
#endif
