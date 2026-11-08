using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows.Data;
using System.Windows.Markup;

namespace A2G.SavConverter.Localization
{
    /// <summary>
    /// XAML markup extension: <c>Text="{loc:LocalizedText 10001}"</c>. Follows the approach of the A2G-Setup project (numbered text
    /// keys in tab separated text files, live language switching) but keeps only weak references to the live instances.
    /// Code behind uses <see cref="Loc.T(int, object[])"/>.
    /// </summary>
    public class LocalizedText : MarkupExtension, INotifyPropertyChanged
    {
        private static readonly List<WeakReference<LocalizedText>> Live = new List<WeakReference<LocalizedText>>();
        private string _value;

        public LocalizedText() { }
        public LocalizedText(string nr) { Nr = nr; }

        [ConstructorArgument("nr")]
        public string Nr { get; set; }

        /// <summary>Optional composite format; {0} is the localized text (e.g. "{0}:").</summary>
        public string Format { get; set; }

        public string Value
        {
            get { return _value; }
            private set { if (_value != value) { _value = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Value")); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            Refresh();
            lock (Live)
            {
                Live.RemoveAll(w => { LocalizedText t; return !w.TryGetTarget(out t); });
                Live.Add(new WeakReference<LocalizedText>(this));
            }
            return new Binding("Value") { Source = this, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
        }

        private void Refresh()
        {
            var text = Loc.Get(Nr);
            Value = string.IsNullOrEmpty(Format) ? text : string.Format(Format, text);
        }

        internal static void RefreshAll()
        {
            List<LocalizedText> items;
            lock (Live)
            {
                items = new List<LocalizedText>();
                foreach (var w in Live) { LocalizedText t; if (w.TryGetTarget(out t)) items.Add(t); }
            }
            foreach (var t in items) t.Refresh();
        }
    }

    /// <summary>One selectable UI language, found among the embedded Localization\*.txt files.</summary>
    public sealed class LanguageInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>
    /// Language state and text lookup. A language is any embedded file Localization\&lt;code&gt;.txt (tab separated "key TAB text");
    /// its key 0 is the language's own display name. Adding a language = adding such a file.
    /// </summary>
    public static class Loc
    {
        private const string Fallback = "en";
        private static readonly string PrefPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2G-SAV-Converter", "language.txt");
        private static readonly Dictionary<string, Dictionary<string, string>> Cache = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static List<LanguageInfo> _available;
        private static string _code = Fallback;

        public static event Action LanguageChanged;

        public static string CurrentCode { get { return _code; } }

        /// <summary>All languages shipped with the application (sorted by name).</summary>
        public static IList<LanguageInfo> Available
        {
            get
            {
                if (_available == null)
                {
                    var codes = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                        .Select(n => Regex.Match(n, @"Localization\.([A-Za-z\-]+)\.txt$", RegexOptions.IgnoreCase))
                        .Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase);
                    _available = codes.Select(c => new LanguageInfo { Code = c.ToLowerInvariant(), Name = Table(c).ContainsKey("0") ? Table(c)["0"] : c })
                        .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                }
                return _available;
            }
        }

        /// <summary>Saved choice, else the Windows display language if available, else English.</summary>
        public static void Initialize()
        {
            string saved = null;
            try { if (File.Exists(PrefPath)) saved = File.ReadAllText(PrefPath).Trim(); } catch { }
            var wanted = saved ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var match = Available.FirstOrDefault(l => string.Equals(l.Code, wanted, StringComparison.OrdinalIgnoreCase))
                        ?? Available.FirstOrDefault(l => l.Code == Fallback) ?? Available.FirstOrDefault();
            _code = match != null ? match.Code : Fallback;
        }

        public static void SetLanguage(string code)
        {
            if (string.IsNullOrEmpty(code) || string.Equals(code, _code, StringComparison.OrdinalIgnoreCase)) return;
            _code = code.ToLowerInvariant();
            try { Directory.CreateDirectory(Path.GetDirectoryName(PrefPath)); File.WriteAllText(PrefPath, _code); } catch { }
            LocalizedText.RefreshAll();
            if (LanguageChanged != null) LanguageChanged();
        }

        public static string T(int nr, params object[] args)
        {
            var text = Get(nr.ToString(CultureInfo.InvariantCulture));
            return args == null || args.Length == 0 ? text : string.Format(text, args);
        }

        public static string Get(string nr)
        {
            string value;
            var key = nr ?? string.Empty;
            if (Table(_code).TryGetValue(key, out value)) return value;
            if (Table(Fallback).TryGetValue(key, out value)) return value;
            return "[" + key + "]";
        }

        private static Dictionary<string, string> Table(string code)
        {
            lock (Cache)
            {
                Dictionary<string, string> table;
                if (!Cache.TryGetValue(code, out table)) Cache[code] = table = Load(code);
                return table;
            }
        }

        private static Dictionary<string, string> Load(string code)
        {
            var result = new Dictionary<string, string>();
            var assembly = Assembly.GetExecutingAssembly();
            var resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Localization." + code + ".txt", StringComparison.OrdinalIgnoreCase));
            if (resource == null) return result;
            using (var reader = new StreamReader(assembly.GetManifestResourceStream(resource), Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                    var tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    result[line.Substring(0, tab).Trim()] = line.Substring(tab + 1).Replace("\r\n", "\n").Replace("\\n", "\n").Trim();
                }
            }
            return result;
        }
    }
}
