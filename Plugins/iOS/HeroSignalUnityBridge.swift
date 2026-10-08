import Foundation
import HeroSignal
// SPM'de çekirdek tipler ayrı modüldedir; CocoaPods (Unity/EDM4U) alt spec'leri tek "HeroSignal" modülünde derler.
#if canImport(HeroSignalCore)
import HeroSignalCore
#endif
import UIKit
import UserNotifications

/*
 Unity (IL2CPP) → HeroSignal iOS SDK köprüsü: `[DllImport("__Internal")]` ile çağrılan C ABI (`@_cdecl`).
 - Metin girdileri UTF-8 `const char*`; metin dönüşleri `strdup` ile ayrılır, C# `hs_unity_free` ile serbest bırakır.
 - Karmaşık değerler JSON metnidir; biçimler Runtime/Core/Internal/BridgeJson.cs ile eşleşir (zamanlar epoch ms).
 - Olaylar tek geri çağrıyla (tür, JSON) ana iş parçacığında iletilir; geri çağrı kaydedilmeden gelenler sıraya alınır.
 - Asenkron sonuçlar (izin, bildirim kutusu) istek kimliğiyle sonuç geri çağrısına döner: (kimlik, sonuç JSON, hata JSON).
 */

public typealias HSUnityEventCallback = @convention(c) (UnsafePointer<CChar>, UnsafePointer<CChar>) -> Void
public typealias HSUnityResultCallback = @convention(c) (Int32, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void

private enum HSUnity {
    static var eventCallback: HSUnityEventCallback?
    static var resultCallback: HSUnityResultCallback?
    static var pendingEvents: [(String, String)] = []
    static var listenersRegistered = false
    static var foregroundEvents: [String: HSForegroundEvent] = [:]
    static var cachedPermission = false
    static var cachedOptedIn = false
    static let lifecycle = LifecycleListener()

    final class LifecycleListener: HSInAppLifecycleListener {
        func onWillDisplay(_ message: HSInAppMessage) { emitLifecycle("willDisplay", message) }
        func onDidDisplay(_ message: HSInAppMessage) { emitLifecycle("didDisplay", message) }
        func onWillDismiss(_ message: HSInAppMessage) { emitLifecycle("willDismiss", message) }
        func onDidDismiss(_ message: HSInAppMessage) { emitLifecycle("didDismiss", message) }

        private func emitLifecycle(_ phase: String, _ message: HSInAppMessage) {
            let raw = (try? JSONSerialization.data(withJSONObject: message.raw)).flatMap { String(data: $0, encoding: .utf8) }
            HSUnity.emit("inAppLifecycle", ["phase": phase, "messageId": message.id, "json": HSUnity.nn(raw)])
        }
    }

    static func onMain(_ block: @escaping () -> Void) {
        if Thread.isMainThread { block() } else { DispatchQueue.main.async(execute: block) }
    }

    static func emit(_ type: String, _ payload: [String: Any]) {
        let json = encode(payload)
        onMain {
            guard let callback = eventCallback else { pendingEvents.append((type, json)); return }
            type.withCString { t in json.withCString { j in callback(t, j) } }
        }
    }

    static func complete(_ requestId: Int32, json: String?, error: [String: Any]?) {
        let errorJson = error.map { encode($0) }
        onMain {
            guard let callback = resultCallback else { return }
            withOptionalCString(json) { j in withOptionalCString(errorJson) { e in callback(requestId, j, e) } }
        }
    }

    static func withOptionalCString<T>(_ value: String?, _ body: (UnsafePointer<CChar>?) -> T) -> T {
        guard let value else { return body(nil) }
        return value.withCString { body($0) }
    }

    static func run(_ requestId: Int32, _ body: @escaping () async throws -> String) {
        Task {
            do {
                let value = try await body()
                complete(requestId, json: value, error: nil)
            } catch let error as HSApiError {
                complete(requestId, json: nil, error: ["code": error.code, "message": error.message, "status": error.status])
            } catch {
                complete(requestId, json: nil, error: ["code": "NOT_READY", "message": "\(error)"])
            }
        }
    }

    static func refreshState() {
        Task {
            let state = await HeroSignal.User.pushSubscription.state()
            await MainActor.run {
                cachedOptedIn = state.optedIn
                emit("subscription", map(state))
                updatePermission(state.permission == .authorized || state.permission == .provisional)
            }
        }
    }

    static func updatePermission(_ granted: Bool) {
        onMain {
            guard granted != cachedPermission else { return }
            cachedPermission = granted
            emit("permission", ["permission": granted])
        }
    }

    static func logLevel(_ value: Int) -> HSLog.Level { HSLog.Level(rawValue: value) ?? .warn }

    static func object(_ json: String) -> [String: Any] {
        guard let data = json.data(using: .utf8), let value = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [:] }
        return value
    }

    static func stringMap(_ json: String) -> [String: String] { object(json).compactMapValues { $0 as? String } }

    static func stringList(_ json: String) -> [String] {
        guard let data = json.data(using: .utf8), let value = try? JSONSerialization.jsonObject(with: data) as? [Any] else { return [] }
        return value.compactMap { $0 as? String }
    }

    static func encode(_ value: Any) -> String {
        guard JSONSerialization.isValidJSONObject(value), let data = try? JSONSerialization.data(withJSONObject: value) else { return "{}" }
        return String(data: data, encoding: .utf8) ?? "{}"
    }

    /// JSONSerialization Optional kabul etmez; nil → NSNull (JSON null).
    static func nn(_ value: Any?) -> Any { value ?? NSNull() }

    static func millis(_ date: Date?) -> Any { nn(date.map { Int64($0.timeIntervalSince1970 * 1000) }) }

    static func map(_ n: HSNotification) -> [String: Any] {
        ["title": nn(n.title), "body": nn(n.body), "imageUrl": nn(n.imageUrl), "url": nn(n.url),
         "deliveryId": nn(n.deliveryId), "messageId": nn(n.messageId),
         // iOS'ta tepsiden kaldırma teslimat kimliğiyle eşleşir (HeroSignal.Notifications.removeNotification(id:)).
         "notificationId": nn(n.deliveryId),
         "actions": n.actions.map { ["id": $0.id, "title": $0.title, "url": nn($0.url), "icon": nn($0.icon)] },
         "data": n.data]
    }

    static func map(_ s: HSPushSubscriptionState) -> [String: Any] {
        ["id": nn(s.id), "token": nn(s.token), "optedIn": s.optedIn, "permission": s.permission == .authorized || s.permission == .provisional]
    }

    static func map(_ item: HSInboxItem) -> [String: Any] {
        ["id": item.id, "messageId": nn(item.messageId), "title": nn(item.title), "body": nn(item.body),
         "url": nn(item.url), "imageUrl": nn(item.imageUrl), "data": item.data, "read": item.read,
         "readAt": millis(item.readAt), "archivedAt": millis(item.archivedAt), "expiresAt": millis(item.expiresAt), "createdAt": millis(item.createdAt)]
    }

    static func map(_ page: HSInboxPage) -> [String: Any] {
        ["items": page.items.map { map($0) }, "unreadCount": page.unreadCount, "nextCursor": nn(page.nextCursor), "hasMore": page.hasMore]
    }
}

