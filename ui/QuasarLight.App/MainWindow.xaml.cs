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
        private static readonly string SettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuasarLight", "settings.json");

        public MainWindow()
        {
            InitializeComponent();
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (_, __) => { _saveTimer.Stop(); SaveSettings(); };

            StartAurora();
            BuildEffectChips();
            BuildPalette();

            SystemModel sys;
            try { sys = Native.Detect(); }
            catch (Exception ex) { sys = new SystemModel(); StageSub.Text = "Detection failed: " + ex.Message; }

            _devices = Scene.Build(Stage, sys);
            LoadSettings();
            BuildDeviceList();
            if (_devices.Count > 0) _devices[0].Row!.IsChecked = true;

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
                foreach (var v in d.Visuals) v.Opacity = o * 0.5;
            }
        }

        private void StartAurora()
        {
            void Drift(TranslateTransform tt, double dx, double dy, double secs)
            {
                var ax = new DoubleAnimation(-dx, dx, TimeSpan.FromSeconds(secs)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                var ay = new DoubleAnimation(-dy, dy, TimeSpan.FromSeconds(secs * 1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
                tt.BeginAnimation(TranslateTransform.XProperty, ax);
                tt.BeginAnimation(TranslateTransform.YProperty, ay);
            }
            Drift(TA, 120, 80, 14); Drift(TB, -140, -90, 17); Drift(TC, 160, 110, 21);
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
                rb.Checked += (s, _) => { if (!_loading) Apply(st => st.Type = (int)((RadioButton)s).Tag); };
                EffectPanel.Children.Add(rb);
            }
        }

        private void BuildPalette()
        {
            foreach (uint c in PaletteColors)
            {
                var color = Scene.FromRgb(c);
                var sw = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 8, 8),
                    Background = new SolidColorBrush(color), Cursor = Cursors.Hand,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), BorderThickness = new Thickness(1.5),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.55 }
                };
                uint cc = c;
                sw.MouseLeftButtonUp += (_, __) => SetTargetColor(cc);
                Palette.Children.Add(sw);
            }
        }

        private void BuildDeviceList()
        {
            foreach (var d in _devices)
            {
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 14, Height = 14, Fill = d.Dot, VerticalAlignment = VerticalAlignment.Center,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.White, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.7 }
                };
                var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
                text.Children.Add(new TextBlock { Text = d.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
                text.Children.Add(new TextBlock { Text = d.Subtitle, FontSize = 12, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190 });
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(dot);
                row.Children.Add(text);
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
        }

        private void LoadControls(EffectSettings s)
        {
            _loading = true;
            foreach (RadioButton rb in EffectPanel.Children) rb.IsChecked = (int)rb.Tag == s.Type;
            SpeedSlider.Value = s.Speed;
            BriSlider.Value = s.Brightness;
            ReverseBox.IsChecked = s.Reverse;
            _loading = false;
            SyncHueFromTarget();
        }

        private void Apply(Action<EffectSettings> change)
        {
            if (_loading || _selected == null) return;
            if (SyncBox.IsChecked == true) foreach (var d in _devices) change(d.Settings);
            else change(_selected.Settings);
            _saveTimer.Stop(); _saveTimer.Start();
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
            double sp = SpeedSlider.Value, br = BriSlider.Value;
            Apply(st => { st.Speed = sp; st.Brightness = br; });
        }

        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender == ReverseBox) { bool r = ReverseBox.IsChecked == true; Apply(st => st.Reverse = r); }
            else if (sender == SyncBox && SyncBox.IsChecked == true && _selected != null)
                foreach (var d in _devices) d.Settings = _selected.Settings.Clone();  // adopt selected device's look everywhere
            _saveTimer.Stop(); _saveTimer.Start();
        }

        // ---------- persistence (%APPDATA%\QuasarLight\settings.json) ----------
        private sealed class SavedState
        {
            public bool Sync { get; set; } = true;
            public Dictionary<string, EffectSettings> Devices { get; set; } = new Dictionary<string, EffectSettings>();
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var st = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(SettingsPath));
                if (st == null) return;
                foreach (var d in _devices)
                    if (st.Devices.TryGetValue(d.Id, out var s) && s.Type >= 0 && s.Type < Native.ql_effect_count())
                    {
                        s.Speed = Math.Clamp(s.Speed, 0, 1); s.Brightness = Math.Clamp(s.Brightness, 0, 1);
                        d.Settings = s;
                    }
                SyncBox.IsChecked = st.Sync;
            }
            catch (Exception ex) { Debug.WriteLine("settings load failed: " + ex.Message); }  // corrupt file -> defaults
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var st = new SavedState { Sync = SyncBox.IsChecked == true };
                foreach (var d in _devices) st.Devices[d.Id] = d.Settings;
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
