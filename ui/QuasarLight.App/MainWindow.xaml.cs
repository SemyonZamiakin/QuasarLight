using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace QuasarLight
{
    public partial class MainWindow : Window
    {
        private static readonly uint[] PaletteColors =
        {
            0xFF3B5C, 0xFF7A3D, 0xFFC93C, 0xA6F04D, 0x2EE59D, 0x38D9F0, 0x3D8BFF, 0x8B6CFF,
            0xC95CFF, 0xFF4FA3, 0xFFFFFF, 0xFF9FB6, 0x7CF5E8, 0xFFE9A8
        };

        private readonly List<DeviceVm> _devices = new List<DeviceVm>();
        private DeviceVm? _selected;
        private bool _loading;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _last;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _applyTimer;
        private DispatcherTimer? _connectTimer;
        private bool _connected;
        private string _openRgbPath = "";
        private const int OrgbPort = 6742;
        private readonly Dictionary<string, EffectSettings> _savedSettings = new Dictionary<string, EffectSettings>();
        private readonly Dictionary<string, string> _savedImages = new Dictionary<string, string>();
        private static readonly string SettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuasarLight", "settings.json");

        public MainWindow()
        {
            InitializeComponent();
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (_, __) => { _saveTimer.Stop(); SaveSettings(); };
            _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _applyTimer.Tick += (_, __) => { _applyTimer.Stop(); ApplyToHardware(); };

            BuildEffectChips();
            BuildPalette();

            SystemModel sys;
            try { sys = Native.Detect(); }
            catch (Exception ex) { sys = new SystemModel(); StageSub.Text = "Detection failed: " + ex.Message; }

            _devices = Scene.Build(Stage, sys);
            LoadSettings();
            BuildDeviceList();
            if (_devices.Count > 0) _devices[0].Row!.IsChecked = true;
            TryConnect(quiet: true);  // attach to an already-running OpenRGB server; never launches anything

            CompositionTarget.Rendering += OnRender;
            SizeChanged += (_, __) => UpdateHueThumb();
            Loaded += (_, __) => UpdateHueThumb();
        }

        // ---------- animation / render loop ----------
        private void OnRender(object? sender, EventArgs e)
        {
            if (!IsVisible || WindowState == WindowState.Minimized) return;
            double t = _clock.Elapsed.TotalSeconds, dt = t - _last;
            if (dt < 1.0 / 40) return;  // cap at ~40 fps: blur-heavy scene, keeps CPU low
            _last = t;
            Scene.Update(_devices, t, Math.Min(dt, 0.1));
            DimUnselected();
        }

        private void DimUnselected()
        {
            bool sync = SyncBox.IsChecked == true;
            foreach (var d in _devices)
            {
                double o = sync || d == _selected ? 1.0 : 0.45;
                foreach (var v in d.Visuals) v.Opacity = o * 0.28;
            }
        }

        // ---------- title bar ----------
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
        private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Hide();

        // ---------- build UI ----------
        private void BuildEffectChips()
        {
            int n = Native.ql_effect_count();
            for (int i = 0; i < n; i++)
            {
                var rb = new RadioButton { Content = Native.EffectName(i), Tag = i, GroupName = "fx", Style = (Style)FindResource("Chip") };
                rb.Margin = new Thickness(0, 0, 6, 6);
                rb.Checked += (s, _) => { if (!_loading) Apply(st => st.Type = (int)((RadioButton)s).Tag); };
                EffectPanel.Children.Add(rb);
            }
        }

        private void BuildPalette()
        {
            foreach (uint c in PaletteColors)
            {
                var sw = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 6, 6),
                    Background = new SolidColorBrush(Scene.FromRgb(c)), Cursor = Cursors.Hand,
                    BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), ToolTip = "#" + c.ToString("X6")
                };
                uint cc = c;
                sw.MouseLeftButtonUp += (_, __) => SetTargetColor(cc);
                Palette.Children.Add(sw);
            }
        }

        private void BuildDeviceList()
        {
            foreach (var d in _devices) AddDeviceRow(d);
        }

        private void AddDeviceRow(DeviceVm d)
        {
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = d.Dot, VerticalAlignment = VerticalAlignment.Center };
                var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                text.Children.Add(new TextBlock { Text = d.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
                text.Children.Add(new TextBlock { Text = d.Subtitle, FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 });
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center,
                    Background = (Brush)FindResource("RaisedBg"), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = "PREVIEW", FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("DimBrush") }
                };
                d.BadgeBox = badge;
                d.BadgeText = (TextBlock)badge.Child;
                var row = new DockPanel();
                DockPanel.SetDock(badge, Dock.Right);
                row.Children.Add(badge);
                var left = new StackPanel { Orientation = Orientation.Horizontal };
                left.Children.Add(dot); left.Children.Add(text);
                row.Children.Add(left);
                var rb = new RadioButton { Content = row, GroupName = "dev", Style = (Style)FindResource("DeviceRow"), Tag = d };
                rb.Checked += (s, _) => Select((DeviceVm)((RadioButton)s).Tag);
                d.Row = rb;
                DeviceList.Children.Add(rb);
            }
        }

        // ---------- selection / settings ----------
        private void Select(DeviceVm d)
        {
            _selected = d;
            StageTitle.Text = d.Title;
            StageSub.Text = d.Subtitle;
            InfoText.Text = string.Join("\n", d.Info);
            LoadControls(d.Settings);
            ShowView(d);
        }

        // ---------- device view (custom photo / generic devices) ----------
        private static string FileKey(string id)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in id) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.ToString();
        }

        private static readonly string ImageDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuasarLight", "images");

        private void ShowView(DeviceVm d)
        {
            bool hasImg = !string.IsNullOrEmpty(d.ImagePath) && File.Exists(d.ImagePath);
            bool custom = d.Generic || hasImg;
            StageBox.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
            DeviceView.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            ImgClearBtn.IsEnabled = hasImg;
            if (!custom) return;

            DevGlow.Fill = d.Ambient;
            DevImage.Source = null;
            if (hasImg)
            {
                try
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 1400;
                    bi.UriSource = new Uri(d.ImagePath);
                    bi.EndInit();
                    bi.Freeze();
                    DevImage.Source = bi;
                }
                catch (Exception ex) { Debug.WriteLine("image load failed: " + ex.Message); }
            }
            DevNoImage.Visibility = DevImage.Source == null ? Visibility.Visible : Visibility.Collapsed;

            LedBar.Children.Clear();
            var z = d.Zones.OrderByDescending(q => q.Count).First();
            int n = Math.Min(z.Count, 48);
            double w = Math.Max(4, Math.Min(18, 560.0 / n));
            for (int i = 0; i < n; i++)
                LedBar.Children.Add(new System.Windows.Shapes.Rectangle { Width = w, Height = 10, RadiusX = 2, RadiusY = 2, Margin = new Thickness(1), Fill = z.Brushes[i] });
        }

        private void ImgSet_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Choose a photo of " + _selected.Subtitle,
                Filter = "Images (png, jpg, bmp)|*.png;*.jpg;*.jpeg;*.bmp"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                var fi = new FileInfo(dlg.FileName);
                if (fi.Length > 20 * 1024 * 1024) { StatusText.Text = "Image is larger than 20 MB"; return; }
                Directory.CreateDirectory(ImageDir);
                string dest = Path.Combine(ImageDir, FileKey(_selected.Id) + fi.Extension.ToLowerInvariant());
                File.Copy(fi.FullName, dest, true);  // keep our own copy so the original can move
                _selected.ImagePath = dest;
                _savedImages[_selected.Id] = dest;
                _saveTimer.Stop(); _saveTimer.Start();
                ShowView(_selected);
            }
            catch (Exception ex) { StatusText.Text = "Could not use image: " + ex.Message; }
        }

        private void ImgClear_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || string.IsNullOrEmpty(_selected.ImagePath)) return;
            try { if (File.Exists(_selected.ImagePath)) File.Delete(_selected.ImagePath); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
            _selected.ImagePath = "";
            _savedImages.Remove(_selected.Id);
            _saveTimer.Stop(); _saveTimer.Start();
            ShowView(_selected);
        }

        private void LoadControls(EffectSettings s)
        {
            _loading = true;
            foreach (RadioButton rb in EffectPanel.Children) rb.IsChecked = (int)rb.Tag == s.Type;
            SpeedSlider.Value = s.Speed;
            BriSlider.Value = s.Brightness;
            ReverseBox.IsChecked = s.Reverse;
            _loading = false;
            UpdateValueLabels();
            SyncHueFromTarget();
        }

        private void UpdateValueLabels()
        {
            SpeedVal.Text = (int)Math.Round(SpeedSlider.Value * 100) + "%";
            BriVal.Text = (int)Math.Round(BriSlider.Value * 100) + "%";
        }

        private void Apply(Action<EffectSettings> change)
        {
            if (_loading || _selected == null) return;
            if (SyncBox.IsChecked == true) foreach (var d in _devices) change(d.Settings);
            else change(_selected.Settings);
            _saveTimer.Stop(); _saveTimer.Start();
            if (_connected && AutoBox.IsChecked == true) { _applyTimer.Stop(); _applyTimer.Start(); }
        }

        private void SetTargetColor(uint rgb)
        {
            bool primary = Target1.IsChecked == true;
            Apply(st => { if (primary) st.Color1 = rgb; else st.Color2 = rgb; });
            SyncHueFromTarget();
        }

        private void Target_Changed(object sender, RoutedEventArgs e) => SyncHueFromTarget();

        private void SyncHueFromTarget()
        {
            if (_selected == null || !IsInitialized) return;
            uint c = Target1.IsChecked == true ? _selected.Settings.Color1 : _selected.Settings.Color2;
            var col = Scene.FromRgb(c);
            float h = System.Drawing.Color.FromArgb(col.R, col.G, col.B).GetHue();
            _loading = true; HueSlider.Value = h; _loading = false;
            HexBox.Text = "#" + c.ToString("X6");
            HexSwatch.Background = new SolidColorBrush(col);
            UpdateHueThumb();
        }

        private void Hue_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateHueThumb();
            if (_loading) return;
            uint rgb = Scene.ToRgb(Scene.Hsv(HueSlider.Value, 1, 1));
            bool primary = Target1.IsChecked == true;
            Apply(st => { if (primary) st.Color1 = rgb; else st.Color2 = rgb; });
        }

        private void UpdateHueThumb()
        {
            if (HueSlider == null || HueSlider.ActualWidth <= 0) return;
            System.Windows.Controls.Canvas.SetLeft(HueThumb, (HueSlider.Value / 360.0) * (HueSlider.ActualWidth - 18));
            var c = Scene.Hsv(HueSlider.Value, 1, 1);
            HueThumb.Fill = new SolidColorBrush(c);
        }

        private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_loading || !IsInitialized) return;
            UpdateValueLabels();
            double sp = SpeedSlider.Value, br = BriSlider.Value;
            Apply(st => { st.Speed = sp; st.Brightness = br; });
        }

        private void Hex_Commit(object sender, RoutedEventArgs e)
        {
            string t = HexBox.Text.Trim().TrimStart('#');
            if (t.Length == 6 && uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out uint rgb)) SetTargetColor(rgb);
            else SyncHueFromTarget();  // invalid input: restore current value
        }

        private void Hex_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { Hex_Commit(sender, e); Keyboard.ClearFocus(); }
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender == ReverseBox) { bool r = ReverseBox.IsChecked == true; Apply(st => st.Reverse = r); }
            else if (sender == SyncBox && SyncBox.IsChecked == true && _selected != null)
                foreach (var d in _devices) d.Settings = _selected.Settings.Clone();  // adopt selected device's look everywhere
            _saveTimer.Stop(); _saveTimer.Start();
        }

        // ---------- OpenRGB backend ----------
        private static readonly string[] DefaultOrgbLocations =
        {
            @"OpenRGB\OpenRGB.exe", @"OpenRGB Windows 64-bit\OpenRGB.exe", @"tools\OpenRGB\OpenRGB.exe"
        };

        private void Backend_Click(object sender, RoutedEventArgs e)
        {
            if (_connected) { Disconnect(); return; }
            if (TryConnect(quiet: false)) return;
            LaunchOpenRgb();
        }

        private bool TryConnect(bool quiet)
        {
            int n;
            try { n = Native.ql_orgb_connect(OrgbPort); }
            catch (Exception ex) { SetBackendState(false, "OpenRGB: error - " + ex.Message); return false; }
            if (n < 0)
            {
                if (!quiet) SetBackendState(false, "OpenRGB: not running");
                return false;
            }
            OnConnected(n);
            return true;
        }

        private void LaunchOpenRgb()
        {
            string? exe = FindOpenRgb();
            if (exe == null)
            {
                var dlg = new System.Windows.Forms.OpenFileDialog { Title = "Locate OpenRGB.exe (from the official OpenRGB release)", Filter = "OpenRGB.exe|OpenRGB.exe" };
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) { SetBackendState(false, "OpenRGB: not running"); return; }
                exe = dlg.FileName;
            }
            _openRgbPath = exe;
            SaveSettings();
            try
            {
                Process.Start(new ProcessStartInfo(exe, "--server --startminimized --noautoconnect")
                    { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false });
            }
            catch (Exception ex) { SetBackendState(false, "OpenRGB: cannot start - " + ex.Message); return; }

            int tries = 0;
            SetBackendState(false, "OpenRGB: starting...");
            _connectTimer?.Stop();
            _connectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _connectTimer.Tick += (_, __) =>
            {
                if (TryConnect(quiet: true)) { _connectTimer!.Stop(); return; }
                if (++tries >= 30) { _connectTimer!.Stop(); SetBackendState(false, "OpenRGB: did not respond"); }
                else BackendText.Text = $"OpenRGB: starting... ({tries * 2}s)";
            };
            _connectTimer.Start();
        }

        private string? FindOpenRgb()
        {
            if (File.Exists(_openRgbPath)) return _openRgbPath;
            string baseDir = AppContext.BaseDirectory;
            foreach (var rel in DefaultOrgbLocations)
            {
                string p = Path.Combine(baseDir, rel);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private void RemoveGenerics()
        {
            foreach (var g in _devices.Where(x => x.Generic).ToList())
            {
                if (g.Row != null) DeviceList.Children.Remove(g.Row);
                _devices.Remove(g);
            }
        }

        private void OnConnected(int count)
        {
            OrgbDev[] hw = Native.OrgbDevices();
            RemoveGenerics();
            var used = new HashSet<int>();
            foreach (var d in _devices)
            {
                d.OrgbIdx.Clear();
                int type = d.Kind == "gpu" ? 2 : d.Kind == "ram" ? 1 : 0;
                // memory: every DIMM is its own OpenRGB device, drive them all; GPU/board: the first one
                foreach (var h in hw.Where(h => h.Type == type && !used.Contains(h.Index)))
                {
                    d.OrgbIdx.Add(h.Index); used.Add(h.Index);
                    d.HwName = h.Name;
                    if (d.Kind != "ram") break;
                }
                SetBadge(d, d.Linked);
            }
            // anything else OpenRGB can drive gets a generic card, so every glowing device is controllable
            var seen = new Dictionary<string, int>();
            foreach (var h in hw.Where(h => !used.Contains(h.Index)))
            {
                seen.TryGetValue(h.Name, out int k); seen[h.Name] = k + 1;
                var g = Scene.MakeGeneric(h, "hw:" + h.Name + (k > 0 ? "#" + k : ""));
                if (_savedSettings.TryGetValue(g.Id, out var st)) g.Settings = st;
                if (_savedImages.TryGetValue(g.Id, out var img)) g.ImagePath = img;
                _devices.Add(g);
                AddDeviceRow(g);
                SetBadge(g, true);
            }
            int linked = _devices.Count(d => d.Linked);
            SetBackendState(true, $"OpenRGB: connected - {linked} device(s) linked");
        }

        private void SetBadge(DeviceVm d, bool live)
        {
            if (d.BadgeText == null || d.BadgeBox == null) return;
            d.BadgeText.Text = live ? "LIVE" : "PREVIEW";
            d.BadgeText.Foreground = live ? new SolidColorBrush(Color.FromRgb(0x5F, 0xD1, 0x8A)) : (Brush)FindResource("DimBrush");
            d.BadgeBox.BorderBrush = live ? new SolidColorBrush(Color.FromRgb(0x2F, 0x7A, 0x4E)) : (Brush)FindResource("Line");
        }

        private void Disconnect()
        {
            _connectTimer?.Stop();
            Native.ql_orgb_disconnect();
            RemoveGenerics();
            foreach (var d in _devices) { d.OrgbIdx.Clear(); SetBadge(d, false); }
            if (_selected != null && !_devices.Contains(_selected) && _devices.Count > 0) _devices[0].Row!.IsChecked = true;
            SetBackendState(false, "OpenRGB: not connected");
        }

        private void SetBackendState(bool ok, string text)
        {
            _connected = ok;
            BackendText.Text = text;
            BackendDot.Fill = ok ? new SolidColorBrush(Color.FromRgb(0x5F, 0xD1, 0x8A)) : (Brush)FindResource("WarnBrush");
            BackendBtn.Content = ok ? "Disconnect" : "Connect OpenRGB";
            ApplyBtn.IsEnabled = RestoreBtn.IsEnabled = AutoBox.IsEnabled = ok;
            ApplyBtn.ToolTip = ok ? "Send the current effect to the linked devices" : "Connect OpenRGB first";
            ModeNote.Text = ok
                ? "Changes are sent to linked devices when you press Apply (or automatically if enabled)."
                : "Hardware is controlled through a local OpenRGB server. Until connected, changes affect the preview only.";
            if (_selected != null)
                StatusText.Text = $"{_devices.Count} devices  -  selected: {_selected.Title}  -  backend: " + (ok ? "OpenRGB" : "none (preview)");
        }

        private IEnumerable<DeviceVm> Targets() =>
            (SyncBox.IsChecked == true ? _devices : _selected != null ? new List<DeviceVm> { _selected } : new List<DeviceVm>())
                .Where(d => d.Linked);

        private void ApplyToHardware()
        {
            if (!_connected) return;
            var results = new List<string>();
            foreach (var d in Targets())
            {
                var s = d.Settings;
                bool failed = false, approx = false;
                foreach (int idx in d.OrgbIdx)
                {
                    int mode = Native.ql_orgb_apply(idx, s.Type, (float)s.Speed, (float)s.Brightness, s.Color1, s.Color2, s.Reverse ? 1 : 0);
                    if (mode < 0) { failed = true; results.Add($"{d.Title}: failed - {Native.OrgbError()}"); break; }
                    approx |= Native.ql_orgb_last_approx() == 1;
                }
                if (!failed) results.Add($"{d.Title}: {Native.EffectName(s.Type)}" + (d.OrgbIdx.Count > 1 ? $" x{d.OrgbIdx.Count}" : "") + (approx ? " (no such hardware effect, static colour used)" : ""));
            }
            StatusText.Text = results.Count == 0 ? "No linked hardware devices to apply to" : "Applied - " + string.Join("  |  ", results);
        }

        private void Apply_Click(object sender, RoutedEventArgs e) => ApplyToHardware();

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            int n = 0;
            foreach (var d in Targets()) foreach (int idx in d.OrgbIdx) if (Native.ql_orgb_restore(idx) == 1) n++;
            StatusText.Text = n == 0 ? "Nothing to restore" : $"Restored {n} device(s) to their previous mode";
        }

        private void Auto_Click(object sender, RoutedEventArgs e)
        {
            if (AutoBox.IsChecked == true) ApplyToHardware();
            _saveTimer.Stop(); _saveTimer.Start();
        }

        public void Shutdown()
        {
            _connectTimer?.Stop();
            Native.ql_orgb_disconnect();
        }

        // ---------- persistence (%APPDATA%\QuasarLight\settings.json) ----------
        private sealed class SavedState
        {
            public bool Sync { get; set; } = true;
            public bool AutoApply { get; set; }
            public string OpenRgbPath { get; set; } = "";
            public Dictionary<string, EffectSettings> Devices { get; set; } = new Dictionary<string, EffectSettings>();
            public Dictionary<string, string> Images { get; set; } = new Dictionary<string, string>();
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var st = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(SettingsPath));
                if (st == null) return;
                foreach (var kv in st.Devices)
                    if (kv.Value.Type >= 0 && kv.Value.Type < Native.ql_effect_count())
                    {
                        kv.Value.Speed = Math.Clamp(kv.Value.Speed, 0, 1); kv.Value.Brightness = Math.Clamp(kv.Value.Brightness, 0, 1);
                        _savedSettings[kv.Key] = kv.Value;
                    }
                foreach (var kv in st.Images) if (File.Exists(kv.Value)) _savedImages[kv.Key] = kv.Value;
                foreach (var d in _devices)
                {
                    if (_savedSettings.TryGetValue(d.Id, out var s)) d.Settings = s;
                    if (_savedImages.TryGetValue(d.Id, out var img)) d.ImagePath = img;
                }
                SyncBox.IsChecked = st.Sync;
                AutoBox.IsChecked = st.AutoApply;
                _openRgbPath = st.OpenRgbPath ?? "";
            }
            catch (Exception ex) { Debug.WriteLine("settings load failed: " + ex.Message); }  // corrupt file -> defaults
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var st = new SavedState { Sync = SyncBox.IsChecked == true, AutoApply = AutoBox.IsChecked == true, OpenRgbPath = _openRgbPath };
                foreach (var kv in _savedSettings) st.Devices[kv.Key] = kv.Value;   // keep settings of devices not present right now
                foreach (var d in _devices) { st.Devices[d.Id] = d.Settings; _savedSettings[d.Id] = d.Settings; }
                foreach (var kv in _savedImages) st.Images[kv.Key] = kv.Value;
                string tmp = SettingsPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(st));
                File.Move(tmp, SettingsPath, true);  // atomic replace: no half-written file on crash
            }
            catch (Exception ex) { Debug.WriteLine("settings save failed: " + ex.Message); }
        }

        /// <summary>Tray quick action.</summary>
        public void SetEffectForAll(int type)
        {
            foreach (var d in _devices) d.Settings.Type = type;
            if (_selected != null) LoadControls(_selected.Settings);
            _saveTimer.Stop(); _saveTimer.Start();
        }
    }
}
