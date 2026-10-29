#include "ql/qlcore.h"
#include "ql/effects.h"
#include "ql/sysinfo.h"
#include <cstdio>
#include <string>
#include <vector>

namespace {

void esc(std::string& o, const std::string& s) {
    o += '"';
    for (unsigned char c : s) {
        if (c == '"' || c == '\\') { o += '\\'; o += char(c); }
        else if (c < 0x20) { char b[8]; snprintf(b, sizeof b, "\\u%04x", c); o += b; }
        else o += char(c);
    }
    o += '"';
}

std::string toJson(const ql::SystemInfo& si) {
    std::string o = "{\"board\":";
    esc(o, (si.boardVendor + " " + si.boardModel));
    o += ",\"gpus\":[";
    for (size_t i = 0; i < si.gpus.size(); ++i) {
        const auto& g = si.gpus[i];
        if (i) o += ',';
        o += "{\"name\":"; esc(o, g.name);
        o += ",\"vendor\":"; esc(o, g.vendor);
        o += ",\"driver\":"; esc(o, g.driverVersion);
        o += ",\"vendorId\":" + std::to_string(g.vendorId) + ",\"deviceId\":" + std::to_string(g.deviceId);
        o += ",\"vramBytes\":" + std::to_string(g.vramBytes) + "}";
    }
    o += "],\"ram\":[";
    for (size_t i = 0; i < si.ram.size(); ++i) {
        const auto& m = si.ram[i];
        if (i) o += ',';
        o += "{\"slot\":"; esc(o, m.slot);
        o += ",\"manufacturer\":"; esc(o, m.manufacturer);
        o += ",\"part\":"; esc(o, m.partNumber);
        o += ",\"type\":"; esc(o, m.type);
        o += ",\"capacityBytes\":" + std::to_string(m.capacityBytes) + ",\"speed\":" + std::to_string(m.speedMTs) + "}";
    }
    o += "]}";
    return o;
}

ql::Rgb unpack(uint32_t c) { return {uint8_t(c >> 16), uint8_t(c >> 8), uint8_t(c)}; }

}  // namespace

extern "C" {

int ql_abi_version(void) { return 1; }

const char* ql_system_json(void) {
    static std::string cache;
    cache = toJson(ql::detectSystem());
    return cache.c_str();
}

int ql_effect_count(void) { return int(ql::EffectType::Count); }
const char* ql_effect_name(int t) { return ql::effectName(ql::EffectType(t)); }

void ql_render(int type, float speed, float brightness, uint32_t c1, uint32_t c2, int reverse, double t,
               uint8_t* rgb, int n) {
    if (!rgb || n <= 0 || n > 4096 || type < 0 || type >= int(ql::EffectType::Count)) return;
    ql::EffectParams p;
    p.type = ql::EffectType(type);
    p.speed = speed;
    p.brightness = brightness;
    p.color1 = unpack(c1);
    p.color2 = unpack(c2);
    p.reverse = reverse != 0;
    std::vector<ql::Rgb> px(static_cast<size_t>(n));
    ql::renderEffect(p, t, px.data(), px.size());
    for (int i = 0; i < n; ++i) {
        rgb[3 * i] = px[size_t(i)].r;
        rgb[3 * i + 1] = px[size_t(i)].g;
        rgb[3 * i + 2] = px[size_t(i)].b;
    }
}

}  // extern "C"