private func str(_ value: UnsafePointer<CChar>?) -> String? { value.map { String(cString: $0) } }
private func dup(_ value: String?) -> UnsafeMutablePointer<CChar>? { value.flatMap { strdup($0) } }

// MARK: Bellek ve geri çağrılar

@_cdecl("hs_unity_free")
public func hs_unity_free(_ pointer: UnsafeMutablePointer<CChar>?) { free(pointer) }

@_cdecl("hs_unity_set_event_callback")
public func hs_unity_set_event_callback(_ callback: HSUnityEventCallback?) {
    HSUnity.onMain {
        HSUnity.eventCallback = callback
        guard let callback else { return }
        let queued = HSUnity.pendingEvents
        HSUnity.pendingEvents.removeAll()
        for (type, json) in queued { type.withCString { t in json.withCString { j in callback(t, j) } } }
    }
}

@_cdecl("hs_unity_set_result_callback")
public func hs_unity_set_result_callback(_ callback: HSUnityResultCallback?) { HSUnity.resultCallback = callback }

// MARK: Başlatma

/// `optionsJson`: `{"apiUrl","requiresPrivacyConsent","logLevel"(0-4),"appGroup","apnsEnvironment","provisionalAuthorization"}`.
@_cdecl("hs_unity_start")
public func hs_unity_start(_ appId: UnsafePointer<CChar>, _ optionsJson: UnsafePointer<CChar>) {
    let id = String(cString: appId)
    let o = HSUnity.object(String(cString: optionsJson))
    HeroSignal.initialize(id) { options in
        if let apiUrl = o["apiUrl"] as? String { options.apiUrl = apiUrl }
        options.requiresPrivacyConsent = o["requiresPrivacyConsent"] as? Bool ?? false
        options.logLevel = HSUnity.logLevel(o["logLevel"] as? Int ?? 2)
        options.appGroup = o["appGroup"] as? String
        options.apnsEnvironment = o["apnsEnvironment"] as? String
        options.provisionalAuthorization = o["provisionalAuthorization"] as? Bool ?? false
    }
    guard !HSUnity.listenersRegistered else { HSUnity.refreshState(); return }
    HSUnity.listenersRegistered = true
    HeroSignal.Notifications.addForegroundLifecycleListener { event in
        let eventId = UUID().uuidString
        HSUnity.foregroundEvents[eventId] = event
        // `display()` süresi dolunca yerel SDK bildirimi kapatır; kayıt da temizlenir.
        DispatchQueue.main.asyncAfter(deadline: .now() + HSForegroundEvent.displayTimeout + 1) { HSUnity.foregroundEvents.removeValue(forKey: eventId) }
        // Eşzamanlı iletim: C# işleyici varsa bu çağrı içinde hs_unity_foreground_prevent_default çağırır.
        let json = HSUnity.encode(["eventId": eventId, "notification": HSUnity.map(event.notification)])
        if let callback = HSUnity.eventCallback {
            "foreground".withCString { t in json.withCString { j in callback(t, j) } }
        }
    }
    HeroSignal.User.pushSubscription.addObserver { state in
        HSUnity.cachedOptedIn = state.optedIn
        HSUnity.emit("subscription", HSUnity.map(state))
        HSUnity.updatePermission(state.permission == .authorized || state.permission == .provisional)
    }
    HeroSignal.InAppMessages.addClickListener { e in
        HSUnity.emit("inAppClick", ["messageId": e.messageId, "actionId": e.actionId, "url": HSUnity.nn(e.url)])
    }
    HeroSignal.InAppMessages.addLifecycleListener(HSUnity.lifecycle)
    // Bekleyen (uygulamayı açan) tıklama dinleyici eklenince hemen iletilir.
    HeroSignal.Notifications.addClickListener { e in
        HSUnity.emit("click", ["notification": HSUnity.map(e.notification), "actionId": HSUnity.nn(e.actionId), "url": HSUnity.nn(e.url)])
    }
    NotificationCenter.default.addObserver(forName: UIApplication.didBecomeActiveNotification, object: nil, queue: .main) { _ in HSUnity.refreshState() }
    HSUnity.refreshState()
}

