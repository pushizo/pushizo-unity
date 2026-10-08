using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// iOS köprüsünün (<c>Plugins/iOS/HeroSignalUnityBridge.swift</c>) JSON biçimlerinin ayrıştırıcısı ve köprüye giden
    /// JSON'un üreticisi. Zamanlar epoch milisaniyedir.
    /// </summary>
    internal static class BridgeJson
    {
        public static HeroSignalNotification ParseNotification(string json) => ReadNotification(Object(json));

        public static NotificationClickedEventArgs ParseNotificationClick(string json)
        {
            var root = Object(json);
            var notification = root.TryGetValue("notification", out var n) && n is Dictionary<string, object> map
                ? ReadNotification(map)
                : new HeroSignalNotification();
            return new NotificationClickedEventArgs(notification, Str(root, "actionId"), Str(root, "url"));
        }

        /// <summary>Ön plan olayı: <c>{"eventId":"…","notification":{…}}</c>.</summary>
        public static KeyValuePair<string, HeroSignalNotification> ParseForeground(string json)
        {
            var root = Object(json);
            var eventId = Str(root, "eventId") ?? throw Invalid("eventId");
            var notification = root.TryGetValue("notification", out var n) && n is Dictionary<string, object> map
                ? ReadNotification(map)
                : new HeroSignalNotification();
            return new KeyValuePair<string, HeroSignalNotification>(eventId, notification);
        }

        public static PushSubscriptionState ParseSubscriptionState(string json)
        {
            var root = Object(json);
            return new PushSubscriptionState(Str(root, "id"), Str(root, "token"), Bool(root, "optedIn"), Bool(root, "permission"));
        }

        /// <summary>İzin olayı: <c>{"permission":true}</c>.</summary>
        public static bool ParsePermission(string json) => Bool(Object(json), "permission");

        /// <summary>İzin isteği sonucu: <c>{"granted":true}</c>.</summary>
        public static bool ParsePermissionResult(string json) => Bool(Object(json), "granted");

        public static InAppMessageClickedEventArgs ParseInAppClick(string json)
        {
            var root = Object(json);
            return new InAppMessageClickedEventArgs(Str(root, "messageId") ?? throw Invalid("messageId"), Str(root, "actionId") ?? string.Empty, Str(root, "url"));
        }

        /// <summary>In-app yaşam döngüsü: <c>{"phase":"willDisplay","messageId":"…","json":"…"}</c>.</summary>
        public static KeyValuePair<InAppLifecyclePhase, InAppMessage> ParseInAppLifecycle(string json)
        {
            var root = Object(json);
            InAppLifecyclePhase phase;
            switch (Str(root, "phase"))
            {
                case "willDisplay": phase = InAppLifecyclePhase.WillDisplay; break;
                case "didDisplay": phase = InAppLifecyclePhase.DidDisplay; break;
                case "willDismiss": phase = InAppLifecyclePhase.WillDismiss; break;
                case "didDismiss": phase = InAppLifecyclePhase.DidDismiss; break;
                default: throw Invalid("phase");
            }
            var message = new InAppMessage(Str(root, "messageId") ?? throw Invalid("messageId"), Str(root, "json"));
            return new KeyValuePair<InAppLifecyclePhase, InAppMessage>(phase, message);
        }

        public static InboxPage ParseInboxPage(string json)
        {
            var root = Object(json);
            var items = new List<InboxItem>();
            if (root.TryGetValue("items", out var arr) && arr is List<object> list)
            {
                foreach (var element in list)
                {
                    if (element is Dictionary<string, object> map) items.Add(ReadInboxItem(map));
                }
            }
            var unread = root.TryGetValue("unreadCount", out var u) && IsNumber(u)
                ? Convert.ToInt32(u, CultureInfo.InvariantCulture)
                : items.Count(i => !i.Read);
            return new InboxPage(items, unread, Str(root, "nextCursor"), Bool(root, "hasMore"));
        }

        public static InboxItem ParseInboxItem(string json) => ReadInboxItem(Object(json));

        /// <summary>Sayı yanıtı: <c>{"count":3}</c>.</summary>
        public static int ParseCount(string json)
        {
            var root = Object(json);
            return root.TryGetValue("count", out var c) && IsNumber(c) ? Convert.ToInt32(c, CultureInfo.InvariantCulture) : throw Invalid("count");
        }

        /// <summary>Hata yanıtı: <c>{"code":"NOT_FOUND","message":"…","status":404}</c>.</summary>
        public static HeroSignalException ParseError(string json)
        {
            try
            {
                var root = Object(json);
                var status = root.TryGetValue("status", out var s) && IsNumber(s) ? Convert.ToInt32(s, CultureInfo.InvariantCulture) : 0;
                return new HeroSignalException(Str(root, "code") ?? HeroSignalException.NativeError, Str(root, "message") ?? "Yerel SDK hatası.")
                {
                    Status = status > 0 ? status : (int?)null,
                };
            }
            catch (HeroSignalException e)
            {
                return e;
            }
        }

        /// <summary><c>{"key":"value"}</c> biçimindeki metin sözlüğü (tag/tetik).</summary>
        public static IReadOnlyDictionary<string, string> ParseStringMap(string json) =>
            string.IsNullOrEmpty(json) ? new Dictionary<string, string>() : ReadStringMap(Object(json));

        /// <summary><c>["a","b"]</c>; metin olmayan öğeler atlanır.</summary>
        public static IReadOnlyList<string> ParseStringList(string json)
        {
            if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
            return Parse(json) is List<object> list ? list.OfType<string>().ToList() : (IReadOnlyList<string>)Array.Empty<string>();
        }

        public static string ToJson(IReadOnlyDictionary<string, string> map) =>
            Json.Serialize(map.ToDictionary(p => p.Key, p => (object)p.Value, StringComparer.Ordinal));

        public static string ToJson(IReadOnlyList<string> list) => Json.Serialize(list);

        /// <summary>iOS köprüsü başlatma yapılandırması (boş iOS alanları yazılmaz).</summary>
        public static string OptionsJson(HeroSignalOptions options)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["apiUrl"] = options.ApiUrl,
                ["requiresPrivacyConsent"] = options.RequiresPrivacyConsent,
                ["logLevel"] = (int)options.LogLevel,
                ["provisionalAuthorization"] = options.IosProvisionalAuthorization,
            };
            if (!string.IsNullOrEmpty(options.IosAppGroup)) map["appGroup"] = options.IosAppGroup;
            if (!string.IsNullOrEmpty(options.IosApnsEnvironment)) map["apnsEnvironment"] = options.IosApnsEnvironment;
            return Json.Serialize(map);
        }

        private static HeroSignalNotification ReadNotification(Dictionary<string, object> e)
        {
            var actions = new List<NotificationAction>();
            if (e.TryGetValue("actions", out var arr) && arr is List<object> list)
            {
                foreach (var item in list)
                {
                    if (!(item is Dictionary<string, object> a)) continue;
                    var id = Str(a, "id");
                    if (id == null) continue;
                    actions.Add(new NotificationAction(id, Str(a, "title") ?? id, Str(a, "url"), Str(a, "icon")));
                }
            }
            return new HeroSignalNotification
            {
                Title = Str(e, "title"),
                Body = Str(e, "body"),
                ImageUrl = Str(e, "imageUrl"),
                Url = Str(e, "url"),
                DeliveryId = Str(e, "deliveryId"),
                MessageId = Str(e, "messageId"),
                NotificationId = Str(e, "notificationId"),
                Group = Str(e, "group"),
                Actions = actions,
                Data = e.TryGetValue("data", out var data) && data is Dictionary<string, object> d ? ReadStringMap(d) : EmptyStringMap.Instance,
            };
        }

        private static InboxItem ReadInboxItem(Dictionary<string, object> e) => new InboxItem
        {
            Id = Str(e, "id") ?? throw Invalid("id"),
            MessageId = Str(e, "messageId"),
            Title = Str(e, "title"),
            Body = Str(e, "body"),
            Url = Str(e, "url"),
            ImageUrl = Str(e, "imageUrl"),
            Data = e.TryGetValue("data", out var data) && data is Dictionary<string, object> d ? ReadStringMap(d) : EmptyStringMap.Instance,
            Read = Bool(e, "read"),
            ReadAt = Millis(e, "readAt"),
            ArchivedAt = Millis(e, "archivedAt"),
            ExpiresAt = Millis(e, "expiresAt"),
            CreatedAt = Millis(e, "createdAt"),
        };

        private static IReadOnlyDictionary<string, string> ReadStringMap(Dictionary<string, object> e)
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

        private static object Parse(string json)
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

        private static Dictionary<string, object> Object(string json) =>
            Parse(json) as Dictionary<string, object> ?? throw new HeroSignalException(HeroSignalException.InvalidResponse, "Köprü yanıtı JSON nesnesi değil.");

        private static HeroSignalException Invalid(string field) =>
            new HeroSignalException(HeroSignalException.InvalidResponse, $"Köprü yanıtında '{field}' alanı eksik.");

        private static bool IsNumber(object value) => value is long || value is double;

        private static string Str(Dictionary<string, object> e, string name) => e.TryGetValue(name, out var v) ? v as string : null;

        private static bool Bool(Dictionary<string, object> e, string name) => e.TryGetValue(name, out var v) && v is bool b && b;

        private static DateTimeOffset? Millis(Dictionary<string, object> e, string name) =>
            e.TryGetValue(name, out var v) && v is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;
    }
}
