using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;

/// <summary>
/// 모든 캐릭터에 Knight와 동일한 애니메이션 시스템을 자동으로 설정하는 에디터 스크립트
/// 새 프로젝트에서 Import 후 Tools > Setup All Character Animations 한 번만 실행하면 끝!
/// </summary>
public class CharacterAnimationSetup : EditorWindow
{
    // =============================================
    // EP1 캐릭터 (CharacterArmature/Hips 구조)
    // =============================================
    static readonly string[][] ep1Characters = new string[][]
    {
        new string[] { "GhostCharacter", "Ghost Animated" },
        new string[] { "GlassesCharacter", "Glasses Animated" },
        new string[] { "GreenCharacter", "Green Animated" },
        new string[] { "SsatyCharacter", "Ssaty Animated" },
    };

    // EP1: CharacterArmature bone -> Knight bone
    static readonly Dictionary<string, string> ep1BoneMapping = new Dictionary<string, string>()
    {
        { "CharacterArmature/Hips/LeftThigh", "Rig/root/hips/upperleg.l" },
        { "CharacterArmature/Hips/RightThigh", "Rig/root/hips/upperleg.r" },
        { "CharacterArmature/Hips/Waist", "Rig/root/hips/spine" },
        { "CharacterArmature/Hips/Waist/Chest/Head", "Rig/root/hips/spine/chest/head" },
        { "CharacterArmature/Hips/Waist/Chest/LeftArm", "Rig/root/hips/spine/chest/upperarm.l" },
        { "CharacterArmature/Hips/Waist/Chest/RightArm", "Rig/root/hips/spine/chest/upperarm.r" },
    };

    static readonly HashSet<string> ep1SyncAnimBones = new HashSet<string>()
    {
        "CharacterArmature/Hips/LeftThigh",
        "CharacterArmature/Hips/RightThigh",
    };

    // =============================================
    // Knight (Character) - Rig/root/hips 구조
    // =============================================
    static readonly Dictionary<string, string> knightBoneMapping = new Dictionary<string, string>()
    {
        { "Rig/root/hips/upperleg.l", "Rig/root/hips/upperleg.l" },
        { "Rig/root/hips/upperleg.r", "Rig/root/hips/upperleg.r" },
        { "Rig/root/hips/spine", "Rig/root/hips/spine" },
        { "Rig/root/hips/spine/chest/head", "Rig/root/hips/spine/chest/head" },
        { "Rig/root/hips/spine/chest/upperarm.l", "Rig/root/hips/spine/chest/upperarm.l" },
        { "Rig/root/hips/spine/chest/upperarm.r", "Rig/root/hips/spine/chest/upperarm.r" },
    };

    static readonly HashSet<string> knightSyncAnimBones = new HashSet<string>()
    {
        "Rig/root/hips/upperleg.l",
        "Rig/root/hips/upperleg.r",
    };

    static readonly string[] knightAnimBonePaths = new string[]
    {
        "Rig/root/hips/spine",
        "Rig/root/hips/upperleg.l",
        "Rig/root/hips/upperleg.r",
        "Rig/root/hips/spine/chest/head",
        "Rig/root/hips/spine/chest/upperarm.l",
        "Rig/root/hips/spine/chest/upperarm.r",
    };

    // =============================================
    // 메뉴 아이템
    // =============================================

