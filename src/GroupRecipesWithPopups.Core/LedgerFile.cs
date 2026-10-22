using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>
    /// Append-only persistence for a <see cref="Ledger"/>. Existing lines are never rewritten or removed;
    /// station level increases are stored as additional lines and resolved by the ledger on load.
    /// </summary>
    public sealed class LedgerFile
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private bool _readOnly;

        public LedgerFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Path must not be empty.", nameof(path));
            }

            Path = path;
        }

        public string Path { get; }

        /// <summary>True after loading a file with an unknown version; such a file is never modified.</summary>
        public bool IsReadOnly => _readOnly;

        /// <summary>Loads the file (if it exists) into the ledger and returns the parse result.</summary>
        public LedgerParseResult Load(Ledger ledger)
        {
            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            if (!File.Exists(Path))
            {
                return new LedgerParseResult();
            }

            LedgerParseResult result = LedgerFileFormat.Parse(File.ReadAllLines(Path, Encoding.UTF8));
            if (!result.IsSupported)
            {
                _readOnly = true;
                return result;
            }

            ledger.AddRange(result.Entries);
            return result;
        }

        /// <summary>Appends the entries; returns false when nothing was written.</summary>
        public bool Append(IEnumerable<KnowledgeEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            if (_readOnly)
            {
                return false;
            }

            var sb = new StringBuilder();
            int count = 0;
            foreach (KnowledgeEntry entry in entries)
            {
                if (entry.IsEmpty || entry.Key.Length == 0)
                {
                    continue;
                }

                sb.Append(LedgerFileFormat.FormatLine(entry)).Append('\n');
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                string prefix = string.Empty;
                if (stream.Length == 0)
                {
                    prefix = LedgerFileFormat.Header + "\n";
                }
                else
                {
                    stream.Seek(-1, SeekOrigin.End);
                    if (stream.ReadByte() != '\n')
                    {
                        prefix = "\n";
                    }
                }

                stream.Seek(0, SeekOrigin.End);
                byte[] bytes = Utf8NoBom.GetBytes(prefix + sb);
                stream.Write(bytes, 0, bytes.Length);
            }

            return true;
        }
    }
}
