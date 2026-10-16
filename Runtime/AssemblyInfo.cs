using System.Runtime.CompilerServices;

// EditMode testleri iç katmanı (köprü sözleşmesi, JSON, doğrulama) sahte köprüyle doğrular.
[assembly: InternalsVisibleTo("Pushizo.Unity.Tests")]
// Editor betikleri (google-services.json ayrıştırma) JSON yardımcısını kullanır.
[assembly: InternalsVisibleTo("Pushizo.Unity.Editor")]
