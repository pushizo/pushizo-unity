# HeroSignal Unity SDK (`com.herosignal.unity`)

Push bildirimleri, in-app mesajlar, bildirim kutusu, kullanıcı/tag/alias ve e-posta/SMS abonelikleri için Unity
paketi (UPM). Yerel HeroSignal Android (`com.herosignal:herosignal-android`) ve iOS (`HeroSignal` Swift) SDK'larını
sarar; tetik değerlendirme, çevrimdışı kuyruk, teslimat geri bildirimi ve in-app çizimi yerel SDK'dadır. API yüzeyi
plan 06 §6.1 (eski sağlayıcı v5 ile uyumlu), Flutter eklentisi ve .NET MAUI SDK'sıyla aynıdır.

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
    "com.herosignal.unity": "https://github.com/exec0de/herosignal.git?path=sdk/unity#v0.1.0"
  }
}
```

Project Settings → **HeroSignal**: App ID, API URL (iOS Notification Service Extension için), APNs ortamı, App Group
ve `google-services.json` yolu.

### Android
- Firebase konsolundan `google-services.json` indirip `Assets/google-services.json` yoluna koyun. Derleme sırasında
  Firebase değerleri (`google_app_id`, `gcm_defaultSenderId`, `google_api_key`, `project_id`) unityLibrary
  kaynaklarına yazılır; Firebase Unity SDK'sı gerekmez.
- Player Settings → Minimum API Level 23. `POST_NOTIFICATIONS` izni ve FCM servisi yerel SDK manifestinden birleşir.
- Gradle bağımlılıkları EDM4U ile çözülür (`Editor/HeroSignalDependencies.xml`): yerel SDK, Kotlin stdlib, AndroidX Core.

### iOS
Derleme sonrası işlem (`Editor/HeroSignalIosPostProcessor.cs`) otomatik olarak:
- Push Notifications yeteneği (`aps-environment`) ve Background Modes → Remote notifications,
- isteğe bağlı App Group,
- Swift köprüsü için derleme ayarları (`SWIFT_VERSION`, Swift çalışma zamanı gömme),
- `Classes/Preprocessor.h` → `UNITY_USES_REMOTE_NOTIFICATIONS 1` (Unity bu değeri yalnız kendi bildirim API'leri
  kullanıldığında açar; kapalıyken `UnityAppController` APNs token'ını almaz ve köprüye iletmez),
- **HeroSignalNotificationServiceExtension** hedefi (zengin medya, rozet, teslimat onayı) ve Podfile'a
  `HeroSignal/Extension` pod'u ekler (EDM4U Podfile'ı 40'ta üretir, eklenti 45'te çalışır, `pod install` 50'de).

APNs token'ı Unity'nin `UnityAppController` bildirimlerinden alınır; AppDelegate değişikliği gerekmez.
Xcode'da imzalama ekibini her iki hedef için seçin (Player Settings → iOS → Signing Team ID verildiyse otomatik).

### Yerel SDK'yı kaynaktan kullanma
Yerel SDK'lar Maven Central/CocoaPods'a yayımlanana kadar (Faz 14):
- Android: `gradle -p sdk/android :herosignal:publishToMavenLocal`, ardından Unity'de Player Settings → Publishing
  Settings → Custom Main Gradle Template / Settings Template'te `mavenLocal()` deposunu ekleyin.
- iOS: oluşan Podfile'da `pod 'HeroSignal/App', :path => '<depo>/sdk/ios'` (ve NSE hedefinde `HeroSignal/Extension`).

## Kullanım

```csharp
using System.Collections.Generic;
using HeroSignalSdk;
using UnityEngine;

public class HeroSignalBootstrap : MonoBehaviour
{
    [SerializeField] private bool inBattle;

