#nullable enable
using System;

namespace PushizoSdk.Internal
{
    /// <summary>
    /// Yerel köprü sonucu: başarıda <paramref name="json"/> (işleme göre null olabilir), hatada
    /// <paramref name="errorCode"/> + <paramref name="errorMessage"/>. Herhangi bir iş parçacığından çağrılabilir.
    /// </summary>
    internal delegate void NativeResult(string? json, string? errorCode, string? errorMessage);

    /// <summary>
    /// Yerel köprü sözleşmesi: Android'de <c>com.pushizo.unity.PushizoUnityBridge</c> (Java,
    /// Runtime/Plugins/Android), iOS'ta <c>hs_*</c> C fonksiyonları (Swift, Runtime/Plugins/iOS), Editor'de
    /// <see cref="EditorNativeBridge"/>. Karmaşık değerler JSON metni olarak taşınır (biçimler <see cref="BridgeJson"/>).
    /// </summary>
    internal interface INativeBridge
    {
        bool IsNative { get; }

        /// <summary>Yerel SDK'yı başlatır; olaylar <c>(tür, json)</c> olarak herhangi bir iş parçacığından gelir.</summary>
        void Start(string appId, string optionsJson, Action<string, string> onEvent);

        void SetConsentGiven(bool given);
        /// <summary>Kanıt app'in kimlik doğrulama kipine uymuyorsa hata mesajı döner (giriş yapılmaz); başarıda null.</summary>
        string? Login(string externalId, string? identityHash, string? jwt);

        /// <summary>
        /// Giriş + profil (yerel <c>login(externalId, profile, …)</c>). <paramref name="profileJson"/>:
        /// <c>{"email"?, "sms"?, "tags"?: {}, "aliases"?: {}}</c> (C# katmanında doğrulanmış/normalize edilmiş). Geçersiz
        /// profil veya kanıt hatasında mesaj döner ve giriş yapılmaz; başarıda null.
        /// </summary>
        string? LoginWithProfile(string externalId, string profileJson, string? identityHash, string? jwt);

        /// <summary>Kimlik JWT'sini yeniler; giriş yapılmamışsa hata mesajı döner, başarıda null.</summary>
        string? UpdateUserJwt(string jwt);
        void Logout();
        void SetLogLevel(int level);
        string? GetExternalId();
        string? GetUserId();
        void AddTags(string tagsJson);
        void RemoveTags(string keysJson);
        string GetTags();
        void AddAliases(string aliasesJson);
        void RemoveAlias(string label);
        void SetLanguage(string languageCode);
        void AddEmail(string email);
        void RemoveEmail(string email);
        void AddSms(string phoneNumber);
        void RemoveSms(string phoneNumber);
        /// <summary>Fire OS ADM kayıt kimliği (yalnızca Android). Geçersiz kimlikte hata mesajı, başarıda ve diğer platformlarda null.</summary>
        string? RegisterAdm(string token);
        string GetEmails();
        string GetSmsNumbers();
        string GetPushSubscription();
        void OptIn();
        void OptOut();
        bool GetPermission();
        void RequestPermission(bool fallbackToSettings, Action<bool> completion);
        void ClearAllNotifications();
        /// <summary>Tek bildirimi kaldırır. Android: tepsi kimliği (tamsayı metni); iOS: <c>UNNotificationRequest.identifier</c>.</summary>
        void RemoveNotification(string id);
        /// <summary>Android <c>android_group</c> / iOS <c>thread-id</c> grubundaki bildirimleri kaldırır.</summary>
        void RemoveGroupedNotifications(string group);

        /// <summary>Uygulama rozetini ayarlar (yalnızca iOS; Android'de rozet görünen bildirimlerden hesaplanır, çağrı etkisizdir).</summary>
        void SetBadgeCount(int count);

        /// <summary>
        /// true: ön planda gelen bildirim yerelde ertelenir ve <c>foreground</c> olayı (<c>eventId</c> ile) üretilir;
        /// karar <see cref="ForegroundDisplay"/> ile verilir. false: SDK varsayılanı (bildirim gösterilir).
        /// </summary>
        void SetForegroundHandlerEnabled(bool enabled);

        void ForegroundDisplay(string eventId);
        void AddTriggers(string triggersJson);
        void RemoveTriggers(string keysJson);
        void ClearTriggers();
        string GetTriggers();
        void SetScreen(string? name);
        bool GetInAppPaused();
        void SetInAppPaused(bool paused);
        void InboxList(int limit, string? cursor, NativeResult result);
        void InboxMarkRead(string id, NativeResult result);
        void InboxMarkAllRead(NativeResult result);
        void InboxArchive(string id, NativeResult result);
        void InboxUnreadCount(NativeResult result);

