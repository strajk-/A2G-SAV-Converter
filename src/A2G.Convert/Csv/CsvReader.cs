using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace A2G.Convert.Csv
{
    /// <summary>Streaming RFC 4180 CSV reader (quoted fields, doubled quotes, line breaks inside quotes).</summary>
    public static class CsvReader
    {
        /// <summary>Yields every record, including the header, as an array of fields.</summary>
        public static IEnumerable<string[]> ReadRecords(string path)
        {
            using (var reader = new StreamReader(path, new UTF8Encoding(false), true, 1 << 16))
            {
                var fields = new List<string>();
                var field = new StringBuilder();
                var inQuotes = false;
                var any = false;
                int c;
                while ((c = reader.Read()) >= 0)
                {
                    var ch = (char)c;
                    if (inQuotes)
                    {
                        if (ch == '"')
                        {
                            if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
                            else inQuotes = false;
                        }
                        else field.Append(ch);
                        continue;
                    }

                    if (ch == '"') { inQuotes = true; any = true; }
                    else if (ch == ',') { fields.Add(field.ToString()); field.Length = 0; any = true; }
                    else if (ch == '\r') { /* ignore, handled with \n */ }
                    else if (ch == '\n')
                    {
                        if (any || field.Length > 0)
                        {
                            fields.Add(field.ToString());
                            yield return fields.ToArray();
                        }
                        fields.Clear();
                        field.Length = 0;
                        any = false;
                    }
                    else { field.Append(ch); any = true; }
                }
                if (any || field.Length > 0)
                {
                    fields.Add(field.ToString());
                    yield return fields.ToArray();
                }
            }
        }
    }

    /// <summary>A header row plus a name-to-index lookup, for reading selected columns by name.</summary>
    public sealed class CsvHeader
    {
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

        public CsvHeader(string[] names)
        {
            Names = names;
            for (var i = 0; i < names.Length; i++) _index[names[i]] = i;
        }

        public string[] Names { get; private set; }

        public int IndexOf(string name)
        {
            int i;
            if (!_index.TryGetValue(name, out i)) throw new KeyNotFoundException("CSV column not found: " + name);
            return i;
        }

        public bool Has(string name)
        {
            return _index.ContainsKey(name);
        }

        public static string Get(string[] row, int index)
        {
            return index < row.Length ? row[index] : string.Empty;
        }

        public static int GetInt(string[] row, int index, int fallback = 0)
        {
            int v;
            return index < row.Length && int.TryParse(row[index], System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }
}
