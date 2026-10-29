#pragma once
#include "types.h"
#include <cstddef>
#include <cstdint>

namespace ql {

enum class EffectType : int { Static = 0, Breathing, RainbowWave, SpectrumCycle, Comet, Sparkle, Count };

struct EffectParams {
    EffectType type = EffectType::RainbowWave;
    float speed = 0.5f;       // 0..1
    float brightness = 1.0f;  // 0..1
    Rgb color1{124, 92, 255};
    Rgb color2{60, 220, 230};
    bool reverse = false;
};

// Pure function of time: same inputs -> same frame. n LEDs written to out.
void renderEffect(const EffectParams& p, double tSeconds, Rgb* out, size_t n);
const char* effectName(EffectType t);

}  // namespace ql
