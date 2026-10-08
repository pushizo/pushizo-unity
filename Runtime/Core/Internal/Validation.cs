using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// Girdi doğrulama ve normalizasyon; kurallar yerel SDK'lar ve sunucu (<c>packages/shared</c> messaging,
    /// <c>apps/api</c> olay şeması) ile aynıdır. Ortak test vektörleri: <c>sdk/testdata/messaging-vectors.json</c>.
    /// </summary>
    internal static class Validation
    {
        public const int MaxAliasesPerRequest = 20;

        private static readonly Regex EmailRegex = new Regex(
            @"^[^\s@""(),:;<>\[\\\]]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,62}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,62}[A-Za-z0-9])?)+$",
            RegexOptions.CultureInvariant);

        private static readonly Regex E164Regex = new Regex(@"^\+[1-9][0-9]{6,14}$", RegexOptions.CultureInvariant);
        private static readonly Regex PhoneSeparatorsRegex = new Regex(@"[\s().-]", RegexOptions.CultureInvariant);
        private static readonly Regex AliasLabelRegex = new Regex("^[a-z0-9_]{1,50}$", RegexOptions.CultureInvariant);
        private static readonly Regex EventNameRegex = new Regex("^[a-zA-Z0-9_.:-]{1,100}$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> ReservedAliases = new HashSet<string>(StringComparer.Ordinal) { "external_id", "hs_id" };

        /// <summary>Boşluk kırpma + küçük harf (kültürden bağımsız); geçersizse null.</summary>
        public static string NormalizeEmail(string value)
        {
            if (value == null) return null;
            var v = value.Trim().ToLowerInvariant();
            if (v.Length > 320 || !EmailRegex.IsMatch(v)) return null;
            return v;
        }

        /// <summary>E.164: boşluk, tire, parantez ve nokta atılır, <c>00</c> öneki <c>+</c> olur; geçersizse null.</summary>
        public static string NormalizePhone(string value)
        {
            if (value == null) return null;
            var v = PhoneSeparatorsRegex.Replace(value.Trim(), string.Empty);
            if (v.StartsWith("00", StringComparison.Ordinal)) v = "+" + v.Substring(2);
            return E164Regex.IsMatch(v) ? v : null;
        }

        public static string RequireEmail(string email, string paramName) =>
            NormalizeEmail(email) ?? throw new ArgumentException("Geçersiz e-posta adresi.", paramName);

        public static string RequirePhone(string phoneNumber, string paramName) =>
            NormalizePhone(phoneNumber) ?? throw new ArgumentException("Geçersiz telefon numarası (E.164 bekleniyor, ör. +905321234567).", paramName);

        /// <summary>Alias etiketi geçersiz veya ayrılmışsa (external_id → Login/Logout, hs_id → sistem) hata fırlatır.</summary>
        public static void RequireAliasLabel(string label, string paramName)
        {
            if (label == null) throw new ArgumentNullException(paramName);
            if (!AliasLabelRegex.IsMatch(label))
                throw new ArgumentException($"Geçersiz alias etiketi \"{label}\": küçük harf, rakam ve alt çizgi (en fazla 50).", paramName);
            if (ReservedAliases.Contains(label))
                throw new ArgumentException($"\"{label}\" alias olarak ayarlanamaz (external_id için Login/Logout kullanın).", paramName);
        }

        /// <summary>Alias haritasını doğrular ve değerleri kırpar (1-20 alias, değer 1-255 karakter).</summary>
        public static IReadOnlyDictionary<string, string> NormalizeAliases(IReadOnlyDictionary<string, string> aliases, string paramName)
        {
            if (aliases == null) throw new ArgumentNullException(paramName);
            if (aliases.Count == 0) throw new ArgumentException("En az bir alias gerekli.", paramName);
            if (aliases.Count > MaxAliasesPerRequest)
                throw new ArgumentException($"Tek seferde en fazla {MaxAliasesPerRequest} alias eklenebilir.", paramName);
            var result = new Dictionary<string, string>(aliases.Count, StringComparer.Ordinal);
            foreach (var pair in aliases)
            {
                RequireAliasLabel(pair.Key, paramName);
                var value = (pair.Value ?? string.Empty).Trim();
                if (value.Length < 1 || value.Length > 255)
                    throw new ArgumentException($"\"{pair.Key}\" alias değeri 1-255 karakter olmalı.", paramName);
                result[pair.Key] = value;
            }
            return result;
        }

        /// <summary>Tag haritası: anahtar boş olamaz, değer null olamaz (silmek için RemoveTag).</summary>
        public static IReadOnlyDictionary<string, string> RequireTags(IReadOnlyDictionary<string, string> tags, string paramName)
        {
            if (tags == null) throw new ArgumentNullException(paramName);
            var result = new Dictionary<string, string>(tags.Count, StringComparer.Ordinal);
            foreach (var pair in tags)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) throw new ArgumentException("Tag anahtarı boş olamaz.", paramName);
                result[pair.Key] = pair.Value ?? throw new ArgumentException($"\"{pair.Key}\" tag değeri null olamaz (silmek için RemoveTag kullanın).", paramName);
            }
            return result;
        }

        /// <summary>In-app tetikleri: anahtar 1-64, değer en fazla 200 karakter.</summary>
        public static IReadOnlyDictionary<string, string> RequireTriggers(IReadOnlyDictionary<string, string> triggers, string paramName)
        {
            if (triggers == null) throw new ArgumentNullException(paramName);
            var result = new Dictionary<string, string>(triggers.Count, StringComparer.Ordinal);
            foreach (var pair in triggers)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 64) throw new ArgumentException("Tetik anahtarı 1-64 karakter olmalı.", paramName);
                if (pair.Value == null || pair.Value.Length > 200) throw new ArgumentException("Tetik değeri en fazla 200 karakter olabilir.", paramName);
                result[pair.Key] = pair.Value;
            }
            return result;
        }

        /// <summary>Anahtar listesi: null/boş öğe içeremez.</summary>
        public static IReadOnlyList<string> RequireKeys(IEnumerable<string> keys, string paramName)
        {
            if (keys == null) throw new ArgumentNullException(paramName);
            var list = keys.ToList();
            if (list.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Anahtarlar boş olamaz.", paramName);
            return list;
        }

        /// <summary>Olay adı: kırpılır; 1-100 karakter, harf/rakam/<c>_ . : -</c> (sunucu <c>eventItemSchema.name</c>).</summary>
        public static string RequireEventName(string name, string paramName)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Olay adı gerekli.", paramName);
            var trimmed = name.Trim();
            if (!EventNameRegex.IsMatch(trimmed))
                throw new ArgumentException("Olay adı 1-100 karakter olmalı; harf, rakam, _ . : - içerebilir.", paramName);
            return trimmed;
        }

        public static void RequireInboxLimit(int limit, string paramName)
        {
            if (limit < 1 || limit > 200) throw new ArgumentOutOfRangeException(paramName, limit, "limit 1-200 olmalı.");
        }

        /// <summary>API adresi http(s) ile başlayan mutlak adres olmalı.</summary>
        public static void RequireApiUrl(string apiUrl, string paramName)
        {
            if (string.IsNullOrWhiteSpace(apiUrl) || !Uri.TryCreate(apiUrl, UriKind.Absolute, out var api) ||
                (api.Scheme != Uri.UriSchemeHttps && api.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("ApiUrl http(s) ile başlayan mutlak bir adres olmalı.", paramName);
        }
    }
}
