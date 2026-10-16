#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PushizoSdk.Internal
{
    /// <summary>
    /// Küçük JSON okuyucu/yazıcı (Unity'de System.Text.Json yok, JsonUtility sözlük desteklemez). Okuma sonucu:
    /// nesne → <c>Dictionary&lt;string, object?&gt;</c>, dizi → <c>List&lt;object?&gt;</c>, metin → string,
    /// tam sayı → long, ondalık → double, bool, null.
    /// </summary>
    internal static class Json
    {
        private const int MaxDepth = 64;

        /// <summary>JSON metnini ayrıştırır; geçersizse <see cref="FormatException"/>.</summary>
        public static object? Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var reader = new Reader(json);
            reader.SkipWhitespace();
            var value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw new FormatException("JSON sonunda beklenmeyen karakter.");
            return value;
        }

        public static Dictionary<string, object?> ParseObject(string json) =>
            Parse(json) as Dictionary<string, object?> ?? throw new FormatException("JSON nesnesi bekleniyordu.");

        /// <summary>Değeri JSON metnine yazar (sözlük, liste, metin, sayı, bool, null).</summary>
        public static string Serialize(object? value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object? value, int depth)
        {
            if (depth > MaxDepth) throw new ArgumentException("JSON en fazla 64 seviye iç içe olabilir.");
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("JSON sayıları sonlu olmalı.");
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object?> map:
                {
                    sb.Append('{');
                    var first = true;
                    foreach (var pair in map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        Write(sb, pair.Value, depth + 1);
                    }
                    sb.Append('}');
                    break;
                }
                case IDictionary<string, string> strings:
                {
                    sb.Append('{');
                    var first = true;
                    foreach (var pair in strings)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        WriteString(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    var first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item, depth + 1);
                    }
                    sb.Append(']');
                    break;
                }
                default:
                    throw new ArgumentException($"JSON'a yazılamayan tür: {value.GetType().FullName}.");
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s) => _s = s;

            public bool AtEnd => _i >= _s.Length;

            public void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            private char Peek() => _i < _s.Length ? _s[_i] : throw new FormatException("JSON beklenmedik şekilde bitti.");

            private void Expect(char c)
            {
                if (Peek() != c) throw new FormatException($"JSON'da '{c}' bekleniyordu (konum {_i}).");
                _i++;
            }

            public object? ReadValue(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("JSON çok derin.");
                switch (Peek())
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default: return ReadNumber();
                }
            }

            private Dictionary<string, object?> ReadObject(int depth)
            {
                Expect('{');
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                SkipWhitespace();
                if (Peek() == '}')
                {
                    _i++;
                    return map;
                }
                while (true)
                {
                    SkipWhitespace();
                    var key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    map[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        _i++;
                        continue;
                    }
                    Expect('}');
                    return map;
                }
            }

            private List<object?> ReadArray(int depth)
            {
                Expect('[');
                var list = new List<object?>();
                SkipWhitespace();
                if (Peek() == ']')
                {
                    _i++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Peek() == ',')
                    {
                        _i++;
                        continue;
                    }
                    Expect(']');
                    return list;
                }
            }

            private string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    var c = Peek();
                    _i++;
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    var e = Peek();
                    _i++;
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw new FormatException("Geçersiz \\u kaçışı.");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw new FormatException($"Geçersiz kaçış karakteri: \\{e}.");
                    }
                }
            }

            private void ReadLiteral(string literal)
            {
                if (string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0) throw new FormatException($"JSON'da '{literal}' bekleniyordu.");
                _i += literal.Length;
            }

            private object ReadNumber()
            {
                var start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                var text = _s.Substring(start, _i - start);
                if (text.Length == 0) throw new FormatException($"JSON'da beklenmeyen karakter (konum {start}).");
                if (text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return l;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                throw new FormatException($"Geçersiz JSON sayısı: {text}.");
            }
        }
    }
}
