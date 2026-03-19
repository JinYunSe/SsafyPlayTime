using System.Collections.Generic;
using RootMotion.Dynamics;
using UnityEngine;

namespace SSAFYPlayTime.Character
{
    [DisallowMultipleComponent]
    public sealed class GrabAntiStretchController : MonoBehaviour
    {
        private const int MaxParentSearchDepth = 3;

        [Header("References")]
        [SerializeField] private NetworkPlayer networkPlayer;
        [SerializeField] private Rigidbody fallbackRootBody;
        [SerializeField] private ConfigurableJoint rootMainJoint;
        [SerializeField] private PuppetMaster puppetMaster;

        [Header("Anti-Stretch")]
        [SerializeField] private float handSlack = 0.04f;
        [SerializeField] private float footSlack = 0.05f;
        [SerializeField] private float limitSpring = 80f;
        [SerializeField] private float limitDamper = 8f;
        [SerializeField] private float projectionDistance = 0.08f;
        [SerializeField] private float projectionAngle = 6f;
        [SerializeField] private bool applyDuringRecovering = true;

        [Header("Core Grab Drive")]
        [SerializeField] private float carryingCoreSpringMultiplier = 1.15f;
        [SerializeField] private float carryingCoreDamperMultiplier = 1.1f;
        [SerializeField] private float dualCarryCoreSpringMultiplier = 1.4f;
        [SerializeField] private float dualCarryCoreDamperMultiplier = 1.18f;
        [SerializeField] private float grabbedCoreSpringMultiplier = 1.9f;
        [SerializeField] private float grabbedCoreDamperMultiplier = 1.35f;
        [SerializeField] private float carriedVictimCoreSpringMultiplier = 0.65f;
        [SerializeField] private float carriedVictimCoreDamperMultiplier = 0.70f;
        [SerializeField] private bool verboseWarnings;
        [SerializeField] private bool debugLog;

        private RuntimeLink[] _links;
        private RuntimeDriveLink[] _coreDriveLinks;
        private bool _resolved;
        private bool _coreDriveResolved;
        private bool _active;
        private bool _warnedMissingTargets;
        private bool _warnedMissingCoreJoints;
        private CoreDriveMode _currentCoreDriveMode;

        private enum CoreDriveMode : byte
        {
            Off = 0,
            Carrying = 1,
            DualCarryCarrier = 2,
            GrabbedVictim = 3,
            CarriedVictim = 4
        }

        private sealed class RuntimeLink
        {
            public string label;
            public string[] limbNames;
            public string[] anchorNames;
            public float slack;
            public Transform limbTransform;
            public Rigidbody limbBody;
            public Transform anchorTransform;
            public Rigidbody anchorBody;
            public ConfigurableJoint joint;
        }

        private sealed class RuntimeDriveLink
        {
            public string label;
            public string[] jointNames;
            public ConfigurableJoint joint;
            public JointDrive originalSlerpDrive;
            public JointDrive originalAngularXDrive;
            public JointDrive originalAngularYZDrive;
            public bool cachedOriginal;
        }

        private void Awake()
        {
            debugLog = true; // 디버그 진단용 강제 활성화
            ResolveReferences();
            NormalizeTuning();
            BuildLinkDefinitions();
            BuildCoreDriveDefinitions();
            TryResolveLinkBodies();
            TryResolveCoreDriveJoints();
            debugLog = false;
        }

        private void FixedUpdate()
        {
            if (!CanRunLocalPhysics())
                return;

            RefreshCoreDrive(force: true);
        }

        private void NormalizeTuning()
        {
            handSlack = Mathf.Min(handSlack, 0.025f);
            footSlack = Mathf.Min(footSlack, 0.035f);
            limitSpring = Mathf.Max(limitSpring, 180f);
            limitDamper = Mathf.Max(limitDamper, 18f);
            projectionDistance = Mathf.Min(projectionDistance, 0.045f);
            projectionAngle = Mathf.Min(projectionAngle, 4f);

            grabbedCoreSpringMultiplier = Mathf.Max(grabbedCoreSpringMultiplier, 2.2f);
            grabbedCoreDamperMultiplier = Mathf.Max(grabbedCoreDamperMultiplier, 1.45f);
            carriedVictimCoreSpringMultiplier = Mathf.Max(carriedVictimCoreSpringMultiplier, 0.85f);
            carriedVictimCoreDamperMultiplier = Mathf.Max(carriedVictimCoreDamperMultiplier, 0.9f);
        }

