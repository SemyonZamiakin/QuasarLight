using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace QuasarLight
{
    internal sealed class EffectSettings
    {
        public int Type { get; set; } = 2;  // Rainbow Wave
        public double Speed { get; set; } = 0.5;
        public double Brightness { get; set; } = 1.0;
        public uint Color1 { get; set; } = 0x8B6CFF;
        public uint Color2 { get; set; } = 0x38D9F0;
        public bool Reverse { get; set; }

        public EffectSettings Clone() => (EffectSettings)MemberwiseClone();
    }

    /// <summary>A strip/ring of LEDs rendered together by one core call.</summary>
    internal sealed class Zone
    {
        public readonly int Count;
        public readonly SolidColorBrush[] Brushes;
        public readonly byte[] Buf;
        public Zone(int count)
        {
            Count = count;
            Buf = new byte[count * 3];
            Brushes = new SolidColorBrush[count];
            for (int i = 0; i < count; i++) Brushes[i] = new SolidColorBrush(Colors.Black);
        }
    }

    internal sealed class DeviceVm
    {
        public string Id = "";
        public string Title = "";
        public string Subtitle = "";
        public string Kind = "";
        public EffectSettings Settings = new EffectSettings();
        public readonly List<Zone> Zones = new List<Zone>();
        public readonly List<UIElement> Visuals = new List<UIElement>();
        public readonly List<RotateTransform> Fans = new List<RotateTransform>();
        public readonly SolidColorBrush Ambient = new SolidColorBrush(Colors.Black);
        public readonly SolidColorBrush Dot = new SolidColorBrush(Colors.Black);
        public Color Average = Colors.Black;
        public RadioButton? Row;
        public string[] Info = Array.Empty<string>();
    }

    internal static class Scene
    {
        private static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
        public static Color FromRgb(uint c) => Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
        public static uint ToRgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

        public static Color Hsv(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360 / 60.0;
            int i = (int)Math.Floor(h);
            double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            double r, g, b;
            switch (i) { case 0: r = v; g = t; b = p; break; case 1: r = q; g = v; b = p; break;
                         case 2: r = p; g = v; b = t; break; case 3: r = p; g = q; b = v; break;
                         case 4: r = t; g = p; b = v; break; default: r = v; g = p; b = q; break; }
            return Color.FromRgb((byte)(r * 255 + .5), (byte)(g * 255 + .5), (byte)(b * 255 + .5));
        }

        public static List<DeviceVm> Build(Canvas stage, SystemModel sys)
        {
            stage.Children.Clear();
            var devices = new List<DeviceVm>();

            // motherboard backdrop
            var board = new Rectangle { Width = 600, Height = 420, RadiusX = 20, RadiusY = 20,
                Fill = new LinearGradientBrush(Color.FromRgb(0x14, 0x17, 0x24), Color.FromRgb(0x0C, 0x0E, 0x17), 90),
                Stroke = B("#222740"), StrokeThickness = 1.5 };
            Canvas.SetLeft(board, 10); Canvas.SetTop(board, 10);
            stage.Children.Add(board);
            for (int i = 0; i < 9; i++)  // decorative traces
            {
                var l = new Line { X1 = 240, Y1 = 40 + i * 22, X2 = 320, Y2 = 40 + i * 22, Stroke = B("#1A1F33"), StrokeThickness = 1.2 };
                stage.Children.Add(l);
            }

            // CPU block (decorative) + board ring
            var cpu = new Rectangle { Width = 150, Height = 150, RadiusX = 18, RadiusY = 18,
                Fill = new LinearGradientBrush(Color.FromRgb(0x1D, 0x21, 0x33), Color.FromRgb(0x12, 0x14, 0x20), 45),
                Stroke = B("#2A2F48") };
            Canvas.SetLeft(cpu, 50); Canvas.SetTop(cpu, 50);
            stage.Children.Add(cpu);
            var cpuTxt = new TextBlock { Text = "CPU", Foreground = B("#3A405C"), FontSize = 20, FontWeight = FontWeights.Bold };
            Canvas.SetLeft(cpuTxt, 100); Canvas.SetTop(cpuTxt, 113);
            stage.Children.Add(cpuTxt);

            // ---- Motherboard device ----
            var mb = new DeviceVm { Id = "board", Kind = "board", Title = "Motherboard",
                Subtitle = string.IsNullOrWhiteSpace(sys.Board) ? "Detected via SMBIOS" : Shorten(sys.Board, 34) };
            mb.Info = new[] { "Board: " + (string.IsNullOrWhiteSpace(sys.Board) ? "unknown" : sys.Board),
                              "Lighting: CPU ring + edge strip (preview model)" };
            var ring = new Zone(24);
            var strip = new Zone(26);
            AddAmbient(stage, mb, 125, 125, 170, 170);
            AddRing(stage, ring, 125, 125, 52, 5);
            AddStrip(stage, strip, 36, 413, 22, 4, 0, 0, glowRadius: 8);
            mb.Zones.Add(ring); mb.Zones.Add(strip);
            devices.Add(mb);

            // ---- RAM device ----
            if (sys.Ram.Length > 0)
            {
                var ram = new DeviceVm { Id = "ram", Kind = "ram", Title = "Memory" };
                ulong total = 0;
                foreach (var m in sys.Ram) total += m.CapacityBytes;
                var first = sys.Ram[0];
                ram.Subtitle = $"{sys.Ram.Length}x {first.Part.Trim()}";
                ram.Info = sys.Ram.Select(m => $"{m.Slot}: {m.Manufacturer} {m.Part.Trim()} · {m.CapacityBytes >> 30} GB {m.Type}-{m.Speed}").ToArray();
                int sticks = Math.Min(sys.Ram.Length, 4);
                double x0 = 330 + (4 - sticks) * 26;
                AddAmbient(stage, ram, x0 + sticks * 26, 140, 90 + sticks * 55, 240);
                for (int s = 0; s < sticks; s++)
                {
                    double x = x0 + s * 56, y = 36;
                    var body = new Rectangle { Width = 42, Height = 214, RadiusX = 6, RadiusY = 6,
                        Fill = new LinearGradientBrush(Color.FromRgb(0x26, 0x2A, 0x3C), Color.FromRgb(0x14, 0x16, 0x22), 0),
                        Stroke = B("#323852") };
                    Canvas.SetLeft(body, x); Canvas.SetTop(body, y);
                    stage.Children.Add(body);
                    var pins = new Rectangle { Width = 42, Height = 6, Fill = B("#8C7A3A"), Opacity = .6 };
                    Canvas.SetLeft(pins, x); Canvas.SetTop(pins, y + 214);
                    stage.Children.Add(pins);
                    var z = new Zone(8);
                    AddVerticalBar(stage, z, x + 5, y + 10, 32, 18, 7);
                    ram.Zones.Add(z);
                }
                devices.Add(ram);
            }

            // ---- GPU devices ----
            var gpuList = sys.Gpus.Where(g => !(g.Vendor == "Intel" && sys.Gpus.Length > 1)).ToArray();
            for (int gi = 0; gi < gpuList.Length && gi < 1; gi++)  // one GPU model on stage
            {
                var g = gpuList[gi];
                var vm = new DeviceVm { Id = "gpu" + gi, Kind = "gpu", Title = "Graphics Card", Subtitle = g.Name.Replace("NVIDIA ", "").Replace("GeForce ", "") };
                string vram = g.VramBytes > 0 ? $"{g.VramBytes / (1024.0 * 1024 * 1024):0.#} GB" : "shared";
                vm.Info = new[] { $"GPU: {g.Name}", $"VRAM: {vram}  ·  Driver {g.Driver}  ·  Vendor {g.Vendor}" };
                AddAmbient(stage, vm, 310, 335, 520, 150);
                DrawGpu(stage, vm, g.Name);
                devices.Add(vm);
            }

            // ---- sidebar order: GPU, RAM, Board ----
            devices = devices.OrderBy(d => d.Kind == "gpu" ? 0 : d.Kind == "ram" ? 1 : 2).ToList();
            return devices;
        }

        private static string Shorten(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        private static void AddAmbient(Canvas stage, DeviceVm dev, double cx, double cy, double w, double h)
        {
            var e = new Ellipse { Width = w, Height = h, Fill = dev.Ambient, Opacity = 0.5, IsHitTestVisible = false,
                Effect = new BlurEffect { Radius = 45 } };
            Canvas.SetLeft(e, cx - w / 2); Canvas.SetTop(e, cy - h / 2);
            stage.Children.Add(e);
            dev.Visuals.Add(e);
        }

        private static Canvas GlowLayer(Canvas stage, double radius)
        {
            var c = new Canvas { IsHitTestVisible = false, Effect = new BlurEffect { Radius = radius } };
            stage.Children.Add(c);
            return c;
        }

        private static void AddRing(Canvas stage, Zone z, double cx, double cy, double r, double dot)
        {
            var glow = GlowLayer(stage, 7);
            for (int i = 0; i < z.Count; i++)
            {
                double a = i * 2 * Math.PI / z.Count - Math.PI / 2;
                foreach (var host in new Panel[] { glow, null! })
                {
                    var e = new Ellipse { Width = dot * 2, Height = dot * 2, Fill = z.Brushes[i] };
                    Canvas.SetLeft(e, cx + r * Math.Cos(a) - dot); Canvas.SetTop(e, cy + r * Math.Sin(a) - dot);
                    if (host == null) stage.Children.Add(e); else host.Children.Add(e);
                }
            }
        }

        private static void AddStrip(Canvas stage, Zone z, double x, double y, double step, double h, double dx, double dy, double glowRadius)
        {
            var glow = GlowLayer(stage, glowRadius);
            for (int i = 0; i < z.Count; i++)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    var r = new Rectangle { Width = step - 3, Height = h, RadiusX = 2, RadiusY = 2, Fill = z.Brushes[i] };
                    Canvas.SetLeft(r, x + i * step + dx); Canvas.SetTop(r, y + dy);
                    if (pass == 0) glow.Children.Add(r); else stage.Children.Add(r);
                }
            }
        }

        private static void AddVerticalBar(Canvas stage, Zone z, double x, double y, double w, double h, double gap)
        {
            var glow = GlowLayer(stage, 9);
            for (int i = 0; i < z.Count; i++)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    var r = new Rectangle { Width = w, Height = h, RadiusX = 3, RadiusY = 3, Fill = z.Brushes[i] };
                    Canvas.SetLeft(r, x); Canvas.SetTop(r, y + i * (h + gap));
                    if (pass == 0) glow.Children.Add(r); else stage.Children.Add(r);
                }
            }
        }

        private static void DrawGpu(Canvas stage, DeviceVm vm, string name)
        {
            var shell = new Rectangle { Width = 560, Height = 150, RadiusX = 16, RadiusY = 16,
                Fill = new LinearGradientBrush(Color.FromRgb(0x24, 0x28, 0x3A), Color.FromRgb(0x10, 0x12, 0x1C), 90),
                Stroke = B("#343A58"), StrokeThickness = 1.4 };
            Canvas.SetLeft(shell, 30); Canvas.SetTop(shell, 262);
            stage.Children.Add(shell);

            var strip = new Zone(22);
            AddStrip(stage, strip, 62, 270, 22, 4, 0, 0, glowRadius: 8);
            vm.Zones.Add(strip);

            double[] fx = { 170, 410 };
            foreach (double cx in fx)
            {
                double cy = 345;
                var housing = new Ellipse { Width = 132, Height = 132, Fill = B("#0B0D15"), Stroke = B("#2B3050"), StrokeThickness = 1.5 };
                Canvas.SetLeft(housing, cx - 66); Canvas.SetTop(housing, cy - 66);
                stage.Children.Add(housing);

                var z = new Zone(16);
                AddRing(stage, z, cx, cy, 58, 3.4);
                vm.Zones.Add(z);

                var rot = new RotateTransform(0);
                vm.Fans.Add(rot);
                var blades = new Canvas { RenderTransform = new TransformGroup { Children = { rot, new TranslateTransform(cx, cy) } } };
                for (int k = 0; k < 9; k++)
                {
                    var blade = new Polygon
                    {
                        Points = new PointCollection { new Point(0, 0), new Point(20, -6), new Point(46, -22), new Point(52, -6), new Point(24, 6) },
                        Fill = B("#1B1F30"), Stroke = B("#2C3250"), StrokeThickness = 0.8,
                        RenderTransform = new RotateTransform(k * 40)
                    };
                    blades.Children.Add(blade);
                }
                var hub = new Ellipse { Width = 26, Height = 26, Fill = B("#0E1018"), Stroke = B("#343A58") };
                Canvas.SetLeft(hub, -13); Canvas.SetTop(hub, -13);
                blades.Children.Add(hub);
                stage.Children.Add(blades);
            }

            string label = name.Replace("NVIDIA ", "").Replace("GeForce ", "").ToUpperInvariant();
            var t = new TextBlock { Text = label, Foreground = B("#59607F"), FontSize = 12, FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center, Width = 140 };
            Canvas.SetLeft(t, 220); Canvas.SetTop(t, 336);
            stage.Children.Add(t);
        }

        /// <summary>Renders every zone of every device through the C++ core and paints the brushes.</summary>
        public static unsafe void Update(IReadOnlyList<DeviceVm> devices, double t, double dt)
        {
            foreach (var d in devices)
            {
                var s = d.Settings;
                double r = 0, g = 0, b = 0; int n = 0;
                foreach (var z in d.Zones)
                {
                    fixed (byte* p = z.Buf)
                        Native.ql_render(s.Type, (float)s.Speed, (float)s.Brightness, s.Color1, s.Color2, s.Reverse ? 1 : 0, t, p, z.Count);
                    for (int i = 0; i < z.Count; i++)
                    {
                        byte rr = z.Buf[3 * i], gg = z.Buf[3 * i + 1], bb = z.Buf[3 * i + 2];
                        z.Brushes[i].Color = Color.FromRgb(rr, gg, bb);
                        r += rr; g += gg; b += bb; n++;
                    }
                }
                if (n > 0)
                {
                    // boost saturation of the average a little so the ambient glow stays vivid
                    // The average of a rainbow is grey; use the middle LED of the largest zone instead.
                    var big = d.Zones.OrderByDescending(z => z.Count).First();
                    d.Average = big.Brushes[big.Count / 2].Color;
                    d.Ambient.Color = d.Average;
                    d.Dot.Color = d.Average;
                }
                double spin = (s.Type == 0 ? 20 : 60 + 420 * s.Speed) * dt;
                foreach (var f in d.Fans) f.Angle = (f.Angle + spin) % 360;
            }
        }
    }
}
