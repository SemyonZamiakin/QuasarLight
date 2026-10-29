# Safe bus scanning

RGB controllers share SMBus/I²C with things that must never be written: DIMM SPD EEPROMs (hold the memory's
timing profile, XMP/EXPO and serial data), PMICs and temperature sensors. A wrong write can make RAM unbootable.
The rules below are enforced in code by `GuardedBus` and covered by `core/tests/test_core.cpp`.

## Rules

1. **Detect from firmware first.** GPU/RAM/board identity comes from the registry and SMBIOS, no bus access.
   Only probe the bus for controllers a plugin says could exist on *this* board/RAM.
2. **Probe = one SMBus *Read Byte*, never Quick-Write.** Quick-Write is what `i2cdetect` avoids around EEPROMs.
3. **Skip reserved addresses** `0x00-0x07`, `0x78-0x7F`.
4. **Protected ranges are read-only, forever:** `0x18-0x1F` (DIMM temp), `0x30-0x37` (SPD write-protect),
   `0x50-0x5F` (SPD/hub/PMIC). `grantWrite()` refuses them; `writeByte/writeBlock` deny them even if granted.
5. **Writes need an explicit grant**, issued by a plugin only *after* it positively identified its device
   (vendor register / signature read) at that exact address. Grants are per address and revocable.
6. **Bounded writes:** block length 1-32; optional inter-transfer gap (rate limit) per bus.
7. **Audit log** of every probe/read/write with allowed/denied + reason, shown in the UI "diagnostics" view
   (planned) and attached to bug reports.
8. **Fail closed:** unknown bus, unknown chipset, or failed identification → preview only.
9. **No firmware flashing** (no EEPROM/SPD/PMIC programming, no vendor firmware updates) in this project.

## Scan flow

```
detectSystem()                       # SMBIOS: which RAM, which board
  └─ for each plugin: candidateAddresses(system)      # e.g. 0x71 for some boards' RGB chips
       └─ GuardedBus::probe(addr)    # read-only
            └─ plugin.identify(addr) # read signature registers
                 └─ ok → grantWrite(addr) → setColors()
                 └─ else → leave untouched
```

`scanBus()` (full 0x08-0x77 sweep) exists for diagnostics and reports protected-range hits as
`protectedRange=true`; it never writes.

## Verified / not verified

- Verified (host tests, simulated bus): reserved addresses never reach the bus; SPD range writes denied and
  un-grantable; writes without grant denied; only allowed writes reach the transport.
- **Not verified:** any real SMBus/I²C transport — none is implemented yet.
