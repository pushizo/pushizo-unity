using System;
using System.Collections.Generic;
using HeroSignalSdk.Unity.Internal;

namespace HeroSignalSdk.Unity
{
    /// <summary>
    /// In-app mesajlar (<c>HeroSignal.InAppMessages</c>). Mesajlar yerel SDK'nın yerel arayüzüyle (Android Dialog,
    /// iOS UIKit) oyunun üstünde çizilir; tetikler (oturum başı, olay adı, ekran, tetik değerleri) yerelde değerlendirilir.
    /// </summary>
    public sealed class InAppMessagesManager
    {
        internal InAppMessagesManager()
        {
        }

        /// <summary>Mesaj düğmesine tıklandı.</summary>
        public event EventHandler<InAppMessageClickedEventArgs> Clicked;
        public event EventHandler<InAppMessageLifecycleEventArgs> WillDisplay;
        public event EventHandler<InAppMessageLifecycleEventArgs> DidDisplay;
        public event EventHandler<InAppMessageLifecycleEventArgs> WillDismiss;
        public event EventHandler<InAppMessageLifecycleEventArgs> DidDismiss;

        /// <summary>true iken yeni mesaj gösterilmez (tetikler kaydedilmeye devam eder; ör. oyun sırasında).</summary>
        public bool Paused
        {
            get => HeroSignal.PlatformOrNull()?.InAppPaused ?? false;
            set => HeroSignal.Platform().InAppPaused = value;
        }

        public void AddTrigger(string key, string value) => AddTriggers(new Dictionary<string, string> { [key ?? string.Empty] = value });

        /// <summary>Tetik değerleri ekler/günceller (anahtar 1-64, değer en fazla 200 karakter).</summary>
        public void AddTriggers(IReadOnlyDictionary<string, string> triggers)
        {
            var valid = Validation.RequireTriggers(triggers, nameof(triggers));
            if (valid.Count == 0) return;
            HeroSignal.Platform().AddTriggers(valid);
        }

        public void RemoveTrigger(string key) => RemoveTriggers(new[] { key });

        public void RemoveTriggers(IEnumerable<string> keys)
        {
            var valid = Validation.RequireKeys(keys, nameof(keys));
            if (valid.Count == 0) return;
            HeroSignal.Platform().RemoveTriggers(valid);
        }

        public void ClearTriggers() => HeroSignal.Platform().ClearTriggers();

        /// <summary>Geçerli tetik değerleri.</summary>
        public IReadOnlyDictionary<string, string> GetTriggers() =>
            HeroSignal.PlatformOrNull()?.GetTriggers() ?? new Dictionary<string, string>();

        /// <summary>Geçerli ekran/sahne adı (<c>screen</c> tetikleri); çıkarken null.</summary>
        public void SetScreen(string name) => HeroSignal.Platform().SetScreen(string.IsNullOrWhiteSpace(name) ? null : name.Trim());

        internal void DispatchClicked(InAppMessageClickedEventArgs args) => HeroSignal.Raise(Clicked, args, "InAppMessages.Clicked");

        internal void DispatchLifecycle(InAppLifecyclePhase phase, InAppMessage message)
        {
            var args = new InAppMessageLifecycleEventArgs(message);
            switch (phase)
            {
                case InAppLifecyclePhase.WillDisplay: HeroSignal.Raise(WillDisplay, args, "InAppMessages.WillDisplay"); break;
                case InAppLifecyclePhase.DidDisplay: HeroSignal.Raise(DidDisplay, args, "InAppMessages.DidDisplay"); break;
                case InAppLifecyclePhase.WillDismiss: HeroSignal.Raise(WillDismiss, args, "InAppMessages.WillDismiss"); break;
                case InAppLifecyclePhase.DidDismiss: HeroSignal.Raise(DidDismiss, args, "InAppMessages.DidDismiss"); break;
            }
        }

        internal void Reset()
        {
            Clicked = null;
            WillDisplay = null;
            DidDisplay = null;
            WillDismiss = null;
            DidDismiss = null;
        }
    }
}
