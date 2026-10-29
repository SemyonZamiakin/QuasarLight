#include "ql/effects.h"
#include <algorithm>
#include <cmath>

namespace ql {
namespace {
const double kPi = 3.14159265358979323846;

float clamp01(float v) { return std::min(1.f, std::max(0.f, v)); }

Rgb hsv(float h, float s, float v) {
    h = h - std::floor(h);
    const float c = v * s, x = c * (1 - std::fabs(std::fmod(h * 6, 2.0f) - 1)), m = v - c;
    float r = 0, g = 0, b = 0;
    switch (int(h * 6) % 6) {
        case 0: r = c; g = x; break;
        case 1: r = x; g = c; break;
        case 2: g = c; b = x; break;
        case 3: g = x; b = c; break;
        case 4: r = x; b = c; break;
        default: r = c; b = x; break;
    }
    auto q = [&](float f) { return uint8_t(clamp01(f + m) * 255.f + 0.5f); };
    return {q(r), q(g), q(b)};
}

Rgb scale(Rgb c, float k) {
    k = clamp01(k);
    return {uint8_t(c.r * k + 0.5f), uint8_t(c.g * k + 0.5f), uint8_t(c.b * k + 0.5f)};
}
Rgb mix(Rgb a, Rgb b, float t) {
    return {uint8_t(a.r + (b.r - a.r) * t + 0.5f), uint8_t(a.g + (b.g - a.g) * t + 0.5f),
            uint8_t(a.b + (b.b - a.b) * t + 0.5f)};
}
uint32_t hash32(uint32_t x) {
    x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
    return x;
}
}  // namespace

const char* effectName(EffectType t) {
    switch (t) {
        case EffectType::Static: return "Static";
        case EffectType::Breathing: return "Breathing";
        case EffectType::RainbowWave: return "Rainbow Wave";
        case EffectType::SpectrumCycle: return "Spectrum Cycle";
        case EffectType::Comet: return "Comet";
        case EffectType::Sparkle: return "Sparkle";
        default: return "?";
    }
}

void renderEffect(const EffectParams& p, double t, Rgb* out, size_t n) {
    if (!out || n == 0) return;
    const double spd = 0.1 + 1.9 * clamp01(p.speed);
    const float bri = clamp01(p.brightness);
    for (size_t i = 0; i < n; ++i) {
        const float pos = n > 1 ? float(p.reverse ? n - 1 - i : i) / float(n - 1) : 0.f;
        Rgb c{0, 0, 0};
        switch (p.type) {
            case EffectType::Static: c = p.color1; break;
            case EffectType::Breathing: {
                const float k = 0.5f + 0.5f * float(std::sin(2 * kPi * spd * 0.5 * t - kPi / 2));
                c = scale(mix(p.color1, p.color2, k), 0.12f + 0.88f * k);
                break;
            }
            case EffectType::RainbowWave: c = hsv(float(pos - spd * 0.25 * t), 1.f, 1.f); break;
            case EffectType::SpectrumCycle: c = hsv(float(spd * 0.15 * t), 1.f, 1.f); break;
            case EffectType::Comet: {
                const float head = float(std::fmod(spd * 0.6 * t, 1.0));
                float d = head - pos;
                if (d < 0) d += 1.f;
                c = scale(mix(p.color1, p.color2, clamp01(d * 2)), std::exp(-d * 6.f));
                break;
            }
            case EffectType::Sparkle: {
                const double rate = 2 + 10 * spd;
                const uint32_t slot = uint32_t(t * rate);
                const uint32_t h = hash32(slot * 2654435761u + uint32_t(i) * 97u);
                const float k = (h & 0xFF) / 255.f;
                const float phase = float(t * rate - std::floor(t * rate));
                c = k > 0.7f ? scale(mix(p.color1, p.color2, ((h >> 8) & 0xFF) / 255.f), 1.f - phase)
                             : scale(p.color1, 0.08f);
                break;
            }
            default: break;
        }
        out[i] = scale(c, bri);
    }
}

}  // namespace ql
