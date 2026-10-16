package com.pushizo.unity;

/** Yerel olay dinleyicisi; C# tarafında AndroidJavaProxy ile uygulanır (herhangi bir iş parçacığından çağrılır). */
public interface PushizoUnityListener {
    void onEvent(String type, String json);
}
