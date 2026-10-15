import Foundation
import HeroSignal
// SPM: çekirdek tipler ayrı modülde; CocoaPods'ta (Unity + EDM4U) tek "HeroSignal" modülündedir.
#if canImport(HeroSignalCore)
import HeroSignalCore
#endif
import UIKit
import UserNotifications

/*
 Unity (C#, [DllImport("__Internal")]) → iOS SDK köprüsü. Yerel Swift SDK'sı Objective-C/C'ye açık olmadığından
 @_cdecl ile düz C fonksiyonları dışa aktarılır. Metinler UTF-8 C dizgesidir; dönen dizgeler strdup ile ayrılır ve
 C# tarafı hs_free ile serbest bırakır. JSON biçimleri Runtime/Internal/BridgeJson.cs ile aynıdır (epoch milisaniye).
 Geri çağrılar C işlev işaretçileridir (C# [MonoPInvokeCallback]); istek kimliği ile eşleştirilir.
 */

public typealias HSUEventCallback = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void
public typealias HSUResultCallback = @convention(c) (Int32, UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void
public typealias HSUPermissionCallback = @convention(c) (Int32, Int32) -> Void

/// Köprü durumu (olay geri çağrısı, ön plan kararları, izin önbelleği, Unity APNs token gözlemcisi).
final class HSUnityBridgeState: NSObject {
    static let shared = HSUnityBridgeState()

    let lock = NSLock()
    var eventCallback: HSUEventCallback?
    var listenersRegistered = false
    var foregroundHandlerEnabled = false
    var foregroundEvents: [String: HSForegroundEvent] = [:]
    var cachedOptedIn = false
    var cachedPermission = false
    var cachedStateKnown = false
    let lifecycleRelay = HSUnityLifecycleRelay()

    /// UnityAppController APNs token'ını ve hatasını NSNotification ile yayınlar (Plugins/iOS AppDelegateListener).
    func observeUnityAppController() {
        let center = NotificationCenter.default
        center.addObserver(self, selector: #selector(didRegister(_:)), name: NSNotification.Name("kUnityDidRegisterForRemoteNotificationsWithDeviceToken"), object: nil)
        center.addObserver(self, selector: #selector(didFailToRegister(_:)), name: NSNotification.Name("kUnityDidFailToRegisterForRemoteNotificationsWithError"), object: nil)
    }

    /// Unity token'ı `userInfo` alanında (sözlük değil, doğrudan NSData) gönderir; köprüleme yapmadan okunur.
    @objc private func didRegister(_ notification: NSNotification) {
        if let token = HSUnityBridgeState.payload(notification) as? Data {
            HeroSignal.setDeviceToken(token)
        }
    }

    @objc private func didFailToRegister(_ notification: NSNotification) {
        if let error = HSUnityBridgeState.payload(notification) as? NSError {
            HeroSignal.setDeviceTokenError(error)
        }
    }

    private static func payload(_ notification: NSNotification) -> Any? {
        if let raw = notification.value(forKey: "userInfo") {
            if raw is NSData || raw is NSError { return raw }
            if let dict = raw as? NSDictionary, let first = dict.allValues.first { return first }
        }
        return notification.object
    }
}

final class HSUnityLifecycleRelay: HSInAppLifecycleListener {
    func onWillDisplay(_ message: HSInAppMessage) { send("willDisplay", message) }
    func onDidDisplay(_ message: HSInAppMessage) { send("didDisplay", message) }
    func onWillDismiss(_ message: HSInAppMessage) { send("willDismiss", message) }
    func onDidDismiss(_ message: HSInAppMessage) { send("didDismiss", message) }

    private func send(_ event: String, _ message: HSInAppMessage) {
        hsuEmit("inAppLifecycle", ["event": event, "messageId": message.id, "json": hsuJsonString(message.raw)])
    }
}

// MARK: - Yardımcılar

private func hsuString(_ p: UnsafePointer<CChar>?) -> String? {
    guard let p else { return nil }
    return String(cString: p)
}

private func hsuNonEmpty(_ p: UnsafePointer<CChar>?) -> String? {
    guard let s = hsuString(p), !s.isEmpty else { return nil }
    return s
}

private func hsuDup(_ s: String?) -> UnsafeMutablePointer<CChar>? {
    guard let s else { return nil }
    return strdup(s)
}

func hsuJsonString(_ value: Any) -> String {
    guard JSONSerialization.isValidJSONObject(value),
          let data = try? JSONSerialization.data(withJSONObject: value),
          let text = String(data: data, encoding: .utf8) else { return "{}" }
    return text
}

private func hsuJsonObject(_ p: UnsafePointer<CChar>?) -> Any? {
    guard let s = hsuString(p), let data = s.data(using: .utf8) else { return nil }
    return try? JSONSerialization.jsonObject(with: data)
}

private func hsuStringMap(_ p: UnsafePointer<CChar>?) -> [String: String] {
    ((hsuJsonObject(p) as? [String: Any]) ?? [:]).compactMapValues { $0 as? String }
}

private func hsuStringList(_ p: UnsafePointer<CChar>?) -> [String] {
    (hsuJsonObject(p) as? [Any])?.compactMap { $0 as? String } ?? []
}

func hsuEmit(_ type: String, _ payload: [String: Any]) {
    let state = HSUnityBridgeState.shared
    state.lock.lock()
    let callback = state.eventCallback
    state.lock.unlock()
    guard let callback else { return }
    let json = hsuJsonString(payload)
    type.withCString { t in json.withCString { j in callback(t, j) } }
}

private func hsuReply(_ callback: HSUResultCallback?, _ requestId: Int32, _ json: String?, _ code: String?, _ message: String?) {
    guard let callback else { return }
    func with(_ s: String?, _ body: (UnsafePointer<CChar>?) -> Void) {
        if let s { s.withCString { body($0) } } else { body(nil) }
    }
    with(json) { j in with(code) { c in with(message) { m in callback(requestId, j, c, m) } } }
}

private func hsuLogLevel(_ value: Int) -> HSLog.Level { HSLog.Level(rawValue: value) ?? .warn }

private func hsuIsGranted(_ status: UNAuthorizationStatus) -> Bool {
    switch status {
    case .authorized, .provisional, .ephemeral: return true
    default: return false
    }
}

private func hsuMillis(_ date: Date?) -> Any {
    guard let date else { return NSNull() }
    return NSNumber(value: Int64((date.timeIntervalSince1970 * 1000).rounded()))
}

private func hsuNotificationJson(_ n: HSNotification) -> [String: Any] {
    [
        "title": n.title ?? NSNull(),
        "body": n.body ?? NSNull(),
        "imageUrl": n.imageUrl ?? NSNull(),
        "url": n.url ?? NSNull(),
        "deliveryId": n.deliveryId ?? NSNull(),
        "messageId": n.messageId ?? NSNull(),
        "actions": n.actions.map { a -> [String: Any] in
            ["id": a.id, "title": a.title, "url": a.url ?? NSNull(), "icon": a.icon ?? NSNull()]
        },
        "data": n.data,
    ]
}

private func hsuSubscriptionJson(id: String?, token: String?, optedIn: Bool, permission: Bool) -> [String: Any] {
    ["id": id ?? NSNull(), "token": token ?? NSNull(), "optedIn": optedIn, "permission": permission]
}

private func hsuItemJson(_ item: HSInboxItem) -> [String: Any] {
    [
        "id": item.id,
        "messageId": item.messageId ?? NSNull(),
        "title": item.title ?? NSNull(),
        "body": item.body ?? NSNull(),
        "url": item.url ?? NSNull(),
        "imageUrl": item.imageUrl ?? NSNull(),
        "data": item.data,
        "read": item.read,
        "readAt": hsuMillis(item.readAt),
        "archivedAt": hsuMillis(item.archivedAt),
        "expiresAt": hsuMillis(item.expiresAt),
        "createdAt": hsuMillis(item.createdAt),
    ]
}

private func hsuPageJson(_ page: HSInboxPage) -> [String: Any] {
    [
        "items": page.items.map { hsuItemJson($0) },
        "unreadCount": page.unreadCount,
        "nextCursor": page.nextCursor ?? NSNull(),
        "hasMore": page.hasMore,
    ]
}

/// Geçersiz öğeler (kategorisi metin olmayan) boş kategoriyle iletilir; yerel SDK doğrulaması reddeder.
private func hsuPreferenceInputs(_ p: UnsafePointer<CChar>?) -> [HSPreferenceInput] {
    ((hsuJsonObject(p) as? [Any]) ?? []).map { element in
        let o = element as? [String: Any] ?? [:]
        return HSPreferenceInput(category: o["category"] as? String ?? "", channel: o["channel"] as? String, optedIn: o["optedIn"] as? Bool ?? false)
    }
}

private func hsuPreferenceJson(_ p: HSPreference) -> [String: Any] {
    [
        "category": p.category,
        "channel": p.channel ?? NSNull(),
        "optedIn": p.optedIn,
        "explicit": p.explicit,
        "source": p.source ?? NSNull(),
        "updatedAt": hsuMillis(p.updatedAt),
    ]
}

private func hsuCategoryJson(_ c: HSMessageCategory) -> [String: Any] {
    [
        "key": c.key,
        "name": c.name,
        "description": c.description,
        "channels": c.channels ?? NSNull(),
        "defaultOptedIn": c.defaultOptedIn,
    ]
}

private func hsuPreferencesResultJson(_ r: HSPreferencesResult) -> [String: Any] {
    ["preferences": r.preferences.map { hsuPreferenceJson($0) }, "categories": r.categories.map { hsuCategoryJson($0) }]
}

/// Önbelleği günceller; önceki bilinen durumdan farklıysa true.
@discardableResult
private func hsuUpdateCache(_ s: HSPushSubscriptionState) -> Bool {
    let state = HSUnityBridgeState.shared
    let granted = hsuIsGranted(s.permission)
    state.lock.lock()
    defer { state.lock.unlock() }
    let changed = !state.cachedStateKnown || state.cachedOptedIn != s.optedIn || state.cachedPermission != granted
    state.cachedStateKnown = true
    state.cachedOptedIn = s.optedIn
    state.cachedPermission = granted
    return changed
}

/// İzin ve opt-in durumunu eşzamansız okur; değiştiyse `subscription` olayı üretir.
func hsuRefreshState() {
    Task {
        let s = await HeroSignal.User.pushSubscription.state()
        if hsuUpdateCache(s) {
            hsuEmit("subscription", hsuSubscriptionJson(id: s.id, token: s.token, optedIn: s.optedIn, permission: hsuIsGranted(s.permission)))
        }
    }
}

private func hsuOnMain(_ block: @escaping () -> Void) {
    if Thread.isMainThread { block() } else { DispatchQueue.main.async(execute: block) }
}

private func hsuReadOnMain<T>(_ block: () -> T) -> T {
    if Thread.isMainThread { return block() }
    return DispatchQueue.main.sync(execute: block)
}

@MainActor
private func hsuOpenNotificationSettings() {
    let raw: String
    if #available(iOS 16.0, *) {
        raw = UIApplication.openNotificationSettingsURLString
    } else {
        raw = UIApplication.openSettingsURLString
    }
    guard let url = URL(string: raw) else { return }
    UIApplication.shared.open(url)
}

private func hsuInbox(_ requestId: Int32, _ callback: HSUResultCallback?, _ body: @escaping () async throws -> String?) {
    Task {
        do {
            let json = try await body()
            hsuReply(callback, requestId, json, nil, nil)
        } catch let error as HSApiError {
            hsuReply(callback, requestId, nil, error.code, error.message)
        } catch let error as HSError {
            switch error {
            case .notInitialized: hsuReply(callback, requestId, nil, "NOT_INITIALIZED", "HeroSignal başlatılmadı")
            case .noSubscription: hsuReply(callback, requestId, nil, "NOT_READY", "Abonelik henüz kaydedilmedi")
            case .noUser: hsuReply(callback, requestId, nil, "NOT_READY", "Kullanıcı henüz kaydedilmedi")
            case .invalidArgument(let message): hsuReply(callback, requestId, nil, "INVALID_ARGUMENT", message)
            case .invalidResponse: hsuReply(callback, requestId, nil, "INVALID_RESPONSE", "Sunucu yanıtı ayrıştırılamadı")
            }
        } catch {
            hsuReply(callback, requestId, nil, "HS_ERROR", error.localizedDescription)
        }
    }
}

// MARK: - Dışa aktarılan C fonksiyonları

@_cdecl("hs_free")
public func hs_free(_ p: UnsafeMutablePointer<CChar>?) {
    free(p)
}

/// optionsJson: apiUrl, requiresPrivacyConsent, logLevel (0-4), iosAppGroup, iosApnsEnvironment, iosProvisionalAuthorization.
@_cdecl("hs_start")
public func hs_start(_ appId: UnsafePointer<CChar>?, _ optionsJson: UnsafePointer<CChar>?, _ callback: HSUEventCallback?) {
    guard let id = hsuNonEmpty(appId) else { return }
    let o = (hsuJsonObject(optionsJson) as? [String: Any]) ?? [:]
    let state = HSUnityBridgeState.shared
    state.lock.lock()
    state.eventCallback = callback
    let registerListeners = !state.listenersRegistered
    state.listenersRegistered = true
    state.lock.unlock()

    if registerListeners {
        // Token, SDK'nın başlangıçta yaptığı APNs kaydıyla gelebilir; gözlemci başlatmadan önce bağlanır.
        state.observeUnityAppController()
    }

    HeroSignal.initialize(id) { options in
        if let apiUrl = o["apiUrl"] as? String, !apiUrl.isEmpty { options.apiUrl = apiUrl }
        options.requiresPrivacyConsent = o["requiresPrivacyConsent"] as? Bool ?? false
        options.logLevel = hsuLogLevel((o["logLevel"] as? NSNumber)?.intValue ?? HSLog.Level.warn.rawValue)
        if let group = o["iosAppGroup"] as? String, !group.isEmpty { options.appGroup = group }
        if let env = o["iosApnsEnvironment"] as? String, !env.isEmpty { options.apnsEnvironment = env }
        options.provisionalAuthorization = o["iosProvisionalAuthorization"] as? Bool ?? false
    }

    if registerListeners {
        HeroSignal.User.pushSubscription.addObserver { s in
            hsuUpdateCache(s)
            hsuEmit("subscription", hsuSubscriptionJson(id: s.id, token: s.token, optedIn: s.optedIn, permission: hsuIsGranted(s.permission)))
        }
        HeroSignal.Notifications.addForegroundLifecycleListener { event in
            state.lock.lock()
            let enabled = state.foregroundHandlerEnabled
            state.lock.unlock()
            guard enabled else { return }
            // C# olayları ana iş parçacığında işlenir; gösterim ertelenir, karar hs_foreground_display ile gelir
            // (SDK 25 sn içinde karar gelmezse bildirimi göstermeden kapatır).
            event.preventDefault()
            let eventId = UUID().uuidString
            state.lock.lock(); state.foregroundEvents[eventId] = event; state.lock.unlock()
            DispatchQueue.main.asyncAfter(deadline: .now() + HSForegroundEvent.displayTimeout + 1) {
                state.lock.lock(); state.foregroundEvents.removeValue(forKey: eventId); state.lock.unlock()
            }
            hsuEmit("foreground", ["eventId": eventId, "notification": hsuNotificationJson(event.notification)])
        }
        HeroSignal.InAppMessages.addClickListener { event in
            hsuEmit("inAppClick", ["messageId": event.messageId, "actionId": event.actionId, "url": event.url ?? NSNull()])
        }
        HeroSignal.InAppMessages.addLifecycleListener(state.lifecycleRelay)
        // Yerel SDK izin gözlemcisi → `permission` olayı (`{"permission":bool}`): requestPermission sonrası ve uygulama
        // yeniden etkinleştiğinde (izin Ayarlar'dan değişmiş olabilir) etkin izin değiştiyse çağrılır.
        HeroSignal.Notifications.addPermissionObserver { granted in
            state.lock.lock(); state.cachedPermission = granted; state.lock.unlock()
            hsuEmit("permission", ["permission": granted])
        }
        // En son: uygulamayı açan (bekleyen) tıklama bu çağrıda iletilir.
        HeroSignal.Notifications.addClickListener { event in
            hsuEmit("click", [
                "notification": hsuNotificationJson(event.notification),
                "actionId": event.actionId ?? NSNull(),
                "url": event.url ?? NSNull(),
            ])
        }
    }
    hsuRefreshState()
}

@_cdecl("hs_set_consent_given")
public func hs_set_consent_given(_ given: Int32) { HeroSignal.setConsentGiven(given != 0) }

/// Kanıt app'in kimlik doğrulama kipine uymuyorsa hata mesajı döner (giriş yapılmaz); başarıda nil. C# `hs_free` ile serbest bırakır.
@_cdecl("hs_login")
public func hs_login(_ externalId: UnsafePointer<CChar>?, _ identityHash: UnsafePointer<CChar>?, _ jwt: UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    guard let id = hsuNonEmpty(externalId) else { return hsuDup("externalId gerekli") }
    let hash = hsuNonEmpty(identityHash), token = hsuNonEmpty(jwt)
    if let message = HeroSignal.loginProofError(identityHash: hash, jwt: token) { return hsuDup(message) }
    HeroSignal.login(id, identityHash: hash, jwt: token)
    return nil
}

/// Kimlik JWT'sini yeniler; giriş yapılmamışsa hata mesajı döner, başarıda nil.
@_cdecl("hs_update_user_jwt")
public func hs_update_user_jwt(_ jwt: UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>? {
    guard let token = hsuNonEmpty(jwt) else { return hsuDup("jwt gerekli") }
    do {
        try HeroSignal.updateUserJwt(token)
        return nil
    } catch {
        return hsuDup("Önce login ile kullanıcı kimliklendirilmeli")
    }
}

@_cdecl("hs_logout")
public func hs_logout() { HeroSignal.logout() }

@_cdecl("hs_set_log_level")
public func hs_set_log_level(_ level: Int32) { HeroSignal.Debug.setLogLevel(hsuLogLevel(Int(level))) }

@_cdecl("hs_external_id")
public func hs_external_id() -> UnsafeMutablePointer<CChar>? { hsuDup(HeroSignal.User.externalId) }

@_cdecl("hs_user_id")
public func hs_user_id() -> UnsafeMutablePointer<CChar>? { hsuDup(HeroSignal.User.id) }

@_cdecl("hs_add_tags")
public func hs_add_tags(_ json: UnsafePointer<CChar>?) { HeroSignal.User.addTags(hsuStringMap(json)) }

@_cdecl("hs_remove_tags")
public func hs_remove_tags(_ json: UnsafePointer<CChar>?) { HeroSignal.User.removeTags(hsuStringList(json)) }

@_cdecl("hs_tags")
public func hs_tags() -> UnsafeMutablePointer<CChar>? { hsuDup(hsuJsonString(HeroSignal.User.getTags())) }

@_cdecl("hs_add_aliases")
public func hs_add_aliases(_ json: UnsafePointer<CChar>?) { HeroSignal.User.addAliases(hsuStringMap(json)) }

@_cdecl("hs_remove_alias")
public func hs_remove_alias(_ label: UnsafePointer<CChar>?) {
    guard let label = hsuNonEmpty(label) else { return }
    HeroSignal.User.removeAlias(label)
}

@_cdecl("hs_set_language")
public func hs_set_language(_ code: UnsafePointer<CChar>?) {
    guard let code = hsuNonEmpty(code) else { return }
    HeroSignal.User.setLanguage(code)
}

@_cdecl("hs_add_email")
public func hs_add_email(_ email: UnsafePointer<CChar>?) { HeroSignal.User.addEmail(hsuString(email) ?? "") }

@_cdecl("hs_remove_email")
public func hs_remove_email(_ email: UnsafePointer<CChar>?) { HeroSignal.User.removeEmail(hsuString(email) ?? "") }

@_cdecl("hs_add_sms")
public func hs_add_sms(_ phone: UnsafePointer<CChar>?) { HeroSignal.User.addSms(hsuString(phone) ?? "") }

@_cdecl("hs_remove_sms")
public func hs_remove_sms(_ phone: UnsafePointer<CChar>?) { HeroSignal.User.removeSms(hsuString(phone) ?? "") }

@_cdecl("hs_emails")
public func hs_emails() -> UnsafeMutablePointer<CChar>? { hsuDup(hsuJsonString(HeroSignal.User.emails)) }

@_cdecl("hs_sms_numbers")
public func hs_sms_numbers() -> UnsafeMutablePointer<CChar>? { hsuDup(hsuJsonString(HeroSignal.User.smsNumbers)) }

/// `{"id","token","optedIn","permission"}`; id/token canlı, optedIn/permission son yenilemeden.
@_cdecl("hs_push_subscription")
public func hs_push_subscription() -> UnsafeMutablePointer<CChar>? {
    let state = HSUnityBridgeState.shared
    state.lock.lock()
    let optedIn = state.cachedOptedIn
    let permission = state.cachedPermission
    state.lock.unlock()
    return hsuDup(hsuJsonString(hsuSubscriptionJson(id: HeroSignal.User.pushSubscription.id, token: HeroSignal.User.pushSubscription.token, optedIn: optedIn, permission: permission)))
}

@_cdecl("hs_opt_in")
public func hs_opt_in() { HeroSignal.User.pushSubscription.optIn() }

@_cdecl("hs_opt_out")
public func hs_opt_out() { HeroSignal.User.pushSubscription.optOut() }

@_cdecl("hs_permission")
public func hs_permission() -> Int32 {
    let state = HSUnityBridgeState.shared
    state.lock.lock(); defer { state.lock.unlock() }
    return state.cachedPermission ? 1 : 0
}

/// İzin daha önce reddedildiyse ve fallbackToSettings != 0 ise uygulamanın bildirim ayarları açılır.
@_cdecl("hs_request_permission")
public func hs_request_permission(_ fallbackToSettings: Int32, _ requestId: Int32, _ callback: HSUPermissionCallback?) {
    Task {
        let status = await HeroSignal.Notifications.permission()
        if status == .denied {
            if fallbackToSettings != 0 { await hsuOpenNotificationSettings() }
            hsuRefreshState()
            callback?(requestId, 0)
            return
        }
        HeroSignal.Notifications.requestPermission { granted in
            hsuRefreshState()
            callback?(requestId, granted ? 1 : 0)
        }
    }
}

@_cdecl("hs_clear_all_notifications")
public func hs_clear_all_notifications() { HeroSignal.Notifications.clearAll() }

/// Tek bildirimi istek kimliğiyle (`UNNotificationRequest.identifier`) bildirim merkezinden kaldırır.
@_cdecl("hs_remove_notification")
public func hs_remove_notification(_ identifier: UnsafePointer<CChar>?) {
    guard let id = hsuString(identifier), !id.isEmpty else { return }
    HeroSignal.Notifications.removeNotification(identifier: id)
}

/// Aynı `thread-id`'ye sahip bildirimleri bildirim merkezinden kaldırır.
@_cdecl("hs_remove_grouped_notifications")
public func hs_remove_grouped_notifications(_ group: UnsafePointer<CChar>?) {
    guard let threadId = hsuString(group), !threadId.isEmpty else { return }
    HeroSignal.Notifications.removeGroupedNotifications(threadId)
}

@_cdecl("hs_set_foreground_handler_enabled")
public func hs_set_foreground_handler_enabled(_ enabled: Int32) {
    let state = HSUnityBridgeState.shared
    state.lock.lock(); state.foregroundHandlerEnabled = enabled != 0; state.lock.unlock()
}

@_cdecl("hs_foreground_display")
public func hs_foreground_display(_ eventId: UnsafePointer<CChar>?) {
    guard let id = hsuString(eventId) else { return }
    let state = HSUnityBridgeState.shared
    state.lock.lock(); let event = state.foregroundEvents.removeValue(forKey: id); state.lock.unlock()
    event?.display()
}

@_cdecl("hs_add_triggers")
public func hs_add_triggers(_ json: UnsafePointer<CChar>?) {
    let triggers = hsuStringMap(json)
    hsuOnMain { HeroSignal.InAppMessages.addTriggers(triggers) }
}

@_cdecl("hs_remove_triggers")
public func hs_remove_triggers(_ json: UnsafePointer<CChar>?) {
    let keys = hsuStringList(json)
    hsuOnMain { HeroSignal.InAppMessages.removeTriggers(keys) }
}

@_cdecl("hs_clear_triggers")
public func hs_clear_triggers() { hsuOnMain { HeroSignal.InAppMessages.clearTriggers() } }

@_cdecl("hs_triggers")
public func hs_triggers() -> UnsafeMutablePointer<CChar>? {
    hsuDup(hsuReadOnMain { hsuJsonString(HeroSignal.InAppMessages.getTriggers()) })
}

/// Boş ad ekrandan çıkış demektir.
@_cdecl("hs_set_screen")
public func hs_set_screen(_ name: UnsafePointer<CChar>?) {
    let screen = hsuNonEmpty(name)
    hsuOnMain { HeroSignal.InAppMessages.setScreen(screen) }
}

@_cdecl("hs_in_app_paused")
public func hs_in_app_paused() -> Int32 {
    let paused = hsuReadOnMain { HeroSignal.InAppMessages.paused }
    return paused ? 1 : 0
}

@_cdecl("hs_set_in_app_paused")
public func hs_set_in_app_paused(_ paused: Int32) { hsuOnMain { HeroSignal.InAppMessages.paused = paused != 0 } }

@_cdecl("hs_inbox_list")
public func hs_inbox_list(_ limit: Int32, _ cursor: UnsafePointer<CChar>?, _ requestId: Int32, _ callback: HSUResultCallback?) {
    let c = hsuNonEmpty(cursor)
    hsuInbox(requestId, callback) {
        let page = try await HeroSignal.Inbox.list(limit: Int(limit), cursor: c)
        return hsuJsonString(hsuPageJson(page))
    }
}

@_cdecl("hs_inbox_mark_read")
public func hs_inbox_mark_read(_ id: UnsafePointer<CChar>?, _ requestId: Int32, _ callback: HSUResultCallback?) {
    let itemId = hsuString(id) ?? ""
    hsuInbox(requestId, callback) {
        let item = try await HeroSignal.Inbox.markRead(itemId)
        return hsuJsonString(hsuItemJson(item))
    }
}

@_cdecl("hs_inbox_mark_all_read")
public func hs_inbox_mark_all_read(_ requestId: Int32, _ callback: HSUResultCallback?) {
    hsuInbox(requestId, callback) {
        let unread = try await HeroSignal.Inbox.markAllRead()
        return hsuJsonString(["unreadCount": unread])
    }
}

@_cdecl("hs_inbox_archive")
public func hs_inbox_archive(_ id: UnsafePointer<CChar>?, _ requestId: Int32, _ callback: HSUResultCallback?) {
    let itemId = hsuString(id) ?? ""
    hsuInbox(requestId, callback) {
        try await HeroSignal.Inbox.archive(itemId)
        return nil
    }
}

@_cdecl("hs_inbox_unread_count")
public func hs_inbox_unread_count(_ requestId: Int32, _ callback: HSUResultCallback?) {
    hsuInbox(requestId, callback) {
        let unread = try await HeroSignal.Inbox.unreadCount()
        return hsuJsonString(["unreadCount": unread])
    }
}

/// Başarı JSON'u: {"preferences":[…],"categories":[…]} (ADR-0019 §4).
@_cdecl("hs_get_preferences")
public func hs_get_preferences(_ requestId: Int32, _ callback: HSUResultCallback?) {
    hsuInbox(requestId, callback) {
        let result = try await HeroSignal.User.getPreferences()
        return hsuJsonString(hsuPreferencesResultJson(result))
    }
}

/// json: [{"category","channel":null|"…","optedIn"}]; doğrulama yerel SDK'dadır (geçersizse INVALID_ARGUMENT).
@_cdecl("hs_set_preferences")
public func hs_set_preferences(_ json: UnsafePointer<CChar>?, _ requestId: Int32, _ callback: HSUResultCallback?) {
    let inputs = hsuPreferenceInputs(json)
    hsuInbox(requestId, callback) {
        let result: HSPreferencesResult = try await HeroSignal.User.setPreferences(inputs)
        return hsuJsonString(hsuPreferencesResultJson(result))
    }
}

/// propertiesJson JSON nesnesi (iç içe nesne/dizi desteklenir); hasValue 0 ise değer gönderilmez.
@_cdecl("hs_send_event")
public func hs_send_event(_ name: UnsafePointer<CChar>?, _ propertiesJson: UnsafePointer<CChar>?, _ value: Double, _ hasValue: Int32) {
    guard let eventName = hsuNonEmpty(name) else { return }
    let properties = (hsuJsonObject(propertiesJson) as? [String: Any]) ?? [:]
    HeroSignal.Session.sendEvent(eventName, properties: properties, value: hasValue != 0 ? value : nil)
}

// MARK: - Live Activities (ADR-0024)

/// Live Activities çağrısını ana iş parçacığında (ActivityKit) çalıştırır; `HSLiveActivityError` kodu ve mesajı aynen,
/// diğer hatalar `LIVE_ACTIVITY_FAILED` olarak iletilir. Başarı JSON'u nil.
private func hsuLiveActivity(_ requestId: Int32, _ callback: HSUResultCallback?, _ body: @escaping () throws -> Void) {
    hsuOnMain {
        do {
            try body()
            hsuReply(callback, requestId, nil, nil, nil)
        } catch let error as HSLiveActivityError {
            hsuReply(callback, requestId, nil, error.code, error.message)
        } catch {
            hsuReply(callback, requestId, nil, "LIVE_ACTIVITY_FAILED", error.localizedDescription)
        }
    }
}

/// Genel `HeroSignalDefaultLiveActivityAttributes` türünü izlemeye alır (push-to-start iOS 17.2+, güncelleme belirteçleri).
@_cdecl("hs_setup_default_live_activity")
public func hs_setup_default_live_activity(_ requestId: Int32, _ callback: HSUResultCallback?) {
    hsuLiveActivity(requestId, callback) { try HeroSignal.LiveActivities.setupDefaultLiveActivity() }
}

/// Genel türle yerel etkinlik başlatır (iOS 16.1+). attributesJson/contentStateJson: düz JSON nesnesi (metin, sayı,
/// mantıksal değer); doğrulama yerel SDK'dadır. C dizgeleri çağrı süresince geçerli olduğundan hemen çözülür.
@_cdecl("hs_start_default_live_activity")
public func hs_start_default_live_activity(_ activityId: UnsafePointer<CChar>?, _ attributesJson: UnsafePointer<CChar>?, _ contentStateJson: UnsafePointer<CChar>?, _ requestId: Int32, _ callback: HSUResultCallback?) {
    let id = hsuString(activityId) ?? ""
    guard let attributes = hsuJsonObject(attributesJson) as? [String: Any],
          let contentState = hsuJsonObject(contentStateJson) as? [String: Any] else {
        hsuReply(callback, requestId, nil, "INVALID_ARGUMENT", "attributes ve contentState JSON nesnesi olmalı")
        return
    }
    hsuLiveActivity(requestId, callback) {
        try HeroSignal.LiveActivities.startDefaultLiveActivity(activityId: id, attributes: attributes, contentState: contentState)
    }
}
