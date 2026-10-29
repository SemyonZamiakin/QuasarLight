#include "ql/openrgb.h"
#include <algorithm>
#include <cstring>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#endif

namespace ql {

namespace {
enum : uint32_t {
    PKT_REQUEST_CONTROLLER_COUNT = 0,
    PKT_REQUEST_CONTROLLER_DATA = 1,
    PKT_REQUEST_PROTOCOL_VERSION = 40,
    PKT_SET_CLIENT_NAME = 50,
    PKT_UPDATELEDS = 1050,
    PKT_UPDATEMODE = 1101,
};
const uint32_t kMaxPacket = 4u * 1024 * 1024;  // refuse absurd sizes from a misbehaving peer
const uint32_t kOurProto = 3;

void put32(std::vector<uint8_t>& v, uint32_t x) {
    for (int i = 0; i < 4; ++i) v.push_back(uint8_t(x >> (8 * i)));
}
void put16(std::vector<uint8_t>& v, uint16_t x) {
    v.push_back(uint8_t(x));
    v.push_back(uint8_t(x >> 8));
}
void putStr(std::vector<uint8_t>& v, const std::string& s) {
    put16(v, uint16_t(s.size() + 1));
    v.insert(v.end(), s.begin(), s.end());
    v.push_back(0);
}
std::string lower(std::string s) {
    for (auto& c : s) c = char(tolower(static_cast<unsigned char>(c)));
    return s;
}
bool has(const std::string& s, const char* k) { return lower(s).find(k) != std::string::npos; }
}  // namespace

// ---------------- ByteReader ----------------
uint32_t ByteReader::u32() {
    if (!ok || n - o < 4) { ok = false; return 0; }
    uint32_t v = uint32_t(p[o]) | uint32_t(p[o + 1]) << 8 | uint32_t(p[o + 2]) << 16 | uint32_t(p[o + 3]) << 24;
    o += 4;
    return v;
}
uint16_t ByteReader::u16() {
    if (!ok || n - o < 2) { ok = false; return 0; }
    uint16_t v = uint16_t(p[o] | p[o + 1] << 8);
    o += 2;
    return v;
}
void ByteReader::skip(size_t k) {
    if (!ok || n - o < k) { ok = false; return; }
    o += k;
}
std::string ByteReader::str() {
    const uint16_t len = u16();
    if (!ok || n - o < len) { ok = false; return ""; }
    std::string s(reinterpret_cast<const char*>(p + o), len);
    o += len;
    while (!s.empty() && s.back() == '\0') s.pop_back();
    return s;
}

// ---------------- device (de)serialisation ----------------
bool parseOrgbDevice(const uint8_t* data, size_t len, OrgbDevice& out, uint32_t proto) {
    ByteReader r(data, len);
    r.u32();  // total size
    out = OrgbDevice();
    out.type = int32_t(r.u32());
    out.name = r.str();
    if (proto >= 1) out.vendor = r.str();
    out.description = r.str();
    out.version = r.str();
    out.serial = r.str();
    out.location = r.str();
    const uint16_t nModes = r.u16();
    out.activeMode = int32_t(r.u32());
    if (!r.ok || nModes > 256) return false;
    for (uint16_t i = 0; i < nModes && r.ok; ++i) {
        OrgbMode m;
        m.name = r.str();
        m.value = int32_t(r.u32());
        m.flags = r.u32();
        m.speedMin = r.u32();
        m.speedMax = r.u32();
        if (proto >= 3) { m.brightMin = r.u32(); m.brightMax = r.u32(); }
        m.colorsMin = r.u32();
        m.colorsMax = r.u32();
        m.speed = r.u32();
        if (proto >= 3) m.brightness = r.u32();
        m.direction = r.u32();
        m.colorMode = r.u32();
        const uint16_t nc = r.u16();
        if (nc > 4096) return false;
        for (uint16_t c = 0; c < nc && r.ok; ++c) {
            const uint32_t v = r.u32();
            m.colors.push_back({uint8_t(v), uint8_t(v >> 8), uint8_t(v >> 16)});
        }
        out.modes.push_back(m);
    }
    const uint16_t nZones = r.u16();
    if (!r.ok || nZones > 1024) return false;
    for (uint16_t z = 0; z < nZones && r.ok; ++z) {
        r.str();   // zone name
        r.u32();   // type
        r.u32(); r.u32(); r.u32();  // leds min / max / count
        const uint16_t matrixLen = r.u16();
        r.skip(matrixLen);
    }
    const uint16_t nLeds = r.u16();
    if (!r.ok || nLeds > 8192) return false;
    for (uint16_t l = 0; l < nLeds && r.ok; ++l) { r.str(); r.u32(); }
    const uint16_t nCol = r.u16();
    r.skip(size_t(nCol) * 4);
    out.ledCount = nLeds ? nLeds : nCol;
    return r.ok;
}

std::vector<uint8_t> serializeOrgbMode(const OrgbMode& m, uint32_t proto) {
    std::vector<uint8_t> v;
    putStr(v, m.name);
    put32(v, uint32_t(m.value));
    put32(v, m.flags);
    put32(v, m.speedMin);
    put32(v, m.speedMax);
    if (proto >= 3) { put32(v, m.brightMin); put32(v, m.brightMax); }
    put32(v, m.colorsMin);
    put32(v, m.colorsMax);
    put32(v, m.speed);
    if (proto >= 3) put32(v, m.brightness);
    put32(v, m.direction);
    put32(v, m.colorMode);
    put16(v, uint16_t(m.colors.size()));
    for (const Rgb& c : m.colors) put32(v, uint32_t(c.r) | uint32_t(c.g) << 8 | uint32_t(c.b) << 16);
    return v;
}

int chooseOrgbMode(const OrgbDevice& d, EffectType t, bool& approx) {
    auto find = [&](std::initializer_list<const char*> keys) {
        for (const char* k : keys)
            for (size_t i = 0; i < d.modes.size(); ++i)
                if (has(d.modes[i].name, k)) return int(i);
        return -1;
    };
    approx = false;
    int idx = -1;
    switch (t) {
        case EffectType::Static: idx = find({"static", "direct"}); break;
        case EffectType::Breathing: idx = find({"breathing", "pulse"}); break;
        case EffectType::SpectrumCycle: idx = find({"color cycle", "spectrum", "rainbow"}); break;
        case EffectType::RainbowWave: idx = find({"rainbow wave", "wave", "rainbow", "color cycle"}); break;
        case EffectType::Comet: idx = find({"comet", "chase", "scan"}); break;
        case EffectType::Sparkle: idx = find({"sparkle", "twinkle", "flash"}); break;
        default: break;
    }
    if (idx >= 0) return idx;
    approx = true;  // no matching hardware effect: show the primary colour statically instead
    return find({"static", "direct"});
}

// ---------------- network client ----------------
#ifdef _WIN32
namespace {
SOCKET S(uintptr_t s) { return static_cast<SOCKET>(s); }
bool sendAll(SOCKET s, const uint8_t* p, size_t n) {
    while (n) {
        const int k = ::send(s, reinterpret_cast<const char*>(p), int(n), 0);
        if (k <= 0) return false;
        p += k; n -= size_t(k);
    }
    return true;
}
bool recvAll(SOCKET s, uint8_t* p, size_t n) {
    while (n) {
        const int k = ::recv(s, reinterpret_cast<char*>(p), int(n), 0);
        if (k <= 0) return false;
        p += k; n -= size_t(k);
    }
    return true;
}
}  // namespace

bool OpenRgbClient::connect(const std::string& host, uint16_t port, int timeoutMs) {
    disconnect();
    WSADATA wsa;
    if (WSAStartup(MAKEWORD(2, 2), &wsa) != 0) return fail("WSAStartup failed");
    addrinfo hints{};
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    addrinfo* res = nullptr;
    if (getaddrinfo(host.c_str(), std::to_string(port).c_str(), &hints, &res) != 0 || !res)
        return fail("cannot resolve " + host);
    SOCKET s = ::socket(res->ai_family, res->ai_socktype, res->ai_protocol);
    if (s == INVALID_SOCKET) { freeaddrinfo(res); return fail("socket() failed"); }
    DWORD to = DWORD(timeoutMs);
    setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, reinterpret_cast<const char*>(&to), sizeof to);
    setsockopt(s, SOL_SOCKET, SO_SNDTIMEO, reinterpret_cast<const char*>(&to), sizeof to);
    if (::connect(s, res->ai_addr, int(res->ai_addrlen)) != 0) {
        freeaddrinfo(res); closesocket(s);
        return fail("OpenRGB server not reachable at " + host + ":" + std::to_string(port));
    }
    freeaddrinfo(res);
    sock_ = uintptr_t(s);

