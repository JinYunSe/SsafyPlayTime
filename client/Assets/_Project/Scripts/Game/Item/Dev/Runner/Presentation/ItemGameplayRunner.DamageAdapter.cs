/*
 * 파일 개요:
 * - ItemGameplayRunner.DamageAdapter 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Presentation 계층에서 오디오, 시각화, 데미지 표시 같은 테스트 연출 연결을 담당한다.
 * - 실제 스킬 판정과 분리된 표현 계층이므로, 연출 변경이 게임 규칙 변경으로 이어지지 않게 유지한다.
 */
using System;
using System.Reflection;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private bool TryApplyDamageToTarget(Transform hitTransform, float damage, float stunDamage, string source)
        {
            if (hitTransform == null)
            {
                return false;
            }

            var components = hitTransform.GetComponentsInParent<MonoBehaviour>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                var applyDamageMethod = GetApplyDamageMethod(component.GetType());
                if (applyDamageMethod == null)
                {
                    continue;
                }

                try
                {
                    applyDamageMethod.Invoke(component, new object[] { damage, stunDamage, source });
                    return true;
                }
                catch (Exception ex)
                {
                    LogStatus($"ApplyDamage invoke failed: {component.GetType().Name} - {ex.Message}");
                }
            }

            return false;
        }

        private bool HasDamageReceiverComponent(Transform root)
        {
            if (root == null)
            {
                return false;
            }

            var components = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (GetApplyDamageMethod(component.GetType()) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private MethodInfo GetApplyDamageMethod(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (_damageApplyMethodCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            // 오버로드/숨김 메서드 충돌을 피하기 위해 선언 타입 단위로 시그니처를 직접 필터링한다.
            var method = FindApplyDamageMethodBySignature(type);
            if (method == null)
            {
                _damageApplyMethodCache[type] = null;
                return null;
            }

            _damageApplyMethodCache[type] = method;
            return method;
        }

        private static MethodInfo FindApplyDamageMethodBySignature(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var methods = current.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                for (var i = 0; i < methods.Length; i++)
                {
                    var method = methods[i];
                    if (method == null ||
                        !string.Equals(method.Name, "ApplyDamage", StringComparison.Ordinal) ||
                        method.IsGenericMethod)
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length == 3 &&
                        parameters[0].ParameterType == typeof(float) &&
                        parameters[1].ParameterType == typeof(float) &&
                        parameters[2].ParameterType == typeof(string))
                    {
                        return method;
                    }
                }
            }

            return null;
        }
    }
}

