// host_ios.m — iOS platform host'u (docs/platform-hosts.md H6). Android'deki GameActivity.kt + host_android.c'nin
// tek dosyada karsiligi: UIKit pencere/goruntu (CAMetalLayer'li UIView), dongu (CADisplayLink), dokunma
// (touches* -> de_app_event kuyrugu), yasam dongusu (foreground/background -> pause/resume, memory warning).
// Oyun mantigi uretilen C + runtime icinde; burasi yalnizca host. Her build'de generated/ altina yeniden
// yazilir — degistirmeyin; SDK entegrasyonu icin kendi kodunuzu shell/ (Xcode projesi) tarafina ekleyin.
//
// THREAD: display link ana thread'de kosar; init/frame/pause/resume/shutdown hep ana thread -> de_app.h sozlesmesi.
// KOORDINAT: dokunma UIKit point'tir = MANTIKSAL px (fbw/scale uzayi) -> dogrudan iletilir (Android: px/density).
// PAK: game.pak uygulama bundle'inda Build/game.pak (folder reference). dataRoot = bundle resourcePath ->
//      GameHost "<root>/Build/game.pak" acar (duz dosya; kopya yok). writable = Application Support/<bundle id>.
#import <UIKit/UIKit.h>
#import <QuartzCore/CAMetalLayer.h>
#import <objc/runtime.h>
#include <stdio.h>
#include <string.h>
#include "de_app.h"
#include "de_plugin.h"
#include "vmrt.h"
#import "DEPlugin.h"

void de_metal_init_layer(void *caMetalLayer); // sokol_shim.m (SOKOL_METAL, iOS dali)

void de_host_set_title(const char *utf8) { fprintf(stderr, "[host] title: %s\n", utf8); }
void de_host_log(int level, const char *utf8) { fprintf(level >= 2 ? stderr : stdout, "%s\n", utf8); }

// de_host_fatal (docs/plugins.md): yakalanmayan managed exception -> NSException (reason = "Tip: mesaj" + .cs frame'leri).
// Yakalanmayan ObjC exception -> "Terminating app due to uncaught exception" -> crash log "Application Specific Information" /
// Crashlytics / Organizer satir bilgili trace gosterir. exit() reporter'lara gorunmezdi. Donmez.
void de_host_fatal(const char *reason, const RtFrame *frames, int count)
{
    NSMutableString *text = [NSMutableString stringWithUTF8String:reason ?: "managed exception"];
    int n = count > 64 ? 64 : count;
    for (int i = 0; i < n; i++)
    {
        const RtFrame *f = &frames[count - 1 - i];
        char type[128], method[128]; const char *file; int line, col;
        digitoyengine_frame_info(f, type, sizeof type, method, sizeof method, &file, &line, &col);
        const char *fname = file ? strrchr(file, '/') : 0; fname = fname ? fname + 1 : file;
        [text appendFormat:@"\n  at %s.%s(%s:%d)", type[0] ? type : "Managed", method[0] ? method : "?", fname ?: "?", line];
    }
    [[NSException exceptionWithName:@"DigitoyManagedException" reason:text userInfo:nil] raise];
    abort(); // raise donmez; derleyici icin
}

// ---------------------------------------------------------------------------
// Plugin host (docs/plugins.md): kayit, dispatch (main thread, @try/@catch), lifecycle fan-out.
static NSMutableArray<Class> *g_pluginClasses;
static NSMutableDictionary<NSString *, DEPlugin *> *g_plugins;
static __weak UIViewController *g_gameVC;
static __weak UIView *g_overlay;
static void (^g_errorReporter)(NSException *);

static void de_ios_report(DEPlugin *p, NSException *e)
{
    fprintf(stderr, "[plugin] hata (%s) — oyun devam ediyor: %s: %s\n", p ? p.name.UTF8String : "?", e.name.UTF8String, e.reason.UTF8String);
    @try { if (g_errorReporter) g_errorReporter(e); } @catch (NSException *ignored) {}
}
#define DE_GUARD(p, body) @try { body; } @catch (NSException *e) { de_ios_report(p, e); }

static void de_ios_on_message(void *user, const char *msg)
{
    DEPlugin *p = (__bridge DEPlugin *)user;
    NSString *s = [NSString stringWithUTF8String:msg ?: ""];
    DE_GUARD(p, [p onMessage:s]);
}
// de_plugin_send main thread'den gelir (display link); degilse main'e atla.
static void de_ios_run_on_main(DePluginDispatchFn fn, void *user, char *name, char *msg)
{
    if ([NSThread isMainThread]) { fn(user, name, msg); de_plugin_free_dispatch(name, msg); return; }
    dispatch_async(dispatch_get_main_queue(), ^{ fn(user, name, msg); de_plugin_free_dispatch(name, msg); });
}

