using UnityEditor;
using UnityEngine;
using SSAFYPlayTime.Character;

/// <summary>
/// GrabAnchorPoint / GrabSensor 시스템 설정 유틸리티.
/// 1) 레이어 충돌 매트릭스 설정 (GrabHurtbox ↔ GrabSensor만 충돌)
/// 2) 선택된 캐릭터 프리팹/오브젝트에 앵커 포인트 자식 오브젝트 자동 생성
/// 3) 선택된 캐릭터의 손 뼈에 GrabSensor 자식 오브젝트 자동 생성
/// </summary>
public static class GrabSystemSetup
{
    private const string GrabHurtboxLayerName = "GrabHurtbox";
    private const string GrabSensorLayerName = "GrabSensor";

    // ═══════════════════════════════════════
    // 레이어 충돌 매트릭스
    // ═══════════════════════════════════════

    [MenuItem("Tools/Grab System/Setup Layer Collision Matrix")]
    public static void SetupLayerCollisionMatrix()
    {
        var hurtboxLayer = LayerMask.NameToLayer(GrabHurtboxLayerName);
        var sensorLayer = LayerMask.NameToLayer(GrabSensorLayerName);

        if (hurtboxLayer < 0 || sensorLayer < 0)
        {
            Debug.LogError($"[GrabSystemSetup] 레이어 미등록: GrabHurtbox={hurtboxLayer}, GrabSensor={sensorLayer}. " +
                "Project Settings > Tags and Layers에서 먼저 레이어를 추가하세요.");
            return;
        }

        // 모든 레이어와 GrabHurtbox/GrabSensor 충돌 비활성화
        for (int i = 0; i < 32; i++)
        {
            Physics.IgnoreLayerCollision(hurtboxLayer, i, true);
            Physics.IgnoreLayerCollision(sensorLayer, i, true);
        }

        // GrabHurtbox ↔ GrabSensor만 충돌 활성화
        Physics.IgnoreLayerCollision(hurtboxLayer, sensorLayer, false);

        Debug.Log($"[GrabSystemSetup] 레이어 충돌 매트릭스 설정 완료: " +
            $"GrabHurtbox(L{hurtboxLayer}) ↔ GrabSensor(L{sensorLayer}) 전용 충돌");
    }

    // ═══════════════════════════════════════
    // GrabAnchorPoint 자동 추가
    // ═══════════════════════════════════════

    private struct AnchorDef
    {
        public GrabAnchorPoint.AnchorId id;
        public string[] boneNames;
        public float radius;
        public float priority;
        public Vector3 gripOffset;
    }

    private static readonly AnchorDef[] AnchorDefinitions = new[]
    {
        new AnchorDef
        {
            id = GrabAnchorPoint.AnchorId.Chest,
            boneNames = new[] { "Spine2", "Chest", "Spine1" },
            radius = 0.15f,
            priority = 3f,
            gripOffset = new Vector3(0f, 0f, 0.08f)
        },
        new AnchorDef
        {
            id = GrabAnchorPoint.AnchorId.Hips,
            boneNames = new[] { "Hips", "Pelvis" },
            radius = 0.14f,
            priority = 2.5f,
            gripOffset = new Vector3(0f, 0f, 0.06f)
        },
        new AnchorDef
        {
            id = GrabAnchorPoint.AnchorId.LeftUpperArm,
            boneNames = new[] { "LeftUpperArm", "LeftArm" },
            radius = 0.10f,
            priority = 1.5f,
            gripOffset = new Vector3(0f, 0f, 0.04f)
        },
        new AnchorDef
        {
            id = GrabAnchorPoint.AnchorId.RightUpperArm,
            boneNames = new[] { "RightUpperArm", "RightArm" },
            radius = 0.10f,
            priority = 1.5f,
            gripOffset = new Vector3(0f, 0f, 0.04f)
        },
        new AnchorDef
        {
            id = GrabAnchorPoint.AnchorId.LeftForearm,
            boneNames = new[] { "LeftForeArm", "LeftLowerArm" },
            radius = 0.08f,
            priority = 1f,
            gripOffset = new Vector3(0f, 0f, 0.03f)
        },
        new AnchorDef
        {
            id = GrabAnchorPoint.AnchorId.RightForearm,
            boneNames = new[] { "RightForeArm", "RightLowerArm" },
            radius = 0.08f,
            priority = 1f,
            gripOffset = new Vector3(0f, 0f, 0.03f)
        },
        new AnchorDef
        {
            id = GrabAnchorPoint.AnchorId.Head,
            boneNames = new[] { "Head" },
            radius = 0.10f,
            priority = 0.5f,
            gripOffset = new Vector3(0f, -0.04f, 0f)
        }
    };

