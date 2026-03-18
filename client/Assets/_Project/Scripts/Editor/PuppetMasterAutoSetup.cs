using UnityEngine;
using UnityEditor;
using RootMotion.Dynamics;
using RootMotion;
using RootMotion.FinalIK;

public class PuppetMasterAutoSetup : EditorWindow
{
    [MenuItem("Tools/Setup LookAtIK on All Characters")]
    static void SetupLookAtIK()
    {
        PuppetMaster[] allPM = Object.FindObjectsOfType<PuppetMaster>();
        if (allPM.Length == 0) { Debug.LogWarning("[LookAtIK] No PuppetMaster found."); return; }

        int count = 0;
        foreach (var pm in allPM)
        {
            // Find the animated target root (the child with Animator)
            Transform targetRoot = pm.targetRoot;
            if (targetRoot == null) { Debug.LogWarning($"[LookAtIK] {pm.name} has no targetRoot."); continue; }

            Animator animator = targetRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) { Debug.LogWarning($"[LookAtIK] {targetRoot.name} has no humanoid Animator."); continue; }

            // Get bone references
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform spine1 = animator.GetBoneTransform(HumanBodyBones.Chest);
            Transform spine0 = animator.GetBoneTransform(HumanBodyBones.Spine);

            if (head == null) { Debug.LogWarning($"[LookAtIK] {targetRoot.name} has no Head bone."); continue; }

            // Add LookAtIK to the animated target if not present
            LookAtIK lookAt = targetRoot.GetComponent<LookAtIK>();
            if (lookAt == null) lookAt = targetRoot.gameObject.AddComponent<LookAtIK>();

            // Configure solver via SerializedObject for spine chain
            var so = new SerializedObject(lookAt);

            // Set head
            var headProp = so.FindProperty("solver").FindPropertyRelative("head");
            if (headProp != null)
            {
                headProp.FindPropertyRelative("transform").objectReferenceValue = head;
            }

            // Set spine array (spine + chest)
            var spineProp = so.FindProperty("solver").FindPropertyRelative("spine");
            if (spineProp != null)
            {
                spineProp.ClearArray();
                Transform[] spines = spine0 != null && spine1 != null
                    ? new Transform[] { spine0, spine1 }
                    : spine1 != null ? new Transform[] { spine1 } : new Transform[0];

                foreach (var s in spines)
                {
                    spineProp.InsertArrayElementAtIndex(spineProp.arraySize);
                    var elem = spineProp.GetArrayElementAtIndex(spineProp.arraySize - 1);
                    elem.FindPropertyRelative("transform").objectReferenceValue = s;
                }
            }

            // Set solver settings
            var bodyWeight = so.FindProperty("solver").FindPropertyRelative("bodyWeight");
            if (bodyWeight != null) bodyWeight.floatValue = 0.3f;

            var headWeight = so.FindProperty("solver").FindPropertyRelative("headWeight");
            if (headWeight != null) headWeight.floatValue = 0.8f;

            var eyesWeight = so.FindProperty("solver").FindPropertyRelative("eyesWeight");
            if (eyesWeight != null) eyesWeight.floatValue = 0f;

            var clampWeight = so.FindProperty("solver").FindPropertyRelative("clampWeight");
            if (clampWeight != null) clampWeight.floatValue = 0.5f;

            var clampWeightHead = so.FindProperty("solver").FindPropertyRelative("clampWeightHead");
            if (clampWeightHead != null) clampWeightHead.floatValue = 0.5f;

            var clampWeightEyes = so.FindProperty("solver").FindPropertyRelative("clampWeightEyes");
            if (clampWeightEyes != null) clampWeightEyes.floatValue = 0f;

            so.ApplyModifiedProperties();

            // Add LookAtTargetTracker to the animated target
            Transform rootTransform = pm.transform.parent;
            LookAtTargetTracker tracker = rootTransform != null
                ? rootTransform.GetComponentInChildren<LookAtTargetTracker>()
                : null;

            if (tracker == null)
            {
                tracker = targetRoot.gameObject.AddComponent<LookAtTargetTracker>();
            }

            // Set tracker references
            var trackerSO = new SerializedObject(tracker);
            var pmProp = trackerSO.FindProperty("puppetMaster");
            if (pmProp != null) pmProp.objectReferenceValue = pm;
            var ikProp = trackerSO.FindProperty("lookAtIK");
            if (ikProp != null) ikProp.objectReferenceValue = lookAt;
            trackerSO.ApplyModifiedProperties();

            EditorUtility.SetDirty(lookAt);
            EditorUtility.SetDirty(tracker);
            count++;
            Debug.Log($"[LookAtIK] Set up LookAtIK + Tracker on '{targetRoot.name}' (head={head.name})");
        }
        Debug.Log($"[LookAtIK] Done! Applied to {count} characters.");
    }

    [MenuItem("Tools/Fix Ragdoll Layers")]
    static void FixRagdollLayers()
    {
        int ragdollLayer = LayerMask.NameToLayer("Ragdoll");
        if (ragdollLayer < 0)
        {
            Debug.LogError("[FixLayers] 'Ragdoll' layer not found! Add it in Project Settings > Tags and Layers.");
            return;
        }

        // Also need a layer for animated character (ssaty uses layer 8)
        int charLayer = 8; // Match ssaty Root's animated character layer

        string[] rootNames = { "alg_humanoid Root", "fit_humanoid Root", "wise_humanoid Root" };
        string[] charNames = { "alg_humanoid", "fit_humanoid", "wise_humanoid" };

        for (int i = 0; i < rootNames.Length; i++)
        {
            GameObject root = GameObject.Find(rootNames[i]);
            if (root == null) { Debug.LogWarning($"[FixLayers] '{rootNames[i]}' not found."); continue; }

            // Fix PuppetMaster children → Ragdoll layer
            Transform pmTransform = root.transform.Find("PuppetMaster");
            if (pmTransform != null)
            {
                int count = 0;
                SetLayerRecursive(pmTransform, ragdollLayer, ref count);
                Debug.Log($"[FixLayers] Set {count} objects to Ragdoll layer under '{rootNames[i]}/PuppetMaster'");
            }

            // Fix animated character target → layer 8 (matching ssaty)
            Transform charTransform = root.transform.Find(charNames[i]);
            if (charTransform != null)
            {
                int count2 = 0;
                SetLayerRecursive(charTransform, charLayer, ref count2);
                Debug.Log($"[FixLayers] Set {count2} objects to layer {charLayer} under '{rootNames[i]}/{charNames[i]}'");
            }
        }
        Debug.Log("[FixLayers] Done!");
    }

    static void SetLayerRecursive(Transform t, int layer, ref int count)
    {
        t.gameObject.layer = layer;
        count++;
        foreach (Transform child in t)
        {
            SetLayerRecursive(child, layer, ref count);
        }
    }

    [MenuItem("Tools/Apply Muscle Weight Profile")]
    static void ApplyMuscleWeightProfile()
    {
        // Find all PuppetMasters in scene
        PuppetMaster[] allPM = Object.FindObjectsOfType<PuppetMaster>();
        if (allPM.Length == 0)
        {
            Debug.LogWarning("[MuscleProfile] No PuppetMaster found in scene.");
            return;
        }

        // Weight profile: group → (mappingWeight, pinWeight, muscleWeight)
        // Groups: 0=Hips, 1=Spine, 2=Head, 3=Arm, 4=Hand, 5=Leg, 6=Foot
        float[,] profile = new float[,]
        {
            { 1.0f, 1.0f, 1.0f },  // 0: Hips - maximum stability
            { 1.0f, 1.0f, 1.0f },  // 1: Spine - maximum stability
            { 0.8f, 0.8f, 0.7f },  // 2: Head - slight wobble allowed
            { 0.7f, 0.7f, 0.5f },  // 3: Arm (Upper+Lower) - floppy arms
            { 0.5f, 0.5f, 0.4f },  // 4: Hand - most floppy (grab physics)
            { 0.9f, 0.9f, 0.85f }, // 5: Leg (Upper+Lower) - stable but some flex
            { 0.7f, 0.7f, 0.6f },  // 6: Foot - moderate flexibility
        };

        foreach (var pm in allPM)
        {
            var so = new SerializedObject(pm);
            var musclesProp = so.FindProperty("muscles");

            for (int i = 0; i < pm.muscles.Length && i < musclesProp.arraySize; i++)
            {
                var muscle = pm.muscles[i];
                int group = (int)muscle.props.group;
                if (group < 0 || group > 6) continue;

                float mw = profile[group, 0];
                float pw = profile[group, 1];
                float muscW = profile[group, 2];

                var muscleProp = musclesProp.GetArrayElementAtIndex(i);
                var propsProp = muscleProp.FindPropertyRelative("props");
                propsProp.FindPropertyRelative("mappingWeight").floatValue = mw;
                propsProp.FindPropertyRelative("pinWeight").floatValue = pw;
                propsProp.FindPropertyRelative("muscleWeight").floatValue = muscW;

                Debug.Log($"[MuscleProfile] {pm.transform.parent.name}/{muscle.joint.name} (group {group}): map={mw} pin={pw} muscle={muscW}");
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(pm);
            Debug.Log($"[MuscleProfile] Applied profile to '{pm.transform.parent.name}' ({pm.muscles.Length} muscles)");
        }
        Debug.Log($"[MuscleProfile] Done! Applied to {allPM.Length} PuppetMasters.");
    }

    [MenuItem("Tools/Auto Setup PuppetMaster on Humanoids")]
    static void SetupAll()
    {
        string[] names = { "alg_humanoid", "fit_humanoid", "wise_humanoid" };
        int successCount = 0;

        foreach (string charName in names)
        {
            GameObject go = GameObject.Find(charName);
            if (go == null)
            {
                Debug.LogWarning($"[AutoSetup] '{charName}' not found in scene, skipping.");
                continue;
            }

            Animator animator = go.GetComponent<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning($"[AutoSetup] '{charName}' has no humanoid Animator, skipping.");
                continue;
            }

            try
            {
                SetupCharacter(go, animator);
                successCount++;
                Debug.Log($"[AutoSetup] Successfully set up '{charName}'");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AutoSetup] Failed to set up '{charName}': {e.Message}\n{e.StackTrace}");
            }
        }

        Debug.Log($"[AutoSetup] Complete: {successCount}/{names.Length} characters set up.");
    }

    static void SetupCharacter(GameObject charGO, Animator animator)
    {
        // === Step 1: Build BipedRagdollReferences from Humanoid Animator ===
        BipedRagdollReferences refs = new BipedRagdollReferences();
        refs.root = charGO.transform;
        refs.hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        refs.spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        refs.chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        refs.head = animator.GetBoneTransform(HumanBodyBones.Head);
        refs.leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        refs.leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        refs.leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        refs.rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        refs.rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        refs.rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        refs.leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        refs.leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        refs.leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        refs.rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        refs.rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        refs.rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        string msg = "";
        if (!refs.IsValid(ref msg))
        {
            Debug.LogError($"[AutoSetup] Invalid biped references for '{charGO.name}': {msg}");
            return;
        }

        // === Step 2: Create Ragdoll (Rigidbodies, Colliders, Joints on bones) ===
        BipedRagdollCreator.Options options = BipedRagdollCreator.AutodetectOptions(refs);
        options.joints = RagdollCreator.JointType.Configurable;
        options.weight = 10f;  // Match ssaty Root total mass feel
        options.hands = true;
        options.feet = true;
        BipedRagdollCreator.Create(refs, options);
        Debug.Log($"[AutoSetup] Ragdoll created for '{charGO.name}'");

        // === Step 3: Set up PuppetMaster (creates Root, duplicates hierarchy, maps muscles) ===
        // Using layer 0 (Default) for both, matching ssaty Root setup
        PuppetMaster pm = PuppetMaster.SetUp(
            charGO.transform,
            0,  // characterControllerLayer
            0   // ragdollLayer
        );
        Debug.Log($"[AutoSetup] PuppetMaster created for '{charGO.name}' with {pm.muscles.Length} muscles");

        // === Step 4: Configure PuppetMaster to match ssaty Root settings ===
        pm.muscleSpring = 500f;
        pm.muscleDamper = 60f;
        pm.pinWeight = 1f;
        pm.muscleWeight = 1f;
        pm.mappingWeight = 1f;
        pm.pinPow = 4f;
        pm.pinDistanceFalloff = 5f;
        pm.angularPinning = true;
        pm.updateJointAnchors = true;
        pm.supportTranslationAnimation = false;
        pm.angularLimits = false;
        pm.internalCollisions = false;
        pm.fixTargetTransforms = true;
        pm.solverIterationCount = 6;

        // === Step 5: Find or create Behaviours child and add BehaviourPuppet ===
        // PuppetMaster.SetUp should create the Behaviours object, find it
        Transform rootTransform = pm.transform.parent; // The "[name] Root" parent
        BehaviourPuppet bp = null;
        
        // Search for existing BehaviourPuppet in children
        bp = rootTransform.GetComponentInChildren<BehaviourPuppet>();
        
        if (bp == null)
        {
            // Create Behaviours child
            GameObject behavioursGO = new GameObject("Behaviours");
            behavioursGO.transform.SetParent(rootTransform);
            behavioursGO.transform.localPosition = Vector3.zero;
            behavioursGO.transform.localRotation = Quaternion.identity;
            bp = behavioursGO.AddComponent<BehaviourPuppet>();
        }

        // Configure BehaviourPuppet to match ssaty Root (hardened settings)
        bp.puppetMaster = pm;
        bp.collisionThreshold = 25f;
        
        // Use reflection or serialized object to set collisionResistance (it's a special type)
        var so = new SerializedObject(bp);
        
        var crProp = so.FindProperty("collisionResistance");
        if (crProp != null)
        {
            var floatVal = crProp.FindPropertyRelative("floatValue");
            if (floatVal != null) floatVal.floatValue = 15f;
        }
        
        so.FindProperty("knockOutDistance").floatValue = 3f;
        so.FindProperty("regainPinSpeed").floatValue = 8f;
        so.FindProperty("unpinnedMuscleWeightMlp").floatValue = 0.3f;
        so.FindProperty("maxRigidbodyVelocity").floatValue = 10f;
        so.FindProperty("pinWeightThreshold").floatValue = 1f;
        so.FindProperty("unpinnedMuscleKnockout").boolValue = false;
        so.FindProperty("canGetUp").boolValue = true;
        so.FindProperty("getUpDelay").floatValue = 1f;
        so.FindProperty("blendToAnimationTime").floatValue = 0.2f;
        so.FindProperty("maxGetUpVelocity").floatValue = 0.3f;
        so.FindProperty("minGetUpDuration").floatValue = 1f;
        so.FindProperty("getUpCollisionResistanceMlp").floatValue = 2f;
        so.FindProperty("getUpRegainPinSpeedMlp").floatValue = 2f;
        so.FindProperty("getUpKnockOutDistanceMlp").floatValue = 10f;
        
        so.ApplyModifiedProperties();

        // === Step 6: Configure the Root object (Rigidbody, Collider, Joint) ===
        // PuppetMaster.SetUp creates a "[name] Root" parent - configure it like ssaty Root
        if (rootTransform != null)
        {
            Rigidbody rootRb = rootTransform.GetComponent<Rigidbody>();
            if (rootRb == null) rootRb = rootTransform.gameObject.AddComponent<Rigidbody>();
            rootRb.mass = 10f;
            rootRb.drag = 1f;
            rootRb.angularDrag = 5f;
            rootRb.useGravity = true;
            rootRb.isKinematic = false;
            rootRb.centerOfMass = new Vector3(0f, 0.3f, 0f);

            SphereCollider sc = rootTransform.GetComponent<SphereCollider>();
            if (sc == null) sc = rootTransform.gameObject.AddComponent<SphereCollider>();
            sc.center = new Vector3(0f, 0.3f, 0f);
            sc.radius = 0.3f;

            ConfigurableJoint cj = rootTransform.GetComponent<ConfigurableJoint>();
            if (cj == null) cj = rootTransform.gameObject.AddComponent<ConfigurableJoint>();
            cj.xMotion = ConfigurableJointMotion.Free;
            cj.yMotion = ConfigurableJointMotion.Free;
            cj.zMotion = ConfigurableJointMotion.Free;
            cj.angularXMotion = ConfigurableJointMotion.Locked;
            cj.angularYMotion = ConfigurableJointMotion.Free;
            cj.angularZMotion = ConfigurableJointMotion.Locked;
            cj.rotationDriveMode = RotationDriveMode.Slerp;
            JointDrive slerpDrive = new JointDrive();
            slerpDrive.positionSpring = 300f;
            slerpDrive.positionDamper = 40f;
            slerpDrive.maximumForce = float.MaxValue;
            cj.slerpDrive = slerpDrive;

            // === Step 7: Add custom scripts to Root ===
            // NetworkPlayer
            if (rootTransform.GetComponent<NetworkPlayer>() == null)
            {
                NetworkPlayer np = rootTransform.gameObject.AddComponent<NetworkPlayer>();
                // Set references via SerializedObject
                var npSO = new SerializedObject(np);
                var rbProp = npSO.FindProperty("rigidbody3D");
                if (rbProp != null) rbProp.objectReferenceValue = rootRb;
                var jointProp = npSO.FindProperty("mainJoint");
                if (jointProp != null) jointProp.objectReferenceValue = cj;
                // Find the animator on the target character
                Transform targetChar = pm.targetRoot;
                if (targetChar != null)
                {
                    Animator targetAnim = targetChar.GetComponent<Animator>();
                    if (targetAnim == null) targetAnim = targetChar.GetComponentInChildren<Animator>();
                    var animProp = npSO.FindProperty("animator");
                    if (animProp != null) animProp.objectReferenceValue = targetAnim;
                }
                npSO.ApplyModifiedProperties();
            }

            // ProceduralGrabArm
            if (rootTransform.GetComponent<ProceduralGrabArm>() == null)
            {
                ProceduralGrabArm pga = rootTransform.gameObject.AddComponent<ProceduralGrabArm>();
                var pgaSO = new SerializedObject(pga);
                var pmProp = pgaSO.FindProperty("puppetMaster");
                if (pmProp != null) pmProp.objectReferenceValue = pm;
                pgaSO.ApplyModifiedProperties();
            }

            // IgnoreCollision - ignore collisions between root and nearby leg/hip muscles
            if (rootTransform.GetComponent<IgnoreCollision>() == null)
            {
                IgnoreCollision ic = rootTransform.gameObject.AddComponent<IgnoreCollision>();
                var icSO = new SerializedObject(ic);
                var thisColl = icSO.FindProperty("thisCollider");
                if (thisColl != null) thisColl.objectReferenceValue = sc;
                var ignoreAll = icSO.FindProperty("ignoreAllChildColliders");
                if (ignoreAll != null) ignoreAll.boolValue = true;
                
                // Find Hips, LeftUpperLeg, RightUpperLeg colliders in PuppetMaster muscles
                var collidersToIgnore = icSO.FindProperty("colliderToIgnore");
                if (collidersToIgnore != null)
                {
                    collidersToIgnore.ClearArray();
                    foreach (var muscle in pm.muscles)
                    {
                        string mName = muscle.joint.name;
                        if (mName == "Hips" || mName == "LeftUpperLeg" || mName == "RightUpperLeg")
                        {
                            Collider mc = muscle.joint.GetComponent<Collider>();
                            if (mc != null)
                            {
                                collidersToIgnore.InsertArrayElementAtIndex(collidersToIgnore.arraySize);
                                collidersToIgnore.GetArrayElementAtIndex(collidersToIgnore.arraySize - 1).objectReferenceValue = mc;
                            }
                        }
                    }
                }
                icSO.ApplyModifiedProperties();
            }

            // === Step 8: Add HandGrabHandler to LeftHand and RightHand muscles ===
            foreach (var muscle in pm.muscles)
            {
                string mName = muscle.joint.name;
                if (mName == "LeftHand" || mName == "RightHand")
                {
                    if (muscle.joint.GetComponent<HandGrabHandler>() == null)
                    {
                        muscle.joint.gameObject.AddComponent<HandGrabHandler>();
                        Debug.Log($"[AutoSetup] Added HandGrabHandler to {mName}");
                    }
                }
            }

            Undo.RegisterCreatedObjectUndo(rootTransform.gameObject, "Auto Setup PuppetMaster");
        }

        EditorUtility.SetDirty(pm);
        Debug.Log($"[AutoSetup] '{charGO.name}' fully configured as PuppetMaster character.");
    }

    // ===========================
    // Fix missing Animator Controllers on all PuppetMaster characters
    // ===========================
    [MenuItem("Tools/Fix Animator Controllers")]
    static void FixAnimatorControllers()
    {
        // Find the controller used by ssaty (the working reference)
        RuntimeAnimatorController refController = null;

        PuppetMaster[] allPM = Object.FindObjectsOfType<PuppetMaster>();
        foreach (var pm in allPM)
        {
            if (pm.targetRoot == null) continue;
            Animator anim = pm.targetRoot.GetComponent<Animator>();
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                refController = anim.runtimeAnimatorController;
                Debug.Log($"[FixAnim] Found reference controller '{refController.name}' from '{pm.targetRoot.name}'");
                break;
            }
        }

        if (refController == null)
        {
            // Fallback: load by path
            // Try multiple known locations
            string[] paths = {
                "Assets/Animations/PartyMonsterSimple.controller",
                "Assets/_Project/Animations/PartyMonsterGameplay.controller",
                "Assets/_Project/Animations/ssatyController.controller",
                "Assets/Animations/PartyMonsterPlayer.controller",
                "Assets/PartyMonsterRumblePBR/Animator/Default.controller"
            };
            foreach (var p in paths)
            {
                refController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(p);
                if (refController != null) break;
            }
            if (refController == null)
            {
                Debug.LogError("[FixAnim] No AnimatorController found! Check Assets/Animations/");
                return;
            }
            Debug.Log($"[FixAnim] Loaded controller from assets: '{refController.name}'");
        }

        int fixCount = 0;
        foreach (var pm in allPM)
        {
            if (pm.targetRoot == null) continue;
            Animator anim = pm.targetRoot.GetComponent<Animator>();
            if (anim == null) continue;

            if (anim.runtimeAnimatorController == null)
            {
                Undo.RecordObject(anim, "Fix Animator Controller");
                anim.runtimeAnimatorController = refController;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                EditorUtility.SetDirty(anim);
                fixCount++;
                Debug.Log($"[FixAnim] Assigned '{refController.name}' to '{pm.targetRoot.name}'");
            }
        }

        if (fixCount > 0)
            Debug.Log($"[FixAnim] Done! Fixed {fixCount} Animators.");
        else
            Debug.Log("[FixAnim] All Animators already have controllers assigned.");
    }

    // ===========================
    // Step 3-3: Setup LimbIK for Arms (ProceduralGrabArm)
    // ===========================
    [MenuItem("Tools/Setup Arm LimbIK on All Characters")]
    static void SetupArmLimbIK()
    {
        PuppetMaster[] allPM = Object.FindObjectsOfType<PuppetMaster>();
        if (allPM.Length == 0) { Debug.LogWarning("[ArmIK] No PuppetMaster found."); return; }

        int count = 0;
        foreach (var pm in allPM)
        {
            Transform targetRoot = pm.targetRoot;
            if (targetRoot == null) continue;

            Animator animator = targetRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) continue;

            // Get arm bone references
            Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            if (leftUpperArm == null || leftHand == null || rightUpperArm == null || rightHand == null)
            {
                Debug.LogWarning($"[ArmIK] {targetRoot.name} missing arm bones, skipping.");
                continue;
            }

            // Add or find LimbIK for left arm
            LimbIK leftArmIK = FindOrAddLimbIK(targetRoot, "LeftArm", leftUpperArm, leftLowerArm, leftHand);
            LimbIK rightArmIK = FindOrAddLimbIK(targetRoot, "RightArm", rightUpperArm, rightLowerArm, rightHand);

            // Wire into ProceduralGrabArm
            Transform rootTransform = pm.transform.parent;
            if (rootTransform == null) continue;

            ProceduralGrabArm grabArm = rootTransform.GetComponentInChildren<ProceduralGrabArm>();
            if (grabArm == null)
            {
                grabArm = rootTransform.gameObject.AddComponent<ProceduralGrabArm>();
            }

            var so = new SerializedObject(grabArm);
            var pmProp = so.FindProperty("puppetMaster");
            if (pmProp != null) pmProp.objectReferenceValue = pm;
            var leftProp = so.FindProperty("leftArmIK");
            if (leftProp != null) leftProp.objectReferenceValue = leftArmIK;
            var rightProp = so.FindProperty("rightArmIK");
            if (rightProp != null) rightProp.objectReferenceValue = rightArmIK;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(grabArm);
            count++;
            Debug.Log($"[ArmIK] Set up LimbIK arms + ProceduralGrabArm on '{rootTransform.name}'");
        }
        Debug.Log($"[ArmIK] Done! Applied to {count} characters.");
    }

    static LimbIK FindOrAddLimbIK(Transform targetRoot, string label, Transform bone1, Transform bone2, Transform bone3)
    {
        // Check if a LimbIK already exists for this chain
        LimbIK[] existing = targetRoot.GetComponents<LimbIK>();
        foreach (var ik in existing)
        {
            if (ik.solver.bone1.transform == bone1) return ik;
        }

        // Add new LimbIK
        LimbIK limbIK = targetRoot.gameObject.AddComponent<LimbIK>();
        limbIK.fixTransforms = true;

        // Configure solver bones via SerializedObject
        var so = new SerializedObject(limbIK);
        var solverProp = so.FindProperty("solver");

        var b1 = solverProp.FindPropertyRelative("bone1");
        if (b1 != null) b1.FindPropertyRelative("transform").objectReferenceValue = bone1;

        var b2 = solverProp.FindPropertyRelative("bone2");
        if (b2 != null) b2.FindPropertyRelative("transform").objectReferenceValue = bone2;

        var b3 = solverProp.FindPropertyRelative("bone3");
        if (b3 != null) b3.FindPropertyRelative("transform").objectReferenceValue = bone3;

        // IK position weight starts at 0 (ProceduralGrabArm controls it)
        var ikPosWeight = solverProp.FindPropertyRelative("IKPositionWeight");
        if (ikPosWeight != null) ikPosWeight.floatValue = 0f;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(limbIK);

        Debug.Log($"[ArmIK] Added LimbIK '{label}' on {targetRoot.name}: {bone1.name} → {bone2.name} → {bone3.name}");
        return limbIK;
    }

    // ===========================
    // Step 3-2: Setup GrounderIK (Foot Placement)
    // ===========================
    [MenuItem("Tools/Setup GrounderIK on All Characters")]
    static void SetupGrounderIK()
    {
        PuppetMaster[] allPM = Object.FindObjectsOfType<PuppetMaster>();
        if (allPM.Length == 0) { Debug.LogWarning("[GrounderIK] No PuppetMaster found."); return; }

        int count = 0;
        foreach (var pm in allPM)
        {
            Transform targetRoot = pm.targetRoot;
            if (targetRoot == null) continue;

            Animator animator = targetRoot.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) continue;

            // Get leg bone references
            Transform leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Transform leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            if (leftUpperLeg == null || leftFoot == null || rightUpperLeg == null || rightFoot == null)
            {
                Debug.LogWarning($"[GrounderIK] {targetRoot.name} missing leg bones, skipping.");
                continue;
            }

            // Add LimbIK for left and right legs
            LimbIK leftLegIK = FindOrAddLimbIK(targetRoot, "LeftLeg", leftUpperLeg, leftLowerLeg, leftFoot);
            LimbIK rightLegIK = FindOrAddLimbIK(targetRoot, "RightLeg", rightUpperLeg, rightLowerLeg, rightFoot);

            // Add or find GrounderIK
            GrounderIK grounder = targetRoot.GetComponent<GrounderIK>();
            if (grounder == null)
                grounder = targetRoot.gameObject.AddComponent<GrounderIK>();

            // Configure GrounderIK via SerializedObject
            var so = new SerializedObject(grounder);

            // Set characterRoot (the animation target root)
            var charRoot = so.FindProperty("characterRoot");
            if (charRoot != null) charRoot.objectReferenceValue = targetRoot;

            // Set legs array
            var legsProp = so.FindProperty("legs");
            if (legsProp != null)
            {
                legsProp.ClearArray();
                legsProp.InsertArrayElementAtIndex(0);
                legsProp.GetArrayElementAtIndex(0).objectReferenceValue = leftLegIK;
                legsProp.InsertArrayElementAtIndex(1);
                legsProp.GetArrayElementAtIndex(1).objectReferenceValue = rightLegIK;
            }

            // Solver settings
            var solverProp = so.FindProperty("solver");
            if (solverProp != null)
            {
                var maxStep = solverProp.FindPropertyRelative("maxStep");
                if (maxStep != null) maxStep.floatValue = 0.5f;

                var heightOffset = solverProp.FindPropertyRelative("heightOffset");
                if (heightOffset != null) heightOffset.floatValue = 0f;

                var footSpeed = solverProp.FindPropertyRelative("lerpSpeed");
                if (footSpeed != null) footSpeed.floatValue = 10f;

                // Ground layers: Default (0) only
                var layers = solverProp.FindPropertyRelative("layers");
                if (layers != null) layers.intValue = 1; // Layer 0 = Default
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(grounder);

            // Add GrounderIKPuppetHandler
            Transform rootTransform = pm.transform.parent;
            if (rootTransform == null) rootTransform = targetRoot;

            GrounderIKPuppetHandler handler = targetRoot.GetComponent<GrounderIKPuppetHandler>();
            if (handler == null)
                handler = targetRoot.gameObject.AddComponent<GrounderIKPuppetHandler>();

            var handlerSO = new SerializedObject(handler);
            var hPM = handlerSO.FindProperty("puppetMaster");
            if (hPM != null) hPM.objectReferenceValue = pm;
            var hGrounder = handlerSO.FindProperty("grounderIK");
            if (hGrounder != null) hGrounder.objectReferenceValue = grounder;
            handlerSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(handler);

            count++;
            Debug.Log($"[GrounderIK] Set up GrounderIK + handler on '{targetRoot.name}'");
        }
        Debug.Log($"[GrounderIK] Done! Applied to {count} characters.");
    }

    // ===========================
    // Step 4-3: Per-Rigidbody Solver Iteration Tuning
    // ===========================
    [MenuItem("Tools/Apply Per-Body Solver Iterations")]
    static void ApplyPerBodySolverIterations()
    {
        PuppetMaster[] allPM = Object.FindObjectsOfType<PuppetMaster>();
        if (allPM.Length == 0) { Debug.LogWarning("[SolverIter] No PuppetMaster found."); return; }

        // Group → (solverIterations, solverVelocityIterations)
        // Core body gets more iterations for stability, extremities get less for performance
        int[,] profile = new int[,]
        {
            { 12, 4 },  // 0: Hips - highest stability
            { 12, 4 },  // 1: Spine
            { 8, 2 },   // 2: Head
            { 6, 1 },   // 3: Arm
            { 4, 1 },   // 4: Hand - lowest (cheapest)
            { 10, 3 },  // 5: Leg
            { 6, 1 },   // 6: Foot
        };

        int count = 0;
        foreach (var pm in allPM)
        {
            foreach (var muscle in pm.muscles)
            {
                if (muscle.joint == null) continue;
                Rigidbody rb = muscle.joint.GetComponent<Rigidbody>();
                if (rb == null) continue;

                int group = (int)muscle.props.group;
                if (group < 0 || group > 6) continue;

                rb.solverIterations = profile[group, 0];
                rb.solverVelocityIterations = profile[group, 1];
                EditorUtility.SetDirty(rb);
            }
            count++;
            Debug.Log($"[SolverIter] Applied per-body solver iterations to '{pm.transform.parent?.name}'");
        }
        Debug.Log($"[SolverIter] Done! Applied to {count} PuppetMasters.");
    }

    // ===========================
    // Step 4-2: Add PuppetMasterLOD + PerformanceManager
    // ===========================
    [MenuItem("Tools/Setup Performance LOD on All Characters")]
    static void SetupPerformanceLOD()
    {
        PuppetMaster[] allPM = Object.FindObjectsOfType<PuppetMaster>();
        if (allPM.Length == 0) { Debug.LogWarning("[LOD] No PuppetMaster found."); return; }

        int count = 0;
        foreach (var pm in allPM)
        {
            Transform rootTransform = pm.transform.parent;
            if (rootTransform == null) continue;

            if (rootTransform.GetComponent<PuppetMasterLOD>() == null)
                rootTransform.gameObject.AddComponent<PuppetMasterLOD>();

            EditorUtility.SetDirty(rootTransform.gameObject);
            count++;
        }

        // Ensure global PuppetPerformanceManager exists
        if (Object.FindObjectOfType<PuppetPerformanceManager>() == null)
        {
            var go = new GameObject("PuppetPerformanceManager");
            go.AddComponent<PuppetPerformanceManager>();
            Undo.RegisterCreatedObjectUndo(go, "Create PuppetPerformanceManager");
            Debug.Log("[LOD] Created PuppetPerformanceManager in scene.");
        }

        Debug.Log($"[LOD] Done! Added PuppetMasterLOD to {count} characters.");
    }

    // ===========================
    // Step 5: Lifecycle + State Sync on All Characters
    // ===========================
    [MenuItem("Tools/Setup Lifecycle and StateSync on All Characters")]
    static void SetupLifecycleAndSync()
    {
        PuppetMaster[] allPM = Object.FindObjectsOfType<PuppetMaster>();
        if (allPM.Length == 0) { Debug.LogWarning("[Lifecycle] No PuppetMaster found."); return; }

        int count = 0;
        foreach (var pm in allPM)
        {
            Transform rootTransform = pm.transform.parent;
            if (rootTransform == null) continue;

            BehaviourPuppet bp = rootTransform.GetComponentInChildren<BehaviourPuppet>();

            // PuppetLifecycleManager
            PuppetLifecycleManager lifecycle = rootTransform.GetComponent<PuppetLifecycleManager>();
            if (lifecycle == null)
                lifecycle = rootTransform.gameObject.AddComponent<PuppetLifecycleManager>();

            var lcSO = new SerializedObject(lifecycle);
            var lcPM = lcSO.FindProperty("puppetMaster");
            if (lcPM != null) lcPM.objectReferenceValue = pm;
            var lcBP = lcSO.FindProperty("behaviourPuppet");
            if (lcBP != null) lcBP.objectReferenceValue = bp;
            lcSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(lifecycle);

            // PuppetStateSync
            PuppetStateSync sync = rootTransform.GetComponent<PuppetStateSync>();
            if (sync == null)
                sync = rootTransform.gameObject.AddComponent<PuppetStateSync>();

            var syncSO = new SerializedObject(sync);
            var sPM = syncSO.FindProperty("puppetMaster");
            if (sPM != null) sPM.objectReferenceValue = pm;
            var sBP = syncSO.FindProperty("behaviourPuppet");
            if (sBP != null) sBP.objectReferenceValue = bp;
            var sLC = syncSO.FindProperty("lifecycleManager");
            if (sLC != null) sLC.objectReferenceValue = lifecycle;
            syncSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(sync);

            count++;
            Debug.Log($"[Lifecycle] Set up LifecycleManager + StateSync on '{rootTransform.name}'");
        }
        Debug.Log($"[Lifecycle] Done! Applied to {count} characters.");
    }
}
