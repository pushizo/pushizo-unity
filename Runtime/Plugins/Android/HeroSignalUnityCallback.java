package com.herosignal.unity;

/** Eşzamansız işlem sonucu: başarıda json, hatada errorCode + errorMessage (arka plan iş parçacığında). */
public interface HeroSignalUnityCallback {
    void onResult(String json, String errorCode, String errorMessage);
}
