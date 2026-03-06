
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class FixSsatyHumanoidAvatar : MonoBehaviour
{
    [MenuItem("Tools/Fix Ssaty Humanoid Avatar")]
    static void FixAvatar()
    {
        string fbxPath = "Assets/ssaty_export/ssaty.fbx";

        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("ssaty.fbx not found at " + fbxPath);
            return;
        }

        // Step 1: Reset to Generic to clear any broken humanoid skeleton data
        Debug.Log("[FixAvatar] Step 1: Resetting to Generic...");
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.SaveAndReimport();

        // Step 2: Set to Humanoid and let Unity auto-detect skeleton from FBX
        Debug.Log("[FixAvatar] Step 2: Setting to Humanoid (auto-detect skeleton)...");
        importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.SaveAndReimport();

        // Step 3: Read the auto-detected humanDescription (has proper skeleton with parents)
        Debug.Log("[FixAvatar] Step 3: Reading auto-detected skeleton and applying bone mapping...");
        importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        HumanDescription hd = importer.humanDescription;

        Debug.Log($"[FixAvatar] Auto-detected: {hd.skeleton.Length} skeleton bones, {hd.human.Length} human bones");

        // Log auto-detected bone mappings
        if (hd.human.Length > 0)
        {
            Debug.Log("[FixAvatar] Auto-detected human bone mappings:");
            foreach (var h in hd.human)
                Debug.Log($"  {h.boneName} -> {h.humanName}");
        }

        // Step 4: Override human bone mapping with our explicit mapping
        // (keeps the auto-detected skeleton intact)
        var boneMapping = new List<HumanBone>
        {
            CreateHumanBone("Hips", "Hips"),
            CreateHumanBone("Spine", "Spine"),
            CreateHumanBone("Spine1", "Chest"),
            CreateHumanBone("Spine2", "UpperChest"),
            CreateHumanBone("Neck", "Neck"),
            CreateHumanBone("Head", "Head"),
            CreateHumanBone("LeftShoulder", "LeftShoulder"),
            CreateHumanBone("LeftUpperArm", "LeftUpperArm"),
            CreateHumanBone("LeftLowerArm", "LeftLowerArm"),
            CreateHumanBone("LeftHand", "LeftHand"),
            CreateHumanBone("RightShoulder", "RightShoulder"),
            CreateHumanBone("RightUpperArm", "RightUpperArm"),
            CreateHumanBone("RightLowerArm", "RightLowerArm"),
            CreateHumanBone("RightHand", "RightHand"),
            CreateHumanBone("LeftUpperLeg", "LeftUpperLeg"),
            CreateHumanBone("LeftLowerLeg", "LeftLowerLeg"),
            CreateHumanBone("LeftFoot", "LeftFoot"),
            CreateHumanBone("RightUpperLeg", "RightUpperLeg"),
            CreateHumanBone("RightLowerLeg", "RightLowerLeg"),
            CreateHumanBone("RightFoot", "RightFoot"),
        };

        hd.human = boneMapping.ToArray();
        hd.upperArmTwist = 0.5f;
        hd.lowerArmTwist = 0.5f;
        hd.upperLegTwist = 0.5f;
        hd.lowerLegTwist = 0.5f;
        hd.armStretch = 0.05f;
        hd.legStretch = 0.05f;
        hd.feetSpacing = 0f;
        hd.hasTranslationDoF = false;

        importer.humanDescription = hd;
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        // Step 5: Verify the avatar
        Debug.Log("[FixAvatar] === Verification ===");

        importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        HumanDescription finalHd = importer.humanDescription;
        Debug.Log($"[FixAvatar] Final: {finalHd.skeleton.Length} skeleton bones, {finalHd.human.Length} human bones");

        var allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var asset in allAssets)
        {
            if (asset is Avatar avatar)
            {
                Debug.Log($"[FixAvatar] Avatar '{avatar.name}' - isValid: {avatar.isValid}, isHuman: {avatar.isHuman}");

                if (!avatar.isValid || !avatar.isHuman)
                {
                    Debug.LogError("[FixAvatar] Avatar is NOT valid or NOT humanoid! Animation retargeting will NOT work.");
                }
                else
                {
                    Debug.Log("[FixAvatar] Avatar is valid and humanoid. Animation retargeting should work correctly.");
                }
            }
        }

        Debug.Log("[FixAvatar] Done! Please test by playing the scene.");
    }

    static HumanBone CreateHumanBone(string boneName, string humanName)
    {
        return new HumanBone
        {
            boneName = boneName,
            humanName = humanName,
            limit = new HumanLimit { useDefaultValues = true }
        };
    }
}
#endif
