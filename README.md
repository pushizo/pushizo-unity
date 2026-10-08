# HeroSignal Unity SDK (`com.herosignal.unity`)

HeroSignal push bildirimleri, in-app mesajlar ve bildirim kutusu için Unity paketi (UPM). Android'de yerel
`com.herosignal:herosignal-android`, iOS'ta yerel `HeroSignal` (Swift) SDK'sını sarar; API eski sağlayıcı v5 yüzeyiyle
uyumludur (plan 06 §6.1). Ayrıntılı kurulum rehberi: [`docs/sdk/unity.md`](../../docs/sdk/unity.md).

```csharp
using HeroSignalSdk.Unity;

HeroSignal.Initialize("app_…", new HeroSignalOptions { ApiUrl = "https://api.example.com" });
await HeroSignal.Notifications.RequestPermissionAsync();
HeroSignal.Login("player_42");
HeroSignal.User.AddTag("tier", "gold");
HeroSignal.Notifications.Clicked += (_, e) => Debug.Log(e.Notification.Title);
HeroSignal.Session.SendEvent("level_complete", new Dictionary<string, object> { ["level"] = 3 });
```

Gereksinimler: Unity 2021.3+, External Dependency Manager for Unity (EDM4U), Android `minSdk 23`, iOS 15+.

## Yapı

| Yol | İçerik |
|-----|--------|
| `Runtime/Core` | Platformdan bağımsız C# çekirdeği: API, doğrulama, JSON köprü biçimleri, olayların ana iş parçacığına taşınması |
| `Runtime/Platform` | Unity'ye bağlı katman: Android (`AndroidJavaClass`/`AndroidJavaProxy`), iOS (`DllImport("__Internal")`), platform seçimi |
| `Plugins/iOS` | `HeroSignalUnityBridge.swift` (C ABI) ve `HeroSignalUnityAppDelegate.mm` (APNs token) |
| `Editor` | EDM4U bağımlılıkları, `google-services.json` dönüştürücüsü, iOS PostProcessBuild (Push, arka plan modu, NSE), ayarlar |
| `Tests/Editor` | NUnit editör testleri (Unity Test Runner) |
| `Samples~/BasicUsage` | Örnek MonoBehaviour |
| `Validation~` | Unity editörü olmadan `dotnet` ile doğrulama (Unity bu klasörü içe aktarmaz) |

## Unity olmadan doğrulama

`Runtime/Core` UnityEngine'e bağımlı değildir; `Validation~/Core` onu `netstandard2.1` (C# 9) olarak derler,
`Validation~/Tests` `Tests/Editor` altındaki NUnit testlerini (ve `Editor/GoogleServicesXml.cs`) `net9.0` ile çalıştırır.
`Runtime/Platform` ve diğer `Editor` betikleri UnityEngine/UnityEditor gerektirdiği için bu derlemeye katılmaz
(taklit tip kullanılmaz); bu durumda çekirdek `UnsupportedBridge` ile çalışır.

```bash
cd sdk/unity/Validation~
dotnet test HeroSignal.Unity.Validation.sln
```
