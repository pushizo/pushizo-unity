package com.pushizo.unity;

/** Eşzamansız işlem sonucu: başarıda json, hatada errorCode + errorMessage (arka plan iş parçacığında). */
public interface PushizoUnityCallback {
    void onResult(String json, String errorCode, String errorMessage);
}