        private void LateUpdate()
        {
            if (!CanRunLocalPhysics())
            {
                if (_active)
                    DisableAntiStretch();

                RestoreCoreDrive();
                return;
            }

            if (!_resolved)
                TryResolveLinkBodies();

            if (!_coreDriveResolved)
                TryResolveCoreDriveJoints();

            var shouldEnable = ShouldEnableAntiStretch();
            if (shouldEnable && !_active)
            {
                if (debugLog)
                    Debug.Log($"[AntiStretch] {name}: ENABLING anti-stretch, " +
                        $"phase={networkPlayer?.GetPhysicalPhase()}, resolved={_resolved}", this);
                EnableAntiStretch();
            }
            else if (!shouldEnable && _active)
            {
                if (debugLog)
                    Debug.Log($"[AntiStretch] {name}: DISABLING anti-stretch, " +
                        $"phase={networkPlayer?.GetPhysicalPhase()}", this);
                DisableAntiStretch();
            }

            var desiredCoreDrive = ResolveCoreDriveMode();
            if (debugLog && desiredCoreDrive != _currentCoreDriveMode)
            {
                Debug.Log($"[AntiStretch] {name}: coreDrive {_currentCoreDriveMode} → {desiredCoreDrive}, " +
                    $"phase={networkPlayer?.GetPhysicalPhase()}, active={_active}", this);
            }

            RefreshCoreDrive(force: false);
        }

        private void OnDisable()
        {
            DisableAntiStretch();
            RestoreCoreDrive();
        }

        private void OnDestroy()
        {
            DisableAntiStretch();
            RestoreCoreDrive();
        }

        private void ResolveReferences()
        {
            if (networkPlayer == null)
                networkPlayer = GetComponent<NetworkPlayer>();

            if (fallbackRootBody == null)
                fallbackRootBody = GetComponent<Rigidbody>();

            if (rootMainJoint == null)
                rootMainJoint = GetComponent<ConfigurableJoint>();

            if (puppetMaster == null)
                puppetMaster = GetComponentInChildren<PuppetMaster>(true);
        }

        private void BuildLinkDefinitions()
        {
            if (_links != null && _links.Length == 4)
                return;

            _links = new[]
            {
                CreateLink("LeftHandToChest", new[] { "LeftHand" }, new[] { "Chest", "Spine2", "Spine1", "Spine" }, handSlack),
                CreateLink("RightHandToChest", new[] { "RightHand" }, new[] { "Chest", "Spine2", "Spine1", "Spine" }, handSlack),
                CreateLink("LeftFootToHips", new[] { "LeftFoot", "LeftLowerLeg" }, new[] { "Hips", "Pelvis" }, footSlack),
                CreateLink("RightFootToHips", new[] { "RightFoot", "RightLowerLeg" }, new[] { "Hips", "Pelvis" }, footSlack)
            };
        }

        private void BuildCoreDriveDefinitions()
        {
            if (_coreDriveLinks != null && _coreDriveLinks.Length == 4)
                return;

            _coreDriveLinks = new[]
            {
                CreateCoreDriveLink("MainRoot"),
                CreateCoreDriveLink("Hips", "Hips", "Pelvis"),
                CreateCoreDriveLink("Spine", "Spine", "Spine1"),
                CreateCoreDriveLink("Chest", "Spine2", "Chest")
            };
        }

        private static RuntimeLink CreateLink(string label, string[] limbNames, string[] anchorNames, float slack)
        {
            return new RuntimeLink
            {
                label = label,
                limbNames = limbNames,
                anchorNames = anchorNames,
                slack = slack
            };
        }

        private static RuntimeDriveLink CreateCoreDriveLink(string label, params string[] jointNames)
        {
            return new RuntimeDriveLink
            {
                label = label,
                jointNames = jointNames
            };
        }

        private bool CanRunLocalPhysics()
        {
            if (networkPlayer == null)
                return true;

            if (networkPlayer.Runner == null || networkPlayer.Object == null || !networkPlayer.Object.IsValid)
                return true;

            return networkPlayer.HasStateAuthority;
        }

        private bool ShouldEnableAntiStretch()
        {
            if (networkPlayer == null)
                return false;

            // AntiStretchConstraint가 양보했으므로, 모든 상태에서 패시브 안티스트레치 제공
            var phase = networkPlayer.GetPhysicalPhase();
            if (phase == NetworkPlayer.PhysicalPhase.BeingCarriedStunned)
                return false;

            if (phase == NetworkPlayer.PhysicalPhase.Recovering && !applyDuringRecovering)
                return false;

            return true;
        }

