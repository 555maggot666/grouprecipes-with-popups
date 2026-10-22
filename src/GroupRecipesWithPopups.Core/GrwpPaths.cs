using System;
using System.Globalization;
using System.Text;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>File name helpers for the per-world ledger file.</summary>
    public static class GrwpPaths
    {
        private const int MaxComponentLength = 64;
        private const string FallbackName = "world";

        /// <summary>Replaces characters that are unsafe in file names and keeps the result short and non-empty.</summary>
        public static string SanitizeFileComponent(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return FallbackName;
            }

            var sb = new StringBuilder(name.Length);
            foreach (char c in name.Trim())
            {
                bool unsafeChar = c < 32 || c == 127 || c == '/' || c == '\\' || c == ':' || c == '*' ||
                                  c == '?' || c == '"' || c == '<' || c == '>' || c == '|' ||
                                  char.IsControl(c);
                sb.Append(unsafeChar ? '_' : c);
            }

            string result = sb.ToString().Trim().TrimEnd('.', ' ');
            while (result.StartsWith(".", StringComparison.Ordinal))
            {
                result = result.Substring(1);
            }

            if (result.Length > MaxComponentLength)
            {
                result = result.Substring(0, MaxComponentLength);
                if (result.Length > 0 && char.IsHighSurrogate(result[result.Length - 1]))
                {
                    result = result.Substring(0, result.Length - 1);
                }

                result = result.TrimEnd('.', ' ');
            }

            return result.Length == 0 ? FallbackName : result;
        }

        /// <summary>Builds <c>&lt;sanitized world&gt;_&lt;uid&gt;.txt</c>.</summary>
        public static string LedgerFileName(string worldName, long worldUid)
        {
            return SanitizeFileComponent(worldName) + "_" + worldUid.ToString(CultureInfo.InvariantCulture) + ".txt";
        }
    }
}
