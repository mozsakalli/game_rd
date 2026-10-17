// DEPlugin.h — iOS plugin tabani (docs/plugins.md). Plugin yazari Assets/Plugins/<Ad>/iOS/*.m|.swift icinde bu
// siniftan turer ve DE_REGISTER_PLUGIN(Sinif) ile kaydolur; ad = managed Plugin sinifinin kisa adi.
// Protokol iki yonde string: onMessage: <- managed Plugin.Send (main thread; host @try/@catch — exception oyunu
// dusurmez), send: -> managed Plugin.OnMessage (her thread; inbox, frame basinda cekilir).
// Swift: ObjC base'den turetilen Swift sinifi override'lari ObjC dispatch'ten gecer -> ayni koruma.
#ifndef DE_PLUGIN_IOS_H
#define DE_PLUGIN_IOS_H
#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface DEPlugin : NSObject
@property(nonatomic, readonly) NSString *name;
// Oyun VC'si (present icin) ve Metal yuzeyinin USTUNDEKI bos overlay (banner/webview; tiklamayi gecirir).
@property(nonatomic, readonly, nullable) UIViewController *viewController;
@property(nonatomic, readonly, nullable) UIView *overlayView;

- (instancetype)initWithName:(NSString *)name;
- (void)onMessage:(NSString *)msg;                 // override
- (void)send:(NSString *)msg;                      // managed'a

// AppDelegate forward seti (hepsi opsiyonel override; varsayilan bos / NO)
- (void)didFinishLaunching:(nullable NSDictionary *)launchOptions;
- (BOOL)openURL:(NSURL *)url options:(NSDictionary<UIApplicationOpenURLOptionsKey, id> *)options;
- (BOOL)continueUserActivity:(NSUserActivity *)activity;
- (void)didRegisterForRemoteNotifications:(NSData *)deviceToken;
- (void)didFailToRegisterForRemoteNotifications:(NSError *)error;
- (void)didReceiveRemoteNotification:(NSDictionary *)userInfo;
- (void)didBecomeActive;
- (void)willResignActive;
- (void)didEnterBackground;
- (void)willEnterForeground;
@end

// Host: kayit (DE_REGISTER_PLUGIN kullanin) + hata raporlayici kancasi (Crashlytics plugin'i doldurur).
@interface DEPluginHost : NSObject
+ (void)registerClass:(Class)cls;
@property(class, nonatomic, nullable) void (^errorReporter)(NSException *e);
@end

// Statik init'te sinifi kaydeder; instance didFinishLaunching'de kurulur (pre-main UIKit dokunusu yok).
#define DE_REGISTER_PLUGIN(CLS) \
    __attribute__((constructor)) static void de_register_plugin_##CLS(void) { [DEPluginHost registerClass:[CLS class]]; }

NS_ASSUME_NONNULL_END
#endif
