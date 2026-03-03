using Fusion;
using UnityEngine;

namespace SSAFYPlayTime
{
    public sealed partial class ItemPrototypeHotkeyRunner
    {
        private void CacheBaseScale()
        {
            if (targetRoot == null)
            {
                return;
            }

            _baseScale = targetRoot.localScale;
            _hasBaseScale = true;
        }

        private void ApplyInvisibility(float alpha)
        {
            RestoreRendererColors();
            _originalRendererColors.Clear();

            var renderers = targetRoot != null ? targetRoot.GetComponentsInChildren<Renderer>(true) : null;
            if (renderers == null)
            {
                return;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.materials;
                var colors = new Color[materials.Length];
                var hasAnyColor = false;

                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null || !material.HasProperty("_Color"))
                    {
                        continue;
                    }

                    var color = material.color;
                    colors[m] = color;
                    color.a = alpha;
                    material.color = color;
                    hasAnyColor = true;
                }

                if (hasAnyColor)
                {
                    _originalRendererColors[renderer] = colors;
                }
            }
        }

        private void RestoreRendererColors()
        {
            foreach (var pair in _originalRendererColors)
            {
                var renderer = pair.Key;
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.materials;
                var colors = pair.Value;
                var count = Mathf.Min(materials.Length, colors.Length);

                for (var i = 0; i < count; i++)
                {
                    var material = materials[i];
                    if (material == null || !material.HasProperty("_Color"))
                    {
                        continue;
                    }

                    material.color = colors[i];
                }
            }

            _originalRendererColors.Clear();
        }

        private Vector3 GetTargetPosition()
        {
            return targetRoot != null ? targetRoot.position : transform.position;
        }

        private Vector3 GetTargetForward()
        {
            if (targetRoot == null)
            {
                return transform.forward;
            }

            var forward = targetRoot.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }

        private void SetStatus(string message)
        {
            _statusLine = message;
            Debug.Log($"[ItemPrototype] {message}");
        }

        private float GetRemainingSeconds(TickTimer timer, float fallbackEndTime)
        {
            if (TryGetRunner(out var runner))
            {
                var remain = timer.RemainingTime(runner);
                return remain ?? 0f;
            }

            return Mathf.Max(0f, fallbackEndTime - Time.time);
        }

        private void DrawHotkeyGuide()
        {
            const float top = 16f;
            var lineCount = 27;
            var height = 20f + lineCount * UiLineHeight;
            var rect = new Rect(Screen.width - UiPanelWidth - 16f, top, UiPanelWidth, height);
            GUI.Box(rect, "Item Prototype (1~7)");

            var x = rect.x + 12f;
            var y = rect.y + 28f;

            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "1: 블랙홀 폭탄");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "2: 커지는 아이템");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "3: 작아지는 아이템");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "4: 아이스 아메리카노");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "5: 화염 방사기 (토글, 최대 5초)");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "6: 투명화 아이템");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "7: 위성 폭격");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "0: 기절 드랍 시뮬레이션");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "WASD: 이동");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "우클릭+마우스: 카메라 회전 / 휠: 줌");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "Space: 위치 + 상태 초기화");
            y += UiLineHeight;

            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), $"Target: {(targetRoot != null ? targetRoot.name : "None")}");
            y += UiLineHeight;

            var flameRemain = _isFlamethrowerActive ? GetRemainingSeconds(_flamethrowerEndTickTimer, _flamethrowerEndTime) : 0f;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), _isFlamethrowerActive
                ? $"Flamethrower: ON ({flameRemain:0.0}s)"
                : "Flamethrower: OFF");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), _isFlamethrowerRangeBoostedByBlackhole
                ? $"FlameRange: {flamethrowerRange:0.0} (블랙홀 버프)"
                : $"FlameRange: {flamethrowerRange:0.0}");
            y += UiLineHeight;
            var tickPerSec = flamethrowerTickIntervalSec > 0f ? 1f / flamethrowerTickIntervalSec : 0f;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight),
                $"FlameTick: {tickPerSec:0.0}/s  TickDmg:{flamethrowerDamagePerTick:0.0}  LastHit:{_lastFlamethrowerTickHitCount}");
            y += UiLineHeight;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight),
                $"FlameLastDmg: {_lastFlamethrowerTickDamage:0.0}");
            y += UiLineHeight;

            var armorRemain = _isSuperArmorActive ? GetRemainingSeconds(_superArmorTickTimer, _superArmorEndTime) : 0f;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), _isSuperArmorActive
                ? $"SuperArmor: ON ({armorRemain:0.0}s)"
                : "SuperArmor: OFF");
            y += UiLineHeight;

            var scaleRemain = _isScaleBuffActive ? Mathf.Max(0f, _scaleBuffEndTime - Time.time) : 0f;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), _isScaleBuffActive
                ? $"ScaleBuff: {_scaleBuffLabel} ({scaleRemain:0.0}s)"
                : "ScaleBuff: OFF");
            y += UiLineHeight;

            var invisRemain = _isInvisibilityActive ? Mathf.Max(0f, _invisibilityEndTime - Time.time) : 0f;
            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), _isInvisibilityActive
                ? $"Invisibility: ON ({invisRemain:0.0}s)"
                : "Invisibility: OFF");
            y += UiLineHeight;

            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), $"RunnerHostOnly: {(hostAuthorityOnlyWhenRunnerExists ? "ON" : "OFF")}");
            y += UiLineHeight;

            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), $"ItemTable: {(loadValuesFromItemTable ? "ON" : "OFF")}");
            y += UiLineHeight;

            if (_hitDummy != null)
            {
                var hpRatio = _hitDummy.MaxHp > 0f ? _hitDummy.CurrentHp / _hitDummy.MaxHp : 0f;
                var sinceHit = _hitDummy.LastHitTime > 0f ? Time.time - _hitDummy.LastHitTime : -1f;
                var sinceText = sinceHit < 0f ? "-" : $"{sinceHit:0.0}s ago";

                GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight),
                    $"DummyHP: {_hitDummy.CurrentHp:0.0}/{_hitDummy.MaxHp:0.0} ({hpRatio * 100f:0}%)");
                y += UiLineHeight;
                GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight),
                    $"DummyHits: {_hitDummy.HitCount}  TotalDmg: {_hitDummy.TotalDamageTaken:0.0}");
                y += UiLineHeight;
                GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight),
                    $"DummyLast: {_hitDummy.LastDamageSource} {_hitDummy.LastDamageAmount:0.0} ({sinceText})");
                y += UiLineHeight;
            }
            else
            {
                GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight), "Dummy: None");
                y += UiLineHeight;
            }

            GUI.Label(new Rect(x, y, UiPanelWidth - 24f, UiLineHeight * 2f), $"Status: {_statusLine}");
        }
    }
}
