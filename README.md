# HeroSignal Unity SDK (`com.herosignal.unity`)

Push bildirimleri, in-app mesajlar, bildirim kutusu, kullanıcı/tag/alias ve e-posta/SMS abonelikleri için Unity
paketi (UPM). Yerel HeroSignal Android (`com.herosignal:herosignal-android`) ve iOS (`HeroSignal` Swift) SDK'larını
sarar; tetik değerlendirme, çevrimdışı kuyruk, teslimat geri bildirimi ve in-app çizimi yerel SDK'dadır. API yüzeyi
plan 06 §6.1 (eski sağlayıcı v5 ile uyumlu), Flutter eklentisi ve .NET MAUI SDK'sıyla aynıdır.

| Platform | Davranış |
|----------|----------|
| Android cihaz (minSdk 23) | Yerel Android SDK'sı; Java köprüsü `Runtime/Plugins/Android` |
| iOS cihaz (iOS 15+) | Yerel iOS SDK'sı; Swift köprüsü `Runtime/Plugins/iOS` (`[DllImport("__Internal")]`) |
| Unity Editor, Standalone, WebGL | Ağ çağrısı ve push yok; çağrılar günlüğe yazılır, tag/trigger/e-posta bellekte tutulur, bildirim kutusu `PLATFORM_NOT_SUPPORTED` |

Gereksinimler: Unity 2021.3+, [External Dependency Manager for Unity (EDM4U)](https://github.com/googlesamples/unity-jar-resolver)
(Android Gradle ve iOS CocoaPods bağımlılıkları için), iOS için CocoaPods.

## Kurulum

`Packages/manifest.json` (ya da Window → Package Manager → + → Add package from git URL):

```json
{
  "dependencies": {
    "com.herosignal.unity": "https://github.com/herosignal/herosignal.git?path=sdk/unity#v0.1.0"
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
        HeroSignal.Notifications.PermissionChanged += granted => Debug.Log(granted);
        HeroSignal.InAppMessages.Clicked += e => Debug.Log(e.ActionId);

        await HeroSignal.Notifications.RequestPermissionAsync(fallbackToSettings: true);
        HeroSignal.Login("user_42");                     // identity verification açıksa: Login(id, identityHash)
        HeroSignal.User.AddTags(new Dictionary<string, string> { ["level"] = "12" });
        HeroSignal.User.AddAlias("crm_id", "42");
        HeroSignal.User.AddEmail("ali@example.com");
        HeroSignal.User.AddSms("+905321234567");
        HeroSignal.User.SetLanguage("tr");

        HeroSignal.InAppMessages.AddTrigger("level", "gold");
        HeroSignal.InAppMessages.SetScreen("shop");
        HeroSignal.InAppMessages.Paused = false;         // ara sahnede true

        HeroSignal.Session.SendEvent("purchase", new Dictionary<string, object> { ["sku"] = "gems_100" }, value: 4.99);

        var page = await HeroSignal.Inbox.ListAsync(limit: 20);
        await HeroSignal.Inbox.MarkAllReadAsync();
        HeroSignal.Notifications.ClearAll();
        HeroSignal.Debug.SetLogLevel(LogLevel.Debug);
    }
}
```

Tüm olaylar ve `Task` sonuçları Unity ana iş parçacığında teslim edilir (gizli `HeroSignalDispatcher` nesnesi).
Ön plan bildirimi için abone varken yerel gösterim ertelenir; işleyiciler `PreventDefault()` çağırmazsa bildirim
gösterilir (iOS'ta karar 25 sn içinde verilmelidir). Bildirim kutusu hataları `HeroSignalException.Code` taşır
(`NOT_READY`: abonelik/kullanıcı kaydı henüz tamamlanmadı; diğerleri sunucu hata kodu).

## Mimari

```
HeroSignal (C#: doğrulama, JSON sözleşmesi, ana iş parçacığı dağıtıcısı)
 ├─ Android: AndroidJavaClass/AndroidJavaProxy → com.herosignal.unity.HeroSignalUnityBridge (Java) → com.herosignal.sdk.HeroSignal
 ├─ iOS:     [DllImport("__Internal")] hs_* → HeroSignalUnityBridge.swift (@_cdecl) → HeroSignal (Swift)
 └─ Editor:  EditorNativeBridge (günlük + bellek içi durum)
```

Köprüler yalnızca metin (JSON), sayı, bool ve geri çağrı taşır; JSON biçimleri .NET MAUI köprüsüyle aynıdır.
Android köprüsü Java'dır (Unity Gradle derlemesinde Kotlin eklentisi gerekmez); Kotlin SDK'nın `suspend` bildirim
kutusu fonksiyonları `Continuation` ile çağrılır.

## Testler

Yönetilen katmanın EditMode testleri `Tests/Editor` altındadır. Çalıştırmak için projenin `Packages/manifest.json`
dosyasına `"testables": ["com.herosignal.unity"]` ekleyip Window → General → Test Runner → EditMode'u açın.

## Örnek

Package Manager → HeroSignal → Samples → **Demo** → Import. Boş bir sahnede bir GameObject'e `HeroSignalDemo`
bileşenini ekleyip App ID ve API adresini girin.
