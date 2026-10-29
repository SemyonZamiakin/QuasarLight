#include "ql/guard.h"
#include <chrono>
#include <thread>

namespace ql {

bool isProtectedAddress(uint8_t a) {
    // 0x50-0x5F: DIMM SPD EEPROM / SPD5 hub / PMIC, 0x30-0x37: SPD write-protect control,
    // 0x18-0x1F: DIMM temperature sensors. Writes here can permanently damage RAM.
    return (a >= 0x30 && a <= 0x37) || (a >= 0x50 && a <= 0x5F) || (a >= 0x18 && a <= 0x1F);
}

bool isReservedAddress(uint8_t a) { return a <= 0x07 || a >= 0x78; }

bool GuardedBus::allow(AuditEntry::Op op, uint8_t addr, uint8_t reg, bool ok, const char* why) {
    audit_.push_back({op, addr, reg, ok, ok ? "" : why});
    if (audit_.size() > 4096) audit_.erase(audit_.begin(), audit_.begin() + 1024);
    return ok;
}

bool GuardedBus::probe(uint8_t addr) {
    if (isReservedAddress(addr)) return allow(AuditEntry::Probe, addr, 0, false, "reserved address");
    allow(AuditEntry::Probe, addr, 0, true, "");
    if (gap_) std::this_thread::sleep_for(std::chrono::microseconds(gap_));
    return bus_.probe(addr);
}

bool GuardedBus::readByte(uint8_t addr, uint8_t reg, uint8_t& out) {
    if (isReservedAddress(addr)) return allow(AuditEntry::Read, addr, reg, false, "reserved address");
    allow(AuditEntry::Read, addr, reg, true, "");
    if (gap_) std::this_thread::sleep_for(std::chrono::microseconds(gap_));
    return bus_.readByte(addr, reg, out);
}

bool GuardedBus::grantWrite(uint8_t addr) {
    if (isReservedAddress(addr) || isProtectedAddress(addr)) return false;
    granted_.insert(addr);
    return true;
}

bool GuardedBus::writeByte(uint8_t addr, uint8_t reg, uint8_t value) {
    if (isProtectedAddress(addr)) return allow(AuditEntry::Write, addr, reg, false, "protected SPD/PMIC range");
    if (!granted_.count(addr)) return allow(AuditEntry::Write, addr, reg, false, "no write grant for address");
    allow(AuditEntry::Write, addr, reg, true, "");
    if (gap_) std::this_thread::sleep_for(std::chrono::microseconds(gap_));
    return bus_.writeByte(addr, reg, value);
}

bool GuardedBus::writeBlock(uint8_t addr, uint8_t reg, const uint8_t* data, size_t len) {
    if (isProtectedAddress(addr)) return allow(AuditEntry::Write, addr, reg, false, "protected SPD/PMIC range");
    if (!granted_.count(addr)) return allow(AuditEntry::Write, addr, reg, false, "no write grant for address");
    if (!data || len == 0 || len > 32) return allow(AuditEntry::Write, addr, reg, false, "bad block length");
    allow(AuditEntry::Write, addr, reg, true, "");
    if (gap_) std::this_thread::sleep_for(std::chrono::microseconds(gap_));
    return bus_.writeBlock(addr, reg, data, len);
}

std::vector<ProbeHit> scanBus(GuardedBus& bus) {
    std::vector<ProbeHit> hits;
    const std::string id = bus.info().id;
    for (int a = 0x08; a <= 0x77; ++a)
        if (bus.probe(uint8_t(a))) hits.push_back({id, uint8_t(a), isProtectedAddress(uint8_t(a))});
    return hits;
}

}  // namespace ql
