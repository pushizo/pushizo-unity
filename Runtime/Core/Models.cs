using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HeroSignalSdk.Unity
{
    /// <summary>SDK günlük seviyesi (yerel SDK'lardaki <c>HsLog.Level</c> ile aynı sıra).</summary>
    public enum LogLevel
    {
        None = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Debug = 4,
    }

    /// <summary><see cref="HeroSignal.Initialize"/> yapılandırması.</summary>
    public sealed class HeroSignalOptions
    {
        /// <summary>HeroSignal API adresi (yerelde Android emülatöründen <c>http://10.0.2.2:4000</c>).</summary>
        public string ApiUrl { get; set; } = "https://api.herosignal.dev";

        /// <summary>KVKK/GDPR: <see cref="HeroSignal.SetConsentGiven"/> çağrılana kadar ağ çağrısı yapılmaz.</summary>
        public bool RequiresPrivacyConsent { get; set; }

        public LogLevel LogLevel { get; set; } = LogLevel.Warn;

        /// <summary>Android 8+ varsayılan bildirim kanalı adı.</summary>
        public string AndroidDefaultChannelName { get; set; } = "Bildirimler";

        /// <summary>Android 8+ varsayılan bildirim kanalı kimliği.</summary>
        public string AndroidDefaultChannelId { get; set; } = "herosignal_default";

        /// <summary>iOS: NSE ile rozet sayacı paylaşımı için App Group (ör. <c>group.com.example.app</c>).</summary>
        public string IosAppGroup { get; set; }

        /// <summary>iOS: APNs ortamı (<c>sandbox</c> / <c>production</c>); null ise provisioning profilinden tespit edilir.</summary>
        public string IosApnsEnvironment { get; set; }

        /// <summary>iOS: provisional (sessiz) izinle başla.</summary>
        public bool IosProvisionalAuthorization { get; set; }

        internal HeroSignalOptions Clone() => (HeroSignalOptions)MemberwiseClone();
    }

    /// <summary>Bildirim aksiyon düğmesi.</summary>
    public sealed class NotificationAction
    {
        public NotificationAction(string id, string title, string url, string icon)
        {
            Id = id;
            Title = title;
            Url = url;
            Icon = icon;
        }

        public string Id { get; }
        public string Title { get; }
        public string Url { get; }
        public string Icon { get; }
    }

    /// <summary>HeroSignal push bildirimi (tıklama ve ön plan olaylarında).</summary>
    public sealed class HeroSignalNotification
    {
        public string Title { get; internal set; }
        public string Body { get; internal set; }
        public string ImageUrl { get; internal set; }
        public string Url { get; internal set; }
        /// <summary>Teslimat kimliği (<c>hs.dlv</c>); Mesaj İzleme'deki kayıtla eşleşir.</summary>
        public string DeliveryId { get; internal set; }
        public string MessageId { get; internal set; }
        /// <summary>
        /// Tepsiden kaldırma kimliği (<see cref="NotificationsManager.RemoveNotification(string)"/>): Android'de sistem
        /// bildirim kimliği (sayı), iOS'ta teslimat kimliği.
        /// </summary>
        public string NotificationId { get; internal set; }
        /// <summary>Bildirim grubu (Android <c>hs_group</c>).</summary>
        public string Group { get; internal set; }
        public IReadOnlyList<NotificationAction> Actions { get; internal set; } = Array.Empty<NotificationAction>();
        /// <summary>Müşteri verileri (<c>hs_</c> önekli alanlar hariç).</summary>
        public IReadOnlyDictionary<string, string> Data { get; internal set; } = EmptyStringMap.Instance;
    }

    /// <summary>Push aboneliği durumu.</summary>
    public sealed class PushSubscriptionState : IEquatable<PushSubscriptionState>
    {
        internal static readonly PushSubscriptionState Empty = new PushSubscriptionState(null, null, false, false);

        public PushSubscriptionState(string id, string token, bool optedIn, bool permission)
        {
            Id = id;
            Token = token;
            OptedIn = optedIn;
            Permission = permission;
        }

        /// <summary>Sunucudaki abonelik kimliği; kayıt tamamlanana kadar null.</summary>
        public string Id { get; }
        /// <summary>FCM/APNs token'ı.</summary>
        public string Token { get; }
        /// <summary>Kullanıcı push almayı kabul etti ve sistem izni var.</summary>
        public bool OptedIn { get; }
        /// <summary>Sistem bildirim izni.</summary>
        public bool Permission { get; }

        public bool Equals(PushSubscriptionState other) =>
            other != null && Id == other.Id && Token == other.Token && OptedIn == other.OptedIn && Permission == other.Permission;

        public override bool Equals(object obj) => Equals(obj as PushSubscriptionState);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = Id?.GetHashCode() ?? 0;
                h = (h * 397) ^ (Token?.GetHashCode() ?? 0);
                h = (h * 397) ^ OptedIn.GetHashCode();
                return (h * 397) ^ Permission.GetHashCode();
            }
        }

        public override string ToString() => $"PushSubscriptionState(Id={Id}, OptedIn={OptedIn}, Permission={Permission})";
    }

    /// <summary>Bildirim kutusu öğesi.</summary>
    public sealed class InboxItem
    {
        public string Id { get; internal set; }
        public string MessageId { get; internal set; }
        public string Title { get; internal set; }
        public string Body { get; internal set; }
        public string Url { get; internal set; }
        public string ImageUrl { get; internal set; }
        public IReadOnlyDictionary<string, string> Data { get; internal set; } = EmptyStringMap.Instance;
        public bool Read { get; internal set; }
        public DateTimeOffset? ReadAt { get; internal set; }
        public DateTimeOffset? ArchivedAt { get; internal set; }
        public DateTimeOffset? ExpiresAt { get; internal set; }
        public DateTimeOffset? CreatedAt { get; internal set; }
    }

    /// <summary>Bildirim kutusu sayfası (yeniden eskiye).</summary>
    public sealed class InboxPage
    {
        public InboxPage(IReadOnlyList<InboxItem> items, int unreadCount, string nextCursor, bool hasMore)
        {
            Items = items;
            UnreadCount = unreadCount;
            NextCursor = nextCursor;
            HasMore = hasMore;
        }

        public IReadOnlyList<InboxItem> Items { get; }
        public int UnreadCount { get; }
        public string NextCursor { get; }
        public bool HasMore { get; }
    }

    /// <summary>In-app mesaj özeti (yaşam döngüsü olaylarında).</summary>
    public sealed class InAppMessage
    {
        public InAppMessage(string id, string json)
        {
            Id = id;
            Json = json;
        }

        /// <summary>Mesaj kimliği.</summary>
        public string Id { get; }
        /// <summary>Sunucunun döndürdüğü ham mesaj JSON'u.</summary>
        public string Json { get; }
    }

    /// <summary>SDK çağrısı hatası. <see cref="Code"/> sunucu hata kodu (ör. <c>NOT_FOUND</c>) veya SDK durum kodudur.</summary>
    public sealed class HeroSignalException : Exception
    {
        public const string NotInitialized = "NOT_INITIALIZED";
        public const string NotReady = "NOT_READY";
        public const string PlatformNotSupported = "PLATFORM_NOT_SUPPORTED";
        public const string InvalidResponse = "INVALID_RESPONSE";
        public const string NativeError = "NATIVE_ERROR";

        public HeroSignalException(string code, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        /// <summary>Hata kodu.</summary>
        public string Code { get; }

        /// <summary>HTTP durum kodu (sunucu hatalarında; aksi halde null).</summary>
        public int? Status { get; internal set; }
    }

    internal static class EmptyStringMap
    {
        public static readonly IReadOnlyDictionary<string, string> Instance =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    }
}
