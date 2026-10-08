using System;
using System.Threading.Tasks;
using HeroSignalSdk.Unity.Internal;

namespace HeroSignalSdk.Unity
{
    /// <summary>
    /// Bildirim kutusu (<c>HeroSignal.Inbox</c>; <c>add_to_inbox</c> mesajları). Ana iş parçacığından <c>await</c> edilen
    /// çağrıların devamı Unity ana iş parçacığında çalışır. Hatalar <see cref="HeroSignalException"/> olarak döner
    /// (<c>NOT_READY</c>: abonelik/kullanıcı henüz kayıtlı değil).
    /// </summary>
    public sealed class InboxManager
    {
        internal InboxManager()
        {
        }

        /// <summary>Öğeler (yeniden eskiye), okunmamış sayısı ve sonraki sayfa imleci (<paramref name="limit"/> 1-200).</summary>
        public Task<InboxPage> ListAsync(int limit = 50, string cursor = null)
        {
            Validation.RequireInboxLimit(limit, nameof(limit));
            return Require().InboxListAsync(limit, string.IsNullOrEmpty(cursor) ? null : cursor);
        }

        /// <summary>Öğeyi okundu işaretler; güncel öğeyi döndürür.</summary>
        public Task<InboxItem> MarkReadAsync(string id) => Require().InboxMarkReadAsync(RequireId(id));

        /// <summary>Tümünü okundu işaretler; kalan okunmamış sayısını döndürür.</summary>
        public Task<int> MarkAllReadAsync() => Require().InboxMarkAllReadAsync();

        /// <summary>Öğeyi kutudan kaldırır (arşivler).</summary>
        public Task ArchiveAsync(string id) => Require().InboxArchiveAsync(RequireId(id));

        /// <summary>Okunmamış öğe sayısı.</summary>
        public Task<int> UnreadCountAsync() => Require().InboxUnreadCountAsync();

        private static string RequireId(string id) =>
            string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Öğe kimliği gerekli.", nameof(id)) : id;

        private static IHeroSignalBridge Require()
        {
            var bridge = HeroSignal.PlatformOrNull()
                ?? throw new HeroSignalException(HeroSignalException.NotInitialized, "Önce HeroSignal.Initialize çağrılmalı.");
            if (!bridge.IsSupported)
                throw new HeroSignalException(HeroSignalException.PlatformNotSupported, "Bildirim kutusu yalnızca Android ve iOS'ta kullanılabilir.");
            return bridge;
        }
    }
}
