using System;
using System.Globalization;
using System.Threading.Tasks;

namespace HeroSignalSdk.Unity
{
    /// <summary>Push izni, bildirim olayları ve bildirim tepsisi (<c>HeroSignal.Notifications</c>).</summary>
    public sealed class NotificationsManager
    {
        private readonly object _gate = new object();
        private EventHandler<NotificationClickedEventArgs> _clicked;
        private NotificationClickedEventArgs _pendingClick;
        private bool? _lastPermission;

        internal NotificationsManager()
        {
        }

        /// <summary>
        /// Bildirime veya aksiyon düğmesine tıklandı (ana iş parçacığında). Oyun bildirimle açıldıysa ve henüz işleyici
        /// yoksa son tıklama saklanır, ilk abone olan işleyiciye hemen iletilir.
        /// </summary>
        public event EventHandler<NotificationClickedEventArgs> Clicked
        {
            add
            {
                NotificationClickedEventArgs pending;
                lock (_gate)
                {
                    _clicked += value;
                    pending = _pendingClick;
                    _pendingClick = null;
                }
                if (pending != null && value != null) HeroSignal.Raise(value, pending, "Notifications.Clicked");
            }
            remove
            {
                lock (_gate) _clicked -= value;
            }
        }

        /// <summary>
        /// Oyun ön plandayken bildirim geldi (ana iş parçacığında). İşleyiciler <see cref="NotificationWillDisplayEventArgs.PreventDefault"/>
        /// çağırmazsa bildirim gösterilir. İşleyici yoksa yerel SDK doğrudan gösterir.
        /// </summary>
        public event EventHandler<NotificationWillDisplayEventArgs> ForegroundWillDisplay;

        /// <summary>Sistem bildirim izni değişti (yalnızca gerçek değişiklikte).</summary>
        public event EventHandler<NotificationPermissionChangedEventArgs> PermissionChanged;

        /// <summary>Sistem bildirim izni var mı (Android 13+ POST_NOTIFICATIONS; iOS authorized/provisional).</summary>
        public bool Permission => HeroSignal.PlatformOrNull()?.NotificationPermission ?? false;

        internal bool HasForegroundHandlers => ForegroundWillDisplay != null;

        /// <summary>
        /// Sistem bildirim iznini ister; sonuç izin durumudur. <paramref name="fallbackToSettings"/> true ise izin daha önce
        /// reddedildiyse uygulamanın bildirim ayarları açılır.
        /// </summary>
        public Task<bool> RequestPermissionAsync(bool fallbackToSettings = false) =>
            HeroSignal.Platform().RequestPermissionAsync(fallbackToSettings);

        /// <summary>SDK'nın gösterdiği tüm bildirimleri tepsiden kaldırır (iOS'ta rozet de sıfırlanır).</summary>
        public void ClearAll() => HeroSignal.Platform().ClearAllNotifications();

        /// <summary>
        /// Tek bildirimi kaldırır. Kimlik <see cref="HeroSignalNotification.NotificationId"/> değeridir: Android'de sistem
        /// bildirim kimliği (sayı), iOS'ta istek kimliği veya teslimat kimliği (<c>hs.dlv</c>).
        /// </summary>
        public void RemoveNotification(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Bildirim kimliği gerekli.", nameof(id));
            HeroSignal.Platform().RemoveNotification(id.Trim());
        }

        /// <summary>Android sistem bildirim kimliğiyle kaldırır.</summary>
        public void RemoveNotification(int id) => RemoveNotification(id.ToString(CultureInfo.InvariantCulture));

        /// <summary>Gruptaki HeroSignal bildirimlerini kaldırır (Android <c>hs_group</c>, iOS <c>thread-id</c>).</summary>
        public void RemoveGroupedNotifications(string group)
        {
            if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Grup anahtarı gerekli.", nameof(group));
            HeroSignal.Platform().RemoveGroupedNotifications(group.Trim());
        }

        internal void DispatchClicked(NotificationClickedEventArgs args)
        {
            EventHandler<NotificationClickedEventArgs> handler;
            lock (_gate)
            {
                handler = _clicked;
                if (handler == null)
                {
                    _pendingClick = args;
                    return;
                }
            }
            HeroSignal.Raise(handler, args, "Notifications.Clicked");
        }

        internal void DispatchWillDisplay(NotificationWillDisplayEventArgs args) =>
            HeroSignal.Raise(ForegroundWillDisplay, args, "Notifications.ForegroundWillDisplay");

        internal void DispatchPermission(bool permission)
        {
            lock (_gate)
            {
                if (_lastPermission == permission) return;
                _lastPermission = permission;
            }
            HeroSignal.Raise(PermissionChanged, new NotificationPermissionChangedEventArgs(permission), "Notifications.PermissionChanged");
        }

        internal void Reset()
        {
            lock (_gate)
            {
                _clicked = null;
                _pendingClick = null;
                _lastPermission = null;
            }
            ForegroundWillDisplay = null;
            PermissionChanged = null;
        }
    }
}
