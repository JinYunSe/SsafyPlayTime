using UnityEngine;

namespace SSAFYPlayTime.Character
{
    internal static class GrabRigRuntimeBootstrap
    {
        private const string SensorChildName = "__GrabSensor";
        private const string AnchorChildPrefix = "__GrabAnchor_";
        private const float DefaultSensorRadius = 0.12f;

        public static void EnsureCharacterRig(Transform characterRoot, HandGrabHandler leftHand, HandGrabHandler rightHand)
        {
            if (characterRoot == null)
                return;

            EnsureHandSensor(leftHand != null ? leftHand.gameObject : null);
            EnsureHandSensor(rightHand != null ? rightHand.gameObject : null);

            var animator = characterRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman)
                return;

            EnsureAnchor(
                animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine),
                GrabAnchorPoint.AnchorId.Chest,
                radius: 0.16f,
                priority: 3.0f);
            EnsureAnchor(
                animator.GetBoneTransform(HumanBodyBones.Hips),
                GrabAnchorPoint.AnchorId.Hips,
                radius: 0.15f,
                priority: 2.7f);
            EnsureAnchor(
                animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                GrabAnchorPoint.AnchorId.LeftUpperArm,
                radius: 0.12f,
                priority: 2.1f);
            EnsureAnchor(
                animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                GrabAnchorPoint.AnchorId.RightUpperArm,
                radius: 0.12f,
                priority: 2.1f);
            EnsureAnchor(
                animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                GrabAnchorPoint.AnchorId.LeftForearm,
                radius: 0.11f,
                priority: 1.8f);
            EnsureAnchor(
                animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                GrabAnchorPoint.AnchorId.RightForearm,
                radius: 0.11f,
                priority: 1.8f);
            EnsureAnchor(
                animator.GetBoneTransform(HumanBodyBones.Head),
                GrabAnchorPoint.AnchorId.Head,
                radius: 0.10f,
                priority: 1.2f);
        }

        public static GrabSensor EnsureHandSensor(GameObject handObject)
        {
            if (handObject == null)
                return null;

            var sensorTransform = handObject.transform.Find(SensorChildName);
            if (sensorTransform == null)
            {
                var sensorObject = new GameObject(SensorChildName);
                sensorTransform = sensorObject.transform;
                sensorTransform.SetParent(handObject.transform, false);
                sensorTransform.localPosition = Vector3.zero;
                sensorTransform.localRotation = Quaternion.identity;
                sensorTransform.localScale = Vector3.one;
            }

            sensorTransform.gameObject.layer = handObject.layer;

            var sphere = sensorTransform.GetComponent<SphereCollider>();
            if (sphere == null)
                sphere = sensorTransform.gameObject.AddComponent<SphereCollider>();

            sphere.isTrigger = true;
            sphere.radius = DefaultSensorRadius;
            sphere.center = Vector3.zero;

            var sensor = sensorTransform.GetComponent<GrabSensor>();
            if (sensor == null)
                sensor = sensorTransform.gameObject.AddComponent<GrabSensor>();

            sensor.ConfigureRuntime(sphere.radius);
            return sensor;
        }

        private static void EnsureAnchor(
            Transform bone,
            GrabAnchorPoint.AnchorId anchorId,
            float radius,
            float priority)
        {
            if (bone == null)
                return;

            var anchorName = AnchorChildPrefix + anchorId;
            var anchorTransform = bone.Find(anchorName);
            if (anchorTransform == null)
            {
                var anchorObject = new GameObject(anchorName);
                anchorTransform = anchorObject.transform;
                anchorTransform.SetParent(bone, false);
                anchorTransform.localPosition = Vector3.zero;
                anchorTransform.localRotation = Quaternion.identity;
                anchorTransform.localScale = Vector3.one;
            }

            var hurtboxLayer = LayerMask.NameToLayer("GrabHurtbox");
            anchorTransform.gameObject.layer = hurtboxLayer >= 0 ? hurtboxLayer : bone.gameObject.layer;

            var sphere = anchorTransform.GetComponent<SphereCollider>();
            if (sphere == null)
                sphere = anchorTransform.gameObject.AddComponent<SphereCollider>();

            sphere.isTrigger = true;
            sphere.radius = radius;
            sphere.center = Vector3.zero;

            var anchor = anchorTransform.GetComponent<GrabAnchorPoint>();
            if (anchor == null)
                anchor = anchorTransform.gameObject.AddComponent<GrabAnchorPoint>();

            anchor.ConfigureRuntime(anchorId, radius, priority, Vector3.zero, Quaternion.identity);
        }
    }
}
