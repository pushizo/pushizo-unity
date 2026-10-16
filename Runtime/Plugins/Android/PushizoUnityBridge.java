package com.pushizo.unity;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.app.Application;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;

import androidx.core.app.ActivityCompat;

import com.pushizo.sdk.Pushizo;
import com.pushizo.sdk.HsAction;
import com.pushizo.sdk.HsApiException;
import com.pushizo.sdk.HsForegroundEvent;
import com.pushizo.sdk.HsInAppLifecycleEvent;
import com.pushizo.sdk.HsInAppLifecycleListener;
import com.pushizo.sdk.HsInboxItem;
import com.pushizo.sdk.HsInboxPage;
import com.pushizo.sdk.HsLog;
import com.pushizo.sdk.HsMessageCategory;
import com.pushizo.sdk.HsNotification;
import com.pushizo.sdk.HsPreference;
import com.pushizo.sdk.HsPreferenceInput;
import com.pushizo.sdk.HsPreferencesResult;
import com.pushizo.sdk.HsPushSubscriptionState;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.lang.ref.WeakReference;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

import kotlin.Result;
import kotlin.Unit;
import kotlin.coroutines.Continuation;
import kotlin.coroutines.CoroutineContext;
import kotlin.coroutines.EmptyCoroutineContext;
import kotlin.coroutines.intrinsics.IntrinsicsKt;

/**
 * Unity (C#) → Android SDK köprüsü. C# tarafı AndroidJavaClass/AndroidJavaProxy ile yalnızca metin (JSON), bool, int,
 * double ve arayüz geri çağrıları kullanır; JSON biçimleri Runtime/Internal/BridgeJson.cs ve .NET MAUI köprüsüyle aynıdır.
 * Kotlin SDK'nın `suspend` bildirim kutusu fonksiyonları Java'dan Continuation ile çağrılır (Kotlin `Result` parametreli
 * HsCallback Java'dan uygulanamaz). Unity Gradle derlemesinde Kotlin eklentisi gerekmez.
 */
public final class PushizoUnityBridge {
    private static final int MAX_PENDING_FOREGROUND = 32;
    /** İzin iletişim kutusu açılmazsa (kalıcı ret) sonucun bildirileceği süre. */
    private static final long PERMISSION_NO_DIALOG_TIMEOUT_MS = 1000;

    private static volatile PushizoUnityListener listener;
    private static volatile Context appContext;
    private static volatile boolean listenersRegistered;
    private static volatile boolean foregroundHandlerEnabled;

    private static final Handler MAIN = new Handler(Looper.getMainLooper());

    /** Ertelenmiş ön plan bildirimleri (foreground olayının eventId'si → olay). */
    private static final Map<String, HsForegroundEvent> FOREGROUND_EVENTS = new LinkedHashMap<String, HsForegroundEvent>() {
        @Override
        protected boolean removeEldestEntry(Map.Entry<String, HsForegroundEvent> eldest) {
            return size() > MAX_PENDING_FOREGROUND;
        }
    };

    private static final Object PERMISSION_LOCK = new Object();
    private static final List<PushizoUnityPermissionCallback> PERMISSION_CALLBACKS = new ArrayList<>();
    private static boolean permissionPending;
    private static boolean permissionFallbackToSettings;
    private static boolean pausedSinceRequest;
    private static WeakReference<Activity> permissionActivity;

    /** Konum izni isteği: açıklama diyaloğu → sistem izni → sonuç (bildirim izniyle aynı ön plana dönüş deseni). */
    private static final Object LOCATION_LOCK = new Object();
    private static final List<PushizoUnityPermissionCallback> LOCATION_CALLBACKS = new ArrayList<>();
    private static final int LOCATION_IDLE = 0;
    private static final int LOCATION_RATIONALE = 1;
    private static final int LOCATION_SYSTEM = 2;
    private static int locationPhase = LOCATION_IDLE;
    private static boolean locationPausedSinceRequest;

    private PushizoUnityBridge() {
    }

    private static final HsInAppLifecycleListener IN_APP_LIFECYCLE = new HsInAppLifecycleListener() {
        @Override
        public void onWillDisplay(HsInAppLifecycleEvent event) { sendLifecycle("willDisplay", event); }

        @Override
        public void onDidDisplay(HsInAppLifecycleEvent event) { sendLifecycle("didDisplay", event); }

        @Override
        public void onWillDismiss(HsInAppLifecycleEvent event) { sendLifecycle("willDismiss", event); }

        @Override
        public void onDidDismiss(HsInAppLifecycleEvent event) { sendLifecycle("didDismiss", event); }
    };

