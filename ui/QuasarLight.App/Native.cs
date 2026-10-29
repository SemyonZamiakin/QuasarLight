using System;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace QuasarLight
{
    /// <summary>P/Invoke surface of qlcore.dll (see core/include/ql/qlcore.h).</summary>
    internal static class Native
    {
        private const string Dll = "qlcore.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int ql_abi_version();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ql_system_json();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int ql_effect_count();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ql_effect_name(int type);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe void ql_render(int type, float speed, float brightness, uint c1, uint c2,
                                                    int reverse, double t, byte* rgb, int n);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int ql_orgb_connect(int port);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void ql_orgb_disconnect();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ql_orgb_devices_json();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int ql_orgb_apply(int dev, int effect, float speed, float brightness, uint c1, uint c2, int reverse);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int ql_orgb_last_approx();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int ql_orgb_restore(int dev);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ql_orgb_error();

        public static string OrgbError() => Marshal.PtrToStringUTF8(ql_orgb_error()) ?? "";
        public static OrgbDev[] OrgbDevices()
        {
            string json = Marshal.PtrToStringUTF8(ql_orgb_devices_json()) ?? "[]";
            return JsonSerializer.Deserialize<OrgbDev[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Array.Empty<OrgbDev>();
        }

        public const int AbiVersion = 1;

        public static string EffectName(int t) => Marshal.PtrToStringUTF8(ql_effect_name(t)) ?? "?";

        public static SystemModel Detect()
        {
            string json = Marshal.PtrToStringUTF8(ql_system_json()) ?? "{}";
            return JsonSerializer.Deserialize<SystemModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new SystemModel();
        }
    }

    internal sealed class OrgbDev
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public int Type { get; set; }
        public int Leds { get; set; }
        public string[] Modes { get; set; } = Array.Empty<string>();
    }

    internal sealed class SystemModel
    {
        public string Board { get; set; } = "";
        public GpuModel[] Gpus { get; set; } = Array.Empty<GpuModel>();
        public RamModel[] Ram { get; set; } = Array.Empty<RamModel>();
    }

    internal sealed class GpuModel
    {
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public string Driver { get; set; } = "";
        public ulong VramBytes { get; set; }
    }

    internal sealed class RamModel
    {
        public string Slot { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Part { get; set; } = "";
        public string Type { get; set; } = "";
        public ulong CapacityBytes { get; set; }
        public int Speed { get; set; }
    }
}
