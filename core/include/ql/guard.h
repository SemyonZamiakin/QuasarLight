// GuardedBus: the only way plugins touch hardware. Enforces the safety policy
// (see docs/SAFE_SCANNING.md): SPD/EEPROM ranges are read-only, writes need an
// explicit grant for one address after a driver identified its device, and
// every access is rate limited and audited.
#pragma once
#include "bus.h"
#include <functional>
#include <set>
#include <string>
#include <vector>

namespace ql {

struct AuditEntry {
    enum Op { Probe, Read, Write } op;
    uint8_t addr, reg;
    bool allowed;
    std::string reason;  // why it was denied (empty if allowed)
};

// True for addresses that may hold DIMM SPD / PMIC data. Writes there can brick RAM.
bool isProtectedAddress(uint8_t addr);
// True for addresses a scan must never touch (I2C reserved ranges).
bool isReservedAddress(uint8_t addr);

class GuardedBus {
public:
    explicit GuardedBus(IBus& bus, uint32_t minGapMicros = 0) : bus_(bus), gap_(minGapMicros) {}

    const BusInfo info() const { return bus_.info(); }
    bool probe(uint8_t addr);
    bool readByte(uint8_t addr, uint8_t reg, uint8_t& out);
    // Writes are denied unless grantWrite(addr) was called AND addr is not protected.
    bool writeByte(uint8_t addr, uint8_t reg, uint8_t value);
    bool writeBlock(uint8_t addr, uint8_t reg, const uint8_t* data, size_t len);

    // Called by a driver only after it positively identified its device at `addr`.
    // Refuses (returns false) for protected or reserved addresses.
    bool grantWrite(uint8_t addr);
    void revokeAll() { granted_.clear(); }

    const std::vector<AuditEntry>& audit() const { return audit_; }
    void clearAudit() { audit_.clear(); }

private:
    bool allow(AuditEntry::Op op, uint8_t addr, uint8_t reg, bool ok, const char* why);
    IBus& bus_;
    uint32_t gap_;
    std::set<uint8_t> granted_;
    std::vector<AuditEntry> audit_;
};

struct ProbeHit {
    std::string busId;
    uint8_t addr;
    bool protectedRange;  // SPD/PMIC range: detected, but will never be written
};

// Read-only enumeration of one bus. Skips reserved addresses; uses only probe().
std::vector<ProbeHit> scanBus(GuardedBus& bus);

}  // namespace ql
