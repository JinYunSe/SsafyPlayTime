/*
 * 파일 개요:
 * - ItemRuntimeController.Actions 스크립트가 들어 있는 파일이다.
 * - Runtime/Controller 계층에서 아이템 상태 전이, 사용 요청 처리, 공용 브리지 호출을 조합한다.
 * - 아이템 공통 흐름을 수정할 때 진입점으로 삼는 파일이며, 개별 아이템 예외는 Modules 계층으로 분리하는 것을 우선한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 사용 액션(스킬 발동/장비 토글/소모)을 담당한다.
    /// </summary>
    public sealed partial class ItemRuntimeController
    {
        public bool TryUseHeldItem(Vector3 ownerPosition, Vector3 ownerForward, Vector3 targetPosition, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(_heldItemId))
            {
                reason = "No held item.";
                ItemRuntimeLog.Warn("Use", $"사용 거부: {reason}");
                return false;
            }

            if (!_catalog.TryGetDefinition(_heldItemId, out var def))
            {
                reason = $"Held item is missing in catalog: {_heldItemId}";
                ItemRuntimeLog.Error(_heldItemId, $"카탈로그 누락: {reason}");
                return false;
            }

            ownerForward = NormalizeForward(ownerForward);
            if (targetPosition == Vector3.zero)
            {
                targetPosition = ownerPosition + ownerForward * 6f;
            }

            PlayUsePresentation(def, ownerPosition);
            ItemRuntimeLog.Info(def.Master.ItemId, $"사용 요청 시작: owner={ownerPosition}, forward={ownerForward}, target={targetPosition}");
            var context = new ItemUseModuleContext(this, def, ownerPosition, ownerForward, targetPosition);
            var result = _useModuleRegistry.TryUse(def.Master.ItemId, context);
            if (!result.Success)
            {
                reason = string.IsNullOrWhiteSpace(result.Reason)
                    ? $"Failed to use item module: {def.Master.ItemId}"
                    : result.Reason;
                ItemRuntimeLog.Warn(def.Master.ItemId, $"사용 모듈 실패: {reason}");
                return false;
            }

            if (result.ConsumeHeldItem)
            {
                ConsumeHeldItem(def);
            }

            ItemRuntimeLog.Info(def.Master.ItemId, $"사용 요청 완료: consume={result.ConsumeHeldItem}");
            return true;
        }

        internal void UseBlackhole(ItemDefinition def, Vector3 ownerPosition, Vector3 ownerForward)
        {
            var request = new BlackholeSkillRequest(
                ownerPosition + ownerForward * 6f,
                Mathf.Max(0f, def.Master.UseDelaySec),
                Mathf.Max(0f, def.Master.DurationSec),
                Mathf.Max(0f, def.Master.Range),
                Mathf.Max(0f, def.Master.Force));
            ItemRuntimeLog.Info(def.Master.ItemId, $"블랙홀 요청: center={request.Center}, delay={request.DelaySec:0.00}, duration={request.DurationSec:0.00}, radius={request.Radius:0.00}");
            _bridge.OnBlackholeRequested(request);
        }

        internal void UseSatelliteStrike(ItemDefinition def, Vector3 ownerPosition, Vector3 ownerForward, Vector3 targetPosition)
        {
            // 한국어: 위성 레이저는 블랙홀과 같은 투척 감각으로 처리한다.
            // 한국어: 조준점 좌표를 즉시 확정하지 않고, 전방으로 던진 뒤 실제 착지 지점에서 발동한다.
            var fallbackCenter = ownerPosition + ownerForward * 6f;
            var request = new SatelliteStrikeRequest(
                fallbackCenter,
                ownerPosition,
                ownerForward,
                Mathf.Max(0f, def.Master.WarningTimeSec),
                Mathf.Max(0f, def.Master.DurationSec),
                Mathf.Max(0f, def.Master.Range),
                Mathf.Max(0f, def.Master.Force),
                Mathf.Max(0f, def.Master.BaseDamage),
                Mathf.Max(0f, def.Master.StunDamage));
            ItemRuntimeLog.Info(def.Master.ItemId, $"위성 레이저 요청: origin={ownerPosition}, forward={ownerForward}, fallbackCenter={fallbackCenter}, warning={request.WarningSec:0.00}");
            _bridge.OnSatelliteStrikeRequested(request);
        }

        internal void ToggleFlamethrower(ItemDefinition def)
        {
            if (_isFlamethrowerActive)
            {
                ItemRuntimeLog.Info(def.Master.ItemId, "화염방사기 종료 요청");
                StopFlamethrowerIfNeeded();
                return;
            }

            var now = _bridge.Now;
            var maxUseSec = def.Master.MaxActiveUseSec > 0f ? def.Master.MaxActiveUseSec : 5f;
            _equipmentEndAt = now + maxUseSec;
            _nextFlamethrowerTickAt = now;
            _isFlamethrowerActive = true;
            ItemRuntimeLog.Info(def.Master.ItemId, $"화염방사기 시작: now={now:0.00}, endAt={_equipmentEndAt:0.00}");
            _bridge.OnFlamethrowerStart(def.Master.ItemId, _equipmentEndAt);
        }

        internal void UseWatermelonSword(ItemDefinition def)
        {
            if (def == null)
            {
                return;
            }

            // 수박검 근접 타격은 현재 고정 피해(체력/스턴 각 5) 정책으로 요청한다.
            var request = new MeleeSwingRequest(
                def.Master.ItemId,
                DefaultWatermelonSwordActiveDuration,
                DefaultWatermelonSwordHealthDamage,
                DefaultWatermelonSwordStunDamage);
            ItemRuntimeLog.Info(def.Master.ItemId, $"근접 사용 요청: activeDuration={request.ActiveDurationSec:0.00}, hp={request.HealthDamage:0.00}, stun={request.StunDamage:0.00}");
            _bridge.OnMeleeSwingRequested(request);
        }

        private void TickFlamethrower(float now, Vector3 ownerPosition, Vector3 ownerForward)
        {
            if (!_isFlamethrowerActive)
            {
                return;
            }

            if (now >= _equipmentEndAt)
            {
                StopFlamethrowerIfNeeded();
                return;
            }

            if (!_catalog.TryGetDefinition(ItemIds.Flamethrower, out var flameDef))
            {
                StopFlamethrowerIfNeeded();
                return;
            }

            if (now < _nextFlamethrowerTickAt)
            {
                return;
            }

            var interval = flameDef.Master.TickIntervalSec > 0f ? flameDef.Master.TickIntervalSec : DefaultFlamethrowerTickInterval;
            _nextFlamethrowerTickAt = now + interval;

            ownerForward = NormalizeForward(ownerForward);
            var request = new FlamethrowerTickRequest(
                ownerPosition + Vector3.up * 1.2f + ownerForward * 0.7f,
                ownerForward,
                Mathf.Max(0f, flameDef.Master.Range),
                DefaultFlamethrowerRadius,
                Mathf.Max(0f, flameDef.Master.Force),
                Mathf.Max(0f, flameDef.Master.BaseDamage),
                Mathf.Max(0f, flameDef.Master.StunDamage));
            ItemRuntimeLog.Info(flameDef.Master.ItemId, $"화염 틱: origin={request.Origin}, forward={request.Forward}, range={request.Range:0.00}, radius={request.Radius:0.00}");
            _bridge.OnFlamethrowerTick(request);
        }

        private void StopFlamethrowerIfNeeded()
        {
            if (!_isFlamethrowerActive)
            {
                return;
            }

            _isFlamethrowerActive = false;
            _equipmentEndAt = 0f;
            _nextFlamethrowerTickAt = 0f;
            ItemRuntimeLog.Info(ItemIds.Flamethrower, "화염방사기 종료 확정");
            _bridge.OnFlamethrowerStop(ItemIds.Flamethrower);
        }

        private void PlayUsePresentation(ItemDefinition def, Vector3 position)
        {
            var sfxId = def.ResolveUseSfxId();
            if (!string.IsNullOrWhiteSpace(sfxId))
            {
                var loop = _catalog.TryGetSound(sfxId, out var soundRow) && soundRow.Loop;
                _bridge.OnPlaySfx(sfxId, position, loop);
            }
            // 테이블 기반 시작 VFX는 현재 전부 비활성화한다.
            // (투사체형 프리팹 자동 생성 방지 목적)
        }

        private void ConsumeHeldItem(ItemDefinition def)
        {
            var consumedId = def.Master.ItemId;
            SetHeldItem(string.Empty);
            ItemRuntimeLog.Info(consumedId, "소비형 아이템 소모 처리");
            _bridge.OnItemConsumed(consumedId);
        }
    }
}

