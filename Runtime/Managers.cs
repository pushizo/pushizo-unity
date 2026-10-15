#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroSignalSdk.Internal;

namespace HeroSignalSdk
{
    /// <summary>Kullanıcı: tag, alias, dil, e-posta/SMS abonelikleri, bildirim tercihleri ve push aboneliği (<c>HeroSignal.User</c>).</summary>
    public sealed class UserManager
    {
        internal UserManager()
        {
        }

        /// <summary>Push aboneliği.</summary>
        public PushSubscriptionManager PushSubscription { get; } = new PushSubscriptionManager();

        /// <summary>Login ile verilen kullanıcı kimliği; anonimse null.</summary>
        public string? ExternalId => HeroSignal.BridgeOrNull()?.GetExternalId();

        /// <summary>HeroSignal kullanıcı kimliği (abonelik kaydından sonra).</summary>
        public string? Id => HeroSignal.BridgeOrNull()?.GetUserId();

        /// <summary>Bu cihazda kayıtlı e-posta adresleri.</summary>
        public IReadOnlyList<string> Emails => BridgeJson.ParseStringList(HeroSignal.BridgeOrNull()?.GetEmails());

        /// <summary>Bu cihazda kayıtlı SMS numaraları (E.164).</summary>
        public IReadOnlyList<string> SmsNumbers => BridgeJson.ParseStringList(HeroSignal.BridgeOrNull()?.GetSmsNumbers());

        public void AddTag(string key, string value) => AddTags(new Dictionary<string, string> { [key] = value });

        /// <summary>Tag ekler/günceller; çevrimdışıyken yerel kuyrukta bekler.</summary>
        public void AddTags(IReadOnlyDictionary<string, string> tags)
        {
            var valid = Validation.RequireTags(tags, nameof(tags));
            if (valid.Count == 0) return;
            HeroSignal.Bridge().AddTags(BridgeJson.ToJson(valid));
        }

        public void RemoveTag(string key) => RemoveTags(new[] { key });

        public void RemoveTags(IEnumerable<string> keys)
        {
            var valid = Validation.RequireKeys(keys, nameof(keys));
            if (valid.Count == 0) return;
            HeroSignal.Bridge().RemoveTags(BridgeJson.ToJson(valid));
        }

        /// <summary>Sunucudan son alınan tag'ler (yerel önbellek).</summary>
        public IReadOnlyDictionary<string, string> GetTags() => BridgeJson.ParseStringMap(HeroSignal.BridgeOrNull()?.GetTags());

        /// <summary>Alias ekler (ör. <c>AddAlias("crm_id", "42")</c>); etiket küçük harf/rakam/_ (en fazla 50).</summary>
        public void AddAlias(string label, string id) => AddAliases(new Dictionary<string, string> { [label] = id });

        /// <summary>En fazla 20 alias'ı tek istekte ekler.</summary>
        public void AddAliases(IReadOnlyDictionary<string, string> aliases) =>
            HeroSignal.Bridge().AddAliases(BridgeJson.ToJson(Validation.NormalizeAliases(aliases, nameof(aliases))));

        public void RemoveAlias(string label)
        {
            Validation.RequireAliasLabel(label, nameof(label));
            HeroSignal.Bridge().RemoveAlias(label);
        }

