package com.herosignal.unity;

/** Yerel olay dinleyicisi; C# tarafında AndroidJavaProxy ile uygulanır (herhangi bir iş parçacığından çağrılır). */
public interface HeroSignalUnityListener {
    void onEvent(String type, String json);
}
