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

#ifdef __cplusplus
}
#endif