    private async void Start()
    {
        HeroSignal.Initialize("app_…", new HeroSignalOptions { ApiUrl = "https://api.example.com" });

        HeroSignal.Notifications.Clicked += e => Debug.Log($"Tıklandı: {e.Url}");
        HeroSignal.Notifications.ForegroundWillDisplay += e =>
        {
            if (inBattle) e.PreventDefault();   // ana iş parçacığında; gerekirse daha sonra e.Display()
        };
        HeroSignal.User.PushSubscription.Changed += e => Debug.Log(e.Current.Id);
        HeroSignal.Notifications.PermissionChanged += granted => Debug.Log(granted); // yerel izin gözlemcisinden
        HeroSignal.InAppMessages.Clicked += e => Debug.Log(e.ActionId);

        await HeroSignal.Notifications.RequestPermissionAsync(fallbackToSettings: true);
        HeroSignal.Login("user_42");                     // identity verification açıksa: Login(id, identityHash)
        HeroSignal.User.AddTags(new Dictionary<string, string> { ["level"] = "12" });
        HeroSignal.User.AddAlias("crm_id", "42");
        HeroSignal.User.AddEmail("ali@example.com");
        HeroSignal.User.AddSms("+905321234567");
        HeroSignal.User.SetLanguage("tr");

        // Bildirim tercihleri (ADR-0019 §4): kategori × kanal (channel null = tüm kanallar)
        var prefs = await HeroSignal.User.GetPreferencesAsync();
        foreach (var category in prefs.Categories) Debug.Log(category.LocalizedName("tr"));
        await HeroSignal.User.SetPreferencesAsync(new[]
        {
            new PreferenceInput("promo", optedIn: false),
            new PreferenceInput("news", optedIn: true, channel: "email"),
        });

        HeroSignal.InAppMessages.AddTrigger("level", "gold");
        HeroSignal.InAppMessages.SetScreen("shop");
        HeroSignal.InAppMessages.Paused = false;         // ara sahnede true

        HeroSignal.Session.SendEvent("purchase", new Dictionary<string, object> { ["sku"] = "gems_100" }, value: 4.99);

        var page = await HeroSignal.Inbox.ListAsync(limit: 20);
        await HeroSignal.Inbox.MarkAllReadAsync();
        HeroSignal.Notifications.ClearAll();
        HeroSignal.Notifications.RemoveGroupedNotifications("orders"); // Android android_group / iOS thread-id
        HeroSignal.Notifications.RemoveNotification(42);               // Android tepsi kimliği; iOS'ta RemoveNotification("istek-kimligi")
        HeroSignal.Debug.SetLogLevel(LogLevel.Debug);
    }
}
```

Tüm olaylar ve `Task` sonuçları Unity ana iş parçacığında teslim edilir (gizli `HeroSignalDispatcher` nesnesi).
Ön plan bildirimi için abone varken yerel gösterim ertelenir; işleyiciler `PreventDefault()` çağırmazsa bildirim
gösterilir (iOS'ta karar 25 sn içinde verilmelidir). Bildirim kutusu ve tercih hataları `HeroSignalException.Code` taşır
(`NOT_READY`: abonelik/kullanıcı kaydı henüz tamamlanmadı; `INVALID_ARGUMENT`: yerel SDK girdiyi reddetti; diğerleri
sunucu hata kodu). `SetPreferencesAsync` girdiyi köprüden önce doğrular (1-100 kayıt, kategori 1-64 karakter, kanal
`ios_push` | `android_push` | `web_push` | `email` | `sms` ya da null); geçersizse `ArgumentException`.

### JWT ile kimlik doğrulama

App ayarlarında kimlik doğrulama kipi (`identity_verification_mode`) `jwt` veya `both` ise kullanıcı, sunucunuzda
imzalanmış kısa ömürlü bir ES256 JWT ile kimliklendirilir (ADR-0023 §9; talepler: `sub` = external_id, `aud` = app_id, `exp`/`iat` zorunlu, en çok 24 saat, yalnızca ES256).

```csharp
HeroSignal.Login("user_42", jwt: jwtFromYourServer);                         // yalnızca JWT
HeroSignal.Login("user_42", identityHash: hmac, jwt: jwtFromYourServer);     // both kipinde ikisi birlikte
HeroSignal.UpdateUserJwt(refreshedJwt); // süresi dolmadan yenileyin
HeroSignal.Logout();
```

- JWT yerel SDK'da identity hash ile aynı kalıcı depoda saklanır ve tüm `/sdk/v1/...` isteklerinde
  `Authorization: Bearer <jwt>` başlığıyla gider; gövdeye/URL'ye yazılmaz, loglanmaz. `Logout` belirteci siler.
- `/config` yanıtında `identity_verification_required` true ise login öncesi kanıt denetlenir: `jwt` kipinde JWT, `hmac`
  kipinde `identityHash` zorunludur, `both` kipinde biri yeter; eksikse `Login` `HeroSignalException` (`NOT_READY`)
  fırlatır. Alan gelmeyen eski sunucularda kip `hmac` sayılır; mevcut `Login(id, identityHash)` çağrıları aynen çalışır.
- `UpdateUserJwt` giriş yapılmamışsa `HeroSignalException` (`NOT_READY`), boş belirteçte `ArgumentException` fırlatır;
  başarıda kuyrukta bekleyen işlemler yeni belirteçle yeniden denenir.
- Editor ve yerel SDK'sı olmayan platformlarda ağ çağrısı yapılmaz; `UpdateUserJwt` yalnızca login durumunu denetler.

### Live Activities (iOS)

Köprü, iOS SDK'nın genel türü `HeroSignalDefaultLiveActivityAttributes`'ı kullanır (ADR-0024). Dashboard/API'de Live
Activity türü adı tam olarak `HeroSignalDefaultLiveActivityAttributes` olmalıdır.

```csharp
// Initialize'dan sonra, uygulama her açıldığında: push-to-start (iOS 17.2+) ve güncelleme belirteçleri kaydedilir.
await HeroSignal.LiveActivities.SetupDefaultLiveActivityAsync();