    // 1. protocol negotiation
    std::vector<uint8_t> pl, in;
    put32(pl, kOurProto);
    if (!send(0, PKT_REQUEST_PROTOCOL_VERSION, pl) || !recv(PKT_REQUEST_PROTOCOL_VERSION, in) || in.size() < 4) {
        proto_ = 0;  // very old server: no negotiation
        disconnect();
        return fail("OpenRGB handshake failed");
    }
    ByteReader vr(in.data(), in.size());
    proto_ = std::min(vr.u32(), kOurProto);
    if (proto_ < 3) { disconnect(); return fail("OpenRGB server protocol too old (need >= 3)"); }

    // 2. name
    std::vector<uint8_t> name;
    const char* nm = "Quasar Light";
    name.assign(nm, nm + strlen(nm) + 1);
    send(0, PKT_SET_CLIENT_NAME, name);

    // 3. devices
    if (!send(0, PKT_REQUEST_CONTROLLER_COUNT, {}) || !recv(PKT_REQUEST_CONTROLLER_COUNT, in) || in.size() < 4) {
        disconnect();
        return fail("cannot read device count");
    }
    ByteReader cr(in.data(), in.size());
    const uint32_t count = cr.u32();
    if (count > 256) { disconnect(); return fail("implausible device count"); }
    for (uint32_t i = 0; i < count; ++i) {
        std::vector<uint8_t> req;
        put32(req, proto_);
        OrgbDevice d;
        if (!send(i, PKT_REQUEST_CONTROLLER_DATA, req) || !recv(PKT_REQUEST_CONTROLLER_DATA, in) ||
            !parseOrgbDevice(in.data(), in.size(), d, proto_)) {
            disconnect();
            return fail("cannot parse device " + std::to_string(i));
        }
        devs_.push_back(d);
    }
    savedActive_.assign(devs_.size(), -1);
    savedMode_.assign(devs_.size(), OrgbMode());
    err_.clear();
    return true;
}

