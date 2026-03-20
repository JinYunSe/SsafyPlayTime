using UnityEngine;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// Carry 전용 물리 프로필.
    /// normal grab / stunned single carry / dual carry / carried victim 4개 상태별로
    /// root follow, proxy snap, torso reaction, core drive 등의 값을 분리한다.
    /// </summary>
    [CreateAssetMenu(menuName = "SSAFYPlayTime/Character/CarryPhysicsProfile")]
    public sealed class CarryPhysicsProfile : ScriptableObject
    {
        public enum CarryMode : byte
        {
            None = 0,
            NormalGrab = 1,
            StunnedSingleCarry = 2,
            StunnedDualCarry = 3,
            CarriedVictim = 4
        }

        [System.Serializable]
        public struct CarryModeSettings
        {
            [Header("Root Follow")]
            [Tooltip("수평 추적 속도")]
            public float rootPlanarFollowSpeed;
            [Tooltip("수직 추적 속도")]
            public float rootVerticalFollowSpeed;
            [Tooltip("즉시 스냅 거리")]
            public float rootSnapDistance;
            [Tooltip("수직 즉시 스냅 갭")]
            public float rootSnapVerticalGap;

            [Header("Proxy Root")]
            [Tooltip("프록시 루트 추적 속도")]
            public float proxyRootFollowSpeed;
            [Tooltip("프록시 루트 스냅 거리")]
            public float proxyRootSnapDistance;

            [Header("Carry Release")]
            [Tooltip("carry 종료 후 settle 유지 시간")]
            public float carryReleaseSettleDuration;

            [Header("Reaction")]
            [Tooltip("carrier torso 반작용 배율")]
            public float carrierTorsoReactionMultiplier;
            [Tooltip("carrier turn assist 배율")]
            public float carrierTurnAssistMultiplier;

            [Header("Victim")]
            [Tooltip("victim core drive spring 배율")]
            public float victimCoreDriveSpringMultiplier;
            [Tooltip("victim core drive damper 배율")]
            public float victimCoreDriveDamperMultiplier;
        }

        [Header("Normal Grab")]
        public CarryModeSettings normalGrab = new CarryModeSettings
        {
            rootPlanarFollowSpeed = 7.5f,
            rootVerticalFollowSpeed = 14f,
            rootSnapDistance = 1.15f,
            rootSnapVerticalGap = 0.65f,
            proxyRootFollowSpeed = 20f,
            proxyRootSnapDistance = 1.10f,
            carryReleaseSettleDuration = 0.10f,
            carrierTorsoReactionMultiplier = 1.0f,
            carrierTurnAssistMultiplier = 1.0f,
            victimCoreDriveSpringMultiplier = 1.0f,
            victimCoreDriveDamperMultiplier = 1.0f
        };

        [Header("Stunned Single Carry (한손)")]
        public CarryModeSettings stunnedSingleCarry = new CarryModeSettings
        {
            rootPlanarFollowSpeed = 9f,
            rootVerticalFollowSpeed = 16f,
            rootSnapDistance = 1.0f,
            rootSnapVerticalGap = 0.55f,
            proxyRootFollowSpeed = 22f,
            proxyRootSnapDistance = 1.0f,
            carryReleaseSettleDuration = 0.20f,
            carrierTorsoReactionMultiplier = 1.15f,
            carrierTurnAssistMultiplier = 1.1f,
            victimCoreDriveSpringMultiplier = 0.75f,
            victimCoreDriveDamperMultiplier = 0.80f
        };

        [Header("Stunned Dual Carry (양손)")]
        public CarryModeSettings stunnedDualCarry = new CarryModeSettings
        {
            rootPlanarFollowSpeed = 10f,
            rootVerticalFollowSpeed = 18f,
            rootSnapDistance = 0.9f,
            rootSnapVerticalGap = 0.50f,
            proxyRootFollowSpeed = 24f,
            proxyRootSnapDistance = 0.90f,
            carryReleaseSettleDuration = 0.25f,
            carrierTorsoReactionMultiplier = 1.35f,
            carrierTurnAssistMultiplier = 1.25f,
            victimCoreDriveSpringMultiplier = 0.65f,
            victimCoreDriveDamperMultiplier = 0.70f
        };

        [Header("Carried Victim (운반 당하는 쪽)")]
        public CarryModeSettings carriedVictim = new CarryModeSettings
        {
            rootPlanarFollowSpeed = 10f,
            rootVerticalFollowSpeed = 18f,
            rootSnapDistance = 0.90f,
            rootSnapVerticalGap = 0.50f,
            proxyRootFollowSpeed = 24f,
            proxyRootSnapDistance = 0.90f,
            carryReleaseSettleDuration = 0.20f,
            carrierTorsoReactionMultiplier = 1.0f,
            carrierTurnAssistMultiplier = 1.0f,
            victimCoreDriveSpringMultiplier = 0.60f,
            victimCoreDriveDamperMultiplier = 0.65f
        };

        public CarryModeSettings GetSettings(CarryMode mode)
        {
            return mode switch
            {
                CarryMode.NormalGrab => normalGrab,
                CarryMode.StunnedSingleCarry => stunnedSingleCarry,
                CarryMode.StunnedDualCarry => stunnedDualCarry,
                CarryMode.CarriedVictim => carriedVictim,
                _ => normalGrab
            };
        }
    }
}
