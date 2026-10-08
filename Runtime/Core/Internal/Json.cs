using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// Köprü JSON'u için bağımlılıksız okuyucu/yazıcı (Unity'de System.Text.Json yoktur, JsonUtility sözlük desteklemez).
    /// Okuma sonucu: <c>Dictionary&lt;string, object&gt;</c>, <c>List&lt;object&gt;</c>, string, long, double, bool, null.
    /// </summary>
    internal static class Json
    {
        private const int MaxDepth = 64;

        /// <summary>JSON metnini ayrıştırır; biçim hatasında <see cref="FormatException"/>.</summary>
        public static object Parse(string json)
        {
            if (json == null) throw new FormatException("JSON metni boş.");
            var reader = new Reader(json);
            reader.SkipWhitespace();
            var value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw new FormatException($"JSON sonunda beklenmeyen karakter (konum {reader.Position}).");
            return value;
        }

        /// <summary>
        /// Değeri JSON'a yazar: string, bool, tam sayılar, float/double/decimal, null, IDictionary (anahtar metin), IEnumerable.
        /// NaN/Infinity desteklenmez.
        /// </summary>
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, int depth)
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
                case char c:
                    WriteString(sb, c.ToString());
                    break;
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case float f:
                    WriteDouble(sb, f);
                    break;
                case double d:
                    WriteDouble(sb, d);
                    break;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    break;
                case IDictionary dict:
                {
                    sb.Append('{');
                    var first = true;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (!(entry.Key is string key)) throw new ArgumentException("JSON nesne anahtarları metin olmalı.");
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, key);
                        sb.Append(':');
                        Write(sb, entry.Value, depth + 1);
                    }
                    sb.Append('}');
                    break;
                }
                case IEnumerable<KeyValuePair<string, string>> pairs:
                {
                    sb.Append('{');
                    var first = true;
                    foreach (var pair in pairs)
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

        private static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("JSON sayıları sonlu olmalı.");
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
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
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
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
            public int Position => _i;

            public void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("JSON en fazla 64 seviye iç içe olabilir.");
                if (AtEnd) throw new FormatException("JSON beklenmedik biçimde bitti.");
                var c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw new FormatException($"JSON'da beklenmeyen karakter '{c}' (konum {_i}).");
                }
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;
                SkipWhitespace();
                if (Peek('}')) { _i++; return map; }
                while (true)
                {
                    SkipWhitespace();
                    if (!Peek('"')) throw new FormatException($"JSON nesne anahtarı bekleniyordu (konum {_i}).");
                    var key = ReadString();
                    SkipWhitespace();
                    if (!Peek(':')) throw new FormatException($"':' bekleniyordu (konum {_i}).");
                    _i++;
                    SkipWhitespace();
                    map[key] = ReadValue(depth + 1);
                    SkipWhitespace();
                    if (Peek(',')) { _i++; continue; }
                    if (Peek('}')) { _i++; return map; }
                    throw new FormatException($"',' veya '}}' bekleniyordu (konum {_i}).");
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                _i++;
                SkipWhitespace();
                if (Peek(']')) { _i++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Peek(',')) { _i++; continue; }
                    if (Peek(']')) { _i++; return list; }
                    throw new FormatException($"',' veya ']' bekleniyordu (konum {_i}).");
                }
            }

            private string ReadString()
            {
                _i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw new FormatException("JSON metni kapatılmamış.");
                    var c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (AtEnd) throw new FormatException("JSON kaçış dizisi eksik.");
                        var e = _s[_i++];
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
                                if (_i + 4 > _s.Length) throw new FormatException("JSON \\u kaçışı eksik.");
                                if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                    throw new FormatException("JSON \\u kaçışı geçersiz.");
                                sb.Append((char)code);
                                _i += 4;
                                break;
                            default:
                                throw new FormatException($"JSON kaçış dizisi geçersiz: \\{e}.");
                        }
                    }
                    else if (c < 0x20)
                    {
                        throw new FormatException("JSON metninde denetim karakteri.");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            private object ReadNumber()
            {
                var start = _i;
                if (Peek('-')) _i++;
                while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                var isFloat = false;
                if (Peek('.'))
                {
                    isFloat = true;
                    _i++;
                    while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                }
                if (Peek('e') || Peek('E'))
                {
                    isFloat = true;
                    _i++;
                    if (Peek('+') || Peek('-')) _i++;
                    while (!AtEnd && char.IsDigit(_s[_i])) _i++;
                }
                var text = _s.Substring(start, _i - start);
                if (!isFloat && long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return l;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                throw new FormatException($"JSON sayısı geçersiz: {text}.");
            }

            private bool Peek(char c) => _i < _s.Length && _s[_i] == c;

            private void Expect(string literal)
            {
                if (string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                    throw new FormatException($"JSON'da '{literal}' bekleniyordu (konum {_i}).");
                _i += literal.Length;
            }
        }
    }
}