        /// <summary>İçerik kartları (ADR-0082): <c>{"cards":[…],"unviewedCount":n}</c>.</summary>
        void ContentCardsList(int limit, NativeResult result);

        /// <summary>Kart olayı: <c>impression</c>, <c>click</c> veya <c>dismiss</c>; güncel kartı döndürür.</summary>
        void ContentCardEvent(string id, string eventName, NativeResult result);

        /// <summary>
        /// Canlı sohbet çağrısı (ADR-0040). <paramref name="method"/>: start, getConfig, getConversation, getMessages,
        /// sendMessage, sendAttachment (base64), markRead, refreshUnreadCount, getUnreadCount, leaveOfflineMessage, setTyping,
        /// startListening, stopListening, attachmentUrl, getVisitorId, isListening. Başarı JSON'u yerel modelin camelCase sözlüğü
        /// (sayaçlar <c>{"unreadCount"}</c>, adres <c>{"url"}</c>, kimlik <c>{"visitorId"}</c>, dinleme <c>{"listening"}</c>;
        /// dönüşsüz işlemlerde null).
        /// </summary>
        void ChatCall(string method, string argsJson, NativeResult result);

        /// <summary>
        /// Başarı JSON'u: <c>{"preferences":[{"category","channel","optedIn","explicit","source","updatedAt"}],
        /// "categories":[{"key","name":{},"description":{},"channels":[…]|null,"defaultOptedIn"}]}</c>.
        /// </summary>
        void GetPreferences(NativeResult result);

        /// <summary>
        /// <paramref name="preferencesJson"/>: <c>[{"category","channel":null|"…","optedIn"}]</c>; başarı JSON'u
        /// <see cref="GetPreferences"/> ile aynı (güncel liste).
        /// </summary>
        void SetPreferences(string preferencesJson, NativeResult result);
        /// <summary>Reklam kişiselleştirme rızası (ADR-0037); başarı JSON'u <see cref="GetPreferences"/> ile aynı.</summary>
        void SetAdsPersonalizationConsent(bool granted, NativeResult result);

        /// <summary>Rıza kayıtları. Başarı JSON'u: <c>{"consents":[{"purpose","granted","source","updatedAt"}]}</c> (zaman epoch ms).</summary>
        void GetConsents(NativeResult result);

        /// <summary>Mobil reklam kimliği (GAID/IDFA; ADR-0037); null sunucudaki kaydı siler.</summary>
        void SetAdvertisingId(string? id);
        void SendEvent(string name, string propertiesJson, double value, bool hasValue);

        /// <summary>
        /// Yerel SDK'da Live Activities var mı (yalnızca iOS). false ise <see cref="LiveActivitiesManager"/> çağrıları köprüye
        /// iletmeden <c>PLATFORM_NOT_SUPPORTED</c> ile reddeder.
        /// </summary>
        bool SupportsLiveActivities { get; }

        /// <summary>
        /// Genel <c>PushizoDefaultLiveActivityAttributes</c> türünü izlemeye alır. Başarı JSON'u: null; hata kodları
        /// <c>PLATFORM_NOT_SUPPORTED</c> | <c>NOT_READY</c> | <c>INVALID_ARGUMENT</c> | <c>LIVE_ACTIVITY_FAILED</c>.
        /// </summary>
        void SetupDefaultLiveActivity(NativeResult result);

        /// <summary>
        /// Genel türle yerel etkinlik başlatır. <paramref name="attributesJson"/> / <paramref name="contentStateJson"/>: düz
        /// JSON nesnesi (değerler metin, sayı, bool). Başarı JSON'u: null; hata kodları <see cref="SetupDefaultLiveActivity"/> ile aynı.
        /// </summary>
        void StartDefaultLiveActivity(string activityId, string attributesJson, string contentStateJson, NativeResult result);

        /// <summary>Konum paylaşımı açık mı (yerel SDK'nın kalıcı tercihi; izin durumundan bağımsız).</summary>
        bool GetLocationShared();

        /// <summary>Konum paylaşımını açar/kapatır; kapatınca yerel SDK sunucuya <c>location: null</c> gönderir.</summary>
        void SetLocationShared(bool shared);

        /// <summary>
        /// Ön plan konum iznini ister (önce açıklama diyaloğu; <paramref name="rationale"/> null ise SDK'nın varsayılan
        /// metni). Sonuç: izin var/verildi mi; herhangi bir iş parçacığından gelebilir.
        /// </summary>
        void RequestLocationPermission(string? rationale, Action<bool> completion);

