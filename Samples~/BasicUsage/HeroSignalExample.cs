using System.Collections.Generic;
using HeroSignalSdk.Unity;
using UnityEngine;

namespace HeroSignalSdk.Unity.Samples
{
    /// <summary>
    /// Temel kullanım: ilk sahnedeki bir GameObject'e ekleyin, App ID ve API adresini Inspector'dan girin.
    /// Editör'de çağrılar etkisizdir ve Console'a yazılır; push için Android/iOS oyuncusu gerekir.
    /// </summary>
    public sealed class HeroSignalExample : MonoBehaviour
    {
        [SerializeField] private string appId = "app_";
        [SerializeField] private string apiUrl = "https://api.herosignal.dev";
        [SerializeField] private string externalId = string.Empty;

        private void Awake()
        {
            HeroSignal.Initialize(appId, new HeroSignalOptions { ApiUrl = apiUrl, LogLevel = LogLevel.Info });

            HeroSignal.Notifications.Clicked += (_, e) =>
                Debug.Log($"Bildirim tıklandı: {e.Notification.Title} (düğme: {e.ActionId ?? "-"}, url: {e.Url ?? "-"})");
            HeroSignal.Notifications.ForegroundWillDisplay += (_, e) =>
            {
                // Oyun sırasında sessiz bildirimleri gösterme.
                if (e.Notification.Data.ContainsKey("silent_in_game")) e.PreventDefault();
            };
            HeroSignal.User.PushSubscription.Changed += (_, e) =>
                Debug.Log($"Push aboneliği: {e.Current.Id ?? "kayıt bekleniyor"} opt-in={e.Current.OptedIn}");
            HeroSignal.InAppMessages.Clicked += (_, e) => Debug.Log($"In-app {e.MessageId}: {e.ActionId}");
        }

        private async void Start()
        {
            var granted = await HeroSignal.Notifications.RequestPermissionAsync();
            Debug.Log($"Bildirim izni: {granted}");

            if (!string.IsNullOrEmpty(externalId)) HeroSignal.Login(externalId);
            HeroSignal.User.AddTags(new Dictionary<string, string> { ["platform"] = Application.platform.ToString() });
            HeroSignal.InAppMessages.SetScreen("main_menu");
            HeroSignal.Session.SendEvent("game_started", new Dictionary<string, object> { ["version"] = Application.version });

            if (HeroSignal.IsSupported)
            {
                try
                {
                    var page = await HeroSignal.Inbox.ListAsync(limit: 20);
                    Debug.Log($"Bildirim kutusu: {page.Items.Count} öğe, {page.UnreadCount} okunmamış");
                }
                catch (HeroSignalException e)
                {
                    Debug.LogWarning($"Bildirim kutusu alınamadı: {e.Code} {e.Message}");
                }
            }
        }

        /// <summary>Seviye bitince çağırın: in-app tetiklerini ve outcome'u günceller.</summary>
        public void OnLevelCompleted(int level, int score)
        {
            HeroSignal.InAppMessages.AddTrigger("level", level.ToString());
            HeroSignal.Session.SendEvent("level_complete", new Dictionary<string, object> { ["level"] = level }, score);
        }

        private void OnApplicationPause(bool paused)
        {
            // Oyuna dönülünce tepside bekleyen HeroSignal bildirimlerini temizle.
            if (!paused && HeroSignal.IsInitialized) HeroSignal.Notifications.ClearAll();
        }
    }
}
