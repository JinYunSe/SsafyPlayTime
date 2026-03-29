#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// bridge_001 메시에서 밧줄 서브메시(index 1)를 제거하고
/// 바닥 판자만 있는 새 메시 에셋을 생성한다.
///
/// [사용법]
/// 메뉴 상단 → Tools → Bridge → Extract Bridge Floor Mesh
/// 생성된 bridge_001_floor.asset을 bridge_001 프리팹의
/// MeshFilter(또는 SkinnedMeshRenderer)에 할당하면 된다.
/// </summary>
public static class BridgeMeshExtractor
{
    private const string SourceMeshGuid = "a4496ecf1d6c46247b9bfed5dd558878";
    private const string OutputPath = "Assets/ithappy/Platformer_Deathrun/Meshes/Props/bridge_001_floor.asset";
    private const int FloorSubmeshIndex = 0; // 0: Color(바닥), 1: rope_1(밧줄)

    [MenuItem("Tools/Bridge/Extract Bridge Floor Mesh")]
    public static void ExtractFloorMesh()
    {
        // FBX에서 메시 로드
        var assetPath = AssetDatabase.GUIDToAssetPath(SourceMeshGuid);
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog("실패", "bridge_001.fbx를 찾을 수 없습니다.", "OK");
            return;
        }

        var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        Mesh sourceMesh = null;
        foreach (var a in allAssets)
        {
            if (a is Mesh m)
            {
                sourceMesh = m;
                break;
            }
        }

        if (sourceMesh == null)
        {
            EditorUtility.DisplayDialog("실패", "FBX 안에서 Mesh를 찾을 수 없습니다.", "OK");
            return;
        }

        if (sourceMesh.subMeshCount <= FloorSubmeshIndex)
        {
            EditorUtility.DisplayDialog("실패", $"서브메시가 {sourceMesh.subMeshCount}개뿐입니다. (floor index={FloorSubmeshIndex})", "OK");
            return;
        }

        // 바닥 서브메시 삼각형 추출
        var floorTris = sourceMesh.GetTriangles(FloorSubmeshIndex);

        // 실제 사용되는 버텍스 인덱스 수집
        var usedIndices = new System.Collections.Generic.HashSet<int>(floorTris);
        var oldToNew = new System.Collections.Generic.Dictionary<int, int>();
        int newIdx = 0;
        foreach (var idx in usedIndices)
            oldToNew[idx] = newIdx++;

        int vertCount = oldToNew.Count;
        var srcVerts    = sourceMesh.vertices;
        var srcNormals  = sourceMesh.normals;
        var srcUVs      = sourceMesh.uv;
        var srcColors   = sourceMesh.colors;
        var srcTangents = sourceMesh.tangents;

        var newVerts    = new Vector3[vertCount];
        var newNormals  = new Vector3[vertCount];
        var newUVs      = new Vector2[vertCount];
        var newColors   = new Color[vertCount];
        var newTangents = new Vector4[vertCount];

        foreach (var kv in oldToNew)
        {
            int o = kv.Key, n = kv.Value;
            newVerts[n]   = srcVerts[o];
            if (srcNormals.Length  > o) newNormals[n]  = srcNormals[o];
            if (srcUVs.Length      > o) newUVs[n]      = srcUVs[o];
            if (srcColors.Length   > o) newColors[n]   = srcColors[o];
            if (srcTangents.Length > o) newTangents[n] = srcTangents[o];
        }

        // 삼각형 인덱스 remapping
        var newTris = new int[floorTris.Length];
        for (int i = 0; i < floorTris.Length; i++)
            newTris[i] = oldToNew[floorTris[i]];

        // 새 메시 생성
        var newMesh = new Mesh { name = "bridge_001_floor" };
        newMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        newMesh.vertices    = newVerts;
        newMesh.normals     = newNormals;
        newMesh.uv          = newUVs;
        if (srcColors.Length > 0) newMesh.colors = newColors;
        if (srcTangents.Length > 0) newMesh.tangents = newTangents;
        newMesh.SetTriangles(newTris, 0);
        newMesh.RecalculateBounds();

        // 저장
        var dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        AssetDatabase.CreateAsset(newMesh, OutputPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "완료",
            $"바닥 메시 생성 완료!\n{OutputPath}\n\n" +
            $"버텍스: {vertCount}개 / 삼각형: {newTris.Length / 3}개\n\n" +
            "bridge_001 프리팹 열기 →\n" +
            "MeshFilter의 Mesh 슬롯에 bridge_001_floor 할당\n" +
            "Materials에서 Element 1(rope) 슬롯 제거",
            "OK");

        // 생성된 에셋 선택
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Mesh>(OutputPath);
    }
}
#endif