    [MenuItem("Tools/Grab System/Add Anchor Points to Selected")]
    public static void AddAnchorPointsToSelected()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogError("[GrabSystemSetup] 오브젝트를 선택하세요.");
            return;
        }

        var hurtboxLayer = LayerMask.NameToLayer(GrabHurtboxLayerName);
        if (hurtboxLayer < 0)
        {
            Debug.LogError("[GrabSystemSetup] GrabHurtbox 레이어가 없습니다. 먼저 레이어를 추가하세요.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(selected, "Add GrabAnchorPoints");

        int added = 0;
        var allTransforms = selected.GetComponentsInChildren<Transform>(true);

        foreach (var def in AnchorDefinitions)
        {
            var bone = FindBestBone(allTransforms, def.boneNames);
            if (bone == null)
            {
                Debug.LogWarning($"[GrabSystemSetup] 본 미발견: {string.Join("/", def.boneNames)} (anchor={def.id})");
                continue;
            }

            // 이미 존재하면 스킵
            var existing = bone.GetComponentInChildren<GrabAnchorPoint>(true);
            if (existing != null && existing.Id == def.id)
            {
                Debug.Log($"[GrabSystemSetup] 이미 존재: {def.id} on {bone.name}");
                continue;
            }

            var anchorObj = new GameObject($"GrabAnchor_{def.id}");
            anchorObj.transform.SetParent(bone, false);
            anchorObj.transform.localPosition = Vector3.zero;
            anchorObj.transform.localRotation = Quaternion.identity;
            anchorObj.layer = hurtboxLayer;

            var anchor = anchorObj.AddComponent<GrabAnchorPoint>();
            // SerializedObject를 통해 private 필드 설정
            var so = new SerializedObject(anchor);
            so.FindProperty("anchorId").intValue = (int)def.id;
            so.FindProperty("grabRadius").floatValue = def.radius;
            so.FindProperty("grabPriority").floatValue = def.priority;
            so.FindProperty("localGripOffset").vector3Value = def.gripOffset;
            so.ApplyModifiedPropertiesWithoutUndo();

            var sphere = anchorObj.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = def.radius;

            added++;
            Debug.Log($"[GrabSystemSetup] Added {def.id} → {bone.name} (r={def.radius}, p={def.priority})");
        }

        Debug.Log($"[GrabSystemSetup] {added}개의 GrabAnchorPoint 추가 완료 ({selected.name})");
    }

    // ═══════════════════════════════════════
    // GrabSensor 자동 추가 (손 뼈)
    // ═══════════════════════════════════════

    [MenuItem("Tools/Grab System/Add Sensors to Selected Hands")]
    public static void AddSensorsToSelectedHands()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogError("[GrabSystemSetup] 오브젝트를 선택하세요.");
            return;
        }

        var sensorLayer = LayerMask.NameToLayer(GrabSensorLayerName);
        if (sensorLayer < 0)
        {
            Debug.LogError("[GrabSystemSetup] GrabSensor 레이어가 없습니다. 먼저 레이어를 추가하세요.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(selected, "Add GrabSensors");

        var allTransforms = selected.GetComponentsInChildren<Transform>(true);
        var handBoneNames = new[]
        {
            new[] { "LeftHand" },
            new[] { "RightHand" }
        };

        int added = 0;
        foreach (var names in handBoneNames)
        {
            var bone = FindBestBone(allTransforms, names);
            if (bone == null)
            {
                Debug.LogWarning($"[GrabSystemSetup] 손 본 미발견: {string.Join("/", names)}");
                continue;
            }

            // HandGrabHandler가 있는 오브젝트의 자식에 센서 추가
            var handler = bone.GetComponent<HandGrabHandler>();
            var sensorParent = handler != null ? bone : bone;

            // 이미 존재하면 스킵
            if (sensorParent.GetComponentInChildren<GrabSensor>(true) != null)
            {
                Debug.Log($"[GrabSystemSetup] GrabSensor 이미 존재: {bone.name}");
                continue;
            }

            var sensorObj = new GameObject($"GrabSensor_{bone.name}");
            sensorObj.transform.SetParent(sensorParent, false);
            sensorObj.transform.localPosition = new Vector3(0f, -0.02f, 0.06f); // 손바닥 부근
            sensorObj.transform.localRotation = Quaternion.identity;
            sensorObj.layer = sensorLayer;

            var sensor = sensorObj.AddComponent<GrabSensor>();
            var so = new SerializedObject(sensor);
            so.FindProperty("sensorRadius").floatValue = 0.06f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var sphere = sensorObj.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.06f;

            added++;
            Debug.Log($"[GrabSystemSetup] Added GrabSensor → {bone.name}");
        }

        Debug.Log($"[GrabSystemSetup] {added}개의 GrabSensor 추가 완료 ({selected.name})");
    }

    // ═══════════════════════════════════════
    // 전체 자동 설정
    // ═══════════════════════════════════════

    [MenuItem("Tools/Grab System/Full Setup on Selected")]
    public static void FullSetupOnSelected()
    {
        SetupLayerCollisionMatrix();
        AddAnchorPointsToSelected();
        AddSensorsToSelectedHands();
    }

    private static Transform FindBestBone(Transform[] allTransforms, string[] candidateNames)
    {
        foreach (var name in candidateNames)
        {
            foreach (var t in allTransforms)
            {
                if (t != null && t.name == name)
                    return t;
            }
        }
        return null;
    }
}
