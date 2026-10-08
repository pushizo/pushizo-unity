# Değişiklik günlüğü

Biçim [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/), sürümleme [SemVer](https://semver.org/lang/tr/).

## [0.1.0] - 2026-10-08
### Eklendi
- `HeroSignal` C# API'si (plan 06 §6.1): `Initialize`, `SetConsentGiven`, `Login`/`Logout`, `User` (tag, alias, dil,
  e-posta/SMS, `PushSubscription`), `Notifications` (izin, tıklama, ön plan, `ClearAll`, `RemoveNotification`,
  `RemoveGroupedNotifications`), `InAppMessages`, `Inbox`, `Session.SendEvent`, `Debug.SetLogLevel`.
- Android köprüsü (`AndroidJavaClass`/`AndroidJavaProxy` → `com.herosignal.sdk.HeroSignal`), iOS köprüsü
  (`[DllImport("__Internal")]` → `Plugins/iOS/HeroSignalUnityBridge.swift` C ABI), Editör ve diğer platformlar için
  etkisiz uygulama.
- Olayların Unity ana iş parçacığına taşınması (`UnitySynchronizationContext`).
- EDM4U bağımlılıkları, `google-services.json` → Android kaynak dönüştürücüsü, iOS Push yeteneği, arka plan modu ve
  Notification Service Extension hedefi (PostProcessBuild).
- Editör testleri (NUnit) ve `Validation~` altında .NET doğrulama projesi.