@_cdecl("hs_unity_set_consent_given")
public func hs_unity_set_consent_given(_ given: Int32) { HeroSignal.setConsentGiven(given != 0) }

@_cdecl("hs_unity_login")
public func hs_unity_login(_ externalId: UnsafePointer<CChar>, _ identityHash: UnsafePointer<CChar>?) {
    HeroSignal.login(String(cString: externalId), identityHash: str(identityHash))
}

@_cdecl("hs_unity_logout")
public func hs_unity_logout() { HeroSignal.logout() }

@_cdecl("hs_unity_set_log_level")
public func hs_unity_set_log_level(_ level: Int32) { HeroSignal.Debug.setLogLevel(HSUnity.logLevel(Int(level))) }

// MARK: APNs token (HeroSignalUnityAppDelegate.mm, Unity AppDelegateListener'dan)

@_cdecl("hs_unity_internal_set_device_token")
public func hs_unity_internal_set_device_token(_ bytes: UnsafeRawPointer, _ length: Int32) {
    HeroSignal.setDeviceToken(Data(bytes: bytes, count: Int(length)))
}

@_cdecl("hs_unity_internal_set_device_token_error")
public func hs_unity_internal_set_device_token_error(_ message: UnsafePointer<CChar>?) {
    let text = str(message) ?? "APNs kaydı başarısız"
    HeroSignal.setDeviceTokenError(NSError(domain: "HeroSignalUnity", code: 0, userInfo: [NSLocalizedDescriptionKey: text]))
}

// MARK: Kullanıcı

@_cdecl("hs_unity_external_id")
public func hs_unity_external_id() -> UnsafeMutablePointer<CChar>? { dup(HeroSignal.User.externalId) }

@_cdecl("hs_unity_user_id")
public func hs_unity_user_id() -> UnsafeMutablePointer<CChar>? { dup(HeroSignal.User.id) }

@_cdecl("hs_unity_add_tags")
public func hs_unity_add_tags(_ json: UnsafePointer<CChar>) { HeroSignal.User.addTags(HSUnity.stringMap(String(cString: json))) }

