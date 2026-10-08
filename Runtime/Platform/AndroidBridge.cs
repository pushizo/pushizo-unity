#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

namespace HeroSignalSdk.Unity.Internal
{
    /// <summary>
    /// Android köprüsü: <c>AndroidJavaClass</c> ile <c>com.herosignal.sdk.HeroSignal</c> (Kotlin <c>object</c>; iç içe
    /// <c>User</c>, <c>Notifications</c>, <c>InAppMessages</c>, <c>Inbox</c>, <c>Session</c>, <c>Debug</c>) çağrılır.
    /// Kotlin dinleyicileri (<c>(T) -&gt; Unit</c>, <c>HsCallback</c>, <c>HsInAppLifecycleListener</c>) <c>AndroidJavaProxy</c>
    /// ile uygulanır; olaylar Java iş parçacıklarından gelir ve çekirdek tarafından ana iş parçacığına taşınır.
    /// Yerel SDK EDM4U ile eklenir (Editor/HeroSignalDependencies.xml).
    /// </summary>
    internal sealed class AndroidBridge : IHeroSignalBridge
    {
        private const string Package = "com.herosignal.sdk.";
        private const string PostNotifications = "android.permission.POST_NOTIFICATIONS";

        private readonly AndroidJavaClass _hs = new AndroidJavaClass(Package + "HeroSignal");
        private readonly AndroidJavaClass _user = new AndroidJavaClass(Package + "HeroSignal$User");
        private readonly AndroidJavaClass _push = new AndroidJavaClass(Package + "HeroSignal$User$pushSubscription");
        private readonly AndroidJavaClass _notifications = new AndroidJavaClass(Package + "HeroSignal$Notifications");
        private readonly AndroidJavaClass _inApp = new AndroidJavaClass(Package + "HeroSignal$InAppMessages");
        private readonly AndroidJavaClass _inbox = new AndroidJavaClass(Package + "HeroSignal$Inbox");
        private readonly AndroidJavaClass _session = new AndroidJavaClass(Package + "HeroSignal$Session");
        private readonly AndroidJavaClass _debug = new AndroidJavaClass(Package + "HeroSignal$Debug");
        private bool _listenersRegistered;
        private IBridgeEventSink _sink;

        public bool IsSupported => true;

        public void Initialize(string appId, HeroSignalOptions options, IBridgeEventSink sink)
        {
            _sink = sink;
            using (var context = AndroidInterop.ApplicationContext())
            using (var level = AndroidInterop.NativeLogLevel(options.LogLevel))
            using (var nativeOptions = new AndroidJavaObject(Package + "HeroSignal$Options",
                       options.ApiUrl, options.RequiresPrivacyConsent, level, options.AndroidDefaultChannelName, options.AndroidDefaultChannelId))
            {
                _hs.CallStatic("initialize", context, appId, nativeOptions);
            }
            RegisterListeners(sink);
        }

        /// <summary>
        /// Dinleyiciler bir kez eklenir. In-app dinleyicileri yerel in-app denetleyicisine bağlıdır (initialize sonrası
        /// oluşur); tıklama dinleyicisi eklenince yerel SDK bekleyen (uygulamayı açan) tıklamayı hemen iletir.
        /// </summary>
        private void RegisterListeners(IBridgeEventSink sink)
        {
            if (_listenersRegistered) return;
            _listenersRegistered = true;
            _notifications.CallStatic("addForegroundLifecycleListener", new KotlinFunction1(e =>
            {
                using (var notification = e.Call<AndroidJavaObject>("getNotification"))
                {
                    var model = AndroidInterop.Notification(notification);
                    sink.OnNotificationWillDisplay(model, () => e.Call("preventDefault"), () => e.Call("display"));
                }
            }));
            _push.CallStatic("addObserver", new KotlinFunction1(state => sink.OnPushSubscriptionChanged(AndroidInterop.SubscriptionState(state))));
            _inApp.CallStatic("addClickListener", new KotlinFunction1(e =>
                sink.OnInAppClicked(new InAppMessageClickedEventArgs(e.Call<string>("getMessageId"), e.Call<string>("getActionId"), e.Call<string>("getUrl")))));
            _inApp.CallStatic("addLifecycleListener", new InAppLifecycleProxy(sink));
            _notifications.CallStatic("addClickListener", new KotlinFunction1(e =>
            {
                using (var notification = e.Call<AndroidJavaObject>("getNotification"))
                {
                    sink.OnNotificationClicked(new NotificationClickedEventArgs(
                        AndroidInterop.Notification(notification), e.Call<string>("getActionId"), e.Call<string>("getUrl")));
                }
            }));
            // Başlangıç durumu: sonraki değişiklikler gözlemciden gelir.
            sink.OnPushSubscriptionChanged(PushSubscription);
        }

