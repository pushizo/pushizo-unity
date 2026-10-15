package com.herosignal.unity;

/** İzin isteğinin (bildirim veya konum) sonucu (güncel izin durumu). */
public interface HeroSignalUnityPermissionCallback {
    void onResult(boolean granted);
}
