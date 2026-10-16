#nullable enable
using System;

namespace PushizoSdk.Internal
{
    /// <summary>Yerel köprü olaylarını ve sonuçlarını Unity ana iş parçacığına taşır.</summary>
    internal interface IMainThreadDispatcher
    {
        void Post(Action action);
    }
}
