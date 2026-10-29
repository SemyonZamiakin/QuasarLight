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

        public const int AbiVersion = 1;

        public static string EffectName(int t) => Marshal.PtrToStringUTF8(ql_effect_name(t)) ?? "?";

        public static SystemModel Detect()
        {
            string json = Marshal.PtrToStringUTF8(ql_system_json()) ?? "{}";
            return JsonSerializer.Deserialize<SystemModel>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new SystemModel();
        }
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
