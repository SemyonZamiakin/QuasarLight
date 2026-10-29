// Manual tool: connects to a running OpenRGB SDK server, dumps devices/modes, and optionally
// applies a colour to one device and restores it afterwards.
//   orgb_probe                      -> list only (read-only)
//   orgb_probe apply <dev> <RRGGBB> -> set Static colour for 3 s, then restore original mode
#include "ql/openrgb.h"
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <thread>

int main(int argc, char** argv) {
    ql::OpenRgbClient c;
    if (!c.connect("127.0.0.1", 6742)) { printf("connect failed: %s\n", c.lastError().c_str()); return 1; }
    printf("connected, protocol %u, %zu devices\n", c.protocol(), c.devices().size());
    for (size_t i = 0; i < c.devices().size(); ++i) {
        const auto& d = c.devices()[i];
        printf("[%zu] type=%d  %s | %s | leds=%u | active=%d\n", i, d.type, d.name.c_str(), d.vendor.c_str(), d.ledCount, d.activeMode);
        for (size_t m = 0; m < d.modes.size(); ++m)
            printf("      mode %zu: %-18s flags=0x%x colorMode=%u colors=%zu speed=%u bri=%u\n", m, d.modes[m].name.c_str(),
                   d.modes[m].flags, d.modes[m].colorMode, d.modes[m].colors.size(), d.modes[m].speed, d.modes[m].brightness);
    }
    if (argc >= 4 && std::string(argv[1]) == "apply") {
        const size_t dev = size_t(atoi(argv[2]));
        const unsigned rgb = unsigned(strtoul(argv[3], nullptr, 16));
        ql::EffectParams p;
        p.type = ql::EffectType::Static;
        p.color1 = {uint8_t(rgb >> 16), uint8_t(rgb >> 8), uint8_t(rgb)};
        bool approx = false;
        const int mode = c.apply(dev, p, &approx);
        printf("apply -> mode %d%s  %s\n", mode, approx ? " (approx)" : "", mode < 0 ? c.lastError().c_str() : "");
        std::this_thread::sleep_for(std::chrono::seconds(3));
        printf("restore -> %s\n", c.restore(dev) ? "ok" : "failed");
    }
    return 0;
}