@_cdecl("hs_unity_remove_tags")
public func hs_unity_remove_tags(_ json: UnsafePointer<CChar>) { HeroSignal.User.removeTags(HSUnity.stringList(String(cString: json))) }

@_cdecl("hs_unity_tags")
public func hs_unity_tags() -> UnsafeMutablePointer<CChar>? { dup(HSUnity.encode(HeroSignal.User.getTags())) }

@_cdecl("hs_unity_add_aliases")
public func hs_unity_add_aliases(_ json: UnsafePointer<CChar>) { HeroSignal.User.addAliases(HSUnity.stringMap(String(cString: json))) }

@_cdecl("hs_unity_remove_alias")
public func hs_unity_remove_alias(_ label: UnsafePointer<CChar>) { HeroSignal.User.removeAlias(String(cString: label)) }

@_cdecl("hs_unity_set_language")
public func hs_unity_set_language(_ code: UnsafePointer<CChar>) { HeroSignal.User.setLanguage(String(cString: code)) }

@_cdecl("hs_unity_add_email")
public func hs_unity_add_email(_ email: UnsafePointer<CChar>) { HeroSignal.User.addEmail(String(cString: email)) }

@_cdecl("hs_unity_remove_email")
public func hs_unity_remove_email(_ email: UnsafePointer<CChar>) { HeroSignal.User.removeEmail(String(cString: email)) }

@_cdecl("hs_unity_add_sms")
public func hs_unity_add_sms(_ phone: UnsafePointer<CChar>) { HeroSignal.User.addSms(String(cString: phone)) }

@_cdecl("hs_unity_remove_sms")
public func hs_unity_remove_sms(_ phone: UnsafePointer<CChar>) { HeroSignal.User.removeSms(String(cString: phone)) }

@_cdecl("hs_unity_emails")
public func hs_unity_emails() -> UnsafeMutablePointer<CChar>? { dup(HSUnity.encode(HeroSignal.User.emails)) }

@_cdecl("hs_unity_sms_numbers")
public func hs_unity_sms_numbers() -> UnsafeMutablePointer<CChar>? { dup(HSUnity.encode(HeroSignal.User.smsNumbers)) }

// MARK: Push aboneliği

/// Son bilinen durum; izin uygulama etkinleştiğinde yenilenir.
@_cdecl("hs_unity_push_subscription")
public func hs_unity_push_subscription() -> UnsafeMutablePointer<CChar>? {
    dup(HSUnity.encode(["id": HSUnity.nn(HeroSignal.User.pushSubscription.id), "token": HSUnity.nn(HeroSignal.User.pushSubscription.token),
                        "optedIn": HSUnity.cachedOptedIn, "permission": HSUnity.cachedPermission]))
}

@_cdecl("hs_unity_opt_in")
public func hs_unity_opt_in() { HeroSignal.User.pushSubscription.optIn() }

@_cdecl("hs_unity_opt_out")
public func hs_unity_opt_out() { HeroSignal.User.pushSubscription.optOut() }

// MARK: Bildirimler

@_cdecl("hs_unity_notification_permission")
public func hs_unity_notification_permission() -> Int32 { HSUnity.cachedPermission ? 1 : 0 }

/// Sonuç `{"granted":bool}`; reddedilmiş izinde `fallbackToSettings` ise bildirim ayarları açılır.
@_cdecl("hs_unity_request_permission")
public func hs_unity_request_permission(_ requestId: Int32, _ fallbackToSettings: Int32) {
    UNUserNotificationCenter.current().getNotificationSettings { settings in
        if settings.authorizationStatus == .denied {
            HSUnity.onMain {
                HSUnity.updatePermission(false)
                if fallbackToSettings != 0 {
                    let url: String
                    if #available(iOS 16.0, *) { url = UIApplication.openNotificationSettingsURLString } else { url = UIApplication.openSettingsURLString }
                    if let target = URL(string: url) { UIApplication.shared.open(target) }
                }
                HSUnity.complete(requestId, json: HSUnity.encode(["granted": false]), error: nil)
            }
            return
        }
        HeroSignal.Notifications.requestPermission { granted in
            HSUnity.updatePermission(granted)
            HSUnity.complete(requestId, json: HSUnity.encode(["granted": granted]), error: nil)
        }
    }
}

@_cdecl("hs_unity_clear_all_notifications")
public func hs_unity_clear_all_notifications() { HeroSignal.Notifications.clearAll() }

