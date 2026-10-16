package com.pushizo.unity;

/** İzin isteğinin (bildirim veya konum) sonucu (güncel izin durumu). */
public interface PushizoUnityPermissionCallback {
    void onResult(boolean granted);
}
