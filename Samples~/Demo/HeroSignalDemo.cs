using System;
using System.Collections.Generic;
using HeroSignalSdk;
using UnityEngine;

namespace HeroSignalSdk.Samples
{
    /// <summary>
    /// HeroSignal demo: boş bir sahnede bir GameObject'e ekleyin ve App ID'yi Inspector'dan girin. IMGUI ile tek ekran:
    /// izin, push aboneliği aç/kapa, kimlik, dil, tag/alias, e-posta/SMS, olay, in-app tetik, ön plan gösterimi, bildirim
    /// kutusu ve olay günlüğü.
    /// Metinler sistem diline göre Türkçe/İngilizce; arayüz koyu temadır.
    /// </summary>
    public sealed class HeroSignalDemo : MonoBehaviour
    {
        [Tooltip("Dashboard'daki uygulama kimliği (app_…).")]
        [SerializeField] private string appId = "app_01M4884V0AAMP3Z9RF8R8NFN7A";

        [Tooltip("HeroSignal API adresi (Android emülatörü → ana makine: http://10.0.2.2:4000).")]
        [SerializeField] private string apiUrl = "http://10.0.2.2:4000";

        private readonly List<string> _log = new List<string>();
        private readonly List<InboxItem> _inbox = new List<InboxItem>();
        private string _externalId = string.Empty;
        private string _contact = string.Empty;
        private bool _showInForeground = true;
        private bool _inboxOpen;
        private bool _inboxLoading;
        private string _inboxError;
        private Vector2 _scroll;
        private Texture2D _background;

        private static bool Turkish => Application.systemLanguage == SystemLanguage.Turkish;

        private static string T(string key) => (Turkish ? Tr : En).TryGetValue(key, out var text) ? text : key;

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["permission"] = "Request notification permission",
            ["optIn"] = "Opt in",
            ["optOut"] = "Opt out",
            ["language"] = "Set language to Turkish",
            ["login"] = "Log in",
            ["logout"] = "Log out",
            ["externalId"] = "External ID",
            ["contact"] = "E-mail or phone (+90…)",
            ["tag"] = "Tag plan=pro",
            ["alias"] = "Alias crm_id=42",
            ["email"] = "Add e-mail",
            ["sms"] = "Add SMS",
            ["event"] = "Send event cart_viewed",
            ["trigger"] = "Trigger level=gold",
            ["foreground"] = "Show while app is open",
            ["clear"] = "Clear notifications",
            ["inbox"] = "Inbox",
            ["refresh"] = "Refresh",
            ["readAll"] = "Read all",
            ["read"] = "Read",
            ["archive"] = "Archive",
            ["loading"] = "Loading…",
            ["empty"] = "Your inbox is empty.",
            ["notReady"] = "The inbox becomes available once the device is registered.",
            ["error"] = "Error: {0}",
            ["status"] = "Permission: {0} · Subscription: {1} · User: {2}",
            ["log"] = "Event log",
        };

        private static readonly Dictionary<string, string> Tr = new Dictionary<string, string>
        {
            ["permission"] = "Bildirim izni iste",
            ["optIn"] = "Aboneliği aç",
            ["optOut"] = "Aboneliği kapat",
            ["language"] = "Dili Türkçe yap",
            ["login"] = "Giriş yap",
            ["logout"] = "Çıkış yap",
            ["externalId"] = "External ID",
            ["contact"] = "E-posta veya telefon (+90…)",
            ["tag"] = "Tag plan=pro",
            ["alias"] = "Alias crm_id=42",
            ["email"] = "E-posta ekle",
            ["sms"] = "SMS ekle",
            ["event"] = "cart_viewed olayı gönder",
            ["trigger"] = "Trigger level=gold",
            ["foreground"] = "Uygulama açıkken göster",
            ["clear"] = "Bildirimleri temizle",
            ["inbox"] = "Bildirim kutusu",
            ["refresh"] = "Yenile",
            ["readAll"] = "Tümünü oku",
            ["read"] = "Okundu",
            ["archive"] = "Arşivle",
            ["loading"] = "Yükleniyor…",
            ["empty"] = "Bildirim kutunuz boş.",
            ["notReady"] = "Cihaz kaydı tamamlanınca bildirim kutusu açılır.",
            ["error"] = "Hata: {0}",
            ["status"] = "İzin: {0} · Abonelik: {1} · Kullanıcı: {2}",
            ["log"] = "Olay günlüğü",
        };