        public void SetConsentGiven(bool given) => _hs.CallStatic("setConsentGiven", given);

        public void Login(string externalId, string identityHash)
        {
            if (identityHash == null) _hs.CallStatic("login", externalId);
            else _hs.CallStatic("login", externalId, identityHash);
        }

        public void Logout() => _hs.CallStatic("logout");

        public void SetLogLevel(LogLevel level)
        {
            using (var native = AndroidInterop.NativeLogLevel(level)) _debug.CallStatic("setLogLevel", native);
        }

        // Kullanıcı
        public string ExternalId => Instance(_user, i => i.Call<string>("getExternalId"));
        public string UserId => Instance(_user, i => i.Call<string>("getId"));

        public void AddTags(IReadOnlyDictionary<string, string> tags)
        {
            using (var map = AndroidInterop.StringMap(tags)) _user.CallStatic("addTags", map);
        }

        public void RemoveTags(IReadOnlyList<string> keys)
        {
            using (var list = AndroidInterop.StringList(keys)) _user.CallStatic("removeTags", list);
        }

        public IReadOnlyDictionary<string, string> GetTags()
        {
            using (var map = _user.CallStatic<AndroidJavaObject>("getTags")) return AndroidInterop.ReadStringMap(map);
        }

        public void AddAliases(IReadOnlyDictionary<string, string> aliases)
        {
            using (var map = AndroidInterop.StringMap(aliases)) _user.CallStatic("addAliases", map);
        }

        public void RemoveAlias(string label) => _user.CallStatic("removeAlias", label);
        public void SetLanguage(string languageCode) => _user.CallStatic("setLanguage", languageCode);
        public void AddEmail(string email) => _user.CallStatic("addEmail", email);
        public void RemoveEmail(string email) => _user.CallStatic("removeEmail", email);
        public void AddSms(string phoneNumber) => _user.CallStatic("addSms", phoneNumber);
        public void RemoveSms(string phoneNumber) => _user.CallStatic("removeSms", phoneNumber);

        public IReadOnlyList<string> Emails => Instance(_user, i =>
        {
            using (var list = i.Call<AndroidJavaObject>("getEmails")) return AndroidInterop.ReadStringList(list);
        });

        public IReadOnlyList<string> SmsNumbers => Instance(_user, i =>
        {
            using (var list = i.Call<AndroidJavaObject>("getSmsNumbers")) return AndroidInterop.ReadStringList(list);
        });

        // Push aboneliği
        public PushSubscriptionState PushSubscription => Instance(_push, i =>
        {
            using (var state = i.Call<AndroidJavaObject>("getState")) return AndroidInterop.SubscriptionState(state);
        });

        public void OptIn() => _push.CallStatic("optIn");
        public void OptOut() => _push.CallStatic("optOut");

        // Bildirimler
        public bool NotificationPermission => Instance(_notifications, i => i.Call<bool>("getPermission"));

