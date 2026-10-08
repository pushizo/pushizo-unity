// APNs token'ını Unity'nin AppDelegateListener mekanizmasıyla alıp HeroSignal köprüsüne (Swift, @_cdecl) iletir.
// UnityAppController didRegisterForRemoteNotifications geri çağrılarını NSNotification olarak yayınlar; dinleyici
// sınıf yüklenirken (+load) kaydedilir, böylece C# Initialize'dan önce gelen token da kaçırılmaz.
#import <Foundation/Foundation.h>
#import "AppDelegateListener.h"

extern "C" void hs_unity_internal_set_device_token(const void* bytes, int length);
extern "C" void hs_unity_internal_set_device_token_error(const char* message);

@interface HeroSignalUnityAppDelegateListener : NSObject <AppDelegateListener>
@end

@implementation HeroSignalUnityAppDelegateListener

static HeroSignalUnityAppDelegateListener* s_listener = nil;

+ (void)load
{
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        s_listener = [[HeroSignalUnityAppDelegateListener alloc] init];
        UnityRegisterAppDelegateListener(s_listener);
    });
}

// Unity bu bildirimde token'ı userInfo yerine doğrudan NSData olarak taşır.
- (void)didRegisterForRemoteNotificationsWithDeviceToken:(NSNotification*)notification
{
    id payload = notification.userInfo;
    if ([payload isKindOfClass:[NSData class]])
    {
        NSData* token = (NSData*)payload;
        hs_unity_internal_set_device_token(token.bytes, (int)token.length);
    }
}

- (void)didFailToRegisterForRemoteNotificationsWithError:(NSNotification*)notification
{
    id payload = notification.userInfo;
    NSString* message = [payload isKindOfClass:[NSError class]] ? [(NSError*)payload localizedDescription] : @"APNs kaydı başarısız";
    hs_unity_internal_set_device_token_error([message UTF8String]);
}

@end
