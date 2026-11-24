#import <GameController/GameController.h>
#include <math.h>
#include <stdint.h>

static BOOL is_color(GCColor *color, float red, float green, float blue) {
    return color && fabsf(color.red - red) <= 1.0f / 255 &&
        fabsf(color.green - green) <= 1.0f / 255 && fabsf(color.blue - blue) <= 1.0f / 255;
}

__attribute__((visibility("default"))) int gunsaw_macos_lightbar(uint8_t red, uint8_t green, uint8_t blue) {
    @autoreleasepool {
        GCDeviceLight *light = nil;
        for (GCController *controller in GCController.controllers) {
            BOOL dualsense = [controller.extendedGamepad isKindOfClass:GCDualSenseGamepad.class] ||
                [controller.physicalInputProfile isKindOfClass:GCDualSenseGamepad.class];
            if (!dualsense || !controller.light) continue;
            if (light) return 0;
            light = controller.light;
        }
        if (!light) return 0;

        float r = red / 255.0f, g = green / 255.0f, b = blue / 255.0f;
        if (!is_color(light.color, r, g, b))
            light.color = [[GCColor alloc] initWithRed:r green:g blue:b];
        return is_color(light.color, r, g, b) ? 1 : -1;
    }
}
