#pragma once
#include <cstdint>
#include <string>
#include <vector>

namespace ql {

struct GpuInfo {
    std::string name;
    std::string vendor;  // NVIDIA / AMD / Intel / Unknown
    uint16_t vendorId = 0, deviceId = 0;
    uint64_t vramBytes = 0;
    std::string driverVersion;
};

struct RamModule {
    std::string slot;  // DeviceLocator
    std::string manufacturer;
    std::string partNumber;
    uint64_t capacityBytes = 0;
    uint32_t speedMTs = 0;
    std::string type;  // DDR4, DDR5, ...
};

struct SystemInfo {
    std::string boardVendor, boardModel;
    std::vector<GpuInfo> gpus;
    std::vector<RamModule> ram;
};

// Windows: registry (GPU) + SMBIOS firmware table (RAM, board). No driver needed.
SystemInfo detectSystem();

// Pure parser, exposed for tests: decodes a raw SMBIOS structure table.
void parseSmbios(const uint8_t* table, size_t len, SystemInfo& out);

}  // namespace ql
