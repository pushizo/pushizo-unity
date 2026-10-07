#nullable enable
using System;

namespace HeroSignalSdk.Internal
{
    /// <summary>Yerel köprü olaylarını ve sonuçlarını Unity ana iş parçacığına taşır.</summary>
    internal interface IMainThreadDispatcher
    {
        void Post(Action action);
    }
}
