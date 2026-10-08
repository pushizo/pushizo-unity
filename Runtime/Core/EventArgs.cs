using System;
using System.Threading;

namespace HeroSignalSdk.Unity
{
    /// <summary>Bildirime (veya aksiyon düğmesine) tıklandı.</summary>
    public sealed class NotificationClickedEventArgs : EventArgs
    {
        public NotificationClickedEventArgs(HeroSignalNotification notification, string actionId, string url)
        {
            Notification = notification;
            ActionId = actionId;
            Url = url;
        }

        public HeroSignalNotification Notification { get; }
        /// <summary>Tıklanan düğmenin kimliği; bildirim gövdesine tıklandıysa null.</summary>
        public string ActionId { get; }
        /// <summary>Düğmenin URL'si, yoksa bildirimin URL'si.</summary>
        public string Url { get; }
    }

    /// <summary>
    /// Uygulama ön plandayken bildirim geldi (ana iş parçacığında). <see cref="PreventDefault"/> çağrılmazsa işleyiciler
    /// bittikten sonra bildirim gösterilir. Karar sonradan verilecekse önce <see cref="PreventDefault"/>, ardından
    /// gerekirse <see cref="Display"/> çağrılır (iOS'ta süre 25 sn'dir).
    /// </summary>
    public sealed class NotificationWillDisplayEventArgs : EventArgs
    {
        private readonly Action _display;
        private int _displayed;

        internal NotificationWillDisplayEventArgs(HeroSignalNotification notification, Action display)
        {
            Notification = notification;
            _display = display;
        }

        public HeroSignalNotification Notification { get; }

        /// <summary>Bu olay için <see cref="PreventDefault"/> çağrıldı mı.</summary>
        public bool IsPrevented { get; private set; }

        /// <summary>Bildirim gösterilmez.</summary>
        public void PreventDefault() => IsPrevented = true;

        /// <summary>Bildirimi şimdi gösterir (yalnızca bir kez).</summary>
        public void Display()
        {
            if (Interlocked.Exchange(ref _displayed, 1) == 0) _display();
        }
    }

    /// <summary>Sistem bildirim izni değişti.</summary>
    public sealed class NotificationPermissionChangedEventArgs : EventArgs
    {
        public NotificationPermissionChangedEventArgs(bool permission) => Permission = permission;

        public bool Permission { get; }
    }

    /// <summary>Push aboneliği (kimlik, token, opt-in veya izin) değişti.</summary>
    public sealed class PushSubscriptionChangedEventArgs : EventArgs
    {
        public PushSubscriptionChangedEventArgs(PushSubscriptionState previous, PushSubscriptionState current)
        {
            Previous = previous;
            Current = current;
        }

        public PushSubscriptionState Previous { get; }
        public PushSubscriptionState Current { get; }
    }

    /// <summary>In-app mesaj düğmesine tıklandı.</summary>
    public sealed class InAppMessageClickedEventArgs : EventArgs
    {
        public InAppMessageClickedEventArgs(string messageId, string actionId, string url)
        {
            MessageId = messageId;
            ActionId = actionId;
            Url = url;
        }

        public string MessageId { get; }
        /// <summary>Düğme kimliği.</summary>
        public string ActionId { get; }
        /// <summary>url/deeplink aksiyonunun adresi; diğer aksiyonlarda null.</summary>
        public string Url { get; }
    }

    /// <summary>In-app mesaj yaşam döngüsü (WillDisplay/DidDisplay/WillDismiss/DidDismiss).</summary>
    public sealed class InAppMessageLifecycleEventArgs : EventArgs
    {
        public InAppMessageLifecycleEventArgs(InAppMessage message) => Message = message;

        public InAppMessage Message { get; }
    }
}
