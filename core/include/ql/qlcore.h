/* Quasar Light core - stable C ABI used by the UI (P/Invoke) and any other front-end. */
#pragma once
#include <stdint.h>

#ifdef _WIN32
#define QL_API __declspec(dllexport)
#else
#define QL_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

QL_API int ql_abi_version(void);

/* UTF-8 JSON of detected hardware: {board, gpus[], ram[]}. Pointer stays valid until next call. */
QL_API const char* ql_system_json(void);

QL_API int ql_effect_count(void);
QL_API const char* ql_effect_name(int type);

/* Renders n LEDs of an effect at time t (seconds) into rgb[3*n]. colors are 0xRRGGBB. */
QL_API void ql_render(int type, float speed, float brightness, uint32_t color1, uint32_t color2, int reverse,
                      double t, uint8_t* rgb, int n);

/* ---- OpenRGB backend (loopback only). All calls must come from one thread. ---- */
/* Returns device count, or -1 (see ql_orgb_error). */
QL_API int ql_orgb_connect(int port);
QL_API void ql_orgb_disconnect(void);
/* [{"index":0,"name":..,"vendor":..,"type":2,"leds":1,"modes":["Direct",..]}, ...] */
QL_API const char* ql_orgb_devices_json(void);
/* Applies an effect to hardware device `dev`. Returns the hardware mode index, or -1. */
QL_API int ql_orgb_apply(int dev, int effect, float speed, float brightness, uint32_t color1, uint32_t color2, int reverse);
/* 1 if the last apply used a stand-in mode (effect not available in hardware). */
QL_API int ql_orgb_last_approx(void);
QL_API int ql_orgb_restore(int dev);
QL_API const char* ql_orgb_error(void);

#ifdef __cplusplus
}
#endif
