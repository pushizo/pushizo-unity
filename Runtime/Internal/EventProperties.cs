#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace HeroSignalSdk.Internal
{
    /// <summary>
    /// <c>Session.SendEvent</c> özelliklerini JSON uyumlu kanonik ağaca çevirir: string, bool, long, double,
    /// iç içe sözlük, liste ve null. Desteklenmeyen tür (ör. GameObject) <see cref="ArgumentException"/> üretir.
    /// </summary>
    internal static class EventProperties
    {
        private const int MaxDepth = 16;

        public static Dictionary<string, object?> Normalize(IReadOnlyDictionary<string, object?>? properties, string paramName)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (properties is null) return result;
            foreach (var pair in properties)
            {
                if (string.IsNullOrEmpty(pair.Key)) throw new ArgumentException("Olay özelliği anahtarı boş olamaz.", paramName);
                result[pair.Key] = NormalizeValue(pair.Value, paramName, 1);
            }
            return result;
        }

        private static object? NormalizeValue(object? value, string paramName, int depth)
        {
            if (depth > MaxDepth) throw new ArgumentException("Olay özellikleri en fazla 16 seviye iç içe olabilir.", paramName);
            switch (value)
            {
                case null: return null;
                case string s: return s;
                case bool b: return b;
                case char c: return c.ToString();
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                case ulong ul: return ul <= long.MaxValue ? (object)(long)ul : (double)ul;
                case float f: return RequireFinite(f, paramName);
                case double d: return RequireFinite(d, paramName);
                case decimal m: return (double)m;
                case DateTimeOffset dto:
                    return dto.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                case DateTime dt:
                    return (dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime())
                        .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                case Guid g: return g.ToString();
                case Enum e: return e.ToString();
                case IDictionary dict:
                {
                    var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (!(entry.Key is string k) || k.Length == 0)
                            throw new ArgumentException("Olay özelliği sözlük anahtarları boş olmayan metin olmalı.", paramName);
                        map[k] = NormalizeValue(entry.Value, paramName, depth + 1);
                    }
                    return map;
                }
                case IEnumerable<KeyValuePair<string, object?>> pairs:
                {
                    var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var pair in pairs)
                    {
                        if (string.IsNullOrEmpty(pair.Key)) throw new ArgumentException("Olay özelliği sözlük anahtarları boş olamaz.", paramName);
                        map[pair.Key] = NormalizeValue(pair.Value, paramName, depth + 1);
                    }
                    return map;
                }
                case IEnumerable list:
                {
                    var items = new List<object?>();
                    foreach (var item in list) items.Add(NormalizeValue(item, paramName, depth + 1));
                    return items;
                }
                default:
                    throw new ArgumentException($"Desteklenmeyen olay özelliği türü: {value.GetType().FullName}.", paramName);
            }
        }

        private static double RequireFinite(double value, string paramName) =>
            !double.IsNaN(value) && !double.IsInfinity(value)
                ? value
                : throw new ArgumentException("Olay özelliği sayıları sonlu olmalı (NaN/Infinity desteklenmez).", paramName);
    }
}