    [MenuItem("Tools/Setup All Character Animations")]
    static void SetupAllCharacterAnimations()
    {
        int successCount = 0;
        int totalCount = 0;

        // 1. Knight (Character) 처리
        totalCount++;
        Debug.Log("[AnimSetup] ========== Processing Character (Knight) ==========");
        if (SetupKnightCharacter())
            successCount++;

        // 2. EP1 캐릭터 처리 (Ghost, Glasses, Green, Ssaty)
        foreach (string[] charInfo in ep1Characters)
        {
            totalCount++;
            string rootName = charInfo[0];
            string animatedName = charInfo[1];

            Debug.Log($"[AnimSetup] ========== Processing {rootName} ==========");

            if (SetupEP1Character(rootName, animatedName))
                successCount++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[AnimSetup] ===== Complete! {successCount}/{totalCount} characters set up. =====");
        EditorUtility.DisplayDialog("Setup Complete",
            $"{successCount}/{totalCount} characters set up.\nSave the scene to preserve changes.", "OK");
    }

    static bool SetupKnightCharacter()
    {
        GameObject root = GameObject.Find("Character");
        if (root == null)
        {
            Debug.LogWarning("[AnimSetup] Character not found in scene, skipping.");
            return false;
        }

        Transform model = root.transform.Find("Model");
        if (model == null)
        {
            Debug.LogError("[AnimSetup] Model not found under Character!");
            return false;
        }

        Transform knightPhysics = model.Find("Knight");
        if (knightPhysics == null)
        {
            Debug.LogError("[AnimSetup] Knight not found under Character/Model!");
            return false;
        }

        Transform knightAnimated = model.Find("Knight Animated");
        if (knightAnimated == null)
        {
            string knightFbxPath = "Assets/Models/KayKit_Adventurers_1.0_FREE/Characters/fbx/Knight.fbx";
            GameObject knightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(knightFbxPath);
            if (knightPrefab == null)
            {
                Debug.LogError("[AnimSetup] Knight FBX not found!");
                return false;
            }

            GameObject animObj = (GameObject)PrefabUtility.InstantiatePrefab(knightPrefab, model);
            animObj.name = "Knight Animated";
            Undo.RegisterCreatedObjectUndo(animObj, "Create Knight Animated");
            animObj.transform.localPosition = knightPhysics.localPosition;
            animObj.transform.localRotation = knightPhysics.localRotation;
            animObj.transform.localScale = knightPhysics.localScale;
            knightAnimated = animObj.transform;

            CleanAnimatedCopy(knightAnimated);
            DisableRenderers(knightAnimated);
            Debug.Log("[AnimSetup] Knight Animated created.");
        }

        AnimatorController knightController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/Animations/Animator Controller Knight.controller");

        Animator animator = knightAnimated.GetComponent<Animator>();
        if (animator == null)
            animator = knightAnimated.gameObject.AddComponent<Animator>();

        animator.runtimeAnimatorController = knightController;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;

        string fbxPath = "Assets/Models/KayKit_Adventurers_1.0_FREE/Characters/fbx/Knight.fbx";
        var allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var asset in allAssets)
        {
            if (asset is Avatar av)
            {
                animator.avatar = av;
                break;
            }
        }

        SetupAnimatedBoneRigidbodies(knightAnimated);
        SetupSyncObjects(knightPhysics, knightAnimated, knightBoneMapping, knightSyncAnimBones);
        ConnectAnimator(root, animator);

        Debug.Log("[AnimSetup] Character (Knight): Setup complete!");
        return true;
    }

