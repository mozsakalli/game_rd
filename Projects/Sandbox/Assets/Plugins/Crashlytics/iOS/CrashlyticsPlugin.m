// Crashlytics host plugin'i — iOS (docs/plugins.md). FirebaseApp configure + core plugin hatalarini non-fatal kaydet.
// GoogleService-Info.plist bundle'da olmali (Xcode'a kullanici ekler). Unhandled managed exception = NSException (host).
#import "DEPlugin.h"
@import FirebaseCore;
@import FirebaseCrashlytics;

@interface CrashlyticsPlugin : DEPlugin
@end

@implementation CrashlyticsPlugin
- (instancetype)init { return [super initWithName:@"Crashlytics"]; }

- (void)didFinishLaunching:(NSDictionary *)launchOptions
{
    if (![FIRApp defaultApp]) [FIRApp configure];
    DEPluginHost.errorReporter = ^(NSException *e) {
        NSError *err = [NSError errorWithDomain:@"DigitoyPlugin" code:1 userInfo:@{ NSLocalizedDescriptionKey : [NSString stringWithFormat:@"%@: %@", e.name, e.reason ?: @""] }];
        [[FIRCrashlytics crashlytics] recordError:err];
    };
}

- (void)onMessage:(NSString *)msg
{
    NSDictionary *j = [NSJSONSerialization JSONObjectWithData:[msg dataUsingEncoding:NSUTF8StringEncoding] options:0 error:nil];
    NSString *m = j[@"method"];
    if ([m isEqualToString:@"log"]) [[FIRCrashlytics crashlytics] log:j[@"message"] ?: @""];
    else if ([m isEqualToString:@"userId"]) [[FIRCrashlytics crashlytics] setUserID:j[@"id"] ?: @""];
}
@end

DE_REGISTER_PLUGIN(CrashlyticsPlugin)