        /// <summary>Ön plan konum izni verilmiş mi (Android yaklaşık/hassas; iOS "kullanırken" veya "her zaman").</summary>
        bool GetLocationPermissionGranted();

        /// <summary>Arka plan konum izni verilmiş mi (Android <c>ACCESS_BACKGROUND_LOCATION</c>; iOS "her zaman").</summary>
        bool GetLocationBackgroundPermissionGranted();

        /// <summary>
        /// Arka plan konum iznini ister (Android: açıklama diyaloğu + sistem izni; iOS: "her zaman" izni,
        /// <paramref name="rationale"/> yok sayılır). Sonuç: arka plan izni var/verildi mi; herhangi bir iş parçacığından gelebilir.
        /// </summary>
        void RequestBackgroundLocationPermission(string? rationale, Action<bool> completion);

        /// <summary>Arka plan bölge izleme açık mı (ADR-0065; yerel SDK'nın kalıcı tercihi, varsayılan kapalı).</summary>
        bool GetGeofencingEnabled();

        /// <summary>Bölge izlemeyi açar/kapatır. Köprü izin istemez; arka plan izni ve platform tanımları uygulamanındır.</summary>
        void SetGeofencingEnabled(bool enabled);

        /// <summary>
        /// Android teslim durumu (ADR-0092) JSON nesnesi olarak: <c>manufacturer</c>, <c>brand</c>, <c>vendor</c>,
        /// <c>aggressiveVendor</c>, <c>rom</c>, <c>romVersion</c>, <c>notificationsEnabled</c>, <c>channelBlocked</c>,
        /// <c>batteryOptimizationExempt</c>, <c>backgroundRestricted</c>, <c>standbyBucket</c>, <c>powerSaveMode</c>,
        /// <c>dataSaverEnabled</c>, <c>restricted</c>, <c>risk</c>, <c>recommendedSettings</c>. Teşhisin olmadığı platformda
        /// (iOS, Editor) veya yerel SDK başlatılmadıysa null.
        /// </summary>
        string? DeliveryStatus();

        /// <summary>Teslim riski var ve hazır diyalog son <paramref name="minIntervalDays"/> günde gösterilmedi mi (Android dışında false).</summary>
        bool DeliveryShouldShowGuidance(int minIntervalDays);

        /// <summary>
        /// Ayar ekranını açar. <paramref name="setting"/>: <c>auto</c> | <c>notifications</c> | <c>auto_start</c> |
        /// <c>battery_optimization</c> | <c>app_details</c>. Ekran açıldıysa true (Android dışında false); istisna atmaz.
        /// </summary>
        bool DeliveryOpenSettings(string setting);

        /// <summary>
        /// Hazır açıklama diyaloğu (<paramref name="message"/> null ise SDK'nın yerelleştirilmiş metni). Sonuç: kullanıcı
        /// kabul etti ve ayar ekranı açıldı mı (Android dışında false); herhangi bir iş parçacığından gelebilir.
        /// </summary>
        void DeliveryShowGuidance(string setting, string? message, Action<bool> completion);

        /// <summary>
        /// Özellik bayraklarını hemen yeniler (ADR-0066). Sonuç: değerler güncellendi mi; herhangi bir iş parçacığından
        /// gelebilir.
        /// </summary>
        void FeatureFlagsRefresh(Action<bool> completion);

        /// <summary>Bayrak açık mı (cihazdaki son değerler); değer yoksa <paramref name="defaultValue"/>.</summary>
        bool FeatureFlagIsEnabled(string key, bool defaultValue);

        /// <summary>Bayrağın değeri <c>{"value":…}</c> olarak (metin, sayı, bool, nesne veya dizi); değer yoksa null.</summary>
        string? FeatureFlagValue(string key);

        /// <summary>
        /// Gömülü yerleşimin içeriği (ADR-0068). Başarı JSON'u: <c>{"messages":[{"id","json"}]}</c> (<c>json</c>: sunucudaki
        /// ham mesaj JSON'u metin olarak). Yerel köprü dönen mesajları kimliğe göre sınırlı bir önbellekte tutar
        /// (<see cref="PlacementsReport"/> için).
        /// </summary>
        void PlacementsList(string key, int limit, NativeResult result);

        /// <summary>
        /// Yerleşim olayını bildirir. <paramref name="eventName"/>: <c>impression</c> | <c>click</c> | <c>dismiss</c>;
        /// <paramref name="buttonId"/> yalnızca <c>click</c> için. Mesaj yerel önbellekte yoksa çağrı yok sayılır.
        /// </summary>
        void PlacementsReport(string messageId, string eventName, string? buttonId);
    }
}