    static bool SetupEP1Character(string rootName, string animatedName)
    {
        GameObject root = GameObject.Find(rootName);
        if (root == null)
        {
            Debug.LogWarning($"[AnimSetup] {rootName} not found in scene, skipping.");
            return false;
        }

        Transform model = root.transform.Find("Model");
        if (model == null)
        {
            Debug.LogError($"[AnimSetup] Model not found under {rootName}!");
            return false;
        }

        Transform originalCharacter = model.Find("Character");
        if (originalCharacter == null)
        {
            Debug.LogError($"[AnimSetup] Character not found under {rootName}/Model!");
            return false;
        }

        Transform existing = model.Find(animatedName);
        if (existing != null)
        {
            Debug.Log($"[AnimSetup] Removing existing {animatedName}...");
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        string knightFbxPath = "Assets/Models/KayKit_Adventurers_1.0_FREE/Characters/fbx/Knight.fbx";
        AnimatorController knightController = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/Animations/Animator Controller Knight.controller");
        GameObject knightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(knightFbxPath);

        if (knightController == null || knightPrefab == null)
        {
            Debug.LogError("[AnimSetup] Knight FBX or Animator Controller not found!");
            return false;
        }

        GameObject animatedObj = (GameObject)PrefabUtility.InstantiatePrefab(knightPrefab, model);
        animatedObj.name = animatedName;
        Undo.RegisterCreatedObjectUndo(animatedObj, $"Create {animatedName}");

        animatedObj.transform.localPosition = originalCharacter.localPosition;
        animatedObj.transform.localRotation = originalCharacter.localRotation;
        animatedObj.transform.localScale = originalCharacter.localScale;

        CleanAnimatedCopy(animatedObj.transform);
        DisableRenderers(animatedObj.transform);

        Animator animator = animatedObj.GetComponent<Animator>();
        if (animator == null)
            animator = animatedObj.AddComponent<Animator>();

        animator.runtimeAnimatorController = knightController;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;

        var allAssets = AssetDatabase.LoadAllAssetsAtPath(knightFbxPath);
        foreach (var asset in allAssets)
        {
            if (asset is Avatar av)
            {
                animator.avatar = av;
                break;
            }
        }

        SetupAnimatedBoneRigidbodies(animatedObj.transform);
        SetupSyncObjects(originalCharacter, animatedObj.transform, ep1BoneMapping, ep1SyncAnimBones);
        ConnectAnimator(root, animator);

        Debug.Log($"[AnimSetup] {rootName}: Setup complete!");
        return true;
    }

    // =============================================
    // 유틸리티
    // =============================================

    static void CleanAnimatedCopy(Transform root)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            foreach (var joint in child.GetComponents<ConfigurableJoint>())
                Object.DestroyImmediate(joint);
            foreach (var collider in child.GetComponents<Collider>())
                Object.DestroyImmediate(collider);
            foreach (var sync in child.GetComponents<SyncPhysicsObject>())
                Object.DestroyImmediate(sync);

            Rigidbody rb = child.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    static void DisableRenderers(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
    }

    static void SetupAnimatedBoneRigidbodies(Transform animRoot)
    {
        foreach (string bonePath in knightAnimBonePaths)
        {
            Transform bone = animRoot.Find(bonePath);
            if (bone != null)
            {
                Rigidbody rb = bone.GetComponent<Rigidbody>();
                if (rb == null)
                    rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    static void SetupSyncObjects(Transform physicsRoot, Transform animRoot,
        Dictionary<string, string> boneMap, HashSet<string> syncBones)
    {
        foreach (var mapping in boneMap)
        {
            Transform physicsBone = physicsRoot.Find(mapping.Key);
            Transform animBone = animRoot.Find(mapping.Value);

            if (physicsBone == null || animBone == null)
            {
                Debug.LogWarning($"[AnimSetup] Bone not found: {mapping.Key} or {mapping.Value}");
                continue;
            }

            if (physicsBone.GetComponent<ConfigurableJoint>() == null)
            {
                Debug.LogWarning($"[AnimSetup] No ConfigurableJoint on {mapping.Key}, skipping");
                continue;
            }

            Rigidbody animRb = animBone.GetComponent<Rigidbody>();
            if (animRb == null)
            {
                animRb = animBone.gameObject.AddComponent<Rigidbody>();
                animRb.isKinematic = true;
                animRb.useGravity = false;
            }

            SyncPhysicsObject existingSync = physicsBone.GetComponent<SyncPhysicsObject>();
            if (existingSync != null)
                Object.DestroyImmediate(existingSync);

            SyncPhysicsObject syncObj = physicsBone.gameObject.AddComponent<SyncPhysicsObject>();

            SerializedObject so = new SerializedObject(syncObj);
            so.FindProperty("animatedRigidbody3D").objectReferenceValue = animRb;
            so.FindProperty("syncAnimation").boolValue = syncBones.Contains(mapping.Key);
            so.ApplyModifiedProperties();

            string status = syncBones.Contains(mapping.Key) ? "SYNC ON" : "sync off";
            Debug.Log($"[AnimSetup] {mapping.Key} -> {mapping.Value} ({status})");
        }
    }

    static void ConnectAnimator(GameObject root, Animator animator)
    {
        NetworkPlayer np = root.GetComponent<NetworkPlayer>();
        if (np != null)
        {
            SerializedObject soNP = new SerializedObject(np);
            SerializedProperty animProp = soNP.FindProperty("animator");
            if (animProp != null)
            {
                animProp.objectReferenceValue = animator;
                soNP.ApplyModifiedProperties();
                Debug.Log($"[AnimSetup] {root.name}: NetworkPlayer.animator connected");
            }
        }
    }
}