@implementation DEPlugin
- (instancetype)initWithName:(NSString *)name { if ((self = [super init])) _name = [name copy]; return self; }
- (UIViewController *)viewController { return g_gameVC; }
- (UIView *)overlayView { return g_overlay; }
- (void)onMessage:(NSString *)msg {}
- (void)send:(NSString *)msg { de_plugin_post(self.name.UTF8String, (msg ?: @"").UTF8String); }
- (void)didFinishLaunching:(NSDictionary *)o {}
- (BOOL)openURL:(NSURL *)url options:(NSDictionary<UIApplicationOpenURLOptionsKey, id> *)options { return NO; }
- (BOOL)continueUserActivity:(NSUserActivity *)activity { return NO; }
- (void)didRegisterForRemoteNotifications:(NSData *)t {}
- (void)didFailToRegisterForRemoteNotifications:(NSError *)e {}
- (void)didReceiveRemoteNotification:(NSDictionary *)u {}
- (void)didBecomeActive {}
- (void)willResignActive {}
- (void)didEnterBackground {}
- (void)willEnterForeground {}
@end

@implementation DEPluginHost
+ (void)registerClass:(Class)cls
{
    if (!g_pluginClasses) g_pluginClasses = [NSMutableArray array];
    [g_pluginClasses addObject:cls];
}
+ (void (^)(NSException *))errorReporter { return g_errorReporter; }
+ (void)setErrorReporter:(void (^)(NSException *))r { g_errorReporter = [r copy]; }
@end

// didFinishLaunching'de: siniflar -> instance'lar (ad = sinifin -name'i; DE_REGISTER_PLUGIN'li sinif initWithName
// ile kendi adini verir: plugin yazari `- (instancetype)init { return [super initWithName:@"Ads"]; }`).
static void de_ios_plugins_create(void)
{
    g_plugins = [NSMutableDictionary dictionary];
    for (Class cls in g_pluginClasses)
    {
        DEPlugin *p = nil;
        DE_GUARD(nil, p = [cls new]);
        if (!p || !p.name.length) { fprintf(stderr, "[plugin] %s: ad yok (initWithName cagrilmali)\n", class_getName(cls)); continue; }
        if (g_plugins[p.name]) { fprintf(stderr, "[plugin] ad cakisti: %s\n", p.name.UTF8String); continue; }
        g_plugins[p.name] = p;
        de_plugin_register(p.name.UTF8String, de_ios_on_message, (__bridge void *)p);
    }
    de_plugin_set_dispatcher(de_ios_run_on_main);
}
#define DE_FANOUT(sel) for (DEPlugin *p in g_plugins.allValues) { DE_GUARD(p, sel); }

// Tiklamayi gecirir: yalniz alt view'lara (banner) vurursa yakalar.
@interface DEOverlayView : UIView
@end
@implementation DEOverlayView
- (UIView *)hitTest:(CGPoint)point withEvent:(UIEvent *)event
{
    UIView *v = [super hitTest:point withEvent:event];
    return v == self ? nil : v;
}
@end

// ---------------------------------------------------------------------------
@interface DEMetalView : UIView
@end
@implementation DEMetalView
+ (Class)layerClass { return [CAMetalLayer class]; }
@end

// ---------------------------------------------------------------------------
@interface DEGameViewController : UIViewController
@end

@implementation DEGameViewController
{
    CADisplayLink *_link;
    CFTimeInterval _last;
    BOOL _ready, _quitting, _paused;
    int _fbw, _fbh;
    float _scale;
    NSMutableArray<UITouch *> *_touches; // index = pointer id (Android getPointerId karsiligi)
}

- (void)loadView
{
    DEMetalView *v = [[DEMetalView alloc] initWithFrame:[UIScreen mainScreen].bounds];
    v.backgroundColor = [UIColor blackColor];
    v.multipleTouchEnabled = YES;
    v.layer.contentsScale = [UIScreen mainScreen].nativeScale;
    self.view = v;
}

- (BOOL)prefersStatusBarHidden { return YES; }
- (BOOL)prefersHomeIndicatorAutoHidden { return YES; }
- (UIRectEdge)preferredScreenEdgesDeferringSystemGestures { return UIRectEdgeAll; }

