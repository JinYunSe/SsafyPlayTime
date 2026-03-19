/*
 * 파일 개요:
 * - ItemModels 스크립트가 들어 있는 파일이다.
 * - Common 계층에서 아이템 시스템 전반이 공유하는 모델, 상수, 인터페이스를 정의한다.
 * - 이 파일이 바뀌면 Character, World, Runtime 전부에 영향이 갈 수 있으므로 하위 호환성을 우선 확인한다.
 */
using System;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public enum ItemType
    {
        Consumable = 0,
        Equipment = 1
    }

    public enum ItemUseType
    {
        Instant = 0,
        Hold = 1
    }

    public enum ItemDropReason
    {
        Stunned = 0,
        Replaced = 1,
        Consumed = 2,
        Manual = 3
    }

    [Flags]
    public enum ItemBuffMask
    {
        None = 0,
        Growth = 1 << 0,
        Shrink = 1 << 1,
        SuperArmor = 1 << 2,
        Invisibility = 1 << 3
    }

    public readonly struct ItemBuffRuntimeState
    {
        public ItemBuffRuntimeState(
            float growthRemainSec,
            float shrinkRemainSec,
            float superArmorRemainSec,
            float invisibilityRemainSec)
        {
            GrowthRemainSec = growthRemainSec;
            ShrinkRemainSec = shrinkRemainSec;
            SuperArmorRemainSec = superArmorRemainSec;
            InvisibilityRemainSec = invisibilityRemainSec;
        }

        public float GrowthRemainSec { get; }
        public float ShrinkRemainSec { get; }
        public float SuperArmorRemainSec { get; }
        public float InvisibilityRemainSec { get; }
    }

    public readonly struct BlackholeSkillRequest
    {
        public BlackholeSkillRequest(
            Vector3 center,
            Vector3 origin,
            Vector3 forward,
            float delaySec,
            float durationSec,
            float radius,
            float force)
        {
            Center = center;
            Origin = origin;
            Forward = forward;
            DelaySec = delaySec;
            DurationSec = durationSec;
            Radius = radius;
            Force = force;
        }

        public Vector3 Center { get; }
        public Vector3 Origin { get; }
        public Vector3 Forward { get; }
        public float DelaySec { get; }
        public float DurationSec { get; }
        public float Radius { get; }
        public float Force { get; }
    }

    public readonly struct SatelliteStrikeRequest
    {
        public SatelliteStrikeRequest(
            Vector3 center,
            Vector3 origin,
            Vector3 forward,
            float warningSec,
            float durationSec,
            float radius,
            float force,
            float baseDamage,
            float stunDamage)
        {
            Center = center;
            Origin = origin;
            Forward = forward;
            WarningSec = warningSec;
            DurationSec = durationSec;
            Radius = radius;
            Force = force;
            BaseDamage = baseDamage;
            StunDamage = stunDamage;
        }

        public Vector3 Center { get; }
        public Vector3 Origin { get; }
        public Vector3 Forward { get; }
        public float WarningSec { get; }
        public float DurationSec { get; }
        public float Radius { get; }
        public float Force { get; }
        public float BaseDamage { get; }
        public float StunDamage { get; }
    }

    public readonly struct FlamethrowerTickRequest
    {
        public FlamethrowerTickRequest(
            Vector3 origin,
            Vector3 forward,
            float range,
            float radius,
            float pushForce,
            float damagePerTick,
            float stunDamagePerTick)
        {
            Origin = origin;
            Forward = forward;
            Range = range;
            Radius = radius;
            PushForce = pushForce;
            DamagePerTick = damagePerTick;
            StunDamagePerTick = stunDamagePerTick;
        }

        public Vector3 Origin { get; }
        public Vector3 Forward { get; }
        public float Range { get; }
        public float Radius { get; }
        public float PushForce { get; }
        public float DamagePerTick { get; }
        public float StunDamagePerTick { get; }
    }

    public readonly struct MeleeSwingRequest
    {
        public MeleeSwingRequest(
            string itemId,
            float activeDurationSec,
            float healthDamage,
            float stunDamage)
        {
            ItemId = itemId;
            ActiveDurationSec = activeDurationSec;
            HealthDamage = healthDamage;
            StunDamage = stunDamage;
        }

        public string ItemId { get; }
        public float ActiveDurationSec { get; }
        public float HealthDamage { get; }
        public float StunDamage { get; }
    }
}

