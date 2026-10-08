using System;
using System.Collections.Generic;
using HeroSignalSdk.Unity.Internal;

namespace HeroSignalSdk.Unity
{
    /// <summary>Kullanıcı: tag, alias, dil, e-posta/SMS abonelikleri ve push aboneliği (<c>HeroSignal.User</c>).</summary>
    public sealed class UserManager
    {
        internal UserManager()
        {
        }

        /// <summary>Push aboneliği.</summary>
        public PushSubscriptionManager PushSubscription { get; } = new PushSubscriptionManager();

        /// <summary>Login ile verilen kullanıcı kimliği; anonimse null.</summary>
        public string ExternalId => HeroSignal.PlatformOrNull()?.ExternalId;

        /// <summary>HeroSignal kullanıcı kimliği (abonelik kaydından sonra).</summary>
        public string Id => HeroSignal.PlatformOrNull()?.UserId;

        /// <summary>Bu cihazda kayıtlı e-posta adresleri.</summary>
        public IReadOnlyList<string> Emails => HeroSignal.PlatformOrNull()?.Emails ?? Array.Empty<string>();

        /// <summary>Bu cihazda kayıtlı SMS numaraları (E.164).</summary>
        public IReadOnlyList<string> SmsNumbers => HeroSignal.PlatformOrNull()?.SmsNumbers ?? Array.Empty<string>();

        public void AddTag(string key, string value) => AddTags(new Dictionary<string, string> { [key ?? string.Empty] = value });

        /// <summary>Tag ekler/günceller; çevrimdışıyken yerel kuyrukta bekler.</summary>
        public void AddTags(IReadOnlyDictionary<string, string> tags)
        {
            var valid = Validation.RequireTags(tags, nameof(tags));
            if (valid.Count == 0) return;
            HeroSignal.Platform().AddTags(valid);
        }

        public void RemoveTag(string key) => RemoveTags(new[] { key });

        public void RemoveTags(IEnumerable<string> keys)
        {
            var valid = Validation.RequireKeys(keys, nameof(keys));
            if (valid.Count == 0) return;
            HeroSignal.Platform().RemoveTags(valid);
        }

        /// <summary>Sunucudan son alınan tag'ler (yerel önbellek).</summary>
        public IReadOnlyDictionary<string, string> GetTags() =>
            HeroSignal.PlatformOrNull()?.GetTags() ?? new Dictionary<string, string>();

        /// <summary>Alias ekler (ör. <c>AddAlias("crm_id", "42")</c>); etiket küçük harf/rakam/_ (en fazla 50).</summary>
        public void AddAlias(string label, string id) => AddAliases(new Dictionary<string, string> { [label ?? string.Empty] = id });

        /// <summary>En fazla 20 alias'ı tek istekte ekler.</summary>
        public void AddAliases(IReadOnlyDictionary<string, string> aliases) =>
            HeroSignal.Platform().AddAliases(Validation.NormalizeAliases(aliases, nameof(aliases)));

        public void RemoveAlias(string label)
        {
            Validation.RequireAliasLabel(label, nameof(label));
            HeroSignal.Platform().RemoveAlias(label);
        }

        /// <summary>Kullanıcının dili (ISO 639-1, ör. <c>tr</c>); mesaj içerikleri bu dilde seçilir.</summary>
        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) throw new ArgumentException("Dil kodu gerekli.", nameof(languageCode));
            HeroSignal.Platform().SetLanguage(languageCode.Trim());
        }

        /// <summary>E-posta aboneliği ekler (küçük harfe çevrilir); geçersiz adreste <see cref="ArgumentException"/>.</summary>
        public void AddEmail(string email) => HeroSignal.Platform().AddEmail(Validation.RequireEmail(email, nameof(email)));

        public void RemoveEmail(string email) => HeroSignal.Platform().RemoveEmail(Validation.RequireEmail(email, nameof(email)));

        /// <summary>SMS aboneliği ekler; numara E.164'e normalize edilir (<c>+90 (532) 123-45-67</c> → <c>+905321234567</c>).</summary>
        public void AddSms(string phoneNumber) => HeroSignal.Platform().AddSms(Validation.RequirePhone(phoneNumber, nameof(phoneNumber)));

        public void RemoveSms(string phoneNumber) => HeroSignal.Platform().RemoveSms(Validation.RequirePhone(phoneNumber, nameof(phoneNumber)));
    }

    /// <summary>Push aboneliği (<c>HeroSignal.User.PushSubscription</c>).</summary>
    public sealed class PushSubscriptionManager
    {
        private readonly object _gate = new object();
        private PushSubscriptionState _last = PushSubscriptionState.Empty;

        internal PushSubscriptionManager()
        {
        }

        /// <summary>Abonelik kimliği, token, opt-in veya izin değiştiğinde (yalnızca gerçek değişiklikte, ana iş parçacığında).</summary>
        public event EventHandler<PushSubscriptionChangedEventArgs> Changed;

        public string Id => Current.Id;
        public string Token => Current.Token;
        public bool OptedIn => Current.OptedIn;

        /// <summary>Güncel durum.</summary>
        public PushSubscriptionState Current => HeroSignal.PlatformOrNull()?.PushSubscription ?? PushSubscriptionState.Empty;

        /// <summary>Push almayı yeniden açar (izin yoksa abonelik izin verilene kadar beklemede kalır).</summary>
        public void OptIn() => HeroSignal.Platform().OptIn();

        /// <summary>Push almayı kapatır (abonelik silinmez).</summary>
        public void OptOut() => HeroSignal.Platform().OptOut();

        internal void Dispatch(PushSubscriptionState state)
        {
            PushSubscriptionState previous;
            lock (_gate)
            {
                if (state.Equals(_last)) return;
                previous = _last;
                _last = state;
            }
            HeroSignal.Raise(Changed, new PushSubscriptionChangedEventArgs(previous, state), "PushSubscription.Changed");
            HeroSignal.Notifications.DispatchPermission(state.Permission);
        }

        internal void Reset()
        {
            lock (_gate) _last = PushSubscriptionState.Empty;
            Changed = null;
        }
    }
}
