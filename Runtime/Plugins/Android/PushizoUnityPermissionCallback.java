package com.pushizo.unity;

/** Tek mantıksal sonuçlu geri çağrı: izin isteğinde (bildirim veya konum) güncel izin durumu, bayrak yenilemede başarı. */
public interface PushizoUnityPermissionCallback {
    void onResult(boolean granted);
}
