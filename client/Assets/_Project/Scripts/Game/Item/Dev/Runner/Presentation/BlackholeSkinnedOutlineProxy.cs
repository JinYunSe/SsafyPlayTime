/*
 * 파일 개요:
 * - BlackholeSkinnedOutlineProxy 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Presentation 계층에서 스킨드 메시용 블랙홀 외곽선 프록시 갱신을 담당한다.
 * - 플레이어처럼 SkinnedMeshRenderer 비중이 높은 대상의 현재 포즈를 베이크해서 외곽선 프록시에 반영한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    [DisallowMultipleComponent]
    public sealed class BlackholeSkinnedOutlineProxy : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer sourceRenderer;
        [SerializeField] private MeshFilter targetMeshFilter;

        private Mesh _bakedMesh;

        public void Initialize(SkinnedMeshRenderer source, MeshFilter target)
        {
            sourceRenderer = source;
            targetMeshFilter = target;
            EnsureBakedMesh();
            RefreshNow();
        }

        private void LateUpdate()
        {
            RefreshNow();
        }

        private void OnDisable()
        {
            if (targetMeshFilter != null)
            {
                targetMeshFilter.sharedMesh = null;
            }
        }

        private void OnDestroy()
        {
            if (_bakedMesh != null)
            {
                Destroy(_bakedMesh);
                _bakedMesh = null;
            }
        }

        private void RefreshNow()
        {
            if (sourceRenderer == null || targetMeshFilter == null)
            {
                return;
            }

            if (!sourceRenderer.enabled || sourceRenderer.sharedMesh == null)
            {
                targetMeshFilter.sharedMesh = null;
                return;
            }

            EnsureBakedMesh();
            if (_bakedMesh == null)
            {
                return;
            }

            // 한국어: 현재 프레임의 스킨 포즈를 프록시 메시에 반영한다.
            sourceRenderer.BakeMesh(_bakedMesh, true);
            targetMeshFilter.sharedMesh = _bakedMesh;
        }

        private void EnsureBakedMesh()
        {
            if (_bakedMesh != null)
            {
                return;
            }

            _bakedMesh = new Mesh
            {
                name = "BlackholeSkinnedOutline_Baked"
            };
            _bakedMesh.MarkDynamic();
        }
    }
}
