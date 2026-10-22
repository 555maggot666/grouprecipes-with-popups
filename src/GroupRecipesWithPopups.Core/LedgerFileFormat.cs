using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>Result of parsing a ledger file.</summary>
    public sealed class LedgerParseResult
    {
        public List<KnowledgeEntry> Entries { get; } = new List<KnowledgeEntry>();

        public List<string> Warnings { get; } = new List<string>();

        /// <summary>False when the file declares a format version this build does not understand.</summary>
        public bool IsSupported { get; set; } = true;
    }

    /// <summary>
    /// Text format of the persistent ledger: a version header followed by one tab separated line per entry
    /// (<c>Kind TAB Key TAB Level</c>). Backslash, tab, LF and CR inside keys are escaped.
    /// </summary>
    public static class LedgerFileFormat
    {
        public const int Version = 1;

        public const string HeaderPrefix = "#grwp-ledger";

        public static readonly string Header = HeaderPrefix + " v" + Version.ToString(CultureInfo.InvariantCulture);

        public static string Escape(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            StringBuilder sb = null;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                string replacement = c switch
                {
                    '\\' => "\\\\",
                    '\t' => "\\t",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    _ => null,
                };

                if (replacement == null)
                {
                    sb?.Append(c);
                    continue;
                }

                if (sb == null)
                {
                    sb = new StringBuilder(value.Length + 8);
                    sb.Append(value, 0, i);
                }

                sb.Append(replacement);
            }

            return sb == null ? value : sb.ToString();
        }

        public static bool TryUnescape(string value, out string result)
        {
            result = null;
            if (value == null)
            {
                return false;
            }

            if (value.IndexOf('\\') < 0)
            {
                result = value;
                return true;
            }

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (i + 1 >= value.Length)
                {
                    return false;
                }

                i++;
                switch (value[i])
                {
                    case '\\': sb.Append('\\'); break;
                    case 't': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    default: return false;
                }
            }

            result = sb.ToString();
            return true;
        }

        public static string FormatLine(KnowledgeEntry entry)
        {
            if (entry.IsEmpty)
            {
                throw new ArgumentException("Cannot format an empty entry.", nameof(entry));
            }

            return KindToText(entry.Kind) + "\t" + Escape(entry.Key) + "\t" +
                   entry.Level.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseLine(string line, out KnowledgeEntry entry, out string error)
        {
            entry = default;
            error = null;
            if (line == null)
            {
                error = "line is null";
                return false;
            }

            if (line.EndsWith("\r", StringComparison.Ordinal))
            {
                line = line.Substring(0, line.Length - 1);
            }

            string[] parts = line.Split('\t');
            if (parts.Length != 3)
            {
                error = "expected 3 tab separated fields but found " + parts.Length.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (!TryParseKind(parts[0], out KnowledgeKind kind))
            {
                error = "unknown kind '" + parts[0] + "'";
                return false;
            }

            if (!TryUnescape(parts[1], out string key))
            {
                error = "invalid escape sequence in key";
                return false;
            }

            if (key.Length == 0)
            {
                error = "empty key";
                return false;
            }

            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int level))
            {
                error = "invalid level '" + parts[2] + "'";
                return false;
            }

            entry = new KnowledgeEntry(kind, key, level);
            return true;
        }

        /// <summary>
        /// Parses a whole file. Blank lines and <c>#</c> comments are skipped, broken lines are skipped with a
        /// warning, an unknown version header marks the result as unsupported.
        /// </summary>
        public static LedgerParseResult Parse(IEnumerable<string> lines)
        {
            if (lines == null)
            {
                throw new ArgumentNullException(nameof(lines));
            }

            var result = new LedgerParseResult();
            int lineNumber = 0;
            foreach (string rawLine in lines)
            {
                lineNumber++;
                string line = rawLine ?? string.Empty;
                if (line.Length > 0 && line[0] == '\uFEFF')
                {
                    line = line.Substring(1);
                }

                if (line.Trim().Length == 0)
                {
                    continue;
                }

                if (line[0] == '#')
                {
                    if (line.StartsWith(HeaderPrefix, StringComparison.Ordinal) && !IsSupportedHeader(line))
                    {
                        result.IsSupported = false;
                        result.Warnings.Add("line " + lineNumber.ToString(CultureInfo.InvariantCulture) +
                                            ": unsupported ledger header '" + line + "'");
                        return result;
                    }

                    continue;
                }

                if (TryParseLine(line, out KnowledgeEntry entry, out string error))
                {
                    result.Entries.Add(entry);
                }
                else
                {
                    result.Warnings.Add("line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ": " + error);
                }
            }

            return result;
        }

        private static bool IsSupportedHeader(string line)
        {
            return string.Equals(line.TrimEnd(), Header, StringComparison.Ordinal);
        }

        private static string KindToText(KnowledgeKind kind)
        {
            switch (kind)
            {
                case KnowledgeKind.Material: return "Material";
                case KnowledgeKind.Recipe: return "Recipe";
                case KnowledgeKind.Piece: return "Piece";
                case KnowledgeKind.Station: return "Station";
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown knowledge kind.");
            }
        }

        private static bool TryParseKind(string text, out KnowledgeKind kind)
        {
            switch (text)
            {
                case "Material": kind = KnowledgeKind.Material; return true;
                case "Recipe": kind = KnowledgeKind.Recipe; return true;
                case "Piece": kind = KnowledgeKind.Piece; return true;
                case "Station": kind = KnowledgeKind.Station; return true;
                default: kind = default; return false;
            }
        }
    }
}
