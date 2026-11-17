#nullable enable
using System;
using System.Collections.Generic;

namespace PushizoSdk
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

    /// <summary>Bildirim aksiyon düğmesi.</summary>
    public sealed class NotificationAction
    {
        public NotificationAction(string id, string title, string? url, string? icon)
        {
            Id = id;
            Title = title;
            Url = url;
            Icon = icon;
        }

        public string Id { get; }
        public string Title { get; }
        public string? Url { get; }
        public string? Icon { get; }
    }

    /// <summary>Pushizo push bildirimi (tıklama ve ön plan olaylarında).</summary>
    public sealed class PushizoNotification
    {
        internal PushizoNotification(
            string? title, string? body, string? imageUrl, string? url, string? deliveryId, string? messageId,
            IReadOnlyList<NotificationAction> actions, IReadOnlyDictionary<string, string> data, int? androidNotificationId = null)
        {
            Title = title;
            Body = body;
            ImageUrl = imageUrl;
            Url = url;
            DeliveryId = deliveryId;
            MessageId = messageId;
            Actions = actions;
            Data = data;
            AndroidNotificationId = androidNotificationId;
        }

        public string? Title { get; }
        public string? Body { get; }
        public string? ImageUrl { get; }
        public string? Url { get; }
        /// <summary>Teslimat kimliği (<c>hs.dlv</c>); Mesaj İzleme'deki kayıtla eşleşir.</summary>
        public string? DeliveryId { get; }
        public string? MessageId { get; }
        public IReadOnlyList<NotificationAction> Actions { get; }
        /// <summary>Müşteri verileri (<c>hs_</c> önekli alanlar hariç).</summary>
        public IReadOnlyDictionary<string, string> Data { get; }
        /// <summary>
        /// Yalnızca Android: tepsi kimliği (<see cref="NotificationsManager.RemoveNotification(int)"/> için); iOS'ta null.
        /// </summary>
        public int? AndroidNotificationId { get; }
    }

    /// <summary>Push aboneliği durumu.</summary>
    public sealed class PushSubscriptionState : IEquatable<PushSubscriptionState>
    {
        internal static readonly PushSubscriptionState Empty = new PushSubscriptionState(null, null, false, false);

        public PushSubscriptionState(string? id, string? token, bool optedIn, bool permission)
        {
            Id = id;
            Token = token;
            OptedIn = optedIn;
            Permission = permission;
        }

        /// <summary>Sunucudaki abonelik kimliği; kayıt tamamlanana kadar null.</summary>
        public string? Id { get; }
        /// <summary>FCM/APNs token'ı.</summary>
        public string? Token { get; }
        /// <summary>Kullanıcı push almayı kabul etti ve sistem izni var.</summary>
        public bool OptedIn { get; }
        /// <summary>Sistem bildirim izni.</summary>
        public bool Permission { get; }

        public bool Equals(PushSubscriptionState? other) =>
            other is not null && Id == other.Id && Token == other.Token && OptedIn == other.OptedIn && Permission == other.Permission;

        public override bool Equals(object? obj) => Equals(obj as PushSubscriptionState);

        public override int GetHashCode() => HashCode.Combine(Id, Token, OptedIn, Permission);
    }

    /// <summary>Bildirim kutusu öğesi.</summary>
    public sealed class InboxItem
    {
        internal InboxItem(
            string id, string? messageId, string? title, string? body, string? url, string? imageUrl,
            IReadOnlyDictionary<string, string> data, bool read, DateTimeOffset? readAt, DateTimeOffset? archivedAt,
            DateTimeOffset? expiresAt, DateTimeOffset? createdAt)
        {
            Id = id;
            MessageId = messageId;
            Title = title;
            Body = body;
            Url = url;
            ImageUrl = imageUrl;
            Data = data;
            Read = read;
            ReadAt = readAt;
            ArchivedAt = archivedAt;
            ExpiresAt = expiresAt;
            CreatedAt = createdAt;
        }

        public string Id { get; }
        public string? MessageId { get; }
        public string? Title { get; }
        public string? Body { get; }
        public string? Url { get; }
        public string? ImageUrl { get; }
        public IReadOnlyDictionary<string, string> Data { get; }
        public bool Read { get; }
        public DateTimeOffset? ReadAt { get; }
        public DateTimeOffset? ArchivedAt { get; }
        public DateTimeOffset? ExpiresAt { get; }
        public DateTimeOffset? CreatedAt { get; }
    }

    /// <summary>İçerik kartı türü (ADR-0082).</summary>
    public enum ContentCardType
    {
        Classic,
        CaptionedImage,
        ImageOnly,
    }

    /// <summary>İçerik kartı (ADR-0082); kartlar bildirim kutusunda görünmez.</summary>
    public sealed class ContentCard
    {
        internal ContentCard(
            string id, string? messageId, ContentCardType type, string? title, string? body, string? url, string? imageUrl,
            string? buttonText, IReadOnlyDictionary<string, string> data, bool pinned, int priority, bool viewed, bool clicked,
            DateTimeOffset? expiresAt, DateTimeOffset? createdAt)
        {
            Id = id;
            MessageId = messageId;
            Type = type;
            Title = title;
            Body = body;
            Url = url;
            ImageUrl = imageUrl;
            ButtonText = buttonText;
            Data = data;
            Pinned = pinned;
            Priority = priority;
            Viewed = viewed;
            Clicked = clicked;
            ExpiresAt = expiresAt;
            CreatedAt = createdAt;
        }

        public string Id { get; }
        public string? MessageId { get; }
        public ContentCardType Type { get; }
        public string? Title { get; }
        public string? Body { get; }
        public string? Url { get; }
        public string? ImageUrl { get; }
        public string? ButtonText { get; }
        public IReadOnlyDictionary<string, string> Data { get; }
        public bool Pinned { get; }
        public int Priority { get; }
        public bool Viewed { get; }
        public bool Clicked { get; }
        public DateTimeOffset? ExpiresAt { get; }
        public DateTimeOffset? CreatedAt { get; }

        /// <summary>Tel değeri (<c>classic</c>, <c>captioned_image</c>, <c>image_only</c>); bilinmeyen değer klasik sayılır.</summary>
        internal static ContentCardType ParseType(string? wire) => wire switch
        {
            "captioned_image" => ContentCardType.CaptionedImage,
            "image_only" => ContentCardType.ImageOnly,
            _ => ContentCardType.Classic,
        };
    }

    /// <summary>Kart akışı (sabitlenmiş → öncelik → yeni) ve görülmemiş kart sayısı.</summary>
    public sealed class ContentCardFeed
    {
        internal ContentCardFeed(IReadOnlyList<ContentCard> cards, int unviewedCount)
        {
            Cards = cards;
            UnviewedCount = unviewedCount;
        }

        public IReadOnlyList<ContentCard> Cards { get; }
        public int UnviewedCount { get; }
    }

    /// <summary>Bildirim kutusu sayfası (yeniden eskiye).</summary>
    public sealed class InboxPage
    {
        internal InboxPage(IReadOnlyList<InboxItem> items, int unreadCount, string? nextCursor, bool hasMore)
        {
            Items = items;
            UnreadCount = unreadCount;
            NextCursor = nextCursor;
            HasMore = hasMore;
        }

        public IReadOnlyList<InboxItem> Items { get; }
        public int UnreadCount { get; }
        public string? NextCursor { get; }
        public bool HasMore { get; }
    }

    /// <summary>
    /// Kullanıcı tercihi (ADR-0019 §4): kategori × kanal (<c>null</c> = tüm kanallar) → opt-in. <see cref="Explicit"/>
    /// false ise değer kategori varsayılanından türetilmiştir; <see cref="Source"/> kaydı yazan taraftır (sdk, api,
    /// preference_center…).
    /// </summary>
    public sealed class UserPreference
    {
        internal UserPreference(string category, string? channel, bool optedIn, bool @explicit, string? source, DateTimeOffset? updatedAt)
        {
            Category = category;
            Channel = channel;
            OptedIn = optedIn;
            Explicit = @explicit;
            Source = source;
            UpdatedAt = updatedAt;
        }

        public string Category { get; }
        /// <summary><c>null</c> (tüm kanallar) veya <c>ios_push</c> | <c>android_push</c> | <c>web_push</c> | <c>email</c> | <c>sms</c>.</summary>
        public string? Channel { get; }
        public bool OptedIn { get; }
        public bool Explicit { get; }
        public string? Source { get; }
        /// <summary>Türetilmiş değerde null.</summary>
        public DateTimeOffset? UpdatedAt { get; }
    }

    /// <summary>Mesaj kategorisi: çok dilli ad/açıklama (<c>dil kodu → metin</c>), kanal kısıtı (<c>null</c> = tümü) ve varsayılan opt-in.</summary>
    public sealed class PreferenceCategory
    {
        internal PreferenceCategory(
            string key, IReadOnlyDictionary<string, string> name, IReadOnlyDictionary<string, string> description,
            IReadOnlyList<string>? channels, bool defaultOptedIn)
        {
            Key = key;
            Name = name;
            Description = description;
            Channels = channels;
            DefaultOptedIn = defaultOptedIn;
        }

        public string Key { get; }
        public IReadOnlyDictionary<string, string> Name { get; }
        public IReadOnlyDictionary<string, string> Description { get; }
        public IReadOnlyList<string>? Channels { get; }
        public bool DefaultOptedIn { get; }

        /// <summary>Verilen dilde ad; yoksa İngilizce, o da yoksa ilk tanımlı dil (anahtar sırasıyla), hiçbiri yoksa anahtar.</summary>
        public string LocalizedName(string language)
        {
            if (Name.TryGetValue(language, out var name)) return name;
            if (Name.TryGetValue("en", out var en)) return en;
            string? firstKey = null;
            foreach (var key in Name.Keys)
            {
                if (firstKey is null || string.CompareOrdinal(key, firstKey) < 0) firstKey = key;
            }
            return firstKey is null ? Key : Name[firstKey];
        }
    }

    /// <summary><c>User.GetPreferencesAsync</c> / <c>SetPreferencesAsync</c> sonucu: kullanıcının tercihleri ve app'in kategorileri.</summary>
    public sealed class UserPreferences
    {
        internal UserPreferences(IReadOnlyList<UserPreference> preferences, IReadOnlyList<PreferenceCategory> categories)
        {
            Preferences = preferences;
            Categories = categories;
        }

        public IReadOnlyList<UserPreference> Preferences { get; }
        public IReadOnlyList<PreferenceCategory> Categories { get; }
    }

    /// <summary><c>User.SetPreferencesAsync</c> girdisi; <see cref="Channel"/> null ise tüm kanallar.</summary>
    public sealed class PreferenceInput
    {
        /// <param name="category">Kategori anahtarı (1-64 karakter).</param>
        /// <param name="optedIn">Kategoriye (ve kanala) abone mi.</param>
        /// <param name="channel"><c>null</c> veya <c>ios_push</c> | <c>android_push</c> | <c>web_push</c> | <c>email</c> | <c>sms</c>.</param>
        public PreferenceInput(string category, bool optedIn, string? channel = null)
        {
            Category = category;
            OptedIn = optedIn;
            Channel = channel;
        }

        public string Category { get; }
        public bool OptedIn { get; }
        public string? Channel { get; }
    }

    /// <summary>In-app mesaj özeti (yaşam döngüsü olaylarında ve gömülü yerleşimlerde).</summary>
    public sealed class InAppMessage
    {
        internal InAppMessage(string id, string? json)
        {
            Id = id;
            Json = json;
        }

        /// <summary>Mesaj kimliği.</summary>
        public string Id { get; }
        /// <summary>Sunucunun döndürdüğü ham mesaj JSON'u (özel işleme için).</summary>
        public string? Json { get; }
    }

    /// <summary>Bildirim teslim riski (ADR-0092; yerel Android <c>HsDeliveryRisk</c>).</summary>
    public enum DeliveryRisk
    {
        /// <summary>Bilinen bir engel yok.</summary>
        None = 0,
        /// <summary>Bildirimler gecikebilir (saldırgan pil yönetimli üretici ve pil muafiyeti yok, seyrek kullanım kovası, veri tasarrufu).</summary>
        Elevated = 1,
        /// <summary>Bildirimler büyük olasılıkla gelmiyor (bildirim izni ya da kanalı kapalı, arka plan etkinliği kısıtlı).</summary>
        High = 2,
    }

    /// <summary><see cref="DeliveryManager.OpenSettings"/> ve <see cref="DeliveryManager.ShowGuidanceAsync"/> ile açılabilen Android ayar ekranı.</summary>
    public enum DeliverySetting
    {
        /// <summary>Duruma en uygun ekran (<see cref="DeliveryStatus.RecommendedSettings"/> ilk öğesi; öneri yoksa pil optimizasyonu).</summary>
        Auto = 0,
        /// <summary>Uygulamanın bildirim ayarları.</summary>
        Notifications = 1,
        /// <summary>Üreticinin otomatik başlatma / arka planda çalışma yöneticisi (yoksa uygulama ayrıntıları).</summary>
        AutoStart = 2,
        /// <summary>Pil optimizasyonu: üreticinin pil yöneticisi, yoksa sistemin pil optimizasyonu listesi.</summary>
        BatteryOptimization = 3,
        /// <summary>Uygulama ayrıntıları (arka plan kısıtı buradan kaldırılır).</summary>
        AppDetails = 4,
    }

    /// <summary>
    /// Cihazın bildirim teslim durumu (<see cref="DeliveryManager.GetStatus"/>; yerel Android <c>HsDeliveryStatus</c>).
    /// Kişisel veri içermez: üretici, ROM ve kısıt bayrakları. Yalnızca Android cihaz derlemesinde doludur; iOS'ta, Editor'de
    /// ve SDK başlatılmadan önce <see cref="Supported"/> false, <see cref="Risk"/> <see cref="DeliveryRisk.None"/> ve
    /// <see cref="RecommendedSettings"/> boştur.
    /// </summary>
    public sealed class DeliveryStatus
    {
        private static readonly IReadOnlyList<DeliverySetting> NoSettings = new DeliverySetting[0];

        /// <summary>Teşhisin desteklenmediği platformların durumu (risk yok).</summary>
        public static DeliveryStatus Unsupported { get; } = new DeliveryStatus(
            false, string.Empty, string.Empty, "other", false, string.Empty, null, true, false, true, false, null, false, false, false,
            DeliveryRisk.None, NoSettings);

        internal DeliveryStatus(
            bool supported, string manufacturer, string brand, string vendor, bool aggressiveVendor, string rom, string? romVersion,
            bool notificationsEnabled, bool channelBlocked, bool batteryOptimizationExempt, bool backgroundRestricted, string? standbyBucket,
            bool powerSaveMode, bool dataSaverEnabled, bool restricted, DeliveryRisk risk, IReadOnlyList<DeliverySetting> recommendedSettings)
        {
            Supported = supported;
            Manufacturer = manufacturer;
            Brand = brand;
            Vendor = vendor;
            AggressiveVendor = aggressiveVendor;
            Rom = rom;
            RomVersion = romVersion;
            NotificationsEnabled = notificationsEnabled;
            ChannelBlocked = channelBlocked;
            BatteryOptimizationExempt = batteryOptimizationExempt;
            BackgroundRestricted = backgroundRestricted;
            StandbyBucket = standbyBucket;
            PowerSaveMode = powerSaveMode;
            DataSaverEnabled = dataSaverEnabled;
            Restricted = restricted;
            Risk = risk;
            RecommendedSettings = recommendedSettings;
        }

        /// <summary>Teşhis bu platformda okunabildi mi.</summary>
        public bool Supported { get; }
        /// <summary><c>Build.MANUFACTURER</c> (küçük harf).</summary>
        public string Manufacturer { get; }
        /// <summary><c>Build.BRAND</c> (küçük harf).</summary>
        public string Brand { get; }
        /// <summary>Üretici ailesi: <c>xiaomi</c>, <c>huawei</c>, <c>honor</c>, <c>oppo</c>, <c>realme</c>, <c>oneplus</c>, <c>vivo</c>, <c>samsung</c>, <c>meizu</c>, <c>asus</c>, <c>transsion</c>, <c>other</c>.</summary>
        public string Vendor { get; }
        /// <summary>Üreticinin kendi pil/otomatik başlatma yöneticisi arka plandaki uygulamaları durdurabiliyor mu.</summary>
        public bool AggressiveVendor { get; }
        /// <summary>ROM kimliği: <c>miui</c>, <c>hyperos</c>, <c>emui</c>, <c>harmonyos</c>, <c>magicos</c>, <c>coloros</c>, <c>oxygenos</c>, <c>realmeui</c>, <c>funtouchos</c>, <c>originos</c>, <c>oneui</c>, <c>flyme</c>, <c>android</c>.</summary>
        public string Rom { get; }
        public string? RomVersion { get; }
        /// <summary>Bildirim izni verilmiş ve bildirimler açık mı.</summary>
        public bool NotificationsEnabled { get; }
        /// <summary>Varsayılan bildirim kanalı kullanıcı tarafından kapatılmış mı.</summary>
        public bool ChannelBlocked { get; }
        /// <summary>Uygulama pil optimizasyonundan muaf mı.</summary>
        public bool BatteryOptimizationExempt { get; }
        /// <summary>Kullanıcı arka plan etkinliğini kısıtlamış mı.</summary>
        public bool BackgroundRestricted { get; }
        /// <summary>Bekleme kovası: <c>exempted</c>, <c>active</c>, <c>working_set</c>, <c>frequent</c>, <c>rare</c>, <c>restricted</c> (okunamadıysa null).</summary>
        public string? StandbyBucket { get; }
        /// <summary>Pil tasarrufu kipi açık mı.</summary>
        public bool PowerSaveMode { get; }
        /// <summary>Veri tasarrufu uygulamanın arka plan verisini engelliyor mu.</summary>
        public bool DataSaverEnabled { get; }
        /// <summary>Sistem arka plan kısıtı: kullanıcı kısıtlamış ya da uygulama <c>rare</c>/<c>restricted</c> kovasında.</summary>
        public bool Restricted { get; }
        public DeliveryRisk Risk { get; }
        /// <summary>Kullanıcının açması önerilen ayar ekranları (önem sırasıyla; öneri yoksa boş).</summary>
        public IReadOnlyList<DeliverySetting> RecommendedSettings { get; }
    }

    /// <summary>SDK çağrısı hatası. <see cref="Code"/> sunucu hata kodu (ör. <c>NOT_FOUND</c>) veya SDK durum kodudur.</summary>
    public sealed class PushizoException : Exception
    {
        public const string NotReady = "NOT_READY";
        public const string PlatformNotSupported = "PLATFORM_NOT_SUPPORTED";
        public const string InvalidResponse = "INVALID_RESPONSE";
        public const string InvalidArgument = "INVALID_ARGUMENT";
        /// <summary>iOS Live Activity başlatılamadı (Live Activities kapalı, <c>NSSupportsLiveActivities</c> yok vb.).</summary>
        public const string LiveActivityFailed = "LIVE_ACTIVITY_FAILED";

        public PushizoException(string code, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        /// <summary>Hata kodu.</summary>
        public string Code { get; }
    }
}