        private CoreDriveMode ResolveCoreDriveMode()
        {
            if (networkPlayer == null)
                return CoreDriveMode.Off;

            return networkPlayer.GetPhysicalPhase() switch
            {
                NetworkPlayer.PhysicalPhase.Holding => CoreDriveMode.Carrying,
                NetworkPlayer.PhysicalPhase.CarryingStunned => networkPlayer.IsDualGrabbingStunnedPlayer
                    ? CoreDriveMode.DualCarryCarrier
                    : CoreDriveMode.Carrying,
                NetworkPlayer.PhysicalPhase.BeingGrabbed => CoreDriveMode.GrabbedVictim,
                NetworkPlayer.PhysicalPhase.Dragged => CoreDriveMode.GrabbedVictim,
                NetworkPlayer.PhysicalPhase.BeingCarriedStunned => CoreDriveMode.CarriedVictim,
                _ => CoreDriveMode.Off
            };
        }

        private void RefreshCoreDrive(bool force)
        {
            var desiredMode = ResolveCoreDriveMode();
            if (desiredMode == CoreDriveMode.Off)
            {
                RestoreCoreDrive();
                return;
            }

            if (!_coreDriveResolved)
                TryResolveCoreDriveJoints();

            if (!_coreDriveResolved)
                return;

            if (!force && desiredMode == _currentCoreDriveMode)
                return;

            ApplyCoreDrive(desiredMode);
        }

        private void TryResolveLinkBodies()
        {
            if (_links == null || _links.Length == 0)
                BuildLinkDefinitions();

            ResolveReferences();

            var resolvedCount = 0;
            for (var i = 0; i < _links.Length; i++)
            {
                var link = _links[i];
                if (link == null)
                    continue;

                link.limbTransform = FindBestNamedTransform(link.limbNames);
                link.limbBody = FindNearestRigidbody(link.limbTransform, MaxParentSearchDepth);

                link.anchorTransform = FindBestAnchorTransform(link.anchorNames);
                link.anchorBody = FindNearestRigidbody(link.anchorTransform, MaxParentSearchDepth);
                if (link.anchorBody == null && IsHipsAnchor(link.anchorNames))
                    link.anchorBody = fallbackRootBody;

                if (link.limbBody != null && link.anchorBody != null && link.limbBody != link.anchorBody)
                    resolvedCount++;
            }

            _resolved = resolvedCount == _links.Length;
            if (!_resolved && verboseWarnings && !_warnedMissingTargets)
            {
                Debug.LogWarning($"[GrabAntiStretchController] Missing anti-stretch bodies on {name}. resolved={resolvedCount}/{_links.Length}");
                _warnedMissingTargets = true;
            }
        }

        private void TryResolveCoreDriveJoints()
        {
            if (_coreDriveLinks == null || _coreDriveLinks.Length == 0)
                BuildCoreDriveDefinitions();

            ResolveReferences();

            var allJoints = GetComponentsInChildren<ConfigurableJoint>(true);
            var usedJoints = new List<ConfigurableJoint>(_coreDriveLinks.Length);
            var resolvedCount = 0;

            for (var i = 0; i < _coreDriveLinks.Length; i++)
            {
                var link = _coreDriveLinks[i];
                if (link == null)
                    continue;

                link.joint = i == 0
                    ? rootMainJoint
                    : FindBestNamedJoint(link.jointNames, allJoints, usedJoints);

                if (link.joint == null)
                    continue;

                usedJoints.Add(link.joint);
                CacheOriginalDrive(link);
                resolvedCount++;
            }

            _coreDriveResolved = resolvedCount > 0;
            if (!_coreDriveResolved && verboseWarnings && !_warnedMissingCoreJoints)
            {
                Debug.LogWarning($"[GrabAntiStretchController] Missing core grab joints on {name}.");
                _warnedMissingCoreJoints = true;
            }
        }

        private Transform FindBestNamedTransform(string[] candidateNames)
        {
            var allTransforms = GetComponentsInChildren<Transform>(true);
            Transform best = null;
            var bestDepth = int.MaxValue;

            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null || !MatchesAnyName(candidate.name, candidateNames))
                    continue;

                var depth = FindNearestRigidbodyDepth(candidate, MaxParentSearchDepth);
                if (depth < 0 || depth >= bestDepth)
                    continue;

