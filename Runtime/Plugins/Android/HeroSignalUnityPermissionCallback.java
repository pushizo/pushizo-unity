package com.herosignal.unity;

/** Bildirim izni isteğinin sonucu (güncel izin durumu). */
public interface HeroSignalUnityPermissionCallback {
    void onResult(boolean granted);
}
