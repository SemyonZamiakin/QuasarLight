// Raw bus abstraction. Implementations (SMBus via a signed kernel driver, USB HID, ...)
// live outside the core; the core only ever sees IBus, and always through GuardedBus.
#pragma once
#include <cstdint>
#include <string>
#include <vector>

namespace ql {

enum class BusKind { SMBus, I2C, UsbHid };

struct BusInfo {
    std::string id;    // stable id, e.g. "smbus:piix4:0"
    std::string name;  // human readable
    BusKind kind;
};

class IBus {
public:
    virtual ~IBus() = default;
    virtual BusInfo info() const = 0;
    // Non-destructive "is anything there": SMBus Read Byte at register 0. Must not write.
    virtual bool probe(uint8_t addr) = 0;
    virtual bool readByte(uint8_t addr, uint8_t reg, uint8_t& out) = 0;
    virtual bool writeByte(uint8_t addr, uint8_t reg, uint8_t value) = 0;
    virtual bool writeBlock(uint8_t addr, uint8_t reg, const uint8_t* data, size_t len) = 0;
};

}  // namespace ql