                best = candidate;
                bestDepth = depth;
            }

            return best;
        }

        private Transform FindBestAnchorTransform(string[] anchorNames)
        {
            var allTransforms = GetComponentsInChildren<Transform>(true);
            Transform best = null;
            var bestDepth = int.MaxValue;

            for (var i = 0; i < allTransforms.Length; i++)
            {
                var candidate = allTransforms[i];
                if (candidate == null || !MatchesAnyName(candidate.name, anchorNames))
                    continue;

                var depth = FindNearestRigidbodyDepth(candidate, MaxParentSearchDepth);
                if (depth < 0 || depth >= bestDepth)
                    continue;

                best = candidate;
                bestDepth = depth;
            }

            return best;
        }

        private static ConfigurableJoint FindBestNamedJoint(string[] candidateNames, ConfigurableJoint[] joints, List<ConfigurableJoint> usedJoints)
        {
            if (candidateNames == null || joints == null)
                return null;

            ConfigurableJoint best = null;
            var bestDepth = int.MaxValue;

            for (var i = 0; i < joints.Length; i++)
            {
                var candidate = joints[i];
                if (candidate == null ||
                    usedJoints.Contains(candidate) ||
                    !MatchesAnyName(candidate.transform.name, candidateNames))
                {
                    continue;
                }

                var depth = GetHierarchyDepth(candidate.transform);
                if (depth >= bestDepth)
                    continue;

                best = candidate;
                bestDepth = depth;
            }

            return best;
        }

        private static bool MatchesAnyName(string name, string[] candidateNames)
        {
            if (string.IsNullOrEmpty(name) || candidateNames == null)
                return false;

            for (var i = 0; i < candidateNames.Length; i++)
            {
                if (name == candidateNames[i])
                    return true;
            }

            return false;
        }

        private static int FindNearestRigidbodyDepth(Transform start, int maxDepth)
        {
            if (start == null)
                return -1;

            var current = start;
            for (var depth = 0; current != null && depth <= maxDepth; depth++)
            {
                if (current.GetComponent<Rigidbody>() != null)
                    return depth;
                current = current.parent;
            }

            return -1;
        }

        private static Rigidbody FindNearestRigidbody(Transform start, int maxDepth)
        {
            if (start == null)
                return null;

            var current = start;
            for (var depth = 0; current != null && depth <= maxDepth; depth++)
            {
                var rb = current.GetComponent<Rigidbody>();
                if (rb != null)
                    return rb;
                current = current.parent;
            }

            return null;
        }

        private static int GetHierarchyDepth(Transform target)
        {
            var depth = 0;
            var current = target;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

        private static bool IsHipsAnchor(string[] anchorNames)
        {
            if (anchorNames == null)
                return false;

            for (var i = 0; i < anchorNames.Length; i++)
            {
                if (anchorNames[i] == "Hips" || anchorNames[i] == "Pelvis")
                    return true;
            }

            return false;
        }

        private void EnableAntiStretch()
        {
            if (_links == null)
                return;

            if (!_resolved)
                TryResolveLinkBodies();

            for (var i = 0; i < _links.Length; i++)
                EnsureJoint(_links[i]);

            _active = true;
        }

        private void DisableAntiStretch()
        {
            if (_links == null)
            {
                _active = false;
                return;
            }

            for (var i = 0; i < _links.Length; i++)
            {
                var joint = _links[i]?.joint;
                if (joint == null)
                    continue;

                Destroy(joint);
                _links[i].joint = null;
            }

            _active = false;
        }

        private void EnsureJoint(RuntimeLink link)
        {
            if (link == null || link.joint != null)
                return;
            if (link.limbBody == null || link.anchorBody == null || link.limbBody == link.anchorBody)
                return;

            var limbAnchorWorld = link.limbTransform != null ? link.limbTransform.position : link.limbBody.worldCenterOfMass;
            var connectedAnchorWorld = link.anchorTransform != null ? link.anchorTransform.position : link.anchorBody.worldCenterOfMass;
            var distance = Vector3.Distance(limbAnchorWorld, connectedAnchorWorld) + link.slack;
            if (distance <= 0.001f)
                return;

            var joint = link.limbBody.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = link.anchorBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = link.limbBody.transform.InverseTransformPoint(limbAnchorWorld);
            joint.connectedAnchor = link.anchorBody.transform.InverseTransformPoint(connectedAnchorWorld);
            joint.xMotion = ConfigurableJointMotion.Limited;
            joint.yMotion = ConfigurableJointMotion.Limited;
            joint.zMotion = ConfigurableJointMotion.Limited;
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = projectionDistance;
            joint.projectionAngle = projectionAngle;
            joint.linearLimit = new SoftJointLimit { limit = distance };
            joint.linearLimitSpring = new SoftJointLimitSpring
            {
                spring = limitSpring,
                damper = limitDamper
            };

            link.joint = joint;
        }

        private void CacheOriginalDrive(RuntimeDriveLink link)
        {
            if (link == null || link.joint == null || link.cachedOriginal)
                return;

            link.originalSlerpDrive = link.joint.slerpDrive;
            link.originalAngularXDrive = link.joint.angularXDrive;
            link.originalAngularYZDrive = link.joint.angularYZDrive;
            link.cachedOriginal = true;
        }

        private void ApplyCoreDrive(CoreDriveMode mode)
        {
            ResolveCoreDriveMultipliers(mode, out var springMultiplier, out var damperMultiplier);

            for (var i = 0; i < _coreDriveLinks.Length; i++)
            {
                var link = _coreDriveLinks[i];
                if (link == null || link.joint == null || !link.cachedOriginal)
                    continue;

                link.joint.slerpDrive = ScaleDrive(link.originalSlerpDrive, springMultiplier, damperMultiplier);
                link.joint.angularXDrive = ScaleDrive(link.originalAngularXDrive, springMultiplier, damperMultiplier);
                link.joint.angularYZDrive = ScaleDrive(link.originalAngularYZDrive, springMultiplier, damperMultiplier);
            }

            _currentCoreDriveMode = mode;
        }

        private void RestoreCoreDrive()
        {
            if (_coreDriveLinks == null || _currentCoreDriveMode == CoreDriveMode.Off)
                return;

            for (var i = 0; i < _coreDriveLinks.Length; i++)
            {
                var link = _coreDriveLinks[i];
                if (link == null || link.joint == null || !link.cachedOriginal)
                    continue;

                link.joint.slerpDrive = link.originalSlerpDrive;
                link.joint.angularXDrive = link.originalAngularXDrive;
                link.joint.angularYZDrive = link.originalAngularYZDrive;
            }

            _currentCoreDriveMode = CoreDriveMode.Off;
        }

        private void ResolveCoreDriveMultipliers(CoreDriveMode mode, out float springMultiplier, out float damperMultiplier)
        {
            // CarrySolveFrame: CarryPhysicsProfile이 있으면 그 값 우선
            if (networkPlayer != null)
            {
                var carryProfile = networkPlayer.GetCarryPhysicsProfile();
                if (carryProfile != null)
                {
                    var carryMode = networkPlayer.GetLocalCarryMode();
                    if (carryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
                    {
                        var settings = carryProfile.GetSettings(carryMode);
                        // victim 쪽은 victimCoreDrive 사용, carrier 쪽은 carrierTorsoReaction으로 대체
                        if (mode == CoreDriveMode.CarriedVictim)
                        {
                            springMultiplier = settings.victimCoreDriveSpringMultiplier;
                            damperMultiplier = settings.victimCoreDriveDamperMultiplier;
                            return;
                        }
                    }
                }
            }

            // 폴백: 기존 Inspector 값
            switch (mode)
            {
                case CoreDriveMode.Carrying:
                    springMultiplier = carryingCoreSpringMultiplier;
                    damperMultiplier = carryingCoreDamperMultiplier;
                    break;

                case CoreDriveMode.DualCarryCarrier:
                    springMultiplier = dualCarryCoreSpringMultiplier;
                    damperMultiplier = dualCarryCoreDamperMultiplier;
                    break;

                case CoreDriveMode.CarriedVictim:
                    springMultiplier = carriedVictimCoreSpringMultiplier;
                    damperMultiplier = carriedVictimCoreDamperMultiplier;
                    break;

                default:
                    springMultiplier = grabbedCoreSpringMultiplier;
                    damperMultiplier = grabbedCoreDamperMultiplier;
                    break;
            }
        }

        private static JointDrive ScaleDrive(JointDrive source, float springMultiplier, float damperMultiplier)
        {
            if (source.positionSpring > 0f)
                source.positionSpring *= springMultiplier;

            if (source.positionDamper > 0f)
                source.positionDamper *= damperMultiplier;

            return source;
        }
    }
}
