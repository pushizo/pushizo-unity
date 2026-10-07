#nullable enable

namespace HeroSignalSdk
{
    /// <summary><see cref="HeroSignal.Initialize"/> yapılandırması.</summary>
    public sealed class HeroSignalOptions
    {
        /// <summary>HeroSignal API adresi (yerelde Android emülatöründen <c>http://10.0.2.2:4000</c>).</summary>
        public string ApiUrl { get; set; } = "https://api.herosignal.dev";

        /// <summary>KVKK/GDPR: <see cref="HeroSignal.SetConsentGiven"/> çağrılana kadar ağ çağrısı yapılmaz.</summary>
        public bool RequiresPrivacyConsent { get; set; }

        public LogLevel LogLevel { get; set; } = LogLevel.Warn;

        /// <summary>Android 8+ varsayılan bildirim kanalı adı.</summary>
        public string AndroidDefaultChannelName { get; set; } = "Bildirimler";

        /// <summary>Android 8+ varsayılan bildirim kanalı kimliği.</summary>
        public string AndroidDefaultChannelId { get; set; } = "herosignal_default";

        /// <summary>iOS: NSE ile rozet sayacı paylaşımı için App Group (ör. <c>group.com.example.app</c>).</summary>
        public string? IosAppGroup { get; set; }

        /// <summary>iOS: APNs ortamı (<c>sandbox</c> / <c>production</c>); null ise provisioning profilinden tespit edilir.</summary>
        public string? IosApnsEnvironment { get; set; }

        /// <summary>iOS: provisional (sessiz) izinle başla.</summary>
        public bool IosProvisionalAuthorization { get; set; }

        internal HeroSignalOptions Clone() => (HeroSignalOptions)MemberwiseClone();
    }
}