@_cdecl("hs_unity_remove_notification")
public func hs_unity_remove_notification(_ id: UnsafePointer<CChar>) { HeroSignal.Notifications.removeNotification(id: String(cString: id)) }

@_cdecl("hs_unity_remove_grouped_notifications")
public func hs_unity_remove_grouped_notifications(_ group: UnsafePointer<CChar>) {
    HeroSignal.Notifications.removeGroupedNotifications(group: String(cString: group))
}

@_cdecl("hs_unity_foreground_prevent_default")
public func hs_unity_foreground_prevent_default(_ eventId: UnsafePointer<CChar>) {
    HSUnity.foregroundEvents[String(cString: eventId)]?.preventDefault()
}

@_cdecl("hs_unity_foreground_display")
public func hs_unity_foreground_display(_ eventId: UnsafePointer<CChar>) {
    HSUnity.foregroundEvents.removeValue(forKey: String(cString: eventId))?.display()
}

// MARK: In-app

@_cdecl("hs_unity_add_triggers")
public func hs_unity_add_triggers(_ json: UnsafePointer<CChar>) { HeroSignal.InAppMessages.addTriggers(HSUnity.stringMap(String(cString: json))) }

@_cdecl("hs_unity_remove_triggers")
public func hs_unity_remove_triggers(_ json: UnsafePointer<CChar>) { HeroSignal.InAppMessages.removeTriggers(HSUnity.stringList(String(cString: json))) }

@_cdecl("hs_unity_clear_triggers")
public func hs_unity_clear_triggers() { HeroSignal.InAppMessages.clearTriggers() }

@_cdecl("hs_unity_triggers")
public func hs_unity_triggers() -> UnsafeMutablePointer<CChar>? { dup(HSUnity.encode(HeroSignal.InAppMessages.getTriggers())) }

@_cdecl("hs_unity_set_screen")
public func hs_unity_set_screen(_ name: UnsafePointer<CChar>?) { HeroSignal.InAppMessages.setScreen(str(name)) }

@_cdecl("hs_unity_in_app_paused")
public func hs_unity_in_app_paused() -> Int32 { HeroSignal.InAppMessages.paused ? 1 : 0 }

@_cdecl("hs_unity_set_in_app_paused")
public func hs_unity_set_in_app_paused(_ paused: Int32) { HeroSignal.InAppMessages.paused = paused != 0 }

// MARK: Bildirim kutusu (sonuç geri çağrısı ana iş parçacığında; hata JSON: {"code","message","status"})

@_cdecl("hs_unity_inbox_list")
public func hs_unity_inbox_list(_ requestId: Int32, _ limit: Int32, _ cursor: UnsafePointer<CChar>?) {
    let next = str(cursor)
    HSUnity.run(requestId) { try await HSUnity.encode(HSUnity.map(HeroSignal.Inbox.list(limit: Int(limit), cursor: next))) }
}

@_cdecl("hs_unity_inbox_mark_read")
public func hs_unity_inbox_mark_read(_ requestId: Int32, _ id: UnsafePointer<CChar>) {
    let itemId = String(cString: id)
    HSUnity.run(requestId) { try await HSUnity.encode(HSUnity.map(HeroSignal.Inbox.markRead(itemId))) }
}

/// Sonuç `{"count":n}` (kalan okunmamış).
@_cdecl("hs_unity_inbox_mark_all_read")
public func hs_unity_inbox_mark_all_read(_ requestId: Int32) {
    HSUnity.run(requestId) { try await HSUnity.encode(["count": HeroSignal.Inbox.markAllRead()]) }
}

@_cdecl("hs_unity_inbox_archive")
public func hs_unity_inbox_archive(_ requestId: Int32, _ id: UnsafePointer<CChar>) {
    let itemId = String(cString: id)
    HSUnity.run(requestId) { try await HeroSignal.Inbox.archive(itemId); return "{}" }
}

/// Sonuç `{"count":n}`.
@_cdecl("hs_unity_inbox_unread_count")
public func hs_unity_inbox_unread_count(_ requestId: Int32) {
    HSUnity.run(requestId) { try await HSUnity.encode(["count": HeroSignal.Inbox.unreadCount()]) }
}

// MARK: Oturum

@_cdecl("hs_unity_send_event")
public func hs_unity_send_event(_ name: UnsafePointer<CChar>, _ propertiesJson: UnsafePointer<CChar>, _ hasValue: Int32, _ value: Double) {
    HeroSignal.Session.sendEvent(String(cString: name), properties: HSUnity.object(String(cString: propertiesJson)), value: hasValue != 0 ? value : nil)
}