void OpenRgbClient::disconnect() {
    if (connected()) { closesocket(S(sock_)); WSACleanup(); }
    sock_ = ~uintptr_t(0);
    devs_.clear();
    savedActive_.clear();
    savedMode_.clear();
}

bool OpenRgbClient::send(uint32_t dev, uint32_t id, const std::vector<uint8_t>& payload) {
    if (!connected()) return fail("not connected");
    std::vector<uint8_t> pkt = {'O', 'R', 'G', 'B'};
    put32(pkt, dev);
    put32(pkt, id);
    put32(pkt, uint32_t(payload.size()));
    pkt.insert(pkt.end(), payload.begin(), payload.end());
    return sendAll(S(sock_), pkt.data(), pkt.size()) ? true : fail("send failed");
}

bool OpenRgbClient::recv(uint32_t wantId, std::vector<uint8_t>& payload) {
    for (int guard = 0; guard < 16; ++guard) {  // skip unsolicited packets (e.g. device list updated)
        uint8_t h[16];
        if (!recvAll(S(sock_), h, sizeof h)) return fail("receive failed / timeout");
        if (memcmp(h, "ORGB", 4) != 0) return fail("bad packet magic");
        ByteReader r(h + 4, 12);
        r.u32();
        const uint32_t id = r.u32(), size = r.u32();
        if (size > kMaxPacket) return fail("packet too large");
        payload.assign(size, 0);
        if (size && !recvAll(S(sock_), payload.data(), size)) return fail("receive failed / timeout");
        if (id == wantId) return true;
    }
    return fail("no matching reply");
}