- (void)viewDidLoad
{
    [super viewDidLoad];
    _touches = [NSMutableArray array];
    g_gameVC = self;
    // Plugin overlay'i: Metal yuzeyinin ustunde, tam ekran, safe area'ya saygili (banner centik altina girmesin).
    DEOverlayView *ov = [[DEOverlayView alloc] initWithFrame:self.view.bounds];
    ov.backgroundColor = [UIColor clearColor];
    ov.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:ov];
    UILayoutGuide *g = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [ov.topAnchor constraintEqualToAnchor:g.topAnchor], [ov.bottomAnchor constraintEqualToAnchor:g.bottomAnchor],
        [ov.leadingAnchor constraintEqualToAnchor:g.leadingAnchor], [ov.trailingAnchor constraintEqualToAnchor:g.trailingAnchor]]];
    g_overlay = ov;
    [UIApplication sharedApplication].idleTimerDisabled = YES; // FLAG_KEEP_SCREEN_ON

    NSNotificationCenter *nc = [NSNotificationCenter defaultCenter];
    [nc addObserver:self selector:@selector(onBackground) name:UIApplicationDidEnterBackgroundNotification object:nil];
    [nc addObserver:self selector:@selector(onForeground) name:UIApplicationWillEnterForegroundNotification object:nil];
    [nc addObserver:self selector:@selector(onTerminate) name:UIApplicationWillTerminateNotification object:nil];
    [nc addObserver:self selector:@selector(onResign) name:UIApplicationWillResignActiveNotification object:nil];
    [nc addObserver:self selector:@selector(onActive) name:UIApplicationDidBecomeActiveNotification object:nil];
}

- (void)viewDidLayoutSubviews
{
    [super viewDidLayoutSubviews];
    [self updateSize];
    if (!_ready && _fbw > 0 && _fbh > 0)
    {
        // Metal layer'i shim'e ver (device kurulur) -> runtime init (icinde de_sokol_setup). Tek init: runtime surec omurlu.
        de_metal_init_layer((__bridge void *)self.view.layer);
        NSString *data = [[NSBundle mainBundle] resourcePath];
        NSString *support = NSSearchPathForDirectoriesInDomains(NSApplicationSupportDirectory, NSUserDomainMask, YES).firstObject;
        NSString *writable = [support stringByAppendingPathComponent:[[NSBundle mainBundle] bundleIdentifier]];
        [[NSFileManager defaultManager] createDirectoryAtPath:writable withIntermediateDirectories:YES attributes:nil error:nil];
        fprintf(stderr, "[host] init data=%s writable=%s %dx%d @%.2f\n", data.UTF8String, writable.UTF8String, _fbw, _fbh, _scale);
        de_app_init(data.UTF8String, writable.UTF8String, _fbw, _fbh, _scale);
        _ready = YES;
        _link = [CADisplayLink displayLinkWithTarget:self selector:@selector(onFrame:)];
        [_link addToRunLoop:[NSRunLoop mainRunLoop] forMode:NSRunLoopCommonModes];
    }
    else if (_ready)
    {
        DeEvent e = { DE_EV_RESIZE, 0, 0, 0, _fbw, _fbh };
        de_app_event(&e);
    }
}

- (void)updateSize
{
    CGFloat s = self.view.layer.contentsScale;
    CGSize pt = self.view.bounds.size;
    _scale = (float)s;
    _fbw = (int)(pt.width * s + 0.5);
    _fbh = (int)(pt.height * s + 0.5);
}

- (void)onFrame:(CADisplayLink *)link
{
    if (_quitting || !_ready) return;
    CFTimeInterval now = link.timestamp;
    float dt = _last == 0 ? 0.f : (float)(now - _last);
    _last = now;
    [self updateSize];
    if (de_app_frame(dt, _fbw, _fbh, _scale) == 0)
    {
        // iOS'ta uygulama kendini kapatmaz (HIG); dongu durur, kullanici home'a doner.
        _quitting = YES;
        [_link invalidate];
        _link = nil;
        fprintf(stderr, "[host] oyun cikis istedi; dongu durduruldu\n");
    }
}

// ---- yasam dongusu (GL thread yok: hepsi ana thread) ----
- (void)onBackground { if (_ready && !_paused) { _paused = YES; _link.paused = YES; de_app_pause(); } DE_FANOUT([p didEnterBackground]); }
- (void)onForeground { if (_ready && _paused) { _paused = NO; _last = 0; _link.paused = NO; de_app_resume(); } DE_FANOUT([p willEnterForeground]); }
- (void)onResign { DeEvent e = { DE_EV_FOCUS, 0, 0, 0, 0, 0 }; if (_ready) de_app_event(&e); DE_FANOUT([p willResignActive]); }
- (void)onActive { DeEvent e = { DE_EV_FOCUS, 0, 0, 0, 1, 0 }; if (_ready) de_app_event(&e); DE_FANOUT([p didBecomeActive]); }
- (void)onTerminate { if (_ready) de_app_shutdown(); }
- (void)didReceiveMemoryWarning { [super didReceiveMemoryWarning]; if (_ready) de_app_low_memory(); }