        /// <summary>
        /// Android 13+ POST_NOTIFICATIONS iznini Unity <see cref="Permission"/> API'siyle ister (sonuç Unity aktivitesine
        /// döner), ardından yerel SDK'ya bildirir (abonelik push_capable olarak güncellenir).
        /// </summary>
        public async Task<bool> RequestPermissionAsync(bool fallbackToSettings)
        {
            if (!NotificationPermission && AndroidInterop.SdkInt >= 33 && !Permission.HasUserAuthorizedPermission(PostNotifications))
            {
                var answered = new TaskCompletionSource<bool>();
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => answered.TrySetResult(true);
                callbacks.PermissionDenied += _ => answered.TrySetResult(false);
#pragma warning disable CS0612, CS0618 // Unity 2021.3 uyumluluğu: yeni sürümlerde eskimiş olarak işaretli ama çalışır.
                callbacks.PermissionDeniedAndDontAskAgain += _ => answered.TrySetResult(false);
#pragma warning restore CS0612, CS0618
                Permission.RequestUserPermission(PostNotifications, callbacks);
                await answered.Task;
            }
            _notifications.CallStatic("onPermissionResult");
            var granted = NotificationPermission;
            _sink?.OnPermissionChanged(granted);
            if (!granted && fallbackToSettings) AndroidInterop.OpenNotificationSettings();
            return granted;
        }

        public void ClearAllNotifications() => _notifications.CallStatic("clearAll");

        /// <summary>Android'de kimlik, sistem bildirim kimliğidir (<see cref="HeroSignalNotification.NotificationId"/>).</summary>
        public void RemoveNotification(string id)
        {
            if (!int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                throw new ArgumentException("Android'de bildirim kimliği sayı olmalı (HeroSignalNotification.NotificationId).", nameof(id));
            _notifications.CallStatic("removeNotification", numeric);
        }

        public void RemoveGroupedNotifications(string group) => _notifications.CallStatic("removeGroupedNotifications", group);

        // In-app
        public void AddTriggers(IReadOnlyDictionary<string, string> triggers)
        {
            using (var map = AndroidInterop.StringMap(triggers)) _inApp.CallStatic("addTriggers", map);
        }

        public void RemoveTriggers(IReadOnlyList<string> keys)
        {
            using (var list = AndroidInterop.StringList(keys)) _inApp.CallStatic("removeTriggers", list);
        }

        public void ClearTriggers() => _inApp.CallStatic("clearTriggers");

        public IReadOnlyDictionary<string, string> GetTriggers()
        {
            using (var map = _inApp.CallStatic<AndroidJavaObject>("getTriggers")) return AndroidInterop.ReadStringMap(map);
        }

        /// <summary>null ekran adı JNI ile açık imzayla geçirilir (Unity yansıma eşlemesi null'da tür çıkaramaz).</summary>
        public void SetScreen(string name) => AndroidInterop.CallStaticNullableString(_inApp, "setScreen", name);

        public bool InAppPaused
        {
            get => _inApp.CallStatic<bool>("getPaused");
            set => _inApp.CallStatic("setPaused", value);
        }

        // Bildirim kutusu
        public Task<InboxPage> InboxListAsync(int limit, string cursor)
        {
            var callback = new InboxCallback<InboxPage>(AndroidInterop.InboxPage);
            if (cursor == null) _inbox.CallStatic("list", limit, callback);
            else _inbox.CallStatic("list", limit, cursor, callback);
            return callback.Task;
        }

        public Task<InboxItem> InboxMarkReadAsync(string id)
        {
            var callback = new InboxCallback<InboxItem>(AndroidInterop.InboxItem);
            _inbox.CallStatic("markRead", id, callback);
            return callback.Task;
        }

        public Task<int> InboxMarkAllReadAsync()
        {
            var callback = new InboxCallback<int>(v => v.Call<int>("intValue"));
            _inbox.CallStatic("markAllRead", callback);
            return callback.Task;
        }

        public Task InboxArchiveAsync(string id)
        {
            var callback = new InboxCallback<bool>(_ => true);
            _inbox.CallStatic("archive", id, callback);
            return callback.Task;
        }

        public Task<int> InboxUnreadCountAsync()
        {
            var callback = new InboxCallback<int>(v => v.Call<int>("intValue"));
            _inbox.CallStatic("unreadCount", callback);
            return callback.Task;
        }

        // Oturum
        public void SendEvent(string name, Dictionary<string, object> properties, double? value)
        {
            using (var map = AndroidInterop.ObjectMap(properties))
            {
                if (value.HasValue)
                {
                    using (var boxed = new AndroidJavaObject("java.lang.Double", value.Value)) _session.CallStatic("sendEvent", name, map, boxed);
                }
                else
                {
                    _session.CallStatic("sendEvent", name, map);
                }
            }
        }

        /// <summary>Kotlin <c>object</c> örneği (<c>INSTANCE</c>) üzerinden @JvmStatic olmayan özellik okuması.</summary>
        private static T Instance<T>(AndroidJavaClass cls, Func<AndroidJavaObject, T> read)
        {
            using (var instance = cls.GetStatic<AndroidJavaObject>("INSTANCE")) return read(instance);
        }
    }

