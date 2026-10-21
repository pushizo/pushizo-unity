<!-- pushizo:header -->
<p align="center">
  <a href="https://pushizo.com"><img src="https://raw.githubusercontent.com/pushizo/.github/main/profile/assets/mark.svg" alt="Pushizo" width="56" height="56"></a>
</p>
<p align="center">
  <a href="https://docs.pushizo.com/docs/sdk/unity"><img src="https://img.shields.io/badge/docs-docs.pushizo.com-5b4bdb" alt="Docs"></a>
  <a href="https://docs.pushizo.com/docs/api"><img src="https://img.shields.io/badge/API-reference-b44fc4" alt="API reference"></a>
  <a href="https://github.com/pushizo/pushizo-unity/blob/main/LICENSE.md"><img src="https://img.shields.io/badge/license-MIT-16130f" alt="License: MIT"></a>
</p>
<p align="center"><a href="#english">English</a> · <a href="#türkçe">Türkçe</a></p>

## English

**Pushizo Unity SDK** — Unity package (UPM) for push notifications, in-app messages, notification inbox, users, tags and aliases, and email and SMS subscriptions. Wraps the native Pushizo Android and iOS SDKs. Unity 2021.3+.

> **Status:** The package installs from this repository; its native Android and iOS dependencies are being published to Maven Central and CocoaPods.<br>
> **Durum:** Paket bu depodan kurulur; yerel Android ve iOS bağımlılıklarının Maven Central ve CocoaPods yayını hazırlanıyor.

```json
// Packages/manifest.json
"com.pushizo.unity": "https://github.com/pushizo/pushizo-unity.git#0.1.0"
```