        /// <summary>Kullanıcının dili (ISO 639-1, ör. <c>tr</c>).</summary>
        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) throw new ArgumentException("Dil kodu gerekli.", nameof(languageCode));
            HeroSignal.Bridge().SetLanguage(languageCode.Trim());
        }

        /// <summary>E-posta aboneliği ekler (küçük harfe çevrilir); geçersiz adreste <see cref="ArgumentException"/>.</summary>
        public void AddEmail(string email) => HeroSignal.Bridge().AddEmail(Validation.RequireEmail(email, nameof(email)));

        public void RemoveEmail(string email) => HeroSignal.Bridge().RemoveEmail(Validation.RequireEmail(email, nameof(email)));

        /// <summary>SMS aboneliği ekler; numara E.164'e normalize edilir (<c>+90 (532) 123-45-67</c> → <c>+905321234567</c>).</summary>
        public void AddSms(string phoneNumber) => HeroSignal.Bridge().AddSms(Validation.RequirePhone(phoneNumber, nameof(phoneNumber)));

        public void RemoveSms(string phoneNumber) => HeroSignal.Bridge().RemoveSms(Validation.RequirePhone(phoneNumber, nameof(phoneNumber)));

        /// <summary>
        /// Kullanıcının kategori/kanal tercihleri ve app'in mesaj kategorileri (ADR-0019 §4); sonuç ana iş parçacığında
        /// teslim edilir. Abonelik kaydı tamamlanmadan <see cref="HeroSignalException"/> (<c>NOT_READY</c>); Editor'de
        /// <c>PLATFORM_NOT_SUPPORTED</c>.
        /// </summary>
        public Task<UserPreferences> GetPreferencesAsync()
        {
            var bridge = HeroSignal.Bridge();
            return HeroSignal.Call(r => bridge.GetPreferences(r), json => BridgeJson.ParsePreferences(RequireJson(json)));
        }

        /// <summary>
        /// Tercihleri yazar (1-100 kayıt; <see cref="PreferenceInput.Channel"/> null = tüm kanallar) ve güncel listeyi
        /// döndürür. Geçersiz girdide <see cref="ArgumentException"/>.
        /// </summary>
        public Task<UserPreferences> SetPreferencesAsync(IEnumerable<PreferenceInput> preferences)
        {
            var json = BridgeJson.PreferencesToJson(Validation.RequirePreferences(preferences, nameof(preferences)));
            var bridge = HeroSignal.Bridge();
            return HeroSignal.Call(r => bridge.SetPreferences(json, r), result => BridgeJson.ParsePreferences(RequireJson(result)));
        }

        private static string RequireJson(string? json) =>
            json ?? throw new HeroSignalException(HeroSignalException.InvalidResponse, "Köprü yanıtı boş.");
    }

    /// <summary>Push aboneliği (<c>HeroSignal.User.PushSubscription</c>).</summary>
    public sealed class PushSubscriptionManager
    {
        private readonly object _gate = new object();
        private PushSubscriptionState _last = PushSubscriptionState.Empty;

        internal PushSubscriptionManager()
        {
        }

        /// <summary>Abonelik kimliği, token, opt-in veya izin değiştiğinde (ana iş parçacığında, yalnızca gerçek değişiklikte).</summary>
        public event Action<PushSubscriptionChangedEventArgs>? Changed;

        public string? Id => Current.Id;
        public string? Token => Current.Token;
        public bool OptedIn => Current.OptedIn;

        /// <summary>Güncel durum.</summary>
        public PushSubscriptionState Current
        {
            get
            {
                var bridge = HeroSignal.BridgeOrNull();
                return bridge is null ? PushSubscriptionState.Empty : BridgeJson.ParseSubscriptionState(bridge.GetPushSubscription());
            }
        }

        /// <summary>Push almayı yeniden açar.</summary>
        public void OptIn() => HeroSignal.Bridge().OptIn();

        /// <summary>Push almayı kapatır (abonelik silinmez).</summary>
        public void OptOut() => HeroSignal.Bridge().OptOut();

        internal void Seed(PushSubscriptionState state)
        {
            lock (_gate) _last = state;
        }

        internal void Dispatch(PushSubscriptionState state)
        {
            PushSubscriptionState previous;
            lock (_gate)
            {
                if (state.Equals(_last)) return;
                previous = _last;
                _last = state;
            }
            HeroSignal.Raise(Changed, new PushSubscriptionChangedEventArgs(previous, state), "PushSubscription.Changed");
            HeroSignal.Notifications.DispatchPermission(state.Permission);
        }

        internal void Reset()
        {
            lock (_gate) _last = PushSubscriptionState.Empty;
            Changed = null;
        }
    }

    /// <summary>Bildirimler (<c>HeroSignal.Notifications</c>): izin, tıklama, ön plan gösterimi ve temizleme.</summary>
    public sealed class NotificationsManager
    {
        private readonly object _gate = new object();
        private Action<NotificationClickedEventArgs>? _clicked;
        private Action<NotificationWillDisplayEventArgs>? _willDisplay;
        private NotificationClickedEventArgs? _pendingClick;
        private bool? _lastPermission;

        internal NotificationsManager()
        {
        }

        /// <summary>
        /// Bildirime (veya aksiyon düğmesine) tıklandı. Uygulama bildirimle açıldığında henüz abone yoksa son tıklama
        /// saklanır ve ilk abone eklendiğinde iletilir.
        /// </summary>
        public event Action<NotificationClickedEventArgs>? Clicked
        {
            add
            {
                NotificationClickedEventArgs? pending;
                lock (_gate)
                {
                    _clicked += value;
                    pending = _pendingClick;
                    _pendingClick = null;
                }
                if (pending != null && value != null) HeroSignal.Raise(value, pending, "Notifications.Clicked");
            }
            remove
            {
                lock (_gate) _clicked -= value;
            }
        }

        /// <summary>
        /// Uygulama ön plandayken bildirim geldi. Abone varken yerel gösterim ertelenir; işleyiciler
        /// <see cref="NotificationWillDisplayEventArgs.PreventDefault"/> çağırmazsa bildirim gösterilir.
        /// </summary>
        public event Action<NotificationWillDisplayEventArgs>? ForegroundWillDisplay
        {
            add
            {
                bool changed;
                lock (_gate)
                {
                    var before = _willDisplay != null;
                    _willDisplay += value;
                    changed = before != (_willDisplay != null);
                }
                if (changed) HeroSignal.BridgeOrNull()?.SetForegroundHandlerEnabled(true);
            }
            remove
            {
                bool changed;
                lock (_gate)
                {
                    var before = _willDisplay != null;
                    _willDisplay -= value;
                    changed = before != (_willDisplay != null);
                }
                if (changed) HeroSignal.BridgeOrNull()?.SetForegroundHandlerEnabled(false);
            }
        }

        /// <summary>Sistem bildirim izni değişti (yalnızca gerçek değişiklikte).</summary>
        public event Action<bool>? PermissionChanged;

        /// <summary>Sistem bildirim izni var mı.</summary>
        public bool Permission => HeroSignal.BridgeOrNull()?.GetPermission() ?? false;

        internal bool HasForegroundHandlers
        {
            get
            {
                lock (_gate) return _willDisplay != null;
            }
        }

        /// <summary>
        /// Sistem bildirim iznini ister; sonuç izin durumudur. <paramref name="fallbackToSettings"/> true ise izin daha önce
        /// kalıcı olarak reddedildiğinde uygulamanın bildirim ayarları açılır.
        /// </summary>
        public Task<bool> RequestPermissionAsync(bool fallbackToSettings = false) => HeroSignal.RequestPermission(fallbackToSettings);

        /// <summary>Bildirim merkezindeki bu uygulamaya ait tüm bildirimleri kaldırır (iOS'ta rozet de sıfırlanır).</summary>
        public void ClearAll() => HeroSignal.Bridge().ClearAllNotifications();

        /// <summary>
        /// Tek bir HeroSignal bildirimini Android tepsi kimliğiyle (<see cref="HeroSignalNotification.AndroidNotificationId"/>)
        /// kaldırır. iOS'ta kimlik metne çevrilerek <c>UNNotificationRequest.identifier</c> olarak aranır.
        /// </summary>
        public void RemoveNotification(int androidNotificationId) =>
            HeroSignal.Bridge().RemoveNotification(androidNotificationId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>
        /// Tek bildirimi kaldırır. iOS: bildirim isteği kimliği (<c>UNNotificationRequest.identifier</c>). Android: tepsi
        /// kimliğinin metni; tamsayıya çevrilemezse <see cref="ArgumentException"/> fırlatılır.
        /// </summary>
        public void RemoveNotification(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) throw new ArgumentException("Bildirim kimliği gerekli.", nameof(identifier));
            HeroSignal.Bridge().RemoveNotification(identifier.Trim());
        }

        /// <summary>Aynı gruptaki bildirimleri kaldırır (Android <c>android_group</c>, iOS <c>thread-id</c>).</summary>
        public void RemoveGroupedNotifications(string group)
        {
            if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Grup anahtarı gerekli.", nameof(group));
            HeroSignal.Bridge().RemoveGroupedNotifications(group);
        }

        internal void DispatchClicked(NotificationClickedEventArgs args)
        {
            Action<NotificationClickedEventArgs>? handler;
            lock (_gate)
            {
                handler = _clicked;
                if (handler is null)
                {
                    _pendingClick = args;
                    return;
                }
            }
            HeroSignal.Raise(handler, args, "Notifications.Clicked");
        }

        internal void DispatchWillDisplay(NotificationWillDisplayEventArgs args)
        {
            Action<NotificationWillDisplayEventArgs>? handler;
            lock (_gate) handler = _willDisplay;
            HeroSignal.Raise(handler, args, "Notifications.ForegroundWillDisplay");
        }

        internal void DispatchPermission(bool permission)
        {
            lock (_gate)
            {
                var previous = _lastPermission;
                _lastPermission = permission;
                // İlk gözlem (Initialize sırasında) başlangıç durumudur; değişiklik sayılmaz.
                if (previous is null || previous == permission) return;
            }
            HeroSignal.Raise(PermissionChanged, permission, "Notifications.PermissionChanged");
        }

        internal void Reset()
        {
            lock (_gate)
            {
                _clicked = null;
                _willDisplay = null;
                _pendingClick = null;
                _lastPermission = null;
            }
            PermissionChanged = null;
        }
    }

    /// <summary>
    /// In-app mesajlar (<c>HeroSignal.InAppMessages</c>): tetikler yerel SDK'da değerlendirilir (<c>session_start</c>,
    /// <see cref="SessionManager.SendEvent"/> adı, <see cref="SetScreen"/>, <see cref="AddTrigger"/> koşulları); mesajlar
    /// yerel arayüzle (Android Dialog, iOS UIKit) oyunun üzerinde çizilir.
    /// </summary>
    public sealed class InAppMessagesManager
    {
        internal InAppMessagesManager()
        {
        }

        public event Action<InAppMessageClickedEventArgs>? Clicked;
        public event Action<InAppMessageLifecycleEventArgs>? WillDisplay;
        public event Action<InAppMessageLifecycleEventArgs>? DidDisplay;
        public event Action<InAppMessageLifecycleEventArgs>? WillDismiss;
        public event Action<InAppMessageLifecycleEventArgs>? DidDismiss;

        public void AddTrigger(string key, string value) => AddTriggers(new Dictionary<string, string> { [key] = value });

        /// <summary>Trigger ekler/günceller (anahtar 1-64, değer en fazla 200 karakter).</summary>
        public void AddTriggers(IReadOnlyDictionary<string, string> triggers)
        {
            var valid = Validation.RequireTriggers(triggers, nameof(triggers));
            if (valid.Count == 0) return;
            HeroSignal.Bridge().AddTriggers(BridgeJson.ToJson(valid));
        }

        public void RemoveTrigger(string key) => RemoveTriggers(new[] { key });

        public void RemoveTriggers(IEnumerable<string> keys)
        {
            var valid = Validation.RequireKeys(keys, nameof(keys));
            if (valid.Count == 0) return;
            HeroSignal.Bridge().RemoveTriggers(BridgeJson.ToJson(valid));
        }

        public void ClearTriggers() => HeroSignal.Bridge().ClearTriggers();

        public IReadOnlyDictionary<string, string> GetTriggers() => BridgeJson.ParseStringMap(HeroSignal.BridgeOrNull()?.GetTriggers());

        /// <summary>Geçerli ekran/sahne adı (<c>screen</c> tetikleri); çıkarken null.</summary>
        public void SetScreen(string? name) =>
            HeroSignal.Bridge().SetScreen(string.IsNullOrWhiteSpace(name) ? null : name!.Trim());

        /// <summary>true iken yeni mesaj gösterilmez (ör. oyun içi ara sahne); tetikler kaydedilmeye devam eder.</summary>
        public bool Paused
        {
            get => HeroSignal.BridgeOrNull()?.GetInAppPaused() ?? false;
            set => HeroSignal.Bridge().SetInAppPaused(value);
        }

        internal void DispatchClicked(InAppMessageClickedEventArgs args) => HeroSignal.Raise(Clicked, args, "InAppMessages.Clicked");

        internal void DispatchLifecycle(string phase, InAppMessage message)
        {
            var args = new InAppMessageLifecycleEventArgs(message);
            switch (phase)
            {
                case "willDisplay": HeroSignal.Raise(WillDisplay, args, "InAppMessages.WillDisplay"); break;
                case "didDisplay": HeroSignal.Raise(DidDisplay, args, "InAppMessages.DidDisplay"); break;
                case "willDismiss": HeroSignal.Raise(WillDismiss, args, "InAppMessages.WillDismiss"); break;
                case "didDismiss": HeroSignal.Raise(DidDismiss, args, "InAppMessages.DidDismiss"); break;
            }
        }

        internal void Reset()
        {
            Clicked = null;
            WillDisplay = null;
            DidDisplay = null;
            WillDismiss = null;
            DidDismiss = null;
        }
    }

    /// <summary>
    /// Bildirim kutusu (<c>HeroSignal.Inbox</c>). Abonelik/kullanıcı kaydı tamamlanmadan çağrılar
    /// <see cref="HeroSignalException"/> (<c>NOT_READY</c>) ile sonuçlanır. Sonuçlar ana iş parçacığında teslim edilir.
    /// </summary>
    public sealed class InboxManager
    {
        internal InboxManager()
        {
        }

        /// <summary>Öğeler (yeniden eskiye), okunmamış sayısı ve sonraki sayfa imleci (<paramref name="limit"/> 1-200).</summary>
        public Task<InboxPage> ListAsync(int limit = 50, string? cursor = null)
        {
            Validation.RequireInboxLimit(limit, nameof(limit));
            var bridge = HeroSignal.Bridge();
            var c = string.IsNullOrEmpty(cursor) ? null : cursor;
            return HeroSignal.Call(r => bridge.InboxList(limit, c, r), json => BridgeJson.ParseInboxPage(Require(json)));
        }

        /// <summary>Okundu işaretler; güncel öğeyi döndürür.</summary>
        public Task<InboxItem> MarkReadAsync(string id)
        {
            var valid = RequireId(id);
            var bridge = HeroSignal.Bridge();
            return HeroSignal.Call(r => bridge.InboxMarkRead(valid, r), json => BridgeJson.ParseInboxItem(Require(json)));
        }

        /// <summary>Tümünü okundu işaretler; kalan okunmamış sayısını döndürür.</summary>
        public Task<int> MarkAllReadAsync()
        {
            var bridge = HeroSignal.Bridge();
            return HeroSignal.Call(r => bridge.InboxMarkAllRead(r), json => BridgeJson.ParseUnreadCount(Require(json)));
        }

        /// <summary>Öğeyi kutudan kaldırır (arşivler).</summary>
        public Task ArchiveAsync(string id)
        {
            var valid = RequireId(id);
            var bridge = HeroSignal.Bridge();
            return HeroSignal.Call<object?>(r => bridge.InboxArchive(valid, r), _ => null);
        }

        /// <summary>Okunmamış öğe sayısı.</summary>
        public Task<int> UnreadCountAsync()
        {
            var bridge = HeroSignal.Bridge();
            return HeroSignal.Call(r => bridge.InboxUnreadCount(r), json => BridgeJson.ParseUnreadCount(Require(json)));
        }

        private static string RequireId(string id) =>
            string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Öğe kimliği gerekli.", nameof(id)) : id;

        private static string Require(string? json) =>
            json ?? throw new HeroSignalException(HeroSignalException.InvalidResponse, "Köprü yanıtı boş.");
    }

    /// <summary>Oturum ve özel olaylar (<c>HeroSignal.Session</c>).</summary>
    public sealed class SessionManager
    {
        internal SessionManager()
        {
        }

        /// <summary>
        /// Özel olay / outcome gönderir (çevrimdışıyken kuyrukta bekler); olay adı in-app tetiklerini de çalıştırır.
        /// Özellikler: metin, sayı, bool, tarih, Guid, enum, iç içe sözlük ve liste (en fazla 16 seviye).
        /// </summary>
        public void SendEvent(string name, IReadOnlyDictionary<string, object?>? properties = null, double? value = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Olay adı gerekli.", nameof(name));
            if (value is double v && (double.IsNaN(v) || double.IsInfinity(v)))
                throw new ArgumentException("Olay değeri sonlu bir sayı olmalı.", nameof(value));
            var json = Json.Serialize(EventProperties.Normalize(properties, nameof(properties)));
            HeroSignal.Bridge().SendEvent(name.Trim(), json, value ?? 0d, value.HasValue);
        }
    }

    /// <summary>
    /// iOS Live Activities (<c>HeroSignal.LiveActivities</c>, ADR-0024). Köprü, genel
    /// <c>HeroSignalDefaultLiveActivityAttributes</c> türünü kullanır; sunucuda (dashboard/API) tür adı
    /// <see cref="DefaultActivityType"/> olarak tanımlanır ve widget extension'da
    /// <c>ActivityConfiguration(for: HeroSignalDefaultLiveActivityAttributes.self)</c> yazılır. Android ve Editor'de
    /// çağrılar <see cref="HeroSignalException"/> (<c>PLATFORM_NOT_SUPPORTED</c>) ile sonuçlanır. Sonuçlar ana iş
    /// parçacığında teslim edilir.
    /// </summary>
    public sealed class LiveActivitiesManager
    {
        /// <summary>Genel türün sunucudaki adı.</summary>
        public const string DefaultActivityType = "HeroSignalDefaultLiveActivityAttributes";

        /// <summary>Live Activities iOS'a özgüdür (Android ve Editor'de aynı hata).</summary>
        internal const string UnsupportedMessage = "Live Activities yalnızca iOS'ta desteklenir.";

        internal LiveActivitiesManager()
        {
        }

        /// <summary>
        /// Genel türü izlemeye alır: push-to-start belirteci (iOS 17.2+) ve başlayan her etkinliğin güncelleme belirteci
        /// sunucuya kaydedilir. <see cref="HeroSignal.Initialize"/>'dan sonra, uygulama her açıldığında çağrılmalıdır.
        /// Hata: <c>PLATFORM_NOT_SUPPORTED</c> (iOS 16.1 öncesi, Android, Editor), <c>NOT_READY</c> (yerel SDK başlatılmadı).
        /// </summary>
        public Task SetupDefaultLiveActivityAsync()
        {
            var bridge = HeroSignal.Bridge();
            if (!bridge.SupportsLiveActivities) return NotSupported();
            return HeroSignal.Call<object?>(r => bridge.SetupDefaultLiveActivity(r), _ => null);
        }

        /// <summary>
        /// Genel türle yerel bir etkinlik başlatır (iOS 16.1+); sunucu sonraki <c>update</c>/<c>end</c> çağrılarını
        /// <paramref name="activityId"/> ile yapar. Değerler metin (en çok 4000 karakter), sonlu sayı veya bool; anahtarlar
        /// 1-64 karakter; <paramref name="attributes"/> içinde <c>herosignal</c> anahtarı ayrılmıştır. Widget'ta değerler
        /// <c>context.attributes.data["…"]</c> ve <c>context.state.data["…"]</c> ile okunur. Hata: <c>INVALID_ARGUMENT</c>,
        /// <c>PLATFORM_NOT_SUPPORTED</c>, <c>NOT_READY</c>, <c>LIVE_ACTIVITY_FAILED</c> (Live Activities kapalı vb.).
        /// </summary>
        public Task StartDefaultLiveActivityAsync(string activityId, IDictionary<string, object> attributes, IDictionary<string, object> contentState)
        {
            var bridge = HeroSignal.Bridge();
            string id, attributesJson, contentStateJson;
            try
            {
                id = Validation.RequireLiveActivityId(activityId);
                attributesJson = Json.Serialize(Validation.NormalizeLiveActivityValues(attributes, nameof(attributes), reserveHeroSignalKey: true));
                contentStateJson = Json.Serialize(Validation.NormalizeLiveActivityValues(contentState, nameof(contentState), reserveHeroSignalKey: false));
            }
            catch (HeroSignalException e)
            {
                // Tüm Live Activity hataları (yerel SDK'dan gelenlerle aynı biçimde) Task üzerinden iletilir.
                return Task.FromException(e);
            }
            if (!bridge.SupportsLiveActivities) return NotSupported();
            return HeroSignal.Call<object?>(r => bridge.StartDefaultLiveActivity(id, attributesJson, contentStateJson, r), _ => null);
        }

        private static Task NotSupported() =>
            Task.FromException(new HeroSignalException(HeroSignalException.PlatformNotSupported, UnsupportedMessage));
    }

    /// <summary>Hata ayıklama (<c>HeroSignal.Debug</c>).</summary>
    public sealed class DebugManager
    {
        private LogLevel? _explicitLevel;

        internal DebugManager()
        {
        }

        public LogLevel LogLevel => SdkLog.Level;

        /// <summary>
        /// C# katmanının ve yerel SDK'nın günlük seviyesini ayarlar. <see cref="HeroSignal.Initialize"/> öncesinde çağrılırsa
        /// <see cref="HeroSignalOptions.LogLevel"/> yerine bu değer kullanılır.
        /// </summary>
        public void SetLogLevel(LogLevel level)
        {
            if (!Enum.IsDefined(typeof(LogLevel), level)) throw new ArgumentOutOfRangeException(nameof(level), level, "Geçersiz günlük seviyesi.");
            _explicitLevel = level;
            SdkLog.Level = level;
            HeroSignal.BridgeOrNull()?.SetLogLevel((int)level);
        }

        internal LogLevel? ExplicitLevel => _explicitLevel;

        internal void Reset() => _explicitLevel = null;
    }
}
