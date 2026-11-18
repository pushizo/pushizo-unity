#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PushizoSdk.Internal
{
    /// <summary>
    /// Yerel köprülerin (Android <c>PushizoUnityBridge.java</c>, iOS <c>PushizoUnityBridge.swift</c>) ortak JSON
    /// biçimlerinin ayrıştırıcısı ve yazıcısı; .NET MAUI köprüsüyle aynı biçimlerdir. Zamanlar epoch milisaniyedir.
    /// </summary>
    internal static class BridgeJson
    {
        public static PushizoNotification ParseNotification(string json) => ReadNotification(Object(json));

        public static NotificationClickedEventArgs ParseNotificationClick(string json)
        {
            var root = Object(json);
            var notification = root.TryGetValue("notification", out var n) && n is Dictionary<string, object?> map
                ? ReadNotification(map)
                : EmptyNotification();
            return new NotificationClickedEventArgs(notification, Str(root, "actionId"), Str(root, "url"));
        }

        /// <summary><c>{"eventId":"…","notification":{…}}</c>.</summary>
        public static KeyValuePair<string, PushizoNotification> ParseForeground(string json)
        {
            var root = Object(json);
            var eventId = Str(root, "eventId") ?? throw Invalid("eventId");
            var notification = root.TryGetValue("notification", out var n) && n is Dictionary<string, object?> map
                ? ReadNotification(map)
                : EmptyNotification();
            return new KeyValuePair<string, PushizoNotification>(eventId, notification);
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

        public static ContentCardFeed ParseContentCardFeed(string json)
        {
            var root = Object(json);
            var cards = new List<ContentCard>();
            if (root.TryGetValue("cards", out var arr) && arr is List<object?> list)
            {
                foreach (var element in list)
                {
                    if (element is Dictionary<string, object?> map) cards.Add(ReadContentCard(map));
                }
            }
            var unviewed = root.TryGetValue("unviewedCount", out var u) && u is long count ? (int)count : cards.Count(c => !c.Viewed);
            return new ContentCardFeed(cards, unviewed);
        }

        public static ContentCard ParseContentCard(string json) => ReadContentCard(Object(json));

        /// <summary>
        /// <c>inbox</c> olayı: <c>{"type":"listed|read|read_all|archived|count","itemId":…|null,"unreadCount":n}</c>.
        /// Tanınmayan tür (yeni yerel SDK) null döner ve olay yok sayılır.
        /// </summary>
        public static InboxChangedEventArgs? ParseInboxChange(string json)
        {
            var root = Object(json);
            InboxChangeType type;
            switch (Str(root, "type"))
            {
                case "listed": type = InboxChangeType.Listed; break;
                case "read": type = InboxChangeType.Read; break;
                case "read_all": type = InboxChangeType.ReadAll; break;
                case "archived": type = InboxChangeType.Archived; break;
                case "count": type = InboxChangeType.Count; break;
                default: return null;
            }
            var unread = root.TryGetValue("unreadCount", out var u) && u is long count ? (int)count : throw Invalid("unreadCount");
            return new InboxChangedEventArgs(type, Str(root, "itemId"), unread);
        }

        /// <summary><c>{"unreadCount":n}</c>.</summary>
        public static int ParseUnreadCount(string json)
        {
            var root = Object(json);
            return root.TryGetValue("unreadCount", out var u) && u is long count ? (int)count : throw Invalid("unreadCount");
        }

        /// <summary>
        /// <c>{"preferences":[…],"categories":[…]}</c> (<see cref="INativeBridge.GetPreferences"/>). Kategorisiz tercih
        /// <see cref="PushizoException.InvalidResponse"/> verir; anahtarsız kategori atlanır (yerel SDK'larla aynı).
        /// </summary>
        public static UserPreferences ParsePreferences(string json)
        {
            var root = Object(json);
            var preferences = new List<UserPreference>();
            if (root.TryGetValue("preferences", out var prefs) && prefs is List<object?> prefList)
            {
                foreach (var element in prefList)
                {
                    if (!(element is Dictionary<string, object?> e)) continue;
                    preferences.Add(new UserPreference(
                        Str(e, "category") ?? throw Invalid("category"),
                        Str(e, "channel"),
                        Bool(e, "optedIn"),
                        Bool(e, "explicit"),
                        Str(e, "source"),
                        Millis(e, "updatedAt")));
                }
            }
            var categories = new List<PreferenceCategory>();
            if (root.TryGetValue("categories", out var cats) && cats is List<object?> catList)
            {
                foreach (var element in catList)
                {
                    if (!(element is Dictionary<string, object?> e)) continue;
                    var key = Str(e, "key");
                    if (string.IsNullOrEmpty(key)) continue;
                    categories.Add(new PreferenceCategory(
                        key!,
                        e.TryGetValue("name", out var name) && name is Dictionary<string, object?> n ? ReadStringMap(n) : new Dictionary<string, string>(),
                        e.TryGetValue("description", out var description) && description is Dictionary<string, object?> d ? ReadStringMap(d) : new Dictionary<string, string>(),
                        e.TryGetValue("channels", out var channels) && channels is List<object?> c
                            ? c.OfType<string>().Where(s => s.Length > 0).ToList()
                            : null,
                        // Alan yoksa varsayılan açık (yerel SDK'lardaki `default_opted_in` varsayılanı).
                        !(e.TryGetValue("defaultOptedIn", out var defaultOptedIn) && defaultOptedIn is bool b && !b)));
                }
            }
            return new UserPreferences(preferences, categories);
        }

        /// <summary><c>{"consents":[{"purpose","granted","source","updatedAt"}]}</c>; amacı olmayan kayıt atlanır (yerel SDK'larla aynı).</summary>
        public static IReadOnlyList<UserConsent> ParseConsents(string json)
        {
            var consents = new List<UserConsent>();
            if (Object(json).TryGetValue("consents", out var value) && value is List<object?> list)
            {
                foreach (var element in list)
                {
                    if (!(element is Dictionary<string, object?> e)) continue;
                    var purpose = Str(e, "purpose");
                    if (string.IsNullOrEmpty(purpose)) continue;
                    consents.Add(new UserConsent(purpose!, Bool(e, "granted"), Str(e, "source"), Millis(e, "updatedAt")));
                }
            }
            return consents;
        }

        /// <summary>
        /// <see cref="INativeBridge.LoginWithProfile"/> gövdesi: doğrulanmış profil; boş alanlar yazılmaz. Profil tümüyle
        /// boşsa null (düz giriş yeterlidir).
        /// </summary>
        public static string? ProfileToJson(PushizoUserProfile profile)
        {
            var body = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(profile.Email)) body["email"] = Validation.RequireEmail(profile.Email!, nameof(profile));
            if (!string.IsNullOrWhiteSpace(profile.Sms)) body["sms"] = Validation.RequirePhone(profile.Sms!, nameof(profile));
            if (profile.Tags != null)
            {
                var tags = Validation.RequireTags(profile.Tags, nameof(profile));
                if (tags.Count > 0) body["tags"] = tags.ToDictionary(p => p.Key, p => (object?)p.Value);
            }
            if (profile.Aliases != null && profile.Aliases.Count > 0)
                body["aliases"] = Validation.NormalizeAliases(profile.Aliases, nameof(profile)).ToDictionary(p => p.Key, p => (object?)p.Value);
            return body.Count == 0 ? null : Json.Serialize(body);
        }

        /// <summary>
        /// <c>{"value":…}</c> (<see cref="INativeBridge.FeatureFlagValue"/>): metin, long, double, bool, sözlük veya liste;
        /// yanıt boşsa ya da değer yoksa <paramref name="fallback"/>.
        /// </summary>
        public static object? ParseFeatureFlagValue(string? json, object? fallback)
        {
            if (string.IsNullOrEmpty(json)) return fallback;
            return Object(json!).TryGetValue("value", out var value) && value != null ? value : fallback;
        }

        /// <summary><c>{"messages":[{"id","json"}]}</c> (<see cref="INativeBridge.PlacementsList"/>); kimliksiz öğe atlanır.</summary>
        public static IReadOnlyList<InAppMessage> ParsePlacementMessages(string json)
        {
            var messages = new List<InAppMessage>();
            if (Object(json).TryGetValue("messages", out var arr) && arr is List<object?> list)
            {
                foreach (var element in list)
                {
                    if (!(element is Dictionary<string, object?> e)) continue;
                    var id = Str(e, "id");
                    if (string.IsNullOrEmpty(id)) continue;
                    messages.Add(new InAppMessage(id!, Str(e, "json")));
                }
            }
            return messages;
        }

        /// <summary>Tercih girdileri → <c>[{"category","channel","optedIn"}]</c> (kanal yoksa JSON null).</summary>
        public static string PreferencesToJson(IEnumerable<PreferenceInput> preferences) =>
            Json.Serialize(preferences.Select(p => (object?)new Dictionary<string, object?>
            {
                ["category"] = p.Category,
                ["channel"] = p.Channel,
                ["optedIn"] = p.OptedIn,
            }).ToList());

        /// <summary><see cref="DeliverySetting"/> → köprü değeri (<c>auto_start</c> …).</summary>
        public static string DeliverySettingWire(DeliverySetting setting) => setting switch
        {
            DeliverySetting.Notifications => "notifications",
            DeliverySetting.AutoStart => "auto_start",
            DeliverySetting.BatteryOptimization => "battery_optimization",
            DeliverySetting.AppDetails => "app_details",
            _ => "auto",
        };

        /// <summary>
        /// Android teslim durumu (ADR-0092). JSON yoksa (iOS, Editor, başlatılmamış yerel SDK) <see cref="DeliveryStatus.Unsupported"/>;
        /// yerel SDK'nın ileride ekleyeceği, bu sürümün tanımadığı ayar ekranı atlanır.
        /// </summary>
        public static DeliveryStatus ParseDeliveryStatus(string? json)
        {
            if (string.IsNullOrEmpty(json)) return DeliveryStatus.Unsupported;
            var root = Object(json!);
            var settings = new List<DeliverySetting>();
            if (root.TryGetValue("recommendedSettings", out var arr) && arr is List<object?> list)
            {
                foreach (var wire in list.OfType<string>())
                {
                    switch (wire)
                    {
                        case "notifications": settings.Add(DeliverySetting.Notifications); break;
                        case "auto_start": settings.Add(DeliverySetting.AutoStart); break;
                        case "battery_optimization": settings.Add(DeliverySetting.BatteryOptimization); break;
                        case "app_details": settings.Add(DeliverySetting.AppDetails); break;
                    }
                }
            }
            var risk = Str(root, "risk") switch
            {
                "high" => DeliveryRisk.High,
                "elevated" => DeliveryRisk.Elevated,
                _ => DeliveryRisk.None,
            };
            return new DeliveryStatus(
                true, Str(root, "manufacturer") ?? string.Empty, Str(root, "brand") ?? string.Empty, Str(root, "vendor") ?? "other",
                Bool(root, "aggressiveVendor"), Str(root, "rom") ?? string.Empty, Str(root, "romVersion"),
                Bool(root, "notificationsEnabled"), Bool(root, "channelBlocked"), Bool(root, "batteryOptimizationExempt"),
                Bool(root, "backgroundRestricted"), Str(root, "standbyBucket"), Bool(root, "powerSaveMode"), Bool(root, "dataSaverEnabled"),
                Bool(root, "restricted"), risk, settings.AsReadOnly());
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
        public static string OptionsToJson(PushizoOptions options) => Json.Serialize(new Dictionary<string, object?>
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

        private static PushizoNotification EmptyNotification() =>
            new PushizoNotification(null, null, null, null, null, null, Array.Empty<NotificationAction>(), new Dictionary<string, string>());

        private static PushizoNotification ReadNotification(Dictionary<string, object?> e)
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
            return new PushizoNotification(
                Str(e, "title"), Str(e, "body"), Str(e, "imageUrl"), Str(e, "url"), Str(e, "deliveryId"), Str(e, "messageId"),
                actions,
                e.TryGetValue("data", out var data) && data is Dictionary<string, object?> d ? ReadStringMap(d) : new Dictionary<string, string>(),
                e.TryGetValue("androidNotificationId", out var trayId) && trayId is long l && l >= int.MinValue && l <= int.MaxValue
                    ? (int)l
                    : (int?)null);
        }

        private static ContentCard ReadContentCard(Dictionary<string, object?> e) => new ContentCard(
            Str(e, "id") ?? throw Invalid("id"),
            Str(e, "messageId"),
            ContentCard.ParseType(Str(e, "type")),
            Str(e, "title"),
            Str(e, "body"),
            Str(e, "url"),
            Str(e, "imageUrl"),
            Str(e, "buttonText"),
            e.TryGetValue("data", out var data) && data is Dictionary<string, object?> d ? ReadStringMap(d) : new Dictionary<string, string>(),
            Bool(e, "pinned"),
            e.TryGetValue("priority", out var p) && p is long priority ? (int)priority : 0,
            Bool(e, "viewed"),
            Bool(e, "clicked"),
            Millis(e, "expiresAt"),
            Millis(e, "createdAt"));

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
                throw new PushizoException(PushizoException.InvalidResponse, "Köprü yanıtı ayrıştırılamadı.", e);
            }
        }

        private static Dictionary<string, object?> Object(string json) =>
            ParseAny(json) as Dictionary<string, object?> ?? throw new PushizoException(PushizoException.InvalidResponse, "Köprü yanıtı JSON nesnesi değil.");

        private static PushizoException Invalid(string field) =>
            new PushizoException(PushizoException.InvalidResponse, $"Köprü yanıtında '{field}' alanı eksik.");

        private static string? Str(Dictionary<string, object?> e, string name) =>
            e.TryGetValue(name, out var v) ? v as string : null;

        private static bool Bool(Dictionary<string, object?> e, string name) =>
            e.TryGetValue(name, out var v) && v is bool b && b;

        private static DateTimeOffset? Millis(Dictionary<string, object?> e, string name) =>
            e.TryGetValue(name, out var v) && v is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;
    }
}