    /**
     * İzin iletişim kutusu sonucunu (etkinliğin ön plana dönüşü) yakalar. Ayarlardan yapılan izin değişikliklerini
     * yerel SDK izin gözlemcisi bildirir ({@link #PERMISSION_OBSERVER}).
     */
    private static final Application.ActivityLifecycleCallbacks ACTIVITY_CALLBACKS = new Application.ActivityLifecycleCallbacks() {
        @Override
        public void onActivityPaused(Activity activity) {
            synchronized (PERMISSION_LOCK) {
                if (permissionPending) pausedSinceRequest = true;
            }
            synchronized (LOCATION_LOCK) {
                // Açıklama diyaloğu etkinliği duraklatmaz; yalnızca sistem izin penceresi sayılır.
                if (locationPhase == LOCATION_SYSTEM) locationPausedSinceRequest = true;
            }
        }

        @Override
        public void onActivityResumed(Activity activity) {
            boolean resolve;
            synchronized (PERMISSION_LOCK) {
                resolve = permissionPending && pausedSinceRequest;
            }
            if (resolve) resolvePermissionRequest();
            boolean resolveLocation;
            synchronized (LOCATION_LOCK) {
                resolveLocation = locationPhase == LOCATION_SYSTEM && locationPausedSinceRequest;
            }
            if (resolveLocation) resolveLocationRequest();
        }

        @Override public void onActivityCreated(Activity activity, Bundle savedInstanceState) { }
        @Override public void onActivityStarted(Activity activity) { }
        @Override public void onActivityStopped(Activity activity) { }
        @Override public void onActivitySaveInstanceState(Activity activity, Bundle outState) { }
        @Override public void onActivityDestroyed(Activity activity) { }
    };

    /**
     * Yerel SDK izin gözlemcisi → {@code permission} olayı ({@code {"permission":bool}}): izin isteği sonucu ve uygulama
     * ön plana döndüğünde (izin sistem ayarlarından değişmiş olabilir) etkin izin değiştiyse çağrılır.
     */
    private static final Pushizo.Notifications.PermissionObserver PERMISSION_OBSERVER =
            granted -> emit("permission", object("permission", granted));

    // --- Başlatma ---

    /**
     * Yerel SDK'yı başlatır ve dinleyicileri (bir kez) bağlar. optionsJson: apiUrl, requiresPrivacyConsent,
     * logLevel (0-4), androidDefaultChannelName, androidDefaultChannelId.
     */
    public static void start(Activity activity, String appId, String optionsJson, PushizoUnityListener l) {
        JSONObject o = parseObject(optionsJson);
        Context ctx = activity.getApplicationContext();
        listener = l;
        appContext = ctx;
        Pushizo.Options defaults = new Pushizo.Options();
        Pushizo.Options options = new Pushizo.Options(
                nonBlank(o.optString("apiUrl"), defaults.getApiUrl()),
                o.optBoolean("requiresPrivacyConsent", false),
                logLevel(o.optInt("logLevel", HsLog.Level.WARN.getPriority())),
                nonBlank(o.optString("androidDefaultChannelName"), defaults.getDefaultChannelName()),
                nonBlank(o.optString("androidDefaultChannelId"), defaults.getDefaultChannelId()));
        Pushizo.initialize(ctx, appId, options);
        if (!listenersRegistered) {
            listenersRegistered = true;
            if (ctx instanceof Application) ((Application) ctx).registerActivityLifecycleCallbacks(ACTIVITY_CALLBACKS);
            Pushizo.User.pushSubscription.addObserver(state -> {
                emit("subscription", subscriptionJson(state));
                return Unit.INSTANCE;
            });
            Pushizo.Notifications.addPermissionObserver(PERMISSION_OBSERVER);
            Pushizo.Notifications.addForegroundLifecycleListener(event -> {
                onForeground(event);
                return Unit.INSTANCE;
            });
            Pushizo.InAppMessages.addClickListener(event -> {
                emit("inAppClick", object("messageId", event.getMessageId(), "actionId", event.getActionId(), "url", event.getUrl()));
                return Unit.INSTANCE;
            });
            Pushizo.InAppMessages.addLifecycleListener(IN_APP_LIFECYCLE);
            // En son: uygulamayı açan (bekleyen) tıklama bu çağrıda iletilir.
            Pushizo.Notifications.addClickListener(event -> {
                JSONObject payload = object("actionId", event.getActionId(), "url", event.getUrl());
                put(payload, "notification", notificationJson(event.getNotification()));
                emit("click", payload);
                return Unit.INSTANCE;
            });
        }
    }

    public static void setConsentGiven(boolean given) { Pushizo.setConsentGiven(given); }

