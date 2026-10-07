#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HeroSignalSdk.Internal
{
    /// <summary>
    /// Yerel köprülerin (Android <c>HeroSignalUnityBridge.java</c>, iOS <c>HeroSignalUnityBridge.swift</c>) ortak JSON
    /// biçimlerinin ayrıştırıcısı ve yazıcısı; .NET MAUI köprüsüyle aynı biçimlerdir. Zamanlar epoch milisaniyedir.
    /// </summary>
    internal static class BridgeJson
    {
        public static HeroSignalNotification ParseNotification(string json) => ReadNotification(Object(json));

        public static NotificationClickedEventArgs ParseNotificationClick(string json)
        {
            var root = Object(json);
            var notification = root.TryGetValue("notification", out var n) && n is Dictionary<string, object?> map
                ? ReadNotification(map)
                : EmptyNotification();
            return new NotificationClickedEventArgs(notification, Str(root, "actionId"), Str(root, "url"));
        }

        /// <summary><c>{"eventId":"…","notification":{…}}</c>.</summary>
        public static KeyValuePair<string, HeroSignalNotification> ParseForeground(string json)
        {
            var root = Object(json);
            var eventId = Str(root, "eventId") ?? throw Invalid("eventId");
            var notification = root.TryGetValue("notification", out var n) && n is Dictionary<string, object?> map
                ? ReadNotification(map)
                : EmptyNotification();
            return new KeyValuePair<string, HeroSignalNotification>(eventId, notification);
        }

        public static PushSubscriptionState ParseSubscriptionState(string json)
        {
            var root = Object(json);
            return new PushSubscriptionState(Str(root, "id"), Str(root, "token"), Bool(root, "optedIn"), Bool(root, "permission"));
        }

        /// <summary><c>{"permission":bool}</c> (yerel izin gözlemcisi olayı).</summary>
        public static bool ParsePermission(string json) => Bool(Object(json), "permission");

        public static InAppMessageClickedEventArgs ParseInAppClick(string json)
        {
            var root = Object(json);
            return new InAppMessageClickedEventArgs(Str(root, "messageId") ?? throw Invalid("messageId"), Str(root, "actionId") ?? string.Empty, Str(root, "url"));
        }

        /// <summary><c>{"event":"willDisplay|…","messageId","json"}</c>; bilinmeyen olay adı için olay null.</summary>
        public static KeyValuePair<string?, InAppMessage> ParseInAppLifecycle(string json)
        {
            var root = Object(json);
            var message = new InAppMessage(Str(root, "messageId") ?? throw Invalid("messageId"), Str(root, "json"));
            return new KeyValuePair<string?, InAppMessage>(Str(root, "event"), message);
        }

        public static InboxPage ParseInboxPage(string json)
        {
            var root = Object(json);
            var items = new List<InboxItem>();
            if (root.TryGetValue("items", out var arr) && arr is List<object?> list)
            {
                foreach (var element in list)
                {
                    if (element is Dictionary<string, object?> map) items.Add(ReadInboxItem(map));
                }
            }
            var unread = root.TryGetValue("unreadCount", out var u) && u is long count ? (int)count : items.Count(i => !i.Read);
            return new InboxPage(items, unread, Str(root, "nextCursor"), Bool(root, "hasMore"));
        }

        public static InboxItem ParseInboxItem(string json) => ReadInboxItem(Object(json));

        /// <summary><c>{"unreadCount":n}</c>.</summary>
        public static int ParseUnreadCount(string json)
        {
            var root = Object(json);
            return root.TryGetValue("unreadCount", out var u) && u is long count ? (int)count : throw Invalid("unreadCount");
        }

        /// <summary><c>{"key":"value"}</c> biçimindeki metin sözlüğü (tag/trigger); sayı/bool değerler metne çevrilir.</summary>
        public static Dictionary<string, string> ParseStringMap(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, string>();
            return ReadStringMap(Object(json!));
        }

        /// <summary><c>["a","b"]</c> biçimindeki metin listesi.</summary>
        public static List<string> ParseStringList(string? json)
        {
            if (string.IsNullOrEmpty(json)) return new List<string>();
            var value = ParseAny(json!);
            return value is List<object?> list ? list.OfType<string>().ToList() : new List<string>();
        }

        /// <summary>Yerel köprünün başlatma seçenekleri.</summary>
        public static string OptionsToJson(HeroSignalOptions options) => Json.Serialize(new Dictionary<string, object?>
        {
            ["apiUrl"] = options.ApiUrl,
            ["requiresPrivacyConsent"] = options.RequiresPrivacyConsent,
            ["logLevel"] = (long)options.LogLevel,
            ["androidDefaultChannelName"] = options.AndroidDefaultChannelName,
            ["androidDefaultChannelId"] = options.AndroidDefaultChannelId,
            ["iosAppGroup"] = string.IsNullOrEmpty(options.IosAppGroup) ? null : options.IosAppGroup,
            ["iosApnsEnvironment"] = string.IsNullOrEmpty(options.IosApnsEnvironment) ? null : options.IosApnsEnvironment,
            ["iosProvisionalAuthorization"] = options.IosProvisionalAuthorization,
        });

        public static string ToJson(IReadOnlyDictionary<string, string> map) =>
            Json.Serialize(map.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal));

        public static string ToJson(IEnumerable<string> list) => Json.Serialize(list.Cast<object?>().ToList());

        private static HeroSignalNotification EmptyNotification() =>
            new HeroSignalNotification(null, null, null, null, null, null, Array.Empty<NotificationAction>(), new Dictionary<string, string>());

        private static HeroSignalNotification ReadNotification(Dictionary<string, object?> e)
        {
            var actions = new List<NotificationAction>();
            if (e.TryGetValue("actions", out var arr) && arr is List<object?> list)
            {
                foreach (var item in list)
                {
                    if (!(item is Dictionary<string, object?> a)) continue;
                    var id = Str(a, "id");
                    if (id is null) continue;
                    actions.Add(new NotificationAction(id, Str(a, "title") ?? id, Str(a, "url"), Str(a, "icon")));
                }
            }
            return new HeroSignalNotification(
                Str(e, "title"), Str(e, "body"), Str(e, "imageUrl"), Str(e, "url"), Str(e, "deliveryId"), Str(e, "messageId"),
                actions,
                e.TryGetValue("data", out var data) && data is Dictionary<string, object?> d ? ReadStringMap(d) : new Dictionary<string, string>());
        }

        private static InboxItem ReadInboxItem(Dictionary<string, object?> e) => new InboxItem(
            Str(e, "id") ?? throw Invalid("id"),
            Str(e, "messageId"),
            Str(e, "title"),
            Str(e, "body"),
            Str(e, "url"),
            Str(e, "imageUrl"),
            e.TryGetValue("data", out var data) && data is Dictionary<string, object?> d ? ReadStringMap(d) : new Dictionary<string, string>(),
            Bool(e, "read"),
            Millis(e, "readAt"),
            Millis(e, "archivedAt"),
            Millis(e, "expiresAt"),
            Millis(e, "createdAt"));

        private static Dictionary<string, string> ReadStringMap(Dictionary<string, object?> e)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in e)
            {
                switch (pair.Value)
                {
                    case string s: map[pair.Key] = s; break;
                    case bool b: map[pair.Key] = b ? "true" : "false"; break;
                    case long l: map[pair.Key] = l.ToString(CultureInfo.InvariantCulture); break;
                    case double d: map[pair.Key] = d.ToString("R", CultureInfo.InvariantCulture); break;
                }
            }
            return map;
        }

        private static object? ParseAny(string json)
        {
            try
            {
                return Json.Parse(json);
            }
            catch (FormatException e)
            {
                throw new HeroSignalException(HeroSignalException.InvalidResponse, "Köprü yanıtı ayrıştırılamadı.", e);
            }
        }

        private static Dictionary<string, object?> Object(string json) =>
            ParseAny(json) as Dictionary<string, object?> ?? throw new HeroSignalException(HeroSignalException.InvalidResponse, "Köprü yanıtı JSON nesnesi değil.");

        private static HeroSignalException Invalid(string field) =>
            new HeroSignalException(HeroSignalException.InvalidResponse, $"Köprü yanıtında '{field}' alanı eksik.");

        private static string? Str(Dictionary<string, object?> e, string name) =>
            e.TryGetValue(name, out var v) ? v as string : null;

        private static bool Bool(Dictionary<string, object?> e, string name) =>
            e.TryGetValue(name, out var v) && v is bool b && b;

        private static DateTimeOffset? Millis(Dictionary<string, object?> e, string name) =>
            e.TryGetValue(name, out var v) && v is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;
    }
}
