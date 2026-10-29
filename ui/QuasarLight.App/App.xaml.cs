using System;
using System.Drawing;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace QuasarLight
{
    public partial class App : System.Windows.Application
    {
        private Mutex? _single;
        private Forms.NotifyIcon? _tray;
        private MainWindow? _win;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _single = new Mutex(true, "QuasarLight.SingleInstance", out bool first);
            if (!first) { Shutdown(); return; }

            try
            {
                if (Native.ql_abi_version() != Native.AbiVersion)
                    throw new InvalidOperationException("qlcore.dll ABI mismatch");
                _win = new MainWindow();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Quasar Light could not start:\n" + ex.Message, "Quasar Light",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            BuildTray();
            if (Array.IndexOf(e.Args, "--minimized") < 0) _win.Show();
        }

        private void BuildTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open Quasar Light", null, (_, __) => ShowWindow());
            menu.Items.Add(new Forms.ToolStripSeparator());
            int n = Native.ql_effect_count();
            for (int i = 0; i < n; i++)
            {
                int t = i;
                menu.Items.Add(Native.EffectName(i), null, (_, __) => _win?.SetEffectForAll(t));
            }
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, __) => ExitApp());

            _tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "Quasar Light", Visible = true, ContextMenuStrip = menu };
            _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) ShowWindow(); };
        }

        private void ShowWindow()
        {
            if (_win == null) return;
            _win.Show();
            if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
            _win.Activate();
        }

        private void ExitApp()
        {
            _win?.SaveSettings();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Shutdown();
        }

        private static Icon MakeIcon()
        {
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(139, 108, 255), Color.FromArgb(56, 217, 240), 45f);
                g.FillEllipse(br, 2, 2, 28, 28);
            }
            IntPtr h = bmp.GetHicon();
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();
        }
    }
}