The detailed guide below is in Turkish; code samples are language-neutral. Full documentation:
[docs.pushizo.com/docs/sdk/unity](https://docs.pushizo.com/docs/sdk/unity) · API reference: [docs.pushizo.com/docs/api](https://docs.pushizo.com/docs/api).

## Türkçe
<!-- /pushizo:header -->

# Pushizo Unity SDK (`com.pushizo.unity`)

Push bildirimleri, in-app mesajlar, bildirim kutusu, kullanıcı/tag/alias ve e-posta/SMS abonelikleri için Unity
paketi (UPM). Yerel Pushizo Android (`com.pushizo:pushizo-android`) ve iOS (`Pushizo` Swift) SDK'larını
sarar; tetik değerlendirme, çevrimdışı kuyruk, teslimat geri bildirimi ve in-app çizimi yerel SDK'dadır. API yüzeyi
eski sağlayıcı v5 ile uyumlu, Flutter eklentisi ve .NET MAUI SDK'sıyla aynıdır.

| Platform | Davranış |
|----------|----------|
| Android cihaz (minSdk 23) | Yerel Android SDK'sı; Java köprüsü `Runtime/Plugins/Android` |
| iOS cihaz (iOS 15+) | Yerel iOS SDK'sı; Swift köprüsü `Runtime/Plugins/iOS` (`[DllImport("__Internal")]`) |
| Unity Editor, Standalone, WebGL | Ağ çağrısı ve push yok; çağrılar günlüğe yazılır, tag/trigger/e-posta ve konum tercihi bellekte tutulur, izin istekleri `false`, bildirim kutusu ve tercihler `PLATFORM_NOT_SUPPORTED` |

Gereksinimler: Unity 2021.3+, [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver)
(Android Gradle ve iOS CocoaPods bağımlılıkları için), iOS için CocoaPods.

## Kurulum

`Packages/manifest.json` (ya da Window → Package Manager → + → Add package from git URL):

```json
{
  "dependencies": {
    "com.pushizo.unity": "https://github.com/pushizo/pushizo-unity.git#0.1.0"
  }
}
```

Project Settings → **Pushizo**: App ID, API URL (iOS Notification Service Extension için), APNs ortamı, App Group
ve `google-services.json` yolu.

### Android
- Firebase konsolundan `google-services.json` indirip `Assets/google-services.json` yoluna koyun. Derleme sırasında
  Firebase değerleri (`google_app_id`, `gcm_defaultSenderId`, `google_api_key`, `project_id`) unityLibrary
  kaynaklarına yazılır; Firebase Unity SDK'sı gerekmez.
- Player Settings → Minimum API Level 23. `POST_NOTIFICATIONS` izni ve FCM servisi yerel SDK manifestinden birleşir.
- Gradle bağımlılıkları EDM4U ile çözülür (`Editor/PushizoDependencies.xml`): yerel SDK, Kotlin stdlib, AndroidX Core.

### iOS
Derleme sonrası işlem (`Editor/PushizoIosPostProcessor.cs`) otomatik olarak:
- Push Notifications yeteneği (`aps-environment`) ve Background Modes → Remote notifications,
- isteğe bağlı App Group,
- Swift köprüsü için derleme ayarları (`SWIFT_VERSION`, Swift çalışma zamanı gömme),
- `Classes/Preprocessor.h` → `UNITY_USES_REMOTE_NOTIFICATIONS 1` (Unity bu değeri yalnız kendi bildirim API'leri
  kullanıldığında açar; kapalıyken `UnityAppController` APNs token'ını almaz ve köprüye iletmez),
- **PushizoNotificationServiceExtension** hedefi (zengin medya, rozet, teslimat onayı) ve Podfile'a
  `Pushizo/Extension` pod'u ekler (EDM4U Podfile'ı 40'ta üretir, eklenti 45'te çalışır, `pod install` 50'de).

APNs token'ı Unity'nin `UnityAppController` bildirimlerinden alınır; AppDelegate değişikliği gerekmez.
Xcode'da imzalama ekibini her iki hedef için seçin (Player Settings → iOS → Signing Team ID verildiyse otomatik).

### Yerel SDK'yı kaynaktan kullanma
Yerel SDK'lar Maven Central/CocoaPods'a yayımlanana kadar açık depolardan kullanılabilir:
- Android: `git clone https://github.com/pushizo/pushizo-android && gradle -p pushizo-android :pushizo:publishToMavenLocal`,
  ardından Unity'de Player Settings → Publishing Settings → Custom Main Gradle Template / Settings Template'te
  `mavenLocal()` deposunu ekleyin.
- iOS: oluşan Podfile'da `pod 'Pushizo/App', :git => 'https://github.com/pushizo/pushizo-ios.git', :tag => '0.1.0'`
  (NSE hedefinde `Pushizo/Extension` aynı kaynakla).

## Kullanım

```csharp
using System.Collections.Generic;
using PushizoSdk;
using UnityEngine;

public class PushizoBootstrap : MonoBehaviour
{
    [SerializeField] private bool inBattle;

    private async void Start()
    {
        Pushizo.Initialize("app_…", new PushizoOptions { ApiUrl = "https://api.example.com" });

        Pushizo.Notifications.Clicked += e => Debug.Log($"Tıklandı: {e.Url}");
        Pushizo.Notifications.ForegroundWillDisplay += e =>
        {
            if (inBattle) e.PreventDefault();   // ana iş parçacığında; gerekirse daha sonra e.Display()
        };
        Pushizo.User.PushSubscription.Changed += e => Debug.Log(e.Current.Id);
        Pushizo.Notifications.PermissionChanged += granted => Debug.Log(granted); // yerel izin gözlemcisinden
        Pushizo.InAppMessages.Clicked += e => Debug.Log(e.ActionId);

        await Pushizo.Notifications.RequestPermissionAsync(fallbackToSettings: true);
        Pushizo.Login("user_42");                     // identity verification açıksa: Login(id, identityHash)
        Pushizo.User.AddTags(new Dictionary<string, string> { ["level"] = "12" });
        Pushizo.User.AddAlias("crm_id", "42");
        Pushizo.User.AddEmail("ali@example.com");
        Pushizo.User.AddSms("+905321234567");
        Pushizo.User.SetLanguage("tr");

        // Bildirim tercihleri: kategori × kanal (channel null = tüm kanallar)
        var prefs = await Pushizo.User.GetPreferencesAsync();
        foreach (var category in prefs.Categories) Debug.Log(category.LocalizedName("tr"));
        await Pushizo.User.SetPreferencesAsync(new[]
        {
            new PreferenceInput("promo", optedIn: false),
            new PreferenceInput("news", optedIn: true, channel: "email"),
        });

        Pushizo.InAppMessages.AddTrigger("level", "gold");
        Pushizo.InAppMessages.SetScreen("shop");
        Pushizo.InAppMessages.Paused = false;         // ara sahnede true

        Pushizo.Session.SendEvent("purchase", new Dictionary<string, object> { ["sku"] = "gems_100" }, value: 4.99);

        var page = await Pushizo.Inbox.ListAsync(limit: 20);
        await Pushizo.Inbox.MarkAllReadAsync();
        Pushizo.Notifications.ClearAll();
        Pushizo.Notifications.RemoveGroupedNotifications("orders"); // Android android_group / iOS thread-id
        Pushizo.Notifications.RemoveNotification(42);               // Android tepsi kimliği; iOS'ta RemoveNotification("istek-kimligi")
        Pushizo.Debug.SetLogLevel(LogLevel.Debug);
    }
}
```

Tüm olaylar ve `Task` sonuçları Unity ana iş parçacığında teslim edilir (gizli `PushizoDispatcher` nesnesi).
Ön plan bildirimi için abone varken yerel gösterim ertelenir; işleyiciler `PreventDefault()` çağırmazsa bildirim
gösterilir (iOS'ta karar 25 sn içinde verilmelidir). Bildirim kutusu ve tercih hataları `PushizoException.Code` taşır
(`NOT_READY`: abonelik/kullanıcı kaydı henüz tamamlanmadı; `INVALID_ARGUMENT`: yerel SDK girdiyi reddetti; diğerleri
sunucu hata kodu). `SetPreferencesAsync` girdiyi köprüden önce doğrular (1-100 kayıt, kategori 1-64 karakter, kanal
`ios_push` | `android_push` | `web_push` | `email` | `sms` ya da null); geçersizse `ArgumentException`.

### JWT ile kimlik doğrulama

App ayarlarında kimlik doğrulama kipi (`identity_verification_mode`) `jwt` veya `both` ise kullanıcı, sunucunuzda
imzalanmış kısa ömürlü bir ES256 JWT ile kimliklendirilir (talepler: `sub` = external_id, `aud` = app_id, `exp`/`iat` zorunlu, en çok 24 saat, yalnızca ES256).

```csharp
Pushizo.Login("user_42", jwt: jwtFromYourServer);                         // yalnızca JWT
Pushizo.Login("user_42", identityHash: hmac, jwt: jwtFromYourServer);     // both kipinde ikisi birlikte
Pushizo.UpdateUserJwt(refreshedJwt); // süresi dolmadan yenileyin
Pushizo.Logout();
```

- JWT yerel SDK'da identity hash ile aynı kalıcı depoda saklanır ve tüm `/sdk/v1/...` isteklerinde
  `Authorization: Bearer <jwt>` başlığıyla gider; gövdeye/URL'ye yazılmaz, loglanmaz. `Logout` belirteci siler.
- `/config` yanıtında `identity_verification_required` true ise login öncesi kanıt denetlenir: `jwt` kipinde JWT, `hmac`
  kipinde `identityHash` zorunludur, `both` kipinde biri yeter; eksikse `Login` `PushizoException` (`NOT_READY`)
  fırlatır. Alan gelmeyen eski sunucularda kip `hmac` sayılır; mevcut `Login(id, identityHash)` çağrıları aynen çalışır.
- `UpdateUserJwt` giriş yapılmamışsa `PushizoException` (`NOT_READY`), boş belirteçte `ArgumentException` fırlatır;
  başarıda kuyrukta bekleyen işlemler yeni belirteçle yeniden denenir.
- Editor ve yerel SDK'sı olmayan platformlarda ağ çağrısı yapılmaz; `UpdateUserJwt` yalnızca login durumunu denetler.

### Live Activities (iOS)

Köprü, iOS SDK'nın genel türü `PushizoDefaultLiveActivityAttributes`'ı kullanır. Dashboard/API'de Live
Activity türü adı tam olarak `PushizoDefaultLiveActivityAttributes` olmalıdır.

```csharp
// Initialize'dan sonra, uygulama her açıldığında: push-to-start (iOS 17.2+) ve güncelleme belirteçleri kaydedilir.
await Pushizo.LiveActivities.SetupDefaultLiveActivityAsync();

// Yerel başlatma (iOS 16.1+); sunucu sonraki update/end çağrılarını bu kimlikle yapar.
await Pushizo.LiveActivities.StartDefaultLiveActivityAsync(
    "match_7",
    new Dictionary<string, object> { ["title"] = "Final maçı" },
    new Dictionary<string, object> { ["status"] = "Devre arası", ["score"] = "2-1" });
```

- Widget extension (Unity'nin ürettiği Xcode projesine Widget Extension hedefi olarak eklenir):
  ```swift
  import ActivityKit
  import SwiftUI
  import WidgetKit
  import Pushizo // CocoaPods (EDM4U): hedefe pod 'Pushizo/Extension'; SPM'de PushizoExtension ürünü + import PushizoExtension

  struct MatchLiveActivity: Widget {
      var body: some WidgetConfiguration {
          ActivityConfiguration(for: PushizoDefaultLiveActivityAttributes.self) { context in
              VStack {
                  Text(context.attributes.data["title"]?.stringValue ?? "")
                  Text(context.state.data["status"]?.description ?? "")
              }
          } dynamicIsland: { context in
              DynamicIsland {
                  DynamicIslandExpandedRegion(.center) { Text(context.state.data["status"]?.description ?? "") }
              } compactLeading: { Text("HS") } compactTrailing: { EmptyView() } minimal: { EmptyView() }
          }
      }
  }
  ```
  CocoaPods'ta widget hedefinin Podfile bloğuna `Pushizo/Extension` alt spec'i eklenir (`import Pushizo`); SPM'de
  `PushizoExtension` ürünü (`import PushizoExtension`).
- Unity-iPhone hedefinin `Info.plist` dosyasına `NSSupportsLiveActivities` = `YES` ekleyin (Xcode'da veya kendi
  `PostProcessBuild` betiğinizle).
- Sunucu: `POST /v1/apps/{appId}/live-activities/PushizoDefaultLiveActivityAttributes/start|update|end`
  (`attributes` ve `content_state` düz nesnedir; widget'ta `data` sözlüğünden okunur). Push-to-start iOS 17.2+,
  yerel başlatma iOS 16.1+ gerektirir.
- Köprü girdiyi doğrular: `activityId` 1-100 karakter (harf, rakam, `_ . : -`), anahtar 1-64 karakter, değer metin
  (en çok 4000 karakter), sonlu sayı veya bool; `attributes` içinde `pushizo` anahtarı ayrılmıştır. Geçersizse
  `PushizoException` (`INVALID_ARGUMENT`).
- Hatalar `PushizoException.Code` taşır: `PLATFORM_NOT_SUPPORTED` (iOS 16.1 öncesi, Android, Editor), `NOT_READY`
  (yerel SDK başlatılmadı), `INVALID_ARGUMENT`, `LIVE_ACTIVITY_FAILED` (Live Activities kapalı, `NSSupportsLiveActivities`
  eksik vb.). Sonuçlar ana iş parçacığında teslim edilir.
- Tip-güvenli kendi `ActivityAttributes` türünüz için native tarafta `Pushizo.LiveActivities.register(MyAttributes.self)`
  çağırın (ayrıntı: [ios rehberi](https://docs.pushizo.com/docs/sdk/ios) "Live Activities").

### HTML in-app ve carousel

In-app mesajları yerel Android/iOS SDK'sı çizer; bu paket köprüdür. HTML in-app (sandbox'lı WebView, köprü
`close`/`click`/`addTag`/`trackEvent`) ve carousel blokları yerel SDK'dan olduğu gibi devralınır: ek kod veya
yapılandırma gerekmez, tıklama ve yaşam döngüsü olayları mevcut in-app dinleyicilerine gelir. Yetenek bildirimi
(`supports=html,carousel`) yerel SDK tarafından yapılır; desteklemeyen eski yerel sürümlere sunucu HTML mesaj göndermez
ve carousel bloklarını çıkarır.

### Canlı sohbet

```csharp
await Pushizo.Chat.StartAsync(name: "Ali");
var config = await Pushizo.Chat.GetConfigAsync();          // Online, OfflineMessage, ek sınırları
var page = await Pushizo.Chat.GetMessagesAsync();
Pushizo.Chat.EventReceived += e => { if (e.Type == ChatEventType.Message) Render(e.Message!); }; // ana iş parçacığında
Pushizo.Chat.Opened += e => OpenChat(e.ConversationId);   // sohbet bildirimine tıklama
await Pushizo.Chat.StartListeningAsync();                  // sahne açıkken; ayrılınca StopListeningAsync()
await Pushizo.Chat.SendMessageAsync("Merhaba");            // bot hızlı yanıtı: SendMessageAsync(label, optionId)
await Pushizo.Chat.MarkReadAsync(page.Messages[page.Messages.Count - 1].Seq);
```

Ziyaretçi kimliği, çevrimdışı kuyruk (aynı `client_message_id` ile yeniden deneme), canlı akış (SSE; koparsa üstel
geri çekilme + `after_seq` yoklaması) ve push ile gelen temsilci mesajları (`pushizo_chat`) yerel Android/iOS SDK'sındadır;
`start` sonrası login/logout ve push aboneliği değişince bağlama yerel SDK'da yenilenir, logout ziyaretçiyi sıfırlar.
Mesajlar `seq` ile sıralanır; ağ yokken gönderilen mesaj `pending` döner ve gönderilince olayla sunucu kopyası gelir.
Ayrıntı: [android rehberi](https://docs.pushizo.com/docs/sdk/android) / [ios rehberi](https://docs.pushizo.com/docs/sdk/ios) → "Canlı sohbet".
Editor'de çağrılar `PushizoException` (`PLATFORM_NOT_SUPPORTED`) ile sonuçlanır.

### Konum

Kullanıcı izin verip paylaşımı açtığında yerel SDK yaklaşık konumu push aboneliğine yazar (konum hedefleme). Yalnızca ön
planda çalışır, arka plan konumu istenmez: paylaşım açık ve izin varken oturum başında ve uygulama ön plana döndüğünde en
sık 5 dakikada bir gönderilir (`PATCH /sdk/v1/apps/{appId}/subscriptions/{id}` → `location`). Tercih yerel SDK'da kalıcıdır.

```csharp
// İzin yoksa önce açıklama diyaloğu (metin verilmezse SDK'nın Türkçe/İngilizce varsayılanı), sonra sistem izni.
var granted = await Pushizo.Location.RequestPermissionAsync("Yakındaki mağazaların kampanyalarını gösterebilmek için konumunuzu kullanırız.");
if (granted) Pushizo.Location.SetShared(true);

Debug.Log(Pushizo.Location.IsShared);   // kalıcı tercih (izin durumundan bağımsız)
Pushizo.Location.SetShared(false);      // sunucudaki konum ve rıza kaydı silinir (location: null)
```

- `RequestPermissionAsync(rationale)` sonucu ana iş parçacığında döner: izin var veya verildiyse `true`; kullanıcı
  "Şimdi değil" dedi, sistem iznini reddetti, izin daha önce kalıcı olarak reddedildi (yeniden sorulamaz; kullanıcıyı
  ayarlara yönlendirin) ya da aşağıdaki platform tanımı eksikse `false`.
- **iOS:** Info.plist'te `NSLocationWhenInUseUsageDescription` zorunludur; yoksa istek `false` döner ve yerel SDK hata
  günlüğü yazar. Player Settings → iOS → Other Settings → **Location Usage Description** alanını doldurun (Unity her
  derlemede `NSLocationWhenInUseUsageDescription` olarak yazar) ya da kendi `PostProcessBuild` betiğinizle ekleyin.
  Yalnızca "kullanırken" izni istenir (Always istenmez). CoreLocation çerçevesi `Pushizo/App` pod'uyla gelir.
- **Android:** paket izin eklemez; uygulamanın manifest'ine (Player Settings → Publishing Settings → Custom Main
  Manifest, `Assets/Plugins/Android/AndroidManifest.xml`) ekleyin:
  ```xml
  <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
  <!-- isteğe bağlı, hassas konum için: -->
  <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
  ```
  Manifest'te ikisi de yoksa istek uyarı günlüğüyle `false` döner. Açıklama diyaloğu yerel SDK'nın string kaynaklarını
  (`hs_location_rationale_*`, tr/en) kullanır; sistem izni `Pushizo.Location.PERMISSION_REQUEST_CODE` ile istenir ve
  sonuç izin penceresi kapanıp oyun ön plana döndüğünde Java köprüsünde alınır (Unity etkinliğinde değişiklik gerekmez).
  `ACCESS_BACKGROUND_LOCATION` eklemeyin; SDK arka planda konum okumaz.
- **KVKK/GDPR:** sunucu ilk konumu açık rıza kaydı olarak saklar; paylaşımı yalnızca kullanıcının açık onayıyla açın.
  `RequiresPrivacyConsent` açıkken `SetConsentGiven(true)` verilmeden konum hiç gönderilmez; izin yoksa da sessizce
  gönderilmez. `SetShared(false)` konumu ve rıza kaydını sunucudan sildirir, gönderilmemiş konumu atar.
- **Editor, Standalone, WebGL:** konum okunmaz ve ağ çağrısı yapılmaz; `SetShared` tercihi yalnızca bellekte tutar
  (`IsShared` onu döndürür), `RequestPermissionAsync` her zaman `false` döner.

## Mimari

```
Pushizo (C#: doğrulama, JSON sözleşmesi, ana iş parçacığı dağıtıcısı)
 ├─ Android: AndroidJavaClass/AndroidJavaProxy → com.pushizo.unity.PushizoUnityBridge (Java) → com.pushizo.sdk.Pushizo
 ├─ iOS:     [DllImport("__Internal")] hs_* → PushizoUnityBridge.swift (@_cdecl) → Pushizo (Swift)
 └─ Editor:  EditorNativeBridge (günlük + bellek içi durum)
```

Köprüler yalnızca metin (JSON), sayı, bool ve geri çağrı taşır; JSON biçimleri .NET MAUI köprüsüyle aynıdır.
Android köprüsü Java'dır (Unity Gradle derlemesinde Kotlin eklentisi gerekmez); Kotlin SDK'nın `suspend` bildirim
kutusu fonksiyonları `Continuation` ile çağrılır. İzin değişiklikleri yerel SDK izin gözlemcisinden
(`Pushizo.Notifications.PermissionObserver` / Swift `addPermissionObserver`) `permission` olayı olarak gelir.

## Testler

Yönetilen katmanın EditMode testleri `Tests/Editor` altındadır. Çalıştırmak için projenin `Packages/manifest.json`
dosyasına `"testables": ["com.pushizo.unity"]` ekleyip Window → General → Test Runner → EditMode'u açın.

## Örnek

Package Manager → Pushizo → Samples → **Demo** → Import. Boş bir sahnede bir GameObject'e `PushizoDemo`
bileşenini ekleyip App ID ve API adresini girin.

<!-- pushizo:footer -->
---

## Support · Destek

- **Guide · Rehber:** [docs.pushizo.com/docs/sdk/unity](https://docs.pushizo.com/docs/sdk/unity)
- **API reference & OpenAPI · API referansı ve OpenAPI:** [docs.pushizo.com/docs/api](https://docs.pushizo.com/docs/api) ·
  [pushizo-openapi](https://github.com/pushizo/pushizo-openapi)
- **Bugs & ideas · Hata ve öneri:** [Issues](https://github.com/pushizo/pushizo-unity/issues)
- **Security · Güvenlik:** please do not open public issues; email [security@pushizo.com](mailto:security@pushizo.com) ·
  herkese açık issue açmayın, bu adrese yazın.
- **Help · Destek:** [support@pushizo.com](mailto:support@pushizo.com)

This repository is mirrored automatically from the Pushizo main repository on every release. Please open an issue
instead of a pull request; fixes land in the main repository and ship with the next release.
Bu depo her sürümde Pushizo ana deposundan otomatik yansıtılır. Pull request yerine issue açın; düzeltmeler ana depoda
yapılır ve bir sonraki sürümle buraya gelir.

Released under the MIT License · MIT lisanslıdır — [LICENSE.md](https://github.com/pushizo/pushizo-unity/blob/main/LICENSE.md).
<!-- /pushizo:footer -->
