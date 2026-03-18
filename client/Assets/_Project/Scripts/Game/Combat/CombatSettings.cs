using UnityEngine;
using SSAFYPlayTime.Gameplay.Items;
using System.Collections.Generic;

/// <summary>
/// CombatTable.csv + CombatParamsTable.csv의 전투 수치를 런타임에 로드하고 전역 접근을 제공.
/// </summary>
public class CombatSettings : MonoBehaviour
{
    public static CombatSettings Instance { get; private set; }

    [Header("CSV Paths")]
    [SerializeField] private string combatTablePath = "Assets/_Project/Data/CombatTable.csv";
    [SerializeField] private string combatParamsPath = "Assets/_Project/Data/CombatParamsTable.csv";

    // ─── 공격별 수치 (CombatTable.csv) ───
    public Dictionary<string, CombatTableCsvLoader.Row> AttackStats { get; private set; }
        = new Dictionary<string, CombatTableCsvLoader.Row>();

    // ─── 기절 시스템 파라미터 (fallback 값) ───
    [Header("Knockout System Fallback")]
    public float knockoutThreshold = 30f;
    public float stunAccumulateDecay = 5f;
    public float groggyMultiplier = 1.8f;
    public float airborneMultiplier = 1.5f;
    public float recoveringMultiplier = 2.0f;
    public float stunMinDuration = 1.5f;
    public float stunMaxDuration = 8.0f;
    public float stunVelocityBonus = 0.15f;
    public float stunWeightBonus = 0.02f;
    public float groggyDuration = 2.0f;
    public float groggyToStunChance = 0.7f;
    public float headbuttSelfStunWall = 2.5f;
    public float headbuttSelfStunFloor = 1.5f;

    [Header("Body Part Multipliers")]
    public float bodyPartHeadMultiplier = 1.5f;
    public float bodyPartBodyMultiplier = 1.0f;
    public float bodyPartLimbMultiplier = 0.7f;

    [Header("Grab/Throw")]
    public float grabThrowForceObject = 12f;
    public float grabThrowForceNormal = 10f;
    public float grabThrowForceStunned = 15f;
    public float thrownObjectStunDamage = 20f;
    public float thrownPlayerStunDamage = 25f;

    [Header("HP System")]
    [Tooltip("최대 HP (게임 내 숨겨짐, 디자이너 조정용)")]
    public float maxHealth = 200f;
    [Tooltip("펀치 1회 고정 HP 데미지")]
    public float punchHpDamage = 4f;
    [Tooltip("GhostCube 폭발 중심부 HP 데미지 (거리 감쇠 적용)")]
    public float ghostBombHpDamage = 30f;
    [Tooltip("바나나 밟혔을 때 HP 데미지")]
    public float bananaHpDamage = 10f;
    [Tooltip("맵 밖 추락 시 HP 데미지 (즉사)")]
    public float outOfBoundsHpDamage = 9999f;

    private bool _loaded;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadFromCsv();
    }

    private void LoadFromCsv()
    {
        if (_loaded) return;

        // CombatTable.csv 로드
        if (CombatTableCsvLoader.TryLoadFromDisk(combatTablePath, out var attacks, out _, out var error1))
        {
            AttackStats = attacks;
            Debug.Log($"[CombatSettings] CombatTable 로드 성공: {attacks.Count}개 공격");
        }
        else
        {
            Debug.LogWarning($"[CombatSettings] CombatTable 로드 실패: {error1}");
        }

        // CombatParamsTable.csv 로드
        if (CombatParamsCsvLoader.TryLoadFromDisk(combatParamsPath, out var paramDict, out _, out var error2))
        {
            ApplyParams(paramDict);
            Debug.Log($"[CombatSettings] CombatParams 로드 성공: {paramDict.Count}개 파라미터");
        }
        else
        {
            Debug.LogWarning($"[CombatSettings] CombatParams 로드 실패 (fallback 사용): {error2}");
        }

        _loaded = true;
    }

    private void ApplyParams(Dictionary<string, float> p)
    {
        if (p.TryGetValue("KNOCKOUT_BASE_THRESHOLD", out var v)) knockoutThreshold = v;
        if (p.TryGetValue("KNOCKOUT_ACCUMULATE_DECAY", out v)) stunAccumulateDecay = v;
        if (p.TryGetValue("KNOCKOUT_GROGGY_MULTIPLIER", out v)) groggyMultiplier = v;
        if (p.TryGetValue("KNOCKOUT_AIRBORNE_MULTIPLIER", out v)) airborneMultiplier = v;
        if (p.TryGetValue("KNOCKOUT_RECOVERING_MULTIPLIER", out v)) recoveringMultiplier = v;
        if (p.TryGetValue("STUN_MIN_DURATION", out v)) stunMinDuration = v;
        if (p.TryGetValue("STUN_MAX_DURATION", out v)) stunMaxDuration = v;
        if (p.TryGetValue("STUN_VELOCITY_BONUS", out v)) stunVelocityBonus = v;
        if (p.TryGetValue("STUN_WEIGHT_BONUS", out v)) stunWeightBonus = v;
        if (p.TryGetValue("GROGGY_DURATION", out v)) groggyDuration = v;
        if (p.TryGetValue("GROGGY_TO_STUN_CHANCE", out v)) groggyToStunChance = v;
        if (p.TryGetValue("HEADBUTT_SELF_STUN_WALL", out v)) headbuttSelfStunWall = v;
        if (p.TryGetValue("HEADBUTT_SELF_STUN_FLOOR", out v)) headbuttSelfStunFloor = v;
        if (p.TryGetValue("BODY_PART_HEAD_MULTIPLIER", out v)) bodyPartHeadMultiplier = v;
        if (p.TryGetValue("BODY_PART_BODY_MULTIPLIER", out v)) bodyPartBodyMultiplier = v;
        if (p.TryGetValue("BODY_PART_LIMB_MULTIPLIER", out v)) bodyPartLimbMultiplier = v;
        if (p.TryGetValue("GRAB_THROW_FORCE_OBJECT", out v)) grabThrowForceObject = v;
        if (p.TryGetValue("GRAB_THROW_FORCE_PLAYER_NORMAL", out v)) grabThrowForceNormal = v;
        if (p.TryGetValue("GRAB_THROW_FORCE_PLAYER_STUNNED", out v)) grabThrowForceStunned = v;
        if (p.TryGetValue("THROWN_OBJECT_STUN_DAMAGE", out v)) thrownObjectStunDamage = v;
        if (p.TryGetValue("THROWN_PLAYER_STUN_DAMAGE", out v)) thrownPlayerStunDamage = v;
        if (p.TryGetValue("MAX_HEALTH", out v)) maxHealth = v;
        if (p.TryGetValue("PUNCH_HP_DAMAGE", out v)) punchHpDamage = v;
        if (p.TryGetValue("GHOST_BOMB_HP_DAMAGE", out v)) ghostBombHpDamage = v;
        if (p.TryGetValue("BANANA_HP_DAMAGE", out v)) bananaHpDamage = v;
    }

    /// <summary>공격 ID로 수치 조회. 없으면 null.</summary>
    public CombatTableCsvLoader.Row? GetAttackStat(string attackId)
    {
        if (AttackStats != null && AttackStats.TryGetValue(attackId, out var row))
            return row;
        return null;
    }

    public void ReloadCsv()
    {
        _loaded = false;
        LoadFromCsv();
    }
}
