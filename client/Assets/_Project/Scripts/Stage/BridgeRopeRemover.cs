using UnityEngine;

namespace SSAFYPlayTime.Stage
{
    /// <summary>
    /// bridge_001 프리팹에 부착한다.
    /// Awake 시 rope 서브메시(index 1)를 제거하고 바닥 판자 메시만 남긴다.
    /// bridge_001.fbx의 Read/Write Enabled가 true여야 동작한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class BridgeRopeRemover : MonoBehaviour
    {
        private const int FloorSubmeshIndex = 0; // 0: Color(바닥판자), 1: rope_1(밧줄)

        private void Awake()
        {
            var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null)
            {
                var newMesh = ExtractSubmesh(smr.sharedMesh, FloorSubmeshIndex);
                if (newMesh == null) return;
                smr.sharedMesh = newMesh;
                var mats = smr.sharedMaterials;
                if (mats.Length > 1)
                    smr.sharedMaterials = new[] { mats[FloorSubmeshIndex] };
                return;
            }

            var mf = GetComponentInChildren<MeshFilter>(true);
            if (mf != null)
            {
                var newMesh = ExtractSubmesh(mf.sharedMesh, FloorSubmeshIndex);
                if (newMesh == null) return;
                mf.sharedMesh = newMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) return;
                var mats = mr.sharedMaterials;
                if (mats.Length > 1)
                    mr.sharedMaterials = new[] { mats[FloorSubmeshIndex] };
            }
        }

        private static Mesh ExtractSubmesh(Mesh source, int submeshIndex)
        {
            if (source == null || submeshIndex >= source.subMeshCount)
                return null;

            var tris = source.GetTriangles(submeshIndex);
            var usedSet = new System.Collections.Generic.HashSet<int>(tris);
            var oldToNew = new System.Collections.Generic.Dictionary<int, int>();
            int newIdx = 0;
            foreach (var idx in usedSet)
                oldToNew[idx] = newIdx++;

            int vCount = oldToNew.Count;
            var srcV  = source.vertices;
            var srcN  = source.normals;
            var srcUV = source.uv;
            var srcT  = source.tangents;

            var newV  = new Vector3[vCount];
            var newN  = new Vector3[vCount];
            var newUV = new Vector2[vCount];
            var newT  = new Vector4[vCount];

            foreach (var kv in oldToNew)
            {
                int o = kv.Key, n = kv.Value;
                newV[n]  = srcV[o];
                if (srcN.Length  > o) newN[n]  = srcN[o];
                if (srcUV.Length > o) newUV[n] = srcUV[o];
                if (srcT.Length  > o) newT[n]  = srcT[o];
            }

            var newTris = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
                newTris[i] = oldToNew[tris[i]];

            var mesh = new Mesh
            {
                name = source.name + "_floor",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
            };
            mesh.vertices  = newV;
            mesh.normals   = newN;
            mesh.uv        = newUV;
            mesh.tangents  = newT;
            mesh.SetTriangles(newTris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
