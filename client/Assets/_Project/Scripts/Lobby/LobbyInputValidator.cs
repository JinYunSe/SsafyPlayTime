using System.Linq;

namespace SSAFYPlayTime
{
    internal static class LobbyInputValidator
    {
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

        internal static bool IsWithinNameLengthLimit(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var maxLength = ContainsHangul(value) ? 8 : 16;
            return value.Length <= maxLength;
        }

        internal static bool ContainsHangul(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if ((c >= '\u1100' && c <= '\u11FF') ||
                    (c >= '\u3130' && c <= '\u318F') ||
                    (c >= '\uAC00' && c <= '\uD7AF'))
                {
                    return true;
                }
            }

            return false;
        }

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

        internal static bool IsNumericPasswordChar(char c)
        {
            return c >= '0' && c <= '9';
        }

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
