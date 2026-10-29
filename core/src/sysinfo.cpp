#include "ql/sysinfo.h"
#include <cstdio>
#include <cstring>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif

namespace ql {
namespace {

std::string smbStr(const uint8_t* s, const uint8_t* end, uint8_t idx) {
    if (idx == 0) return "";
    for (uint8_t i = 1; s < end && *s; ++i) {
        const char* p = reinterpret_cast<const char*>(s);
        const size_t l = strnlen(p, size_t(end - s));
        if (i == idx) {
            std::string r(p, l);
            while (!r.empty() && r.back() == ' ') r.pop_back();
            return r;
        }
        s += l + 1;
    }
    return "";
}

const char* memType(uint8_t t) {
    switch (t) {
        case 0x12: return "DDR";   case 0x13: return "DDR2";  case 0x18: return "DDR3";
        case 0x1A: return "DDR4";  case 0x1B: return "LPDDR"; case 0x1C: return "LPDDR2";
        case 0x1D: return "LPDDR3"; case 0x1E: return "LPDDR4"; case 0x22: return "DDR5";
        case 0x23: return "LPDDR5"; default: return "RAM";
    }
}
}  // namespace

void parseSmbios(const uint8_t* t, size_t len, SystemInfo& out) {
    const uint8_t* end = t + len;
    while (t + 4 <= end) {
        const uint8_t type = t[0], flen = t[1];
        if (flen < 4 || t + flen > end) return;
        const uint8_t* str = t + flen;
        const uint8_t* next = str;
        while (next + 1 < end && !(next[0] == 0 && next[1] == 0)) ++next;
        next += 2;
        if (type == 127) return;

        if (type == 2 && flen >= 8) {  // Baseboard
            out.boardVendor = smbStr(str, end, t[4]);
            out.boardModel = smbStr(str, end, t[5]);
        } else if (type == 17 && flen >= 0x15) {  // Memory Device
            const uint16_t size = uint16_t(t[0x0C] | t[0x0D] << 8);
            if (size != 0 && size != 0xFFFF) {
                RamModule m;
                if (size == 0x7FFF && flen >= 0x20)
                    m.capacityBytes = uint64_t(uint32_t(t[0x1C] | t[0x1D] << 8 | t[0x1E] << 16 | t[0x1F] << 24)) * 1048576ull;
                else
                    m.capacityBytes = (size & 0x8000) ? uint64_t(size & 0x7FFF) * 1024ull : uint64_t(size) * 1048576ull;
                m.slot = smbStr(str, end, t[0x10]);
                m.type = memType(t[0x12]);
                const uint16_t cfg = flen >= 0x22 ? uint16_t(t[0x20] | t[0x21] << 8) : 0;
                const uint16_t spd = uint16_t(t[0x15] | t[0x16] << 8);
                m.speedMTs = cfg ? cfg : spd;
                if (flen >= 0x1B) {
                    m.manufacturer = smbStr(str, end, t[0x17]);
                    m.partNumber = smbStr(str, end, t[0x1A]);
                }
                out.ram.push_back(m);
            }
        }
        t = next;
    }
}

#ifdef _WIN32
namespace {
std::string regStr(HKEY k, const char* name) {
    char buf[512];
    DWORD sz = sizeof buf - 1, type = 0;
    if (RegQueryValueExA(k, name, nullptr, &type, reinterpret_cast<BYTE*>(buf), &sz) != ERROR_SUCCESS ||
        (type != REG_SZ && type != REG_EXPAND_SZ))
        return "";
    buf[sz < sizeof buf ? sz : sizeof buf - 1] = 0;
    return buf;
}

void detectGpus(SystemInfo& si) {
    const char* base = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}";
    for (int i = 0; i < 16; ++i) {
        char path[256];
        snprintf(path, sizeof path, "%s\\%04d", base, i);
        HKEY k;
        if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, path, 0, KEY_READ, &k) != ERROR_SUCCESS) continue;
        GpuInfo g;
        g.name = regStr(k, "DriverDesc");
        g.driverVersion = regStr(k, "DriverVersion");
        const std::string id = regStr(k, "MatchingDeviceId");  // PCI\VEN_10DE&DEV_1B06&...
        unsigned ven = 0, dev = 0;
        const size_t v = id.find("VEN_"), d = id.find("DEV_");
        if (v != std::string::npos) sscanf(id.c_str() + v + 4, "%x", &ven);
        if (d != std::string::npos) sscanf(id.c_str() + d + 4, "%x", &dev);
        g.vendorId = uint16_t(ven);
        g.deviceId = uint16_t(dev);
        g.vendor = ven == 0x10DE ? "NVIDIA" : ven == 0x1002 ? "AMD" : ven == 0x8086 ? "Intel" : "Unknown";
        if (ven == 0) {  // some drivers (NVIDIA) leave MatchingDeviceId empty: fall back to the name
            const std::string& n = g.name;
            auto has = [&](const char* s) { return n.find(s) != std::string::npos; };
            if (has("NVIDIA") || has("GeForce") || has("Quadro")) { g.vendor = "NVIDIA"; g.vendorId = 0x10DE; }
            else if (has("AMD") || has("Radeon")) { g.vendor = "AMD"; g.vendorId = 0x1002; }
            else if (has("Intel")) { g.vendor = "Intel"; g.vendorId = 0x8086; }
        }
        uint64_t mem = 0;
        DWORD sz = sizeof mem, type = 0;
        if (RegQueryValueExA(k, "HardwareInformation.qwMemorySize", nullptr, &type, reinterpret_cast<BYTE*>(&mem),
                             &sz) == ERROR_SUCCESS && type == REG_QWORD)
            g.vramBytes = mem;  // real 64-bit size; WMI AdapterRAM caps at 4 GB
        RegCloseKey(k);
        if (!g.name.empty() && ven != 0x1414) si.gpus.push_back(g);  // 0x1414 = Microsoft basic/remote display
    }
}

void detectSmbios(SystemInfo& si) {
    const DWORD sig = 0x52534D42;  // 'RSMB'
    const UINT n = GetSystemFirmwareTable(sig, 0, nullptr, 0);
    if (n < 8) return;
    std::vector<uint8_t> buf(n);
    if (GetSystemFirmwareTable(sig, 0, buf.data(), n) != n) return;
    // RawSMBIOSData: used20, major, minor, dmiRev, Length(DWORD), table...
    const uint32_t tl = uint32_t(buf[4] | buf[5] << 8 | buf[6] << 16 | buf[7] << 24);
    if (8 + size_t(tl) > buf.size()) return;
    parseSmbios(buf.data() + 8, tl, si);
}
}  // namespace
#endif

SystemInfo detectSystem() {
    SystemInfo si;
#ifdef _WIN32
    detectGpus(si);
    detectSmbios(si);
#endif
    return si;
}

}  // namespace ql
