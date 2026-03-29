using System.Linq;

namespace SSAFYPlayTime
{
    // 닉네임·방 이름·비밀번호 입력값의 유효성을 검사하고 정제하는 유틸리티 클래스.
    // MonoBehaviour가 아닌 순수 static 클래스로 씬에 배치하지 않아도 된다.
    internal static class LobbyInputValidator
    {
        // 닉네임·방 이름에서 허용되지 않는 특수문자를 제거하고 앞뒤 공백을 제거한다.
        // 허용 문자: 영문자, 숫자, 공백, '.', '_', '-'
        internal static string SanitizeNameToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var chars = value
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '.' || c == '_' || c == '-')
                .ToArray();
            return new string(chars).Trim();
        }

        // 이름 길이가 허용 범위 이내인지 확인한다.
        // 한글 포함 시 최대 8자, 영문만 사용 시 최대 16자로 제한한다.
        internal static bool IsWithinNameLengthLimit(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var maxLength = ContainsHangul(value) ? 8 : 16;
            return value.Length <= maxLength;
        }

        // 문자열에 한글 문자(초성·중성·완성형)가 포함되어 있는지 확인한다.
        internal static bool ContainsHangul(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if ((c >= '\u1100' && c <= '\u11FF') ||  // 한글 자모
                    (c >= '\u3130' && c <= '\u318F') ||  // 호환 자모
                    (c >= '\uAC00' && c <= '\uD7AF'))    // 완성형 한글
                {
                    return true;
                }
            }

            return false;
        }

        // 비밀번호 문자열이 숫자로만 이루어져 있는지 확인한다.
        internal static bool IsNumericPassword(string value)
        {
            if (value == null)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (!IsNumericPasswordChar(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // 단일 문자가 숫자(0~9)인지 확인한다.
        internal static bool IsNumericPasswordChar(char c)
        {
            return c >= '0' && c <= '9';
        }

        // 비밀번호 문자열에서 숫자가 아닌 문자를 모두 제거한 결과를 반환한다.
        internal static string FilterNumericPassword(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var chars = value.Where(IsNumericPasswordChar).ToArray();
            return new string(chars);
        }
    }
}