// Yerel başlatma (iOS 16.1+); sunucu sonraki update/end çağrılarını bu kimlikle yapar.
await HeroSignal.LiveActivities.StartDefaultLiveActivityAsync(
    "match_7",
    new Dictionary<string, object> { ["title"] = "Final maçı" },
    new Dictionary<string, object> { ["status"] = "Devre arası", ["score"] = "2-1" });
```

- Widget extension (Unity'nin ürettiği Xcode projesine Widget Extension hedefi olarak eklenir):
  ```swift
  import ActivityKit
  import SwiftUI
  import WidgetKit
  import HeroSignal // CocoaPods (EDM4U): hedefe pod 'HeroSignal/Extension'; SPM'de HeroSignalExtension ürünü + import HeroSignalExtension

  struct MatchLiveActivity: Widget {
      var body: some WidgetConfiguration {
          ActivityConfiguration(for: HeroSignalDefaultLiveActivityAttributes.self) { context in
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
  CocoaPods'ta widget hedefinin Podfile bloğuna `HeroSignal/Extension` alt spec'i eklenir (`import HeroSignal`); SPM'de
  `HeroSignalExtension` ürünü (`import HeroSignalExtension`).
- Unity-iPhone hedefinin `Info.plist` dosyasına `NSSupportsLiveActivities` = `YES` ekleyin (Xcode'da veya kendi
  `PostProcessBuild` betiğinizle).
- Sunucu: `POST /v1/apps/{appId}/live-activities/HeroSignalDefaultLiveActivityAttributes/start|update|end`
  (`attributes` ve `content_state` düz nesnedir; widget'ta `data` sözlüğünden okunur). Push-to-start iOS 17.2+,
  yerel başlatma iOS 16.1+ gerektirir.
- Köprü girdiyi doğrular: `activityId` 1-100 karakter (harf, rakam, `_ . : -`), anahtar 1-64 karakter, değer metin
  (en çok 4000 karakter), sonlu sayı veya bool; `attributes` içinde `herosignal` anahtarı ayrılmıştır. Geçersizse
  `HeroSignalException` (`INVALID_ARGUMENT`).
- Hatalar `HeroSignalException.Code` taşır: `PLATFORM_NOT_SUPPORTED` (iOS 16.1 öncesi, Android, Editor), `NOT_READY`
  (yerel SDK başlatılmadı), `INVALID_ARGUMENT`, `LIVE_ACTIVITY_FAILED` (Live Activities kapalı, `NSSupportsLiveActivities`
  eksik vb.). Sonuçlar ana iş parçacığında teslim edilir.
- Tip-güvenli kendi `ActivityAttributes` türünüz için native tarafta `HeroSignal.LiveActivities.register(MyAttributes.self)`
  çağırın (ayrıntı: `docs/sdk/ios.md` "Live Activities").

### HTML in-app ve carousel

In-app mesajları yerel Android/iOS SDK'sı çizer; bu paket köprüdür. HTML in-app (sandbox'lı WebView, köprü
`close`/`click`/`addTag`/`trackEvent`) ve carousel blokları yerel SDK'dan olduğu gibi devralınır: ek kod veya
yapılandırma gerekmez, tıklama ve yaşam döngüsü olayları mevcut in-app dinleyicilerine gelir. Yetenek bildirimi
(`supports=html,carousel`) yerel SDK tarafından yapılır; desteklemeyen eski yerel sürümlere sunucu HTML mesaj göndermez
ve carousel bloklarını çıkarır (ADR-0027 §6–7).

### Konum

Kullanıcı izin verip paylaşımı açtığında yerel SDK yaklaşık konumu push aboneliğine yazar (konum hedefleme). Yalnızca ön
planda çalışır, arka plan konumu istenmez: paylaşım açık ve izin varken oturum başında ve uygulama ön plana döndüğünde en
sık 5 dakikada bir gönderilir (`PATCH /sdk/v1/apps/{appId}/subscriptions/{id}` → `location`). Tercih yerel SDK'da kalıcıdır.

```csharp
// İzin yoksa önce açıklama diyaloğu (metin verilmezse SDK'nın Türkçe/İngilizce varsayılanı), sonra sistem izni.
var granted = await HeroSignal.Location.RequestPermissionAsync("Yakındaki mağazaların kampanyalarını gösterebilmek için konumunuzu kullanırız.");
if (granted) HeroSignal.Location.SetShared(true);

Debug.Log(HeroSignal.Location.IsShared);   // kalıcı tercih (izin durumundan bağımsız)
HeroSignal.Location.SetShared(false);      // sunucudaki konum ve rıza kaydı silinir (location: null)
```

- `RequestPermissionAsync(rationale)` sonucu ana iş parçacığında döner: izin var veya verildiyse `true`; kullanıcı
  "Şimdi değil" dedi, sistem iznini reddetti, izin daha önce kalıcı olarak reddedildi (yeniden sorulamaz; kullanıcıyı
  ayarlara yönlendirin) ya da aşağıdaki platform tanımı eksikse `false`.
- **iOS:** Info.plist'te `NSLocationWhenInUseUsageDescription` zorunludur; yoksa istek `false` döner ve yerel SDK hata
  günlüğü yazar. Player Settings → iOS → Other Settings → **Location Usage Description** alanını doldurun (Unity her
  derlemede `NSLocationWhenInUseUsageDescription` olarak yazar) ya da kendi `PostProcessBuild` betiğinizle ekleyin.
  Yalnızca "kullanırken" izni istenir (Always istenmez). CoreLocation çerçevesi `HeroSignal/App` pod'uyla gelir.
- **Android:** paket izin eklemez; uygulamanın manifest'ine (Player Settings → Publishing Settings → Custom Main
  Manifest, `Assets/Plugins/Android/AndroidManifest.xml`) ekleyin:
  ```xml
  <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
  <!-- isteğe bağlı, hassas konum için: -->
  <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />
  ```
  Manifest'te ikisi de yoksa istek uyarı günlüğüyle `false` döner. Açıklama diyaloğu yerel SDK'nın string kaynaklarını
  (`hs_location_rationale_*`, tr/en) kullanır; sistem izni `HeroSignal.Location.PERMISSION_REQUEST_CODE` ile istenir ve
  sonuç izin penceresi kapanıp oyun ön plana döndüğünde Java köprüsünde alınır (Unity etkinliğinde değişiklik gerekmez).
  `ACCESS_BACKGROUND_LOCATION` eklemeyin; SDK arka planda konum okumaz.
- **KVKK/GDPR:** sunucu ilk konumu açık rıza kaydı olarak saklar; paylaşımı yalnızca kullanıcının açık onayıyla açın.
  `RequiresPrivacyConsent` açıkken `SetConsentGiven(true)` verilmeden konum hiç gönderilmez; izin yoksa da sessizce
  gönderilmez. `SetShared(false)` konumu ve rıza kaydını sunucudan sildirir, gönderilmemiş konumu atar.
- **Editor, Standalone, WebGL:** konum okunmaz ve ağ çağrısı yapılmaz; `SetShared` tercihi yalnızca bellekte tutar
  (`IsShared` onu döndürür), `RequestPermissionAsync` her zaman `false` döner.

## Mimari

```
HeroSignal (C#: doğrulama, JSON sözleşmesi, ana iş parçacığı dağıtıcısı)
 ├─ Android: AndroidJavaClass/AndroidJavaProxy → com.herosignal.unity.HeroSignalUnityBridge (Java) → com.herosignal.sdk.HeroSignal
 ├─ iOS:     [DllImport("__Internal")] hs_* → HeroSignalUnityBridge.swift (@_cdecl) → HeroSignal (Swift)
 └─ Editor:  EditorNativeBridge (günlük + bellek içi durum)
```

Köprüler yalnızca metin (JSON), sayı, bool ve geri çağrı taşır; JSON biçimleri .NET MAUI köprüsüyle aynıdır.
Android köprüsü Java'dır (Unity Gradle derlemesinde Kotlin eklentisi gerekmez); Kotlin SDK'nın `suspend` bildirim
kutusu fonksiyonları `Continuation` ile çağrılır. İzin değişiklikleri yerel SDK izin gözlemcisinden
(`HeroSignal.Notifications.PermissionObserver` / Swift `addPermissionObserver`) `permission` olayı olarak gelir.

## Testler

Yönetilen katmanın EditMode testleri `Tests/Editor` altındadır. Çalıştırmak için projenin `Packages/manifest.json`
dosyasına `"testables": ["com.herosignal.unity"]` ekleyip Window → General → Test Runner → EditMode'u açın.

## Örnek

Package Manager → HeroSignal → Samples → **Demo** → Import. Boş bir sahnede bir GameObject'e `HeroSignalDemo`
bileşenini ekleyip App ID ve API adresini girin.
