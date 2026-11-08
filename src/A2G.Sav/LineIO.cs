using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace A2G.Sav
{
    /// <summary>
    /// Anstoss 2 .sav files are plain text, one field per line, every line terminated by CRLF and
    /// encoded as Latin-1 (single byte per character, e.g. 0xFC = u-umlaut). Latin-1 maps every byte
    /// to exactly one char, so decode/encode is lossless and files round-trip byte for byte.
    /// </summary>
    public static class SavText
    {
        public static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

        /// <summary>Splits file bytes into lines. Every line must end with CRLF; an empty file has no lines.</summary>
        public static List<string> ToLines(byte[] data, string fileName)
        {
            var lines = new List<string>();
            if (data.Length == 0) return lines;

            var text = Latin1.GetString(data);
            var start = 0;
            while (start < text.Length)
            {
                var end = text.IndexOf("\r\n", start, StringComparison.Ordinal);
                if (end < 0)
                    throw new FormatException(fileName + ": last line is not terminated by CRLF.");
                var line = text.Substring(start, end - start);
                if (line.IndexOf('\r') >= 0 || line.IndexOf('\n') >= 0)
                    throw new FormatException(fileName + ": line " + (lines.Count + 1) + " contains a stray CR or LF.");
                lines.Add(line);
                start = end + 2;
            }
            return lines;
        }

        public static byte[] ToBytes(IList<string> lines)
        {
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                if (line == null) throw new InvalidOperationException("Null line.");
                if (line.IndexOf('\r') >= 0 || line.IndexOf('\n') >= 0)
                    throw new InvalidOperationException("A field must not contain line breaks: " + line);
                sb.Append(line).Append("\r\n");
            }
            return Latin1.GetBytes(sb.ToString());
        }

        /// <summary>True if the token is an integer in canonical form (so it re-serialises to the same text).</summary>
        public static bool IsInt(string token)
        {
            int value;
            return int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
                   && value.ToString(CultureInfo.InvariantCulture) == token;
        }

        public static string Format(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Sequential reader over the lines of one file that reports the file and line on errors.</summary>
    internal sealed class LineReader
    {
        private readonly IList<string> _lines;
        private readonly string _file;

        public LineReader(IList<string> lines, string file)
        {
            _lines = lines;
            _file = file;
            Position = 0;
        }

        public int Position { get; private set; }
        public int Remaining { get { return _lines.Count - Position; } }
        public bool AtEnd { get { return Position >= _lines.Count; } }

        /// <summary>Whether the line <paramref name="offset"/> lines ahead exists and is an integer.</summary>
        public bool IntAhead(int offset)
        {
            var i = Position + offset;
            return i < _lines.Count && SavText.IsInt(_lines[i]);
        }

        /// <summary>Whether the line <paramref name="offset"/> lines ahead exists and is not an integer.</summary>
        public bool StringAhead(int offset)
        {
            var i = Position + offset;
            return i < _lines.Count && !SavText.IsInt(_lines[i]);
        }

        public string ReadString()
        {
            if (AtEnd) throw Error("unexpected end of file");
            return _lines[Position++];
        }

        public int ReadInt()
        {
            var text = ReadString();
            if (!SavText.IsInt(text))
            {
                Position--;
                throw Error("expected an integer but found '" + text + "'");
            }
            return int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        public int[] ReadInts(int count)
        {
            var values = new int[count];
            for (var i = 0; i < count; i++) values[i] = ReadInt();
            return values;
        }

        public FormatException Error(string message)
        {
            return new FormatException(_file + ", line " + (Position + 1) + ": " + message);
        }
    }
}
