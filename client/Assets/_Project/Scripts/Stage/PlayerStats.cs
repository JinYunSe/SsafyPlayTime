using System;
using Fusion;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [Header("Offline Fallback")]
    [SerializeField] private int offlineStartingHealth = 260;

    private NetworkPlayer _networkPlayer;
    private int _standaloneCurrentHealth;
    private int _lastObservedHealth = -1;
    private int _lastObservedMaxHealth = -1;
    private bool _lastObservedDead;

    public int currentHealth => CurrentHealth;
    public int maxHealth => MaxHealth;
    public int CurrentHealth => ResolveCurrentHealth();
    public int MaxHealth => ResolveMaxHealth();
    public bool IsDead => ResolveIsDead();

    public event Action<PlayerStats> OnDied;
    public event Action<PlayerStats, int, int> OnHealthChanged;

    private void Awake()
    {
        _networkPlayer = GetComponent<NetworkPlayer>();
        _standaloneCurrentHealth = Mathf.Max(1, offlineStartingHealth);
    }

    private void Start()
    {
        TryPlaceStandaloneAtSpawn();
        RefreshObservedState(forceNotify: true);
    }

    private void Update()
    {
        RefreshObservedState(forceNotify: false);
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0)
            return;

        if (_networkPlayer != null)
        {
            _networkPlayer.ApplyHealthDamage(damage, "LegacyDamage");
            return;
        }

        ApplyStandaloneDamage(damage);
    }

    public void ApplyDamage(float damage, float stunDamage, string source)
    {
        if (_networkPlayer != null)
        {
            _networkPlayer.ApplyCombinedDamage(damage, stunDamage, source);
            return;
        }

        if (damage > 0f)
            ApplyStandaloneDamage(Mathf.Max(1, Mathf.RoundToInt(damage)));
    }

    private void ApplyStandaloneDamage(int damage)
    {
        if (_standaloneCurrentHealth <= 0)
            return;

        _standaloneCurrentHealth = Mathf.Max(0, _standaloneCurrentHealth - damage);
        RefreshObservedState(forceNotify: true);
    }

    private int ResolveCurrentHealth()
    {
        if (_networkPlayer != null)
            return _networkPlayer.CurrentHealth;

        return Mathf.Clamp(_standaloneCurrentHealth, 0, MaxHealth);
    }

    private int ResolveMaxHealth()
    {
        if (_networkPlayer != null)
            return Mathf.Max(1, _networkPlayer.MaxHealth);

        return Mathf.Max(1, offlineStartingHealth);
    }

    private bool ResolveIsDead()
    {
        if (_networkPlayer != null)
            return _networkPlayer.IsDeadState;

        return _standaloneCurrentHealth <= 0;
    }

    private void RefreshObservedState(bool forceNotify)
    {
        var current = ResolveCurrentHealth();
        var max = ResolveMaxHealth();
        var dead = ResolveIsDead();

        if (forceNotify || current != _lastObservedHealth || max != _lastObservedMaxHealth)
        {
            _lastObservedHealth = current;
            _lastObservedMaxHealth = max;
            OnHealthChanged?.Invoke(this, current, max);
        }

        if (!_lastObservedDead && dead)
            OnDied?.Invoke(this);

        _lastObservedDead = dead;
    }

    private void TryPlaceStandaloneAtSpawn()
    {
        // Fusion 네트워크 캐릭터: 스폰 위치는 서버가 결정하므로 로컬 이동 생략
        var networkObject = GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.Runner != null && networkObject.IsValid)
            return;

        var spawnManager = FindObjectOfType<SpawnManager>();
        if (spawnManager == null)
            return;

        transform.position = spawnManager.GetSpawnPosition();
    }
}
