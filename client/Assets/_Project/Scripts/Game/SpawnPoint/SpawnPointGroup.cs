using System.Collections.Generic;
using UnityEngine;

public sealed class SpawnPointGroup : MonoBehaviour
{
    private Transform[] _points;
    private readonly List<int> _availableIndices = new();

    private void Awake()
    {
        _points = new Transform[transform.childCount];
        for (var i = 0; i < transform.childCount; i++)
            _points[i] = transform.GetChild(i);

        ResetAvailable();
    }

    /// <summary>
    /// 아직 사용되지 않은 Point 중 하나를 랜덤으로 선택해 위치와 회전을 반환합니다.
    /// 선택된 Point는 사용됨으로 표시되어 다시 반환되지 않습니다.
    /// </summary>
    public bool ClaimRandomPoint(out Vector3 position, out Quaternion rotation)
    {
        if (_availableIndices.Count == 0)
        {
            position = transform.position;
            rotation = Quaternion.identity;
            return false;
        }

        var pick = Random.Range(0, _availableIndices.Count);
        var index = _availableIndices[pick];
        _availableIndices.RemoveAt(pick);

        position = _points[index].position;
        rotation = _points[index].rotation;
        return true;
    }

    /// <summary>
    /// 모든 Point를 다시 사용 가능 상태로 초기화합니다. (씬 재시작 시 호출)
    /// </summary>
    public void ReleaseAll()
    {
        ResetAvailable();
    }

    private void ResetAvailable()
    {
        _availableIndices.Clear();
        if (_points == null) return;
        for (var i = 0; i < _points.Length; i++)
            _availableIndices.Add(i);
    }
}
