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
        [SerializeField] private float holdingCoreSpringMultiplier = 1.04f;
        [SerializeField] private float holdingCoreDamperMultiplier = 1.03f;
        [SerializeField] private float carryingCoreSpringMultiplier = 1.15f;
        [SerializeField] private float carryingCoreDamperMultiplier = 1.1f;
        [SerializeField] private float dualCarryCoreSpringMultiplier = 1.4f;
        [SerializeField] private float dualCarryCoreDamperMultiplier = 1.18f;
        [SerializeField] private float grabbedCoreSpringMultiplier = 1.9f;
        [SerializeField] private float grabbedCoreDamperMultiplier = 1.35f;
        [SerializeField] private float carriedVictimCoreSpringMultiplier = 2.2f;
        [SerializeField] private float carriedVictimCoreDamperMultiplier = 1.35f;
        [SerializeField] private float carriedVictimLimbSpringMultiplier = 1.5f;
        [SerializeField] private float carriedVictimLimbDamperMultiplier = 1.3f;
        [SerializeField] private bool verboseWarnings;

        private RuntimeLink[] _links;
        private RuntimeDriveLink[] _coreDriveLinks;
        private RuntimeDriveLink[] _limbDriveLinks;
        private bool _resolved;
        private bool _coreDriveResolved;
        private bool _limbDriveResolved;
        private bool _active;
        private bool _warnedMissingTargets;
        private bool _warnedMissingCoreJoints;
        private CoreDriveMode _currentCoreDriveMode;

        // 동적 그랩 링크: 잡힌 앵커 부위 → 코어 본 체인 보강
        private readonly List<RuntimeLink> _dynamicGrabLinks = new();

        private enum CoreDriveMode : byte
        {
            Off = 0,
            Holding = 1,
            Carrying = 2,
            DualCarryCarrier = 3,
            GrabbedVictim = 4,
            CarriedVictim = 5
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
            ResolveReferences();
            BuildLinkDefinitions();
            BuildCoreDriveDefinitions();
            BuildLimbDriveDefinitions();
            TryResolveLinkBodies();
            TryResolveCoreDriveJoints();
            TryResolveLimbDriveJoints();
        }

        private void FixedUpdate()
        {
            if (!CanRunLocalPhysics())
                return;

            RefreshCoreDrive(force: true);
        }

        private void NormalizeTuning()
        {
            // Inspector 값을 존중 — 극단적인 값만 보정
            handSlack = Mathf.Max(handSlack, 0.01f);
            footSlack = Mathf.Max(footSlack, 0.01f);
            limitSpring = Mathf.Max(limitSpring, 10f);
            limitDamper = Mathf.Max(limitDamper, 1f);
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

            if (!_limbDriveResolved)
                TryResolveLimbDriveJoints();

            var shouldEnable = ShouldEnableAntiStretch();
            if (shouldEnable && !_active)
                EnableAntiStretch();
            else if (!shouldEnable && _active)
                DisableAntiStretch();

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

        private void BuildLimbDriveDefinitions()
        {
            if (_limbDriveLinks != null && _limbDriveLinks.Length == 4)
                return;

            _limbDriveLinks = new[]
            {
                CreateCoreDriveLink("LeftUpperArm", "LeftUpperArm", "LeftArm"),
                CreateCoreDriveLink("RightUpperArm", "RightUpperArm", "RightArm"),
                CreateCoreDriveLink("LeftUpperLeg", "LeftUpperLeg", "LeftThigh"),
                CreateCoreDriveLink("RightUpperLeg", "RightUpperLeg", "RightThigh")
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

            // CL_dev 방식: 특정 상태에서만 anti-stretch 활성화
            var phase = networkPlayer.GetPhysicalPhase();
            switch (phase)
            {
                case NetworkPlayer.PhysicalPhase.BeingGrabbed:
                case NetworkPlayer.PhysicalPhase.Dragged:
                case NetworkPlayer.PhysicalPhase.StunnedCollapse:
                case NetworkPlayer.PhysicalPhase.Stunned:
                case NetworkPlayer.PhysicalPhase.BeingCarriedStunned:
                    return true;
                case NetworkPlayer.PhysicalPhase.Recovering:
                    return applyDuringRecovering;
                default:
                    return false;
            }
        }

        private CoreDriveMode ResolveCoreDriveMode()
        {
            if (networkPlayer == null)
                return CoreDriveMode.Off;

            return networkPlayer.GetPhysicalPhase() switch
            {
                NetworkPlayer.PhysicalPhase.Holding => CoreDriveMode.Holding,
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

        private void TryResolveLimbDriveJoints()
        {
            if (_limbDriveLinks == null || _limbDriveLinks.Length == 0)
                BuildLimbDriveDefinitions();

            ResolveReferences();

            var allJoints = GetComponentsInChildren<ConfigurableJoint>(true);
            var usedJoints = new List<ConfigurableJoint>(_limbDriveLinks.Length);
            var resolvedCount = 0;

            for (var i = 0; i < _limbDriveLinks.Length; i++)
            {
                var link = _limbDriveLinks[i];
                if (link == null)
                    continue;

                link.joint = FindBestNamedJoint(link.jointNames, allJoints, usedJoints);

                if (link.joint == null)
                    continue;

                usedJoints.Add(link.joint);
                CacheOriginalDrive(link);
                resolvedCount++;
            }

            _limbDriveResolved = resolvedCount > 0;
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

            // 동적 그랩 링크도 활성화
            for (var i = 0; i < _dynamicGrabLinks.Count; i++)
                EnsureJoint(_dynamicGrabLinks[i]);

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

            // 동적 그랩 링크도 비활성화 (링크 정의는 유지, 조인트만 제거)
            for (var i = 0; i < _dynamicGrabLinks.Count; i++)
            {
                var dynJoint = _dynamicGrabLinks[i]?.joint;
                if (dynJoint == null) continue;
                Destroy(dynJoint);
                _dynamicGrabLinks[i].joint = null;
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

            // CarriedVictim 모드에서 팔다리 관절도 보강
            if (mode == CoreDriveMode.CarriedVictim && _limbDriveLinks != null && _limbDriveResolved)
            {
                for (var i = 0; i < _limbDriveLinks.Length; i++)
                {
                    var link = _limbDriveLinks[i];
                    if (link == null || link.joint == null || !link.cachedOriginal)
                        continue;

                    link.joint.slerpDrive = ScaleDrive(link.originalSlerpDrive, carriedVictimLimbSpringMultiplier, carriedVictimLimbDamperMultiplier);
                    link.joint.angularXDrive = ScaleDrive(link.originalAngularXDrive, carriedVictimLimbSpringMultiplier, carriedVictimLimbDamperMultiplier);
                    link.joint.angularYZDrive = ScaleDrive(link.originalAngularYZDrive, carriedVictimLimbSpringMultiplier, carriedVictimLimbDamperMultiplier);
                }
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

            // 팔다리 관절도 복원
            if (_limbDriveLinks != null)
            {
                for (var i = 0; i < _limbDriveLinks.Length; i++)
                {
                    var link = _limbDriveLinks[i];
                    if (link == null || link.joint == null || !link.cachedOriginal)
                        continue;

                    link.joint.slerpDrive = link.originalSlerpDrive;
                    link.joint.angularXDrive = link.originalAngularXDrive;
                    link.joint.angularYZDrive = link.originalAngularYZDrive;
                }
            }

            _currentCoreDriveMode = CoreDriveMode.Off;
        }

        private void ResolveCoreDriveMultipliers(CoreDriveMode mode, out float springMultiplier, out float damperMultiplier)
        {
            switch (mode)
            {
                case CoreDriveMode.Holding:
                    springMultiplier = holdingCoreSpringMultiplier;
                    damperMultiplier = holdingCoreDamperMultiplier;
                    break;

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

        // =========================================================
        // 동적 그랩 링크 — 잡힌 앵커 부위의 스트레칭 방지
        // =========================================================

        /// <summary>
        /// 잡힌 앵커에 대응하는 동적 안티스트레치 링크 추가.
        /// HandGrabHandler가 AttachGrab 시 타겟의 AntiStretchController에 호출.
        /// </summary>
        public void AddDynamicGrabLink(GrabAnchorPoint anchor)
        {
            if (anchor == null) return;

            // 앵커 부위에 따라 코어 본으로의 링크 결정
            ResolveDynamicLinkTargets(anchor.Id, out var limbNames, out var anchorNames);
            if (limbNames == null || anchorNames == null) return;

            var link = CreateLink($"DynGrab_{anchor.Id}", limbNames, anchorNames, handSlack * 0.5f);
            link.limbTransform = FindBestNamedTransform(link.limbNames);
            link.limbBody = FindNearestRigidbody(link.limbTransform, MaxParentSearchDepth);
            link.anchorTransform = FindBestAnchorTransform(link.anchorNames);
            link.anchorBody = FindNearestRigidbody(link.anchorTransform, MaxParentSearchDepth);
            if (link.anchorBody == null && IsHipsAnchor(link.anchorNames))
                link.anchorBody = fallbackRootBody;

            if (link.limbBody == null || link.anchorBody == null || link.limbBody == link.anchorBody)
                return;

            _dynamicGrabLinks.Add(link);

            if (_active)
                EnsureJoint(link);

            if (verboseWarnings)
                Debug.Log($"[AntiStretch] {name}: Added dynamic link {link.label}, " +
                    $"limb={link.limbBody?.name}, anchor={link.anchorBody?.name}", this);
        }

        /// <summary>
        /// 특정 앵커에 대응하는 동적 링크 제거.
        /// HandGrabHandler가 Release 시 타겟의 AntiStretchController에 호출.
        /// </summary>
        public void RemoveDynamicGrabLink(GrabAnchorPoint.AnchorId anchorId)
        {
            for (int i = _dynamicGrabLinks.Count - 1; i >= 0; i--)
            {
                var link = _dynamicGrabLinks[i];
                if (link == null || !link.label.EndsWith(anchorId.ToString()))
                    continue;

                if (link.joint != null)
                    Destroy(link.joint);
                _dynamicGrabLinks.RemoveAt(i);

                if (verboseWarnings)
                    Debug.Log($"[AntiStretch] {name}: Removed dynamic link {link.label}", this);
            }
        }

        /// <summary>모든 동적 그랩 링크 제거</summary>
        public void ClearAllDynamicGrabLinks()
        {
            for (int i = _dynamicGrabLinks.Count - 1; i >= 0; i--)
            {
                var link = _dynamicGrabLinks[i];
                if (link?.joint != null)
                    Destroy(link.joint);
            }
            _dynamicGrabLinks.Clear();
        }

        /// <summary>앵커 부위별 동적 링크 매핑 (사지 → 코어 본)</summary>
        private static void ResolveDynamicLinkTargets(GrabAnchorPoint.AnchorId anchorId,
            out string[] limbNames, out string[] anchorNames)
        {
            switch (anchorId)
            {
                case GrabAnchorPoint.AnchorId.LeftUpperArm:
                    limbNames = new[] { "LeftUpperArm", "LeftArm" };
                    anchorNames = new[] { "Chest", "Spine2", "Spine1" };
                    break;
                case GrabAnchorPoint.AnchorId.RightUpperArm:
                    limbNames = new[] { "RightUpperArm", "RightArm" };
                    anchorNames = new[] { "Chest", "Spine2", "Spine1" };
                    break;
                case GrabAnchorPoint.AnchorId.LeftForearm:
                    limbNames = new[] { "LeftForeArm", "LeftLowerArm" };
                    anchorNames = new[] { "LeftUpperArm", "LeftArm" };
                    break;
                case GrabAnchorPoint.AnchorId.RightForearm:
                    limbNames = new[] { "RightForeArm", "RightLowerArm" };
                    anchorNames = new[] { "RightUpperArm", "RightArm" };
                    break;
                case GrabAnchorPoint.AnchorId.Head:
                    limbNames = new[] { "Head" };
                    anchorNames = new[] { "Neck", "Chest", "Spine2" };
                    break;
                default:
                    // Chest/Hips는 이미 코어 본 — 추가 링크 불필요
                    limbNames = null;
                    anchorNames = null;
                    break;
            }
        }

        public string BuildCoreDriveDiagnosticsSummary()
        {
            ResolveReferences();

            var desiredMode = ResolveCoreDriveMode();
            ResolveCoreDriveMultipliers(desiredMode, out var springMultiplier, out var damperMultiplier);
            var antiStretchEnabled = networkPlayer != null && ShouldEnableAntiStretch();

            return $"antiStretchActive={(_active ? 1 : 0)} antiStretchEnabled={(antiStretchEnabled ? 1 : 0)} " +
                   $"coreMode={desiredMode} currentMode={_currentCoreDriveMode} " +
                   $"springMult={springMultiplier:F2} damperMult={damperMultiplier:F2} " +
                   $"dynamicLinks={_dynamicGrabLinks.Count} coreResolved={(_coreDriveResolved ? 1 : 0)}";
        }
    }
}
