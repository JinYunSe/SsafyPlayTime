using UnityEditor;
using UnityEngine;
using UnityEditor.Animations;
using SSAFYPlayTime.Character;
using System.Linq;

public static class NetworkPlayerSetupTool
{
    [MenuItem("Tools/Setup NetworkPlayer Config and Animator")]
    public static void Setup()
    {
        // 1. Find the PlayerMotorConfig ScriptableObject
        var configGuids = AssetDatabase.FindAssets("t:PlayerMotorConfig");
        PlayerMotorConfig config = null;
        if (configGuids.Length > 0)
        {
            var configPath = AssetDatabase.GUIDToAssetPath(configGuids[0]);
            config = AssetDatabase.LoadAssetAtPath<PlayerMotorConfig>(configPath);
        }

        if (config == null)
        {
            Debug.LogError("PlayerMotorConfig not found! Make sure you created DefaultPlayerMotorConfig.asset");
            return;
        }

        int updatedCount = 0;

        // 2. Find all prefabs containing NetworkPlayer
        var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        foreach (var guid in prefabGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                var networkPlayers = prefab.GetComponentsInChildren<NetworkPlayer>(true);
                foreach (var np in networkPlayers)
                {
                    bool isModified = false;

                    // Set Config
                    SerializedObject so = new SerializedObject(np);
                    SerializedProperty configProp = so.FindProperty("config");
                    if (configProp != null && configProp.objectReferenceValue != config)
                    {
                        configProp.objectReferenceValue = config;
                        so.ApplyModifiedProperties();
                        isModified = true;
                    }

                    // Assign Animator Parameter
                    var animator = np.GetComponentInChildren<Animator>(true);
                    if (animator != null && animator.runtimeAnimatorController != null)
                    {
                        var controller = animator.runtimeAnimatorController as AnimatorController;
                        if (controller == null && animator.runtimeAnimatorController is AnimatorOverrideController overrideController)
                        {
                            controller = overrideController.runtimeAnimatorController as AnimatorController;
                        }

                        if (controller != null)
                        {
                            bool hasParam = controller.parameters.Any(p => p.name == "MotorState");
                            if (!hasParam)
                            {
                                controller.AddParameter("MotorState", AnimatorControllerParameterType.Int);
                                EditorUtility.SetDirty(controller);
                                Debug.Log($"Added MotorState parameter to AnimatorController: {controller.name}");
                            }
                        }
                    }

                    if (isModified || animator != null)
                    {
                        EditorUtility.SetDirty(np);
                        updatedCount++;
                    }
                }
            }
        }

        // 3. Find NetworkPlayers in the active scene
        var scenePlayers = Object.FindObjectsOfType<NetworkPlayer>(true);
        foreach (var np in scenePlayers)
        {
            SerializedObject so = new SerializedObject(np);
            SerializedProperty configProp = so.FindProperty("config");
            if (configProp != null && configProp.objectReferenceValue != config)
            {
                configProp.objectReferenceValue = config;
                so.ApplyModifiedProperties();
            }

            var animator = np.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                var controller = animator.runtimeAnimatorController as AnimatorController;
                if (controller == null && animator.runtimeAnimatorController is AnimatorOverrideController overrideController)
                {
                    controller = overrideController.runtimeAnimatorController as AnimatorController;
                }

                if (controller != null)
                {
                    bool hasParam = controller.parameters.Any(p => p.name == "MotorState");
                    if (!hasParam)
                    {
                        controller.AddParameter("MotorState", AnimatorControllerParameterType.Int);
                        EditorUtility.SetDirty(controller);
                        Debug.Log($"Added MotorState parameter to AnimatorController: {controller.name}");
                    }
                }
            }

            EditorUtility.SetDirty(np);
            updatedCount++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[NetworkPlayerSetupTool] Successfully updated config properties & Animator parameters! Objects processed: {updatedCount}");
    }
}