    /// <summary>Kotlin <c>(T) -&gt; Unit</c> dinleyicisi; işleyici hatası yerel SDK'ya sızmaz.</summary>
    internal sealed class KotlinFunction1 : AndroidJavaProxy
    {
        private static AndroidJavaObject s_unit;
        private readonly Action<AndroidJavaObject> _action;

        public KotlinFunction1(Action<AndroidJavaObject> action) : base("kotlin.jvm.functions.Function1") => _action = action;

        public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] javaArgs)
        {
            if (methodName != "invoke") return base.Invoke(methodName, javaArgs);
            try
            {
                if (javaArgs != null && javaArgs.Length > 0 && javaArgs[0] != null) _action(javaArgs[0]);
            }
            catch (Exception e)
            {
                SdkLog.Write(LogLevel.Error, "yerel dinleyici hata verdi", e);
            }
            if (s_unit == null)
            {
                using (var unit = new AndroidJavaClass("kotlin.Unit")) s_unit = unit.GetStatic<AndroidJavaObject>("INSTANCE");
            }
            return s_unit;
        }
    }

    /// <summary><c>com.herosignal.sdk.HsInAppLifecycleListener</c> (yerel SDK ana iş parçacığında çağırır).</summary>
    internal sealed class InAppLifecycleProxy : AndroidJavaProxy
    {
        private readonly IBridgeEventSink _sink;

        public InAppLifecycleProxy(IBridgeEventSink sink) : base("com.herosignal.sdk.HsInAppLifecycleListener") => _sink = sink;

        public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] javaArgs)
        {
            InAppLifecyclePhase phase;
            switch (methodName)
            {
                case "onWillDisplay": phase = InAppLifecyclePhase.WillDisplay; break;
                case "onDidDisplay": phase = InAppLifecyclePhase.DidDisplay; break;
                case "onWillDismiss": phase = InAppLifecyclePhase.WillDismiss; break;
                case "onDidDismiss": phase = InAppLifecyclePhase.DidDismiss; break;
                default: return base.Invoke(methodName, javaArgs);
            }
            try
            {
                using (var message = javaArgs[0].Call<AndroidJavaObject>("getMessage"))
                {
                    _sink.OnInAppLifecycle(phase, new InAppMessage(message.Call<string>("getId"), message.Call<string>("toJson")));
                }
            }
            catch (Exception e)
            {
                SdkLog.Write(LogLevel.Error, "in-app yaşam döngüsü olayı iletilemedi", e);
            }
            return null;
        }
    }

    /// <summary>
    /// <c>HsCallback&lt;T&gt;</c> → <see cref="Task{TResult}"/>. Kotlin <c>Result</c> JVM'de kutusuz taşınır: başarıda değerin
    /// kendisi, hatada <c>kotlin.Result$Failure</c> (alan: <c>exception</c>). Sonuç ana iş parçacığında tamamlanır
    /// (devam kodu JNI'ye bağlı olmayan iş parçacığında Java çağırmasın diye).
    /// </summary>
    internal sealed class InboxCallback<T> : AndroidJavaProxy
    {
        private readonly Func<AndroidJavaObject, T> _map;
        private readonly TaskCompletionSource<T> _tcs = new TaskCompletionSource<T>();

        public InboxCallback(Func<AndroidJavaObject, T> map) : base("com.herosignal.sdk.HsCallback") => _map = map;

        public Task<T> Task => _tcs.Task;

        public override AndroidJavaObject Invoke(string methodName, AndroidJavaObject[] javaArgs)
        {
            if (methodName != "onResult") return base.Invoke(methodName, javaArgs);
            T value = default;
            Exception error = null;
            try
            {
                var result = javaArgs != null && javaArgs.Length > 0 ? javaArgs[0] : null;
                using (var failure = AndroidInterop.ResultFailure(result))
                {
                    if (failure != null) error = AndroidInterop.ToException(failure);
                    else value = _map(result);
                }
            }
            catch (Exception e)
            {
                error = new HeroSignalException(HeroSignalException.InvalidResponse, "Yerel SDK yanıtı çözümlenemedi.", e);
            }
            HeroSignal.Post(() =>
            {
                if (error != null) _tcs.TrySetException(error);
                else _tcs.TrySetResult(value);
            });
            return null;
        }
    }

    /// <summary>Java nesneleri ↔ ortak modeller.</summary>
    internal static class AndroidInterop
    {
        private static int s_sdkInt = -1;

        public static int SdkInt
        {
            get
            {
                if (s_sdkInt < 0)
                {
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION")) s_sdkInt = version.GetStatic<int>("SDK_INT");
                }
                return s_sdkInt;
            }
        }

        public static AndroidJavaObject CurrentActivity()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")) return player.GetStatic<AndroidJavaObject>("currentActivity");
        }

        public static AndroidJavaObject ApplicationContext()
        {
            using (var activity = CurrentActivity()) return activity.Call<AndroidJavaObject>("getApplicationContext");
        }

        public static AndroidJavaObject NativeLogLevel(LogLevel level)
        {
            string name;
            switch (level)
            {
                case LogLevel.None: name = "NONE"; break;
                case LogLevel.Error: name = "ERROR"; break;
                case LogLevel.Info: name = "INFO"; break;
                case LogLevel.Debug: name = "DEBUG"; break;
                default: name = "WARN"; break;
            }
            using (var cls = new AndroidJavaClass("com.herosignal.sdk.HsLog$Level")) return cls.GetStatic<AndroidJavaObject>(name);
        }

        /// <summary>Bildirim ayarları (Android 8+ uygulama bildirim ayarları, öncesinde uygulama ayrıntıları).</summary>
        public static void OpenNotificationSettings()
        {
            using (var activity = CurrentActivity())
            {
                var packageName = activity.Call<string>("getPackageName");
                AndroidJavaObject intent;
                if (SdkInt >= 26)
                {
                    intent = new AndroidJavaObject("android.content.Intent", "android.settings.APP_NOTIFICATION_SETTINGS");
                    intent.Call<AndroidJavaObject>("putExtra", "android.provider.extra.APP_PACKAGE", packageName).Dispose();
                }
                else
                {
                    using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                    using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "package:" + packageName))
                    {
                        intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS", uri);
                    }
                }
                using (intent)
                {
                    intent.Call<AndroidJavaObject>("addFlags", 0x10000000).Dispose(); // FLAG_ACTIVITY_NEW_TASK
                    activity.Call("startActivity", intent);
                }
            }
        }

        public static AndroidJavaObject StringMap(IReadOnlyDictionary<string, string> map)
        {
            var result = new AndroidJavaObject("java.util.HashMap");
            foreach (var pair in map) result.Call<AndroidJavaObject>("put", pair.Key, pair.Value)?.Dispose();
            return result;
        }

        public static AndroidJavaObject StringList(IReadOnlyList<string> items)
        {
            var result = new AndroidJavaObject("java.util.ArrayList");
            foreach (var item in items) result.Call<bool>("add", item);
            return result;
        }

        /// <summary>
        /// Kanonik olay özelliği ağacı → Java nesneleri. Yerel SDK <c>JSONObject(map)</c> kullanır: iç içe Map → JSONObject,
        /// Collection → JSONArray; null değer kaybolmasın diye <c>JSONObject.NULL</c> kullanılır.
        /// </summary>
        public static AndroidJavaObject ObjectMap(Dictionary<string, object> properties)
        {
            var result = new AndroidJavaObject("java.util.HashMap");
            foreach (var pair in properties)
            {
                using (var value = ToJava(pair.Value)) result.Call<AndroidJavaObject>("put", pair.Key, value)?.Dispose();
            }
            return result;
        }

        private static AndroidJavaObject ToJava(object value)
        {
            switch (value)
            {
                case null:
                    using (var json = new AndroidJavaClass("org.json.JSONObject")) return json.GetStatic<AndroidJavaObject>("NULL");
                case string s:
                    return new AndroidJavaObject("java.lang.String", s);
                case bool b:
                    return new AndroidJavaObject("java.lang.Boolean", b);
                case long l:
                    return new AndroidJavaObject("java.lang.Long", l);
                case double d:
                    return new AndroidJavaObject("java.lang.Double", d);
                case Dictionary<string, object> nested:
                    return ObjectMap(nested);
                case List<object> items:
                {
                    var list = new AndroidJavaObject("java.util.ArrayList");
                    foreach (var item in items)
                    {
                        using (var element = ToJava(item)) list.Call<bool>("add", element);
                    }
                    return list;
                }
                default:
                    throw new InvalidOperationException("Kanonik olmayan olay özelliği değeri.");
            }
        }

        /// <summary>Map&lt;String, String&gt; → sözlük (org.json üzerinden).</summary>
        public static IReadOnlyDictionary<string, string> ReadStringMap(AndroidJavaObject map)
        {
            if (map == null) return new Dictionary<string, string>();
            using (var json = new AndroidJavaObject("org.json.JSONObject", map)) return BridgeJson.ParseStringMap(json.Call<string>("toString"));
        }

        public static IReadOnlyList<string> ReadStringList(AndroidJavaObject collection)
        {
            if (collection == null) return Array.Empty<string>();
            using (var json = new AndroidJavaObject("org.json.JSONArray", collection)) return BridgeJson.ParseStringList(json.Call<string>("toString"));
        }

        public static PushSubscriptionState SubscriptionState(AndroidJavaObject state) =>
            state == null
                ? PushSubscriptionState.Empty
                : new PushSubscriptionState(state.Call<string>("getId"), state.Call<string>("getToken"), state.Call<bool>("getOptedIn"), state.Call<bool>("getPermission"));

        public static HeroSignalNotification Notification(AndroidJavaObject n)
        {
            var actions = new List<NotificationAction>();
            using (var list = n.Call<AndroidJavaObject>("getActions"))
            {
                var size = list?.Call<int>("size") ?? 0;
                for (var i = 0; i < size; i++)
                {
                    using (var a = list.Call<AndroidJavaObject>("get", i))
                    {
                        actions.Add(new NotificationAction(a.Call<string>("getId"), a.Call<string>("getTitle"), a.Call<string>("getUrl"), a.Call<string>("getIcon")));
                    }
                }
            }
            string notificationId;
            using (var id = n.Call<AndroidJavaObject>("getAndroidNotificationId"))
            {
                notificationId = id?.Call<int>("intValue").ToString(CultureInfo.InvariantCulture);
            }
            using (var data = n.Call<AndroidJavaObject>("getData"))
            {
                return new HeroSignalNotification
                {
                    Title = n.Call<string>("getTitle"),
                    Body = n.Call<string>("getBody"),
                    ImageUrl = n.Call<string>("getImageUrl"),
                    Url = n.Call<string>("getUrl"),
                    DeliveryId = n.Call<string>("getDeliveryId"),
                    MessageId = n.Call<string>("getMessageId"),
                    NotificationId = notificationId,
                    Group = n.Call<string>("getGroupKey"),
                    Actions = actions,
                    Data = ReadStringMap(data),
                };
            }
        }

        public static InboxItem InboxItem(AndroidJavaObject i)
        {
            using (var data = i.Call<AndroidJavaObject>("getData"))
            {
                return new InboxItem
                {
                    Id = i.Call<string>("getId"),
                    MessageId = i.Call<string>("getMessageId"),
                    Title = i.Call<string>("getTitle"),
                    Body = i.Call<string>("getBody"),
                    Url = i.Call<string>("getUrl"),
                    ImageUrl = i.Call<string>("getImageUrl"),
                    Data = ReadStringMap(data),
                    Read = i.Call<bool>("getRead"),
                    ReadAt = Millis(i, "getReadAt"),
                    ArchivedAt = Millis(i, "getArchivedAt"),
                    ExpiresAt = Millis(i, "getExpiresAt"),
                    CreatedAt = Millis(i, "getCreatedAt"),
                };
            }
        }

        public static InboxPage InboxPage(AndroidJavaObject page)
        {
            var items = new List<InboxItem>();
            using (var list = page.Call<AndroidJavaObject>("getItems"))
            {
                var size = list?.Call<int>("size") ?? 0;
                for (var i = 0; i < size; i++)
                {
                    using (var item = list.Call<AndroidJavaObject>("get", i)) items.Add(InboxItem(item));
                }
            }
            return new InboxPage(items, page.Call<int>("getUnreadCount"), page.Call<string>("getNextCursor"), page.Call<bool>("getHasMore"));
        }

        private static DateTimeOffset? Millis(AndroidJavaObject owner, string getter)
        {
            using (var boxed = owner.Call<AndroidJavaObject>(getter))
            {
                return boxed == null ? (DateTimeOffset?)null : DateTimeOffset.FromUnixTimeMilliseconds(boxed.Call<long>("longValue"));
            }
        }

        /// <summary><c>kotlin.Result$Failure</c> ise içindeki Throwable; başarıda null.</summary>
        public static AndroidJavaObject ResultFailure(AndroidJavaObject result)
        {
            if (result == null) return null;
            var className = ClassName(result);
            if (className == "kotlin.Result$Failure") return result.Get<AndroidJavaObject>("exception");
            return null;
        }

        /// <summary>Yerel hata → <see cref="HeroSignalException"/> (sunucu kodu ve HTTP durumu korunur).</summary>
        public static Exception ToException(AndroidJavaObject throwable)
        {
            var className = ClassName(throwable);
            var message = throwable.Call<string>("getMessage");
            switch (className)
            {
                case "com.herosignal.sdk.HsApiException":
                    var status = throwable.Call<int>("getStatus");
                    return new HeroSignalException(throwable.Call<string>("getCode"), message ?? "Sunucu hatası.") { Status = status > 0 ? status : (int?)null };
                case "java.lang.IllegalStateException":
                    return new HeroSignalException(HeroSignalException.NotReady, message ?? "SDK hazır değil.");
                case "java.lang.IllegalArgumentException":
                    return new ArgumentException(message);
                default:
                    return new HeroSignalException(HeroSignalException.NativeError, message ?? className);
            }
        }

        /// <summary>
        /// Tek String parametreli statik metodu null'a izin vererek çağırır (Unity'nin yansıma tabanlı eşlemesi null
        /// argümandan tür çıkaramaz; imza açıkça verilir).
        /// </summary>
        public static void CallStaticNullableString(AndroidJavaClass cls, string method, string value)
        {
            var methodId = AndroidJNI.GetStaticMethodID(cls.GetRawClass(), method, "(Ljava/lang/String;)V");
            var arg = value == null ? IntPtr.Zero : AndroidJNI.NewStringUTF(value);
            try
            {
                AndroidJNI.CallStaticVoidMethod(cls.GetRawClass(), methodId, new[] { new jvalue { l = arg } });
            }
            finally
            {
                if (arg != IntPtr.Zero) AndroidJNI.DeleteLocalRef(arg);
            }
        }

        private static string ClassName(AndroidJavaObject obj)
        {
            using (var cls = obj.Call<AndroidJavaObject>("getClass")) return cls.Call<string>("getName");
        }
    }
}
#endif
