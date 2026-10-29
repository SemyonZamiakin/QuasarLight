// Client for the OpenRGB SDK server (TCP 6742). Quasar Light talks to OpenRGB over the network only,
// so OpenRGB's GPL code is never linked into this MIT project.
// Wire format implemented: SDK protocol version 3 (negotiated down from newer servers).
#pragma once
#include "effects.h"
#include <cstdint>
#include <string>
#include <vector>

namespace ql {

struct OrgbMode {
    std::string name;
    int32_t value = 0;
    uint32_t flags = 0, speedMin = 0, speedMax = 0, brightMin = 0, brightMax = 0;
    uint32_t colorsMin = 0, colorsMax = 0, speed = 0, brightness = 0, direction = 0, colorMode = 0;
    std::vector<Rgb> colors;
};

struct OrgbDevice {
    int32_t type = 0;  // 0 motherboard, 1 DRAM, 2 GPU, 3 cooler, 4 strip, 5 keyboard, 6 mouse, ...
    std::string name, vendor, description, version, serial, location;
    int32_t activeMode = 0;
    std::vector<OrgbMode> modes;
    uint32_t ledCount = 0;
};

// Bounds-checked little-endian reader. Any overrun flips ok=false instead of reading out of range.
struct ByteReader {
    const uint8_t* p;
    size_t n, o = 0;
    bool ok = true;
    ByteReader(const uint8_t* d, size_t len) : p(d), n(len) {}
    uint32_t u32();
    uint16_t u16();
    std::string str();  // u16 length (including NUL) + bytes
    void skip(size_t k);
};

// Pure parser, exposed for tests. Payload = what follows the REQUEST_CONTROLLER_DATA header.
bool parseOrgbDevice(const uint8_t* data, size_t len, OrgbDevice& out, uint32_t proto = 3);
std::vector<uint8_t> serializeOrgbMode(const OrgbMode& m, uint32_t proto = 3);
// Picks the hardware mode that best matches an effect; -1 if none. approx=true when it is only a stand-in.
int chooseOrgbMode(const OrgbDevice& d, EffectType t, bool& approx);

class OpenRgbClient {
public:
    OpenRgbClient() = default;
    ~OpenRgbClient() { disconnect(); }
    OpenRgbClient(const OpenRgbClient&) = delete;
    OpenRgbClient& operator=(const OpenRgbClient&) = delete;

    bool connect(const std::string& host, uint16_t port, int timeoutMs = 3000);
    void disconnect();
    bool connected() const { return sock_ != ~uintptr_t(0); }
    const std::vector<OrgbDevice>& devices() const { return devs_; }
    const std::string& lastError() const { return err_; }
    uint32_t protocol() const { return proto_; }

    // Sets the hardware mode closest to `p.type` with the given colours. Returns the mode index or -1.
    int apply(size_t dev, const EffectParams& p, bool* approx = nullptr);
    // Puts the device back into the mode/colours it had when we first touched it.
    bool restore(size_t dev);

private:
    bool send(uint32_t dev, uint32_t id, const std::vector<uint8_t>& payload);
    bool recv(uint32_t wantId, std::vector<uint8_t>& payload);
    bool fail(const std::string& m) { err_ = m; return false; }
    bool setMode(size_t dev, int modeIdx, const OrgbMode& m);

    uintptr_t sock_ = ~uintptr_t(0);
    uint32_t proto_ = 0;
    std::vector<OrgbDevice> devs_;
    std::vector<int> savedActive_;           // per device, -1 = untouched
    std::vector<OrgbMode> savedMode_;
    std::string err_;
};

}  // namespace ql