        private void Awake()
        {
            HeroSignal.Debug.SetLogLevel(LogLevel.Debug);
            HeroSignal.Initialize(appId, new HeroSignalOptions { ApiUrl = apiUrl });

            HeroSignal.User.PushSubscription.Changed += e => Log($"subscription: {e.Current.Id ?? "-"} (opted in: {e.Current.OptedIn})");
            HeroSignal.Notifications.PermissionChanged += granted => Log($"permission: {granted}");
            HeroSignal.Notifications.Clicked += e => Log($"click: {e.Notification.Title ?? "-"} · {e.ActionId ?? "-"} · {e.Url ?? "-"}");
            HeroSignal.Notifications.ForegroundWillDisplay += e =>
            {
                if (!_showInForeground) e.PreventDefault();
                Log($"foreground: {e.Notification.Title ?? "-"} (prevented: {e.IsPrevented})");
            };
            HeroSignal.InAppMessages.Clicked += e => Log($"in-app click: {e.MessageId} · {e.ActionId}");
            HeroSignal.InAppMessages.DidDisplay += e => Log($"in-app shown: {e.Message.Id}");
            HeroSignal.InAppMessages.SetScreen("demo");

            _background = new Texture2D(1, 1);
            _background.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.09f));
            _background.Apply();
        }

        private void OnDestroy()
        {
            if (_background != null) Destroy(_background);
        }

        private void Log(string line)
        {
            _log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
            if (_log.Count > 50) _log.RemoveAt(_log.Count - 1);
        }

        /// <summary>SDK doğrulama hataları (geçersiz e-posta vb.) günlüğe yazılır.</summary>
        private void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                Log(string.Format(T("error"), e.Message));
            }
        }

        private async void RequestPermission()
        {
            var granted = await HeroSignal.Notifications.RequestPermissionAsync(fallbackToSettings: true);
            Log($"permission: {granted}");
        }

        private async void LoadInbox()
        {
            _inboxLoading = true;
            _inboxError = null;
            try
            {
                var page = await HeroSignal.Inbox.ListAsync(20);
                _inbox.Clear();
                _inbox.AddRange(page.Items);
            }
            catch (HeroSignalException e)
            {
                _inboxError = e.Code == HeroSignalException.NotReady ? T("notReady") : string.Format(T("error"), e.Message);
            }
            finally
            {
                _inboxLoading = false;
            }
        }

        private async void InboxAction(Func<System.Threading.Tasks.Task> action)
        {
            try
            {
                await action();
                LoadInbox();
            }
            catch (HeroSignalException e)
            {
                _inboxError = string.Format(T("error"), e.Message);
            }
        }

        private void OnGUI()
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _background);
            var scale = Screen.dpi > 0 ? Screen.dpi / 160f : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var width = Screen.width / scale - 32f;
            GUILayout.BeginArea(new Rect(16, 16, width, Screen.height / scale - 32f));
            _scroll = GUILayout.BeginScrollView(_scroll);

            var s = HeroSignal.User.PushSubscription.Current;
            GUILayout.Label(string.Format(T("status"), HeroSignal.Notifications.Permission, s.Id ?? "-", HeroSignal.User.ExternalId ?? "-"));

            if (GUILayout.Button(T("permission"))) RequestPermission();
            // Opt-out: izin korunur ama push gönderilmez; opt-in yeniden açar.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("optIn"))) Run(HeroSignal.User.PushSubscription.OptIn);
            if (GUILayout.Button(T("optOut"))) Run(HeroSignal.User.PushSubscription.OptOut);
            GUILayout.EndHorizontal();
            _showInForeground = GUILayout.Toggle(_showInForeground, T("foreground"));
            if (GUILayout.Button(T("clear"))) Run(() => HeroSignal.Notifications.ClearAll());

            GUILayout.Label(T("externalId"));
            _externalId = GUILayout.TextField(_externalId);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("login"))) Run(() => HeroSignal.Login(_externalId.Trim()));
            if (GUILayout.Button(T("logout"))) Run(HeroSignal.Logout);
            GUILayout.EndHorizontal();
            // Dil, segmentlemede ve çok dilli bildirim içeriğinin seçiminde kullanılır (ISO 639-1).
            if (GUILayout.Button(T("language"))) Run(() => HeroSignal.User.SetLanguage("tr"));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("tag"))) Run(() => HeroSignal.User.AddTag("plan", "pro"));
            if (GUILayout.Button(T("alias"))) Run(() => HeroSignal.User.AddAlias("crm_id", "42"));
            GUILayout.EndHorizontal();

            GUILayout.Label(T("contact"));
            _contact = GUILayout.TextField(_contact);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("email"))) Run(() => HeroSignal.User.AddEmail(_contact));
            if (GUILayout.Button(T("sms"))) Run(() => HeroSignal.User.AddSms(_contact));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("event"))) Run(() => HeroSignal.Session.SendEvent("cart_viewed", new Dictionary<string, object> { ["items"] = 2 }));
            if (GUILayout.Button(T("trigger"))) Run(() => HeroSignal.InAppMessages.AddTrigger("level", "gold"));
            GUILayout.EndHorizontal();

            if (GUILayout.Button(T("inbox")))
            {
                _inboxOpen = !_inboxOpen;
                if (_inboxOpen) LoadInbox();
            }
            if (_inboxOpen) DrawInbox();

            GUILayout.Label(T("log"));
            foreach (var line in _log) GUILayout.Label(line);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawInbox()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T("refresh"))) LoadInbox();
            if (GUILayout.Button(T("readAll"))) InboxAction(HeroSignal.Inbox.MarkAllReadAsync);
            GUILayout.EndHorizontal();

            if (_inboxLoading) GUILayout.Label(T("loading"));
            else if (_inboxError != null) GUILayout.Label(_inboxError);
            else if (_inbox.Count == 0) GUILayout.Label(T("empty"));
            else
            {
                foreach (var item in _inbox.ToArray())
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((item.Read ? "  " : "● ") + (item.Title ?? "-") + "\n" + (item.Body ?? string.Empty));
                    if (!item.Read && GUILayout.Button(T("read"), GUILayout.Width(90))) InboxAction(() => HeroSignal.Inbox.MarkReadAsync(item.Id));
                    if (GUILayout.Button(T("archive"), GUILayout.Width(90))) InboxAction(() => HeroSignal.Inbox.ArchiveAsync(item.Id));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }
    }
}
