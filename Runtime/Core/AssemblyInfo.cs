using System.Runtime.CompilerServices;

// Editör testleri (Tests/Editor; Validation~ altındaki .NET doğrulama projesi de aynı derleme adını kullanır).
[assembly: InternalsVisibleTo("HeroSignal.Unity.Tests.Editor")]

// Editör araçları (google-services.json dönüştürücüsü çekirdek JSON okuyucusunu kullanır).
[assembly: InternalsVisibleTo("HeroSignal.Unity.Editor")]
