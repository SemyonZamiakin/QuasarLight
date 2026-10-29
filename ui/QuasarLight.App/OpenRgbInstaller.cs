using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace QuasarLight
{
    /// <summary>
    /// Downloads the official portable OpenRGB release (never bundled with Quasar Light) and verifies its SHA-256
    /// before extracting. The hash is pinned in code, so a tampered or swapped download is rejected.
    /// </summary>
    internal static class OpenRgbInstaller
    {
        public const string Version = "1.1";
        public const string Url = "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip";
        public const string Sha256 = "63291726c6fc84eb479ef2ee752e97e1187f6ce9c49fd27eee40ded588a8172a";
        public const long ApproxBytes = 22_019_605;

        public static string InstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuasarLight", "OpenRGB");

        public static string? InstalledExe()
        {
            string p = Path.Combine(InstallDir, "OpenRGB Windows 64-bit", "OpenRGB.exe");
            return File.Exists(p) ? p : null;
        }

        public static async Task<string> DownloadAsync(IProgress<int> percent, CancellationToken ct)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "QuasarLight_openrgb_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                using (var resp = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? ApproxBytes;
                    if (total > 200L * 1024 * 1024) throw new InvalidDataException("Unexpected download size");
                    using var src = await resp.Content.ReadAsStreamAsync(ct);
                    using var dst = File.Create(tmp);
                    var buf = new byte[81920];
                    long done = 0; int n;
                    while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n, ct);
                        done += n;
                        if (done > 200L * 1024 * 1024) throw new InvalidDataException("Download too large");
                        percent.Report((int)Math.Min(99, done * 100 / total));
                    }
                }

                string actual;
                using (var fs = File.OpenRead(tmp)) actual = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                if (actual != Sha256)
                    throw new InvalidDataException("Checksum mismatch - the download is not the expected OpenRGB release.");

                string staging = InstallDir + ".new";
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
                string root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                using (var zip = ZipFile.OpenRead(tmp))
                {
                    foreach (var e in zip.Entries)
                    {
                        string dest = Path.GetFullPath(Path.Combine(staging, e.FullName));
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Archive contains an unsafe path");  // zip-slip guard
                        if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        e.ExtractToFile(dest, true);
                    }
                }
                if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true);
                Directory.Move(staging, InstallDir);
                percent.Report(100);
                return InstalledExe() ?? throw new FileNotFoundException("OpenRGB.exe not found in the archive");
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* temp cleanup is best effort */ }
            }
        }
    }
}