    /**
     * identityHash/jwt boşsa yok sayılır (Unity JNI çağrıları null metin geçiremez). Kanıt app'in kimlik doğrulama kipine
     * uymuyorsa hata mesajı döner (giriş yapılmaz); başarıda null.
     */
    public static String login(String externalId, String identityHash, String jwt) {
        try {
            Pushizo.login(externalId, emptyToNull(identityHash), emptyToNull(jwt));
            return null;
        } catch (IllegalStateException | IllegalArgumentException e) {
            return e.getMessage() != null ? e.getMessage() : "kimlik doğrulama hatası";
        }
    }

    /** Kimlik JWT'sini yeniler; giriş yapılmamışsa hata mesajı döner, başarıda null. */
    public static String updateUserJwt(String jwt) {
        try {
            Pushizo.updateUserJwt(jwt);
            return null;
        } catch (IllegalStateException | IllegalArgumentException e) {
            return e.getMessage() != null ? e.getMessage() : "kimlik doğrulama hatası";
        }
    }

    public static void logout() { Pushizo.logout(); }

    /** 0 none, 1 error, 2 warn, 3 info, 4 debug. */
    public static void applyLogLevel(int level) { Pushizo.Debug.setLogLevel(logLevel(level)); }

    // --- Kullanıcı ---

    public static String externalId() { return Pushizo.User.INSTANCE.getExternalId(); }

    public static String userId() { return Pushizo.User.INSTANCE.getId(); }

    public static void addTags(String tagsJson) { Pushizo.User.addTags(stringMap(tagsJson)); }

    public static void removeTags(String keysJson) { Pushizo.User.removeTags(stringList(keysJson)); }

    public static String tags() { return new JSONObject(Pushizo.User.getTags()).toString(); }

    public static void addAliases(String aliasesJson) { Pushizo.User.addAliases(stringMap(aliasesJson)); }

    public static void removeAlias(String label) { Pushizo.User.removeAlias(label); }

    public static void setLanguage(String code) { Pushizo.User.setLanguage(code); }

    public static void addEmail(String email) { Pushizo.User.addEmail(email); }

    public static void removeEmail(String email) { Pushizo.User.removeEmail(email); }

    public static void addSms(String phoneNumber) { Pushizo.User.addSms(phoneNumber); }

    public static void removeSms(String phoneNumber) { Pushizo.User.removeSms(phoneNumber); }

    public static String emails() { return new JSONArray(Pushizo.User.INSTANCE.getEmails()).toString(); }

    public static String smsNumbers() { return new JSONArray(Pushizo.User.INSTANCE.getSmsNumbers()).toString(); }

    // --- Push aboneliği ve izin ---

    public static String pushSubscription() {
        return subscriptionJson(Pushizo.User.pushSubscription.INSTANCE.getState()).toString();
    }

    public static void optIn() { Pushizo.User.pushSubscription.optIn(); }

    public static void optOut() { Pushizo.User.pushSubscription.optOut(); }

    public static boolean permission() { return Pushizo.Notifications.INSTANCE.getPermission(); }