// ---- dokunma: UITouch nesnesi -> kararli kucuk id (bos slot yeniden kullanilir) ----
- (int)idFor:(UITouch *)t assign:(BOOL)assign
{
    NSUInteger i = [_touches indexOfObjectIdenticalTo:t];
    if (i != NSNotFound) return (int)i;
    if (!assign) return -1;
    for (NSUInteger k = 0; k < _touches.count; k++)
        if ((id)_touches[k] == [NSNull null]) { _touches[k] = t; return (int)k; }
    [_touches addObject:t];
    return (int)_touches.count - 1;
}
- (void)freeTouch:(UITouch *)t
{
    NSUInteger i = [_touches indexOfObjectIdenticalTo:t];
    if (i != NSNotFound) _touches[i] = (UITouch *)[NSNull null];
    while (_touches.count && (id)_touches.lastObject == [NSNull null]) [_touches removeLastObject];
}
- (void)send:(int)type touch:(UITouch *)t id:(int)pid
{
    CGPoint p = [t locationInView:self.view]; // point = mantiksal px
    DeEvent e = { type, pid, (float)p.x, (float)p.y, 0, 0 };
    de_app_event(&e);
}
- (void)touchesBegan:(NSSet<UITouch *> *)touches withEvent:(UIEvent *)event
{ for (UITouch *t in touches) [self send:DE_EV_POINTER_DOWN touch:t id:[self idFor:t assign:YES]]; }
- (void)touchesMoved:(NSSet<UITouch *> *)touches withEvent:(UIEvent *)event
{ for (UITouch *t in touches) { int i = [self idFor:t assign:NO]; if (i >= 0) [self send:DE_EV_POINTER_MOVE touch:t id:i]; } }
- (void)touchesEnded:(NSSet<UITouch *> *)touches withEvent:(UIEvent *)event
{ for (UITouch *t in touches) { int i = [self idFor:t assign:NO]; if (i >= 0) { [self send:DE_EV_POINTER_UP touch:t id:i]; [self freeTouch:t]; } } }
- (void)touchesCancelled:(NSSet<UITouch *> *)touches withEvent:(UIEvent *)event
{ for (UITouch *t in touches) { int i = [self idFor:t assign:NO]; if (i >= 0) { [self send:DE_EV_POINTER_CANCEL touch:t id:i]; [self freeTouch:t]; } } }
@end

// ---------------------------------------------------------------------------
@interface DEAppDelegate : UIResponder <UIApplicationDelegate>
@property(strong, nonatomic) UIWindow *window;
@end

@implementation DEAppDelegate
- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions
{
    setvbuf(stdout, 0, _IOLBF, 0); // Xcode konsolu satir satir
    de_ios_plugins_create();
    self.window = [[UIWindow alloc] initWithFrame:[UIScreen mainScreen].bounds];
    self.window.rootViewController = [DEGameViewController new];
    [self.window makeKeyAndVisible];
    DE_FANOUT([p didFinishLaunching:launchOptions]);
    return YES;
}
// Plugin forward'lari (ilk kabul eden kazanir; hepsi @try/@catch).
- (BOOL)application:(UIApplication *)app openURL:(NSURL *)url options:(NSDictionary<UIApplicationOpenURLOptionsKey, id> *)options
{
    for (DEPlugin *p in g_plugins.allValues) { BOOL h = NO; DE_GUARD(p, h = [p openURL:url options:options]); if (h) return YES; }
    return NO;
}
- (BOOL)application:(UIApplication *)app continueUserActivity:(NSUserActivity *)activity restorationHandler:(void (^)(NSArray<id<UIUserActivityRestoring>> *))handler
{
    for (DEPlugin *p in g_plugins.allValues) { BOOL h = NO; DE_GUARD(p, h = [p continueUserActivity:activity]); if (h) return YES; }
    return NO;
}
- (void)application:(UIApplication *)app didRegisterForRemoteNotificationsWithDeviceToken:(NSData *)token { DE_FANOUT([p didRegisterForRemoteNotifications:token]); }
- (void)application:(UIApplication *)app didFailToRegisterForRemoteNotificationsWithError:(NSError *)error { DE_FANOUT([p didFailToRegisterForRemoteNotifications:error]); }
- (void)application:(UIApplication *)app didReceiveRemoteNotification:(NSDictionary *)userInfo fetchCompletionHandler:(void (^)(UIBackgroundFetchResult))handler
{
    DE_FANOUT([p didReceiveRemoteNotification:userInfo]);
    handler(UIBackgroundFetchResultNoData);
}
@end

int main(int argc, char *argv[])
{
    @autoreleasepool { return UIApplicationMain(argc, argv, nil, NSStringFromClass([DEAppDelegate class])); }
}