bool OpenRgbClient::setMode(size_t dev, int modeIdx, const OrgbMode& m) {
    std::vector<uint8_t> body = serializeOrgbMode(m, proto_), pl;
    put32(pl, uint32_t(4 + 4 + body.size()));
    put32(pl, uint32_t(modeIdx));
    pl.insert(pl.end(), body.begin(), body.end());
    return send(uint32_t(dev), PKT_UPDATEMODE, pl);
}

int OpenRgbClient::apply(size_t dev, const EffectParams& p, bool* approx) {
    if (!connected() || dev >= devs_.size()) { fail("bad device"); return -1; }
    bool ap = false;
    const OrgbDevice& d = devs_[dev];
    const int idx = chooseOrgbMode(d, p.type, ap);
    if (approx) *approx = ap;
    if (idx < 0) { fail("device has no usable mode"); return -1; }

    if (savedActive_[dev] < 0) {  // remember the original state once, for restore()
        savedActive_[dev] = d.activeMode;
        savedMode_[dev] = d.modes[size_t(std::max(0, std::min(d.activeMode, int(d.modes.size()) - 1)))];
    }
    OrgbMode m = d.modes[size_t(idx)];
    auto scaleTo = [](float f, uint32_t lo, uint32_t hi) {
        f = std::min(1.f, std::max(0.f, f));
        return hi >= lo ? lo + uint32_t(f * float(hi - lo) + 0.5f) : lo;
    };
    if (m.flags & 1u) m.speed = scaleTo(p.speed, std::min(m.speedMin, m.speedMax), std::max(m.speedMin, m.speedMax));
    if (m.brightMax > 0) m.brightness = scaleTo(p.brightness, m.brightMin, m.brightMax);

    auto dim = [&](Rgb c) {
        const float k = m.brightMax > 0 ? 1.f : std::min(1.f, std::max(0.f, p.brightness));  // fake dimming if no HW brightness
        return Rgb{uint8_t(c.r * k + .5f), uint8_t(c.g * k + .5f), uint8_t(c.b * k + .5f)};
    };
    if (m.colorMode == 2) {  // mode-specific colours
        const size_t want = std::max<size_t>(1, std::min<size_t>(m.colorsMax ? m.colorsMax : 2, 2));
        m.colors.clear();
        m.colors.push_back(dim(p.color1));
        if (want > 1) m.colors.push_back(dim(p.color2));
        if (m.colors.size() < m.colorsMin) m.colors.resize(m.colorsMin, dim(p.color1));
    }
    if (!setMode(dev, idx, m)) return -1;
    if (m.colorMode == 1 && d.ledCount > 0) {  // per-LED mode: push a solid frame
        std::vector<uint8_t> pl;
        put32(pl, uint32_t(4 + 2 + 4 * d.ledCount));
        put16(pl, uint16_t(d.ledCount));
        const Rgb c = dim(p.color1);
        for (uint32_t i = 0; i < d.ledCount; ++i) put32(pl, uint32_t(c.r) | uint32_t(c.g) << 8 | uint32_t(c.b) << 16);
        if (!send(uint32_t(dev), PKT_UPDATELEDS, pl)) return -1;
    }
    return idx;
}

bool OpenRgbClient::restore(size_t dev) {
    if (!connected() || dev >= devs_.size() || savedActive_[dev] < 0) return false;
    const bool ok = setMode(dev, savedActive_[dev], savedMode_[dev]);
    if (ok) savedActive_[dev] = -1;
    return ok;
}

#else
bool OpenRgbClient::connect(const std::string&, uint16_t, int) { return fail("unsupported platform"); }
void OpenRgbClient::disconnect() {}
bool OpenRgbClient::send(uint32_t, uint32_t, const std::vector<uint8_t>&) { return false; }
bool OpenRgbClient::recv(uint32_t, std::vector<uint8_t>&) { return false; }
bool OpenRgbClient::setMode(size_t, int, const OrgbMode&) { return false; }
int OpenRgbClient::apply(size_t, const EffectParams&, bool*) { return -1; }
bool OpenRgbClient::restore(size_t) { return false; }
#endif

}  // namespace ql
