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
#include <stdio.h>
#include <string.h>
#include "de_app.h"

void de_metal_init_layer(void *caMetalLayer); // sokol_shim.m (SOKOL_METAL, iOS dali)

void de_host_set_title(const char *utf8) { fprintf(stderr, "[host] title: %s\n", utf8); }
void de_host_log(int level, const char *utf8) { fprintf(level >= 2 ? stderr : stdout, "%s\n", utf8); }

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
- (void)onBackground { if (_ready && !_paused) { _paused = YES; _link.paused = YES; de_app_pause(); } }
- (void)onForeground { if (_ready && _paused) { _paused = NO; _last = 0; _link.paused = NO; de_app_resume(); } }
- (void)onResign { DeEvent e = { DE_EV_FOCUS, 0, 0, 0, 0, 0 }; if (_ready) de_app_event(&e); }
- (void)onActive { DeEvent e = { DE_EV_FOCUS, 0, 0, 0, 1, 0 }; if (_ready) de_app_event(&e); }
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
    self.window = [[UIWindow alloc] initWithFrame:[UIScreen mainScreen].bounds];
    self.window.rootViewController = [DEGameViewController new];
    [self.window makeKeyAndVisible];
    return YES;
}
@end

int main(int argc, char *argv[])
{
    @autoreleasepool { return UIApplicationMain(argc, argv, nil, NSStringFromClass([DEAppDelegate class])); }
}