    /**
     * Android 13+ bildirim iznini ister. Sonuç, izin iletişim kutusu kapanıp etkinlik ön plana döndüğünde bildirilir;
     * iletişim kutusu açılmazsa (kalıcı ret) kısa süre sonra güncel durumla bildirilir. fallbackToSettings: izin kalıcı
     * olarak reddedilmişse uygulamanın bildirim ayarları açılır.
     */
    public static void requestPermission(Activity activity, boolean fallbackToSettings, PushizoUnityPermissionCallback callback) {
        if (Pushizo.Notifications.INSTANCE.getPermission()) {
            callback.onResult(true);
            return;
        }
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU || activity == null) {
            // Android 12 ve öncesinde çalışma zamanı izni yoktur; bildirimler kapalıysa yalnızca ayarlardan açılabilir.
            if (fallbackToSettings) openNotificationSettings(activity != null ? activity : appContext);
            callback.onResult(Pushizo.Notifications.INSTANCE.getPermission());
            return;
        }
        synchronized (PERMISSION_LOCK) {
            PERMISSION_CALLBACKS.add(callback);
            permissionFallbackToSettings = permissionFallbackToSettings || fallbackToSettings;
            if (permissionPending) return;
            permissionPending = true;
            pausedSinceRequest = false;
            permissionActivity = new WeakReference<>(activity);
        }
        activity.runOnUiThread(() -> Pushizo.Notifications.requestPermission(activity));
        MAIN.postDelayed(() -> {
            boolean noDialog;
            synchronized (PERMISSION_LOCK) {
                noDialog = permissionPending && !pausedSinceRequest;
            }
            if (noDialog) resolvePermissionRequest();
        }, PERMISSION_NO_DIALOG_TIMEOUT_MS);
    }

    private static void resolvePermissionRequest() {
        List<PushizoUnityPermissionCallback> callbacks;
        boolean fallback;
        Activity activity;
        synchronized (PERMISSION_LOCK) {
            if (!permissionPending) return;
            permissionPending = false;
            callbacks = new ArrayList<>(PERMISSION_CALLBACKS);
            PERMISSION_CALLBACKS.clear();
            fallback = permissionFallbackToSettings;
            permissionFallbackToSettings = false;
            activity = permissionActivity != null ? permissionActivity.get() : null;
            permissionActivity = null;
        }
        // İzin değiştiyse yerel SDK izin gözlemcisi `permission` olayını gönderir.
        Pushizo.Notifications.onPermissionResult();
        boolean granted = Pushizo.Notifications.INSTANCE.getPermission();
        // Gerekçe gösterilemiyorsa izin kalıcı olarak reddedilmiştir (sistem iletişim kutusu artık açılmaz).
        if (!granted && fallback && activity != null
                && !ActivityCompat.shouldShowRequestPermissionRationale(activity, Manifest.permission.POST_NOTIFICATIONS)) {
            openNotificationSettings(activity);
        }
        for (PushizoUnityPermissionCallback callback : callbacks) {
            try {
                callback.onResult(granted);
            } catch (RuntimeException e) {
                HsLog.INSTANCE.e("izin geri çağrısı hata verdi", e);
            }
        }
    }

    private static void openNotificationSettings(Context context) {
        if (context == null) return;
        Intent intent;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            intent = new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, context.getPackageName());
        } else {
            intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", context.getPackageName(), null));
        }
        if (!(context instanceof Activity)) intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        try {
            context.startActivity(intent);
        } catch (RuntimeException e) {
            HsLog.INSTANCE.w("bildirim ayarları açılamadı", e);
        }
    }

    /** Tepsideki Pushizo bildirimlerini (ve grup özetlerini) kaldırır; uygulamanın diğer bildirimlerine dokunmaz. */
    public static void clearAllNotifications() {
        Pushizo.Notifications.clearAll();
    }

    /** Tek bir Pushizo bildirimini tepsi kimliğiyle (androidNotificationId) kaldırır. */
    public static void removeNotification(int androidNotificationId) {
        Pushizo.Notifications.removeNotification(androidNotificationId);
    }

    /** Aynı grup anahtarına (android_group) sahip bildirimleri ve grup özetini kaldırır. */
    public static void removeGroupedNotifications(String group) {
        Pushizo.Notifications.removeGroupedNotifications(group);
    }

    // --- Konum (v2 Faz 17b; yalnızca ön plan) ---

    /** Konum paylaşımı açık mı (yerel SDK'nın kalıcı tercihi; izin durumundan bağımsız). */
    public static boolean locationShared() { return Pushizo.Location.isShared(); }

    /** Paylaşımı açar/kapatır; kapatınca yerel SDK sunucuya {@code location: null} gönderir. */
    public static void setLocationShared(boolean shared) { Pushizo.Location.setShared(shared); }

    /**
     * Ön plan konum iznini ister. Yerel SDK'nın {@code Location.requestPermission} sonucu bildirmediği ve Unity etkinliği
     * {@code onRequestPermissionsResult}'ı iletmediği için akış köprüdedir: izin zaten varsa true; etkinlik yoksa veya
     * manifest'te ACCESS_COARSE_LOCATION/ACCESS_FINE_LOCATION yoksa (uyarı loglanır) false; aksi halde SDK string
     * kaynaklarıyla açıklama diyaloğu (mesaj: rationale boş değilse o), "Şimdi değil"/iptal → false; "Devam" → sistem izni
     * ({@code Pushizo.Location.PERMISSION_REQUEST_CODE}). Sonuç izin penceresi kapanıp etkinlik ön plana döndüğünde
     * (pencere açılmazsa — kalıcı ret — kısa süre sonra) {@code Pushizo.Location.onPermissionResult()} çağrılarak
     * güncel izin durumuyla bildirilir. Bekleyen istek varken gelen çağrılar aynı sonucu alır.
     */
    public static void requestLocationPermission(Activity activity, String rationale, PushizoUnityPermissionCallback callback) {
        if (Pushizo.Location.getPermissionGranted()) {
            Pushizo.Location.onPermissionResult();
            callback.onResult(true);
            return;
        }
        if (activity == null) {
            callback.onResult(false);
            return;
        }
        String[] permissions = declaredLocationPermissions(activity);
        if (permissions.length == 0) {
            HsLog.INSTANCE.w("konum izni istenemedi: uygulama manifest'inde ACCESS_COARSE_LOCATION/ACCESS_FINE_LOCATION yok", null);
            callback.onResult(false);
            return;
        }
        synchronized (LOCATION_LOCK) {
            LOCATION_CALLBACKS.add(callback);
            if (locationPhase != LOCATION_IDLE) return;
            locationPhase = LOCATION_RATIONALE;
            locationPausedSinceRequest = false;
        }
        String message = rationale == null || rationale.trim().isEmpty() ? null : rationale;
        activity.runOnUiThread(() -> showLocationRationale(activity, message, permissions));
    }

    private static void showLocationRationale(Activity activity, String rationale, String[] permissions) {
        if (activity.isFinishing() || activity.isDestroyed()) {
            resolveLocationRequest();
            return;
        }
        try {
            new AlertDialog.Builder(activity)
                    .setTitle(com.pushizo.sdk.R.string.hs_location_rationale_title)
                    .setMessage(rationale != null ? rationale : activity.getString(com.pushizo.sdk.R.string.hs_location_rationale_message))
                    .setPositiveButton(com.pushizo.sdk.R.string.hs_location_rationale_continue, (dialog, which) -> askLocationSystem(activity, permissions))
                    .setNegativeButton(com.pushizo.sdk.R.string.hs_location_rationale_not_now, (dialog, which) -> resolveLocationRequest())
                    .setOnCancelListener(dialog -> resolveLocationRequest())
                    .show();
        } catch (RuntimeException e) {
            HsLog.INSTANCE.w("konum izni açıklama diyaloğu gösterilemedi", e);
            resolveLocationRequest();
        }
    }

    private static void askLocationSystem(Activity activity, String[] permissions) {
        synchronized (LOCATION_LOCK) {
            if (locationPhase != LOCATION_RATIONALE) return;
            locationPhase = LOCATION_SYSTEM;
            locationPausedSinceRequest = false;
        }
        try {
            ActivityCompat.requestPermissions(activity, permissions, Pushizo.Location.PERMISSION_REQUEST_CODE);
        } catch (RuntimeException e) {
            HsLog.INSTANCE.w("konum izni istenemedi", e);
            resolveLocationRequest();
            return;
        }
        MAIN.postDelayed(() -> {
            boolean noDialog;
            synchronized (LOCATION_LOCK) {
                noDialog = locationPhase == LOCATION_SYSTEM && !locationPausedSinceRequest;
            }
            if (noDialog) resolveLocationRequest();
        }, PERMISSION_NO_DIALOG_TIMEOUT_MS);
    }

    private static void resolveLocationRequest() {
        List<PushizoUnityPermissionCallback> callbacks;
        synchronized (LOCATION_LOCK) {
            if (locationPhase == LOCATION_IDLE) return;
            locationPhase = LOCATION_IDLE;
            locationPausedSinceRequest = false;
            callbacks = new ArrayList<>(LOCATION_CALLBACKS);
            LOCATION_CALLBACKS.clear();
        }
        // İzin verildiyse ve paylaşım açıksa yerel SDK konumu hemen gönderir.
        Pushizo.Location.onPermissionResult();
        boolean granted = Pushizo.Location.getPermissionGranted();
        for (PushizoUnityPermissionCallback callback : callbacks) {
            try {
                callback.onResult(granted);
            } catch (RuntimeException e) {
                HsLog.INSTANCE.e("konum izni geri çağrısı hata verdi", e);
            }
        }
    }

    /** Uygulama manifest'inde tanımlı ön plan konum izinleri (arka plan konumu istenmez). */
    private static String[] declaredLocationPermissions(Context context) {
        List<String> result = new ArrayList<>(2);
        try {
            PackageInfo info = context.getPackageManager().getPackageInfo(context.getPackageName(), PackageManager.GET_PERMISSIONS);
            if (info.requestedPermissions != null) {
                for (String permission : info.requestedPermissions) {
                    if ((Manifest.permission.ACCESS_COARSE_LOCATION.equals(permission) || Manifest.permission.ACCESS_FINE_LOCATION.equals(permission))
                            && !result.contains(permission)) {
                        result.add(permission);
                    }
                }
            }
        } catch (PackageManager.NameNotFoundException e) {
            HsLog.INSTANCE.w("uygulama izinleri okunamadı", e);
        }
        return result.toArray(new String[0]);
    }

    // --- Ön plan ---

    /** true: ön planda gelen bildirim ertelenir ve C#'a sorulur (karar foregroundDisplay ile). */
    public static void setForegroundHandlerEnabled(boolean enabled) { foregroundHandlerEnabled = enabled; }

    private static void onForeground(HsForegroundEvent event) {
        if (!foregroundHandlerEnabled) return;
        // C# olayları ana iş parçacığında işlenir; gösterim önce ertelenir, karar foregroundDisplay ile gelir.
        event.preventDefault();
        String eventId = UUID.randomUUID().toString();
        synchronized (FOREGROUND_EVENTS) {
            FOREGROUND_EVENTS.put(eventId, event);
        }
        JSONObject payload = object("eventId", eventId);
        put(payload, "notification", notificationJson(event.getNotification()));
        emit("foreground", payload);
    }

    public static void foregroundDisplay(String eventId) {
        HsForegroundEvent event;
        synchronized (FOREGROUND_EVENTS) {
            event = FOREGROUND_EVENTS.remove(eventId);
        }
        if (event != null) event.display();
    }

    // --- In-app ---

    public static void addTriggers(String triggersJson) { Pushizo.InAppMessages.addTriggers(stringMap(triggersJson)); }

    public static void removeTriggers(String keysJson) { Pushizo.InAppMessages.removeTriggers(stringList(keysJson)); }

    public static void clearTriggers() { Pushizo.InAppMessages.clearTriggers(); }

    public static String triggers() { return new JSONObject(Pushizo.InAppMessages.getTriggers()).toString(); }

    /** Boş ad ekrandan çıkış demektir. */
    public static void setScreen(String name) { Pushizo.InAppMessages.setScreen(emptyToNull(name)); }

    public static boolean inAppPaused() { return Pushizo.InAppMessages.getPaused(); }

    public static void updateInAppPaused(boolean paused) { Pushizo.InAppMessages.setPaused(paused); }

    // --- Bildirim kutusu (Kotlin suspend fonksiyonları Continuation ile) ---

    public static void inboxList(int limit, String cursor, PushizoUnityCallback callback) {
        InboxContinuation c = new InboxContinuation(callback, value -> pageJson((HsInboxPage) value).toString());
        c.run(() -> Pushizo.Inbox.INSTANCE.list(limit, emptyToNull(cursor), c));
    }

    public static void inboxMarkRead(String id, PushizoUnityCallback callback) {
        InboxContinuation c = new InboxContinuation(callback, value -> itemJson((HsInboxItem) value).toString());
        c.run(() -> Pushizo.Inbox.INSTANCE.markRead(id, c));
    }

    public static void inboxMarkAllRead(PushizoUnityCallback callback) {
        InboxContinuation c = new InboxContinuation(callback, value -> object("unreadCount", value).toString());
        c.run(() -> Pushizo.Inbox.INSTANCE.markAllRead(c));
    }

    public static void inboxArchive(String id, PushizoUnityCallback callback) {
        InboxContinuation c = new InboxContinuation(callback, value -> null);
        c.run(() -> Pushizo.Inbox.INSTANCE.archive(id, c));
    }

    public static void inboxUnreadCount(PushizoUnityCallback callback) {
        InboxContinuation c = new InboxContinuation(callback, value -> object("unreadCount", value).toString());
        c.run(() -> Pushizo.Inbox.INSTANCE.unreadCount(c));
    }

    // --- Tercihler (ADR-0019 §4; Kotlin suspend fonksiyonları Continuation ile) ---

    /** Başarı JSON'u: {"preferences":[…],"categories":[…]} (bkz. preferencesResultJson). */
    public static void getPreferences(PushizoUnityCallback callback) {
        InboxContinuation c = new InboxContinuation(callback, value -> preferencesResultJson((HsPreferencesResult) value).toString());
        c.run(() -> Pushizo.User.INSTANCE.getPreferences(c));
    }

    /**
     * preferencesJson: [{"category","channel":null|"…","optedIn"}]; doğrulama yerel SDK'dadır (geçersizse
     * INVALID_ARGUMENT). Başarı JSON'u getPreferences ile aynıdır (güncel liste).
     */
    public static void setPreferences(String preferencesJson, PushizoUnityCallback callback) {
        InboxContinuation c = new InboxContinuation(callback, value -> preferencesResultJson((HsPreferencesResult) value).toString());
        c.run(() -> Pushizo.User.INSTANCE.setPreferences(preferenceInputs(preferencesJson), c));
    }

    // --- Oturum ---

    /** propertiesJson JSON nesnesi (iç içe nesne/dizi desteklenir); hasValue false ise değer gönderilmez. */
    public static void sendEvent(String name, String propertiesJson, double value, boolean hasValue) {
        Pushizo.Session.sendEvent(name, toMap(parseObject(propertiesJson)), hasValue ? Double.valueOf(value) : null);
    }

    // --- Yardımcılar ---

    private interface SuspendCall {
        Object invoke();
    }

    private interface ValueMapper {
        String map(Object value) throws JSONException;
    }

    /** Kotlin suspend çağrısının sonucunu PushizoUnityCallback'e çevirir (yalnızca bir kez). */
    private static final class InboxContinuation implements Continuation<Object> {
        private final PushizoUnityCallback callback;
        private final ValueMapper mapper;
        private boolean completed;

        InboxContinuation(PushizoUnityCallback callback, ValueMapper mapper) {
            this.callback = callback;
            this.mapper = mapper;
        }

        void run(SuspendCall call) {
            try {
                Object result = call.invoke();
                if (result != IntrinsicsKt.getCOROUTINE_SUSPENDED()) resumeWith(result);
            } catch (Throwable e) {
                fail(e);
            }
        }

        @Override
        public CoroutineContext getContext() {
            return EmptyCoroutineContext.INSTANCE;
        }

        @Override
        public void resumeWith(Object result) {
            if (result instanceof Result.Failure) {
                fail(((Result.Failure) result).exception);
                return;
            }
            String json;
            try {
                json = mapper.map(result);
            } catch (JSONException | RuntimeException e) {
                deliver(null, "INVALID_RESPONSE", e.getMessage());
                return;
            }
            deliver(json, null, null);
        }

        private void fail(Throwable e) {
            String code;
            if (e instanceof HsApiException) code = ((HsApiException) e).getCode();
            else if (e instanceof IllegalStateException) code = "NOT_READY";
            else if (e instanceof IllegalArgumentException) code = "INVALID_ARGUMENT";
            else code = "HS_ERROR";
            deliver(null, code, e.getMessage() != null ? e.getMessage() : code);
        }

        private void deliver(String json, String code, String message) {
            synchronized (this) {
                if (completed) return;
                completed = true;
            }
            callback.onResult(json, code, message);
        }
    }

    private static void sendLifecycle(String event, HsInAppLifecycleEvent e) {
        emit("inAppLifecycle", object("event", event, "messageId", e.getMessage().getId(), "json", e.getMessage().toJson()));
    }

    private static void emit(String type, JSONObject payload) {
        PushizoUnityListener l = listener;
        if (l == null) return;
        try {
            l.onEvent(type, payload.toString());
        } catch (RuntimeException e) {
            HsLog.INSTANCE.e("Unity olay dinleyicisi hata verdi (" + type + ")", e);
        }
    }

    private static HsLog.Level logLevel(int value) {
        for (HsLog.Level level : HsLog.Level.values()) {
            if (level.getPriority() == value) return level;
        }
        return HsLog.Level.WARN;
    }

    private static String emptyToNull(String value) {
        return value == null || value.isEmpty() ? null : value;
    }

    private static String nonBlank(String value, String fallback) {
        return value == null || value.trim().isEmpty() ? fallback : value;
    }

    private static JSONObject parseObject(String json) {
        try {
            return new JSONObject(json);
        } catch (JSONException e) {
            throw new IllegalArgumentException("Geçersiz JSON nesnesi", e);
        }
    }

    private static JSONArray parseArray(String json) {
        try {
            return new JSONArray(json);
        } catch (JSONException e) {
            throw new IllegalArgumentException("Geçersiz JSON dizisi", e);
        }
    }

    private static Map<String, String> stringMap(String json) {
        JSONObject o = parseObject(json);
        Map<String, String> map = new HashMap<>();
        Iterator<String> keys = o.keys();
        while (keys.hasNext()) {
            String key = keys.next();
            map.put(key, o.optString(key));
        }
        return map;
    }

    private static List<String> stringList(String json) {
        JSONArray a = parseArray(json);
        List<String> list = new ArrayList<>(a.length());
        for (int i = 0; i < a.length(); i++) list.add(a.optString(i));
        return list;
    }

    private static Map<String, Object> toMap(JSONObject o) {
        Map<String, Object> map = new HashMap<>();
        Iterator<String> keys = o.keys();
        while (keys.hasNext()) {
            String key = keys.next();
            map.put(key, toValue(o.opt(key)));
        }
        return map;
    }

    private static Object toValue(Object v) {
        if (v == null || v == JSONObject.NULL) return null;
        if (v instanceof JSONObject) return toMap((JSONObject) v);
        if (v instanceof JSONArray) {
            JSONArray a = (JSONArray) v;
            List<Object> list = new ArrayList<>(a.length());
            for (int i = 0; i < a.length(); i++) list.add(toValue(a.opt(i)));
            return list;
        }
        return v;
    }

    /** Anahtar/değer çiftlerinden JSON nesnesi; null değerler JSON null olur. */
    private static JSONObject object(Object... pairs) {
        JSONObject o = new JSONObject();
        for (int i = 0; i + 1 < pairs.length; i += 2) put(o, (String) pairs[i], pairs[i + 1]);
        return o;
    }

    private static void put(JSONObject o, String key, Object value) {
        try {
            o.put(key, value == null ? JSONObject.NULL : value);
        } catch (JSONException e) {
            throw new IllegalArgumentException("JSON değeri yazılamadı: " + key, e);
        }
    }

    private static JSONObject notificationJson(HsNotification n) {
        JSONArray actions = new JSONArray();
        for (HsAction a : n.getActions()) {
            actions.put(object("id", a.getId(), "title", a.getTitle(), "url", a.getUrl(), "icon", a.getIcon()));
        }
        JSONObject o = object(
                "title", n.getTitle(),
                "body", n.getBody(),
                "imageUrl", n.getImageUrl(),
                "url", n.getUrl(),
                "deliveryId", n.getDeliveryId(),
                "messageId", n.getMessageId());
        put(o, "actions", actions);
        put(o, "data", new JSONObject(n.getData()));
        put(o, "androidNotificationId", n.getAndroidNotificationId());
        return o;
    }

    private static JSONObject subscriptionJson(HsPushSubscriptionState s) {
        return object("id", s.getId(), "token", s.getToken(), "optedIn", s.getOptedIn(), "permission", s.getPermission());
    }

    private static JSONObject itemJson(HsInboxItem i) {
        JSONObject o = object(
                "id", i.getId(),
                "messageId", i.getMessageId(),
                "title", i.getTitle(),
                "body", i.getBody(),
                "url", i.getUrl(),
                "imageUrl", i.getImageUrl(),
                "read", i.getRead(),
                "readAt", i.getReadAt(),
                "archivedAt", i.getArchivedAt(),
                "expiresAt", i.getExpiresAt(),
                "createdAt", i.getCreatedAt());
        put(o, "data", new JSONObject(i.getData()));
        return o;
    }

    /** Geçersiz JSON IllegalArgumentException (INVALID_ARGUMENT) olarak geri çağrıya iletilir. */
    private static List<HsPreferenceInput> preferenceInputs(String json) {
        JSONArray a = parseArray(json);
        List<HsPreferenceInput> list = new ArrayList<>(a.length());
        for (int i = 0; i < a.length(); i++) {
            JSONObject o = a.optJSONObject(i);
            if (o == null) throw new IllegalArgumentException("Geçersiz tercih öğesi: " + i);
            String channel = o.isNull("channel") ? null : o.optString("channel");
            list.add(new HsPreferenceInput(o.optString("category"), channel, o.optBoolean("optedIn", false)));
        }
        return list;
    }

    private static JSONObject preferenceJson(HsPreference p) {
        return object(
                "category", p.getCategory(),
                "channel", p.getChannel(),
                "optedIn", p.getOptedIn(),
                "explicit", p.getExplicit(),
                "source", p.getSource(),
                "updatedAt", p.getUpdatedAt());
    }

    private static JSONObject categoryJson(HsMessageCategory c) {
        JSONObject o = object("key", c.getKey(), "defaultOptedIn", c.getDefaultOptedIn());
        put(o, "name", new JSONObject(c.getName()));
        put(o, "description", new JSONObject(c.getDescription()));
        put(o, "channels", c.getChannels() == null ? null : new JSONArray(c.getChannels()));
        return o;
    }

    private static JSONObject preferencesResultJson(HsPreferencesResult r) {
        JSONArray preferences = new JSONArray();
        for (HsPreference p : r.getPreferences()) preferences.put(preferenceJson(p));
        JSONArray categories = new JSONArray();
        for (HsMessageCategory c : r.getCategories()) categories.put(categoryJson(c));
        JSONObject o = new JSONObject();
        put(o, "preferences", preferences);
        put(o, "categories", categories);
        return o;
    }

    private static JSONObject pageJson(HsInboxPage p) {
        JSONArray items = new JSONArray();
        for (HsInboxItem item : p.getItems()) items.put(itemJson(item));
        JSONObject o = object("unreadCount", p.getUnreadCount(), "nextCursor", p.getNextCursor(), "hasMore", p.getHasMore());
        put(o, "items", items);
        return o;
    }
}
