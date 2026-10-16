// 奶蛙桌宠：使用透明逐帧图片，不联网，不需要安装 Python 或 Node.js。
using System;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MilkFrogDesktop
{
    // 只管理当前 Windows 用户的一个启动项；启动程序本身不会自动写入注册表。
    public static class StartupRegistration
    {
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "MilkFrogDesktop";
        public static string Command
        {
            get { return "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\""; }
        }
        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                string saved = key == null ? null : key.GetValue(ValueName) as string;
                return string.Equals(saved, Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        public static void SetEnabled(bool enabled)
        {
            if (enabled)
            {
                string command = Command;
                if (command.Length > 260) throw new InvalidOperationException("程序所在路径太长，请移到较短的文件夹后重试。");
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null) throw new IOException("无法打开当前用户的启动项设置。");
                    key.SetValue(ValueName, command, RegistryValueKind.String);
                }
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null) key.DeleteValue(ValueName, false);
                }
            }
        }
    }

    public class PetWindow : Window
    {
        // 每行对应一种动作，每张小图为 192 × 208 像素。
        readonly int[] counts = { 6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8 };
        readonly BitmapSource[][] sprites = new BitmapSource[11][];
        readonly byte[][][] alpha = new byte[11][][];
        readonly Image pet = new Image();
        readonly Border bubble = new Border();
        readonly TextBlock speech = new TextBlock();
        readonly Canvas canvas = new Canvas();
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly Random random = new Random();
        readonly string folder = AppDomain.CurrentDomain.BaseDirectory;
        readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        ContextMenu menu;
        MenuItem startupMenu;
        Forms.ToolStripMenuItem trayStartupMenu;
        int row, frame, ticks, animationMs;
        int actionMs, nextActionMs = 8000, speechMs;
        bool roaming = true, resting, paused, pointerDown, dragging, noSave;
        double petSize = 220, walkSpeed;
        Point pressedAt;
        double originalLeft, originalTop;
        IntPtr handle;
        public const int WakeMessage = 0x8001;
        [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint p);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X; public int Y; }

        public PetWindow(bool smoke)
        {
            Title = "MilkFrogDesktopCompanion";
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; Topmost = true; ShowActivated = false;
            Content = canvas; LoadSprites();
            speech.Foreground = new SolidColorBrush(Color.FromRgb(69, 58, 30));
            speech.FontSize = 14; speech.TextWrapping = TextWrapping.Wrap;
            speech.TextAlignment = TextAlignment.Center;
            bubble.Child = speech; bubble.Background = new SolidColorBrush(Color.FromRgb(255, 250, 224));
            bubble.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 200, 133));
            bubble.BorderThickness = new Thickness(1); bubble.CornerRadius = new CornerRadius(15);
            bubble.Padding = new Thickness(12, 8, 12, 8); bubble.Visibility = Visibility.Collapsed;
            canvas.Children.Add(pet); canvas.Children.Add(bubble);
            RenderOptions.SetBitmapScalingMode(pet, BitmapScalingMode.HighQuality);
            ApplySize();
            Left = SystemParameters.WorkArea.Right - Width - 45;
            Top = SystemParameters.WorkArea.Bottom - Height;
            LoadSettings(); Clamp(); BuildMenu();
            pet.MouseLeftButtonDown += OnDown;
            pet.MouseMove += OnMove;
            pet.MouseLeftButtonUp += OnUp;
            pet.LostMouseCapture += delegate { pointerDown = false; dragging = false; };
            pet.MouseWheel += delegate(object s, MouseWheelEventArgs e) { ResizePet(e.Delta > 0 ? 1.1 : 1.0 / 1.1); e.Handled = true; };
            pet.MouseRightButtonUp += delegate { walkSpeed = 0; menu.IsOpen = true; };
            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(handle).AddHook(WindowMessage);
                SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000080);
                // 不主动激活窗口；点击角色之外的透明区域可穿透。
                SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
            };
            Closing += delegate { if (!noSave) SaveSettings(); tray.Visible = false; tray.Dispose(); };
            timer.Interval = TimeSpan.FromMilliseconds(40); timer.Tick += Tick;
            Loaded += delegate
            {
                SetState(0, 0); timer.Start(); Say("你好，我是奶蛙。右键可以找到我的菜单。", 6000);
                if (smoke) BeginSmokeTest();
            };
            tray.Icon = System.Drawing.SystemIcons.Information;
            string iconPath = Path.Combine(folder, "milk-frog.ico");
            if (File.Exists(iconPath)) tray.Icon = new System.Drawing.Icon(iconPath);
            tray.Text = "奶蛙 · 双击显示，右键退出";
            Forms.ContextMenuStrip trayMenu = new Forms.ContextMenuStrip();
            trayMenu.Items.Add("显示奶蛙", null, delegate { Dispatcher.Invoke(new Action(Wake)); });
            trayMenu.Items.Add("隐藏奶蛙", null, delegate { Dispatcher.Invoke(new Action(delegate { Hide(); })); });
            trayStartupMenu = new Forms.ToolStripMenuItem("开机自启动");
            trayStartupMenu.CheckOnClick = true;
            trayStartupMenu.Click += delegate { Dispatcher.Invoke(new Action(delegate { SetStartupEnabled(trayStartupMenu.Checked); })); };
            trayMenu.Items.Add(trayStartupMenu);
            trayMenu.Opening += delegate { RefreshStartupMenu(); };
            trayMenu.Items.Add("退出", null, delegate { Dispatcher.Invoke(new Action(Close)); });
            tray.ContextMenuStrip = trayMenu; tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(Wake)); };
            tray.Visible = true;
            RefreshStartupMenu();
        }

        void LoadSprites()
        {
            BitmapImage atlas = new BitmapImage(); atlas.BeginInit();
            atlas.CacheOption = BitmapCacheOption.OnLoad;
            atlas.UriSource = new Uri(Path.Combine(folder, "spritesheet.png")); atlas.EndInit(); atlas.Freeze();
            if (atlas.PixelWidth != 1536 || atlas.PixelHeight != 2288) throw new InvalidDataException("奶蛙动作图尺寸不正确。");
            for (int r = 0; r < counts.Length; r++)
            {
                sprites[r] = new BitmapSource[counts[r]]; alpha[r] = new byte[counts[r]][];
                for (int c = 0; c < counts[r]; c++)
                {
                    CroppedBitmap crop = new CroppedBitmap(atlas, new Int32Rect(c * 192, r * 208, 192, 208));
                    FormatConvertedBitmap bgra = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
                    bgra.Freeze(); sprites[r][c] = bgra;
                    byte[] pixels = new byte[192 * 208 * 4]; bgra.CopyPixels(pixels, 192 * 4, 0);
                    alpha[r][c] = new byte[192 * 208];
                    for (int k = 0; k < 192 * 208; k++) alpha[r][c][k] = pixels[k * 4 + 3];
                }
            }
        }

        void ApplySize()
        {
            Width = petSize + 36; Height = petSize * 208 / 192 + 72;
            pet.Width = petSize; pet.Height = petSize * 208 / 192;
            Canvas.SetLeft(pet, 18); Canvas.SetTop(pet, 68);
            bubble.Width = Width - 12; Canvas.SetLeft(bubble, 6); Canvas.SetTop(bubble, 3);
        }
        void ResizePet(double factor)
        {
            double floor = Top + Height; petSize = Math.Max(105, Math.Min(380, petSize * factor));
            ApplySize(); Top = floor - Height; Clamp(); SaveSettings();
        }
        Rect WorkArea()
        {
            if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
            Forms.Screen screen = Forms.Screen.FromHandle(handle);
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix matrix = source != null ? source.CompositionTarget.TransformFromDevice : Matrix.Identity;
            Point topLeft = matrix.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
            Point bottomRight = matrix.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
            return new Rect(topLeft, bottomRight);
        }
        void Clamp()
        {
            Rect area = WorkArea();
            Left = Math.Max(area.Left - 6, Math.Min(area.Right - Width + 6, Left));
            Top = Math.Max(area.Top - 20, Math.Min(area.Bottom - Height + 6, Top));
        }
        void SetState(int state, int duration)
        {
            row = state; frame = 0; animationMs = 0; actionMs = duration;
            pet.Source = sprites[row][frame];
        }
        void Say(string text, int milliseconds)
        {
            speech.Text = text; speechMs = milliseconds; bubble.Visibility = Visibility.Visible;
        }
        void Tick(object sender, EventArgs e)
        {
            ticks++;
            UpdateClickThrough();
            if (speechMs > 0) { speechMs -= 40; if (speechMs <= 0) bubble.Visibility = Visibility.Collapsed; }
            if (paused || !IsVisible) return;
            if (pointerDown || menu.IsOpen) return;
            if (!resting && actionMs <= 0 && FollowGaze()) return;
            if (row >= 9) SetState(resting ? 6 : 0, 0);
            animationMs += 40;
            int delay = frame == counts[row] - 1 ? 260 : (row == 0 ? 210 : 130);
            if (resting) delay = 650;
            if (animationMs >= delay) { frame = (frame + 1) % counts[row]; animationMs = 0; pet.Source = sprites[row][frame]; }
            if (actionMs > 0)
            {
                actionMs -= 40;
                if (walkSpeed != 0)
                {
                    Left += walkSpeed * .04;
                    Rect area = WorkArea();
                    if (Left < area.Left || Left + Width > area.Right)
                    {
                        walkSpeed = -walkSpeed; row = walkSpeed > 0 ? 1 : 2;
                        Clamp();
                    }
                }
                if (actionMs <= 0) { walkSpeed = 0; SetState(resting ? 6 : 0, 0); nextActionMs = random.Next(9000, 18000); }
            }
            else if (roaming && !resting && !pet.IsMouseOver)
            {
                nextActionMs -= 40;
                if (nextActionMs <= 0)
                {
                    walkSpeed = random.Next(2) == 0 ? -35 : 35;
                    SetState(walkSpeed > 0 ? 1 : 2, random.Next(1800, 4000));
                }
            }
        }
        void OnDown(object sender, MouseButtonEventArgs e)
        {
            NativePoint p; GetCursorPos(out p);
            pressedAt = new Point(p.X, p.Y); originalLeft = Left; originalTop = Top;
            pointerDown = true; dragging = false; walkSpeed = 0; pet.CaptureMouse();
            if (e.ClickCount == 2) { Say("呱。今天也要好好休息。", 3300); SetState(3, 1400); }
            e.Handled = true;
        }
        bool FollowGaze()
        {
            NativePoint mouse; GetCursorPos(out mouse);
            Point p = PointFromScreen(new Point(mouse.X, mouse.Y));
            double dx = p.X - Width / 2, dy = p.Y - (68 + pet.Height * .18);
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 32 || distance > 420) return false;
            double angle = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
            int direction = ((int)Math.Round(angle / 22.5)) % 16;
            row = 9 + direction / 8; frame = direction % 8;
            pet.Source = sprites[row][frame];
            return true;
        }
        void OnMove(object sender, MouseEventArgs e)
        {
            if (!pointerDown) return;
            NativePoint p; GetCursorPos(out p);
            double dx = p.X - pressedAt.X, dy = p.Y - pressedAt.Y;
            if (!dragging && dx * dx + dy * dy < 25) return;
            dragging = true;
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix transform = source.CompositionTarget.TransformFromDevice;
            Vector delta = transform.Transform(new Vector(dx, dy));
            Left = originalLeft + delta.X; Top = originalTop + delta.Y;
            pet.Source = sprites[dx >= 0 ? 1 : 2][2];
        }
        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!pointerDown) return;
            bool wasDragging = dragging; pointerDown = false; dragging = false; pet.ReleaseMouseCapture();
            Clamp(); SaveSettings();
            if (wasDragging) { SetState(0, 0); Say("就待在这里陪你。", 2300); }
            else
            {
                resting = false;
                string[] words = { "呱？", "肚皮软软的。", "我在呢。", "摸摸就开心了。", "今天的我，依然很圆。" };
                SetState(random.Next(2) == 0 ? 3 : 4, 1500); Say(words[random.Next(words.Length)], 2700);
            }
            e.Handled = true;
        }
        MenuItem Item(string name, Action action)
        {
            MenuItem item = new MenuItem(); item.Header = name; item.Click += delegate { action(); }; return item;
        }
        void BuildMenu()
        {
            menu = new ContextMenu();
            menu.Items.Add(Item("摸摸奶蛙", delegate { resting = false; SetState(3, 1400); Say("嘿嘿。", 2400); }));
            menu.Items.Add(Item("跳一下", delegate { resting = false; SetState(4, 1500); }));
            menu.Items.Add(Item("给你打气", delegate { SetState(3, 1600); Say("慢慢来，你已经在前进了。", 4200); }));
            MenuItem roam = Item("自由散步", delegate { roaming = !roaming; walkSpeed = 0; SetState(0, 0); SaveSettings(); });
            roam.IsCheckable = true; roam.IsChecked = roaming; menu.Items.Add(roam);
            MenuItem rest = Item("安静休息", delegate { resting = !resting; walkSpeed = 0; SetState(resting ? 6 : 0, 0); Say(resting ? "我安静待一会儿。" : "休息好了。", 2400); });
            rest.IsCheckable = true; menu.Items.Add(rest);
            MenuItem freeze = Item("暂停动画", delegate { paused = !paused; }); freeze.IsCheckable = true; menu.Items.Add(freeze);
            startupMenu = new MenuItem(); startupMenu.Header = "开机自启动"; startupMenu.IsCheckable = true;
            startupMenu.Click += delegate { SetStartupEnabled(startupMenu.IsChecked); };
            menu.Items.Add(startupMenu);
            menu.Opened += delegate { RefreshStartupMenu(); };
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("变大一点", delegate { ResizePet(1.15); }));
            menu.Items.Add(Item("变小一点", delegate { ResizePet(1.0 / 1.15); }));
            menu.Items.Add(Item("回到屏幕右下角", delegate { Rect a = WorkArea(); Left = a.Right - Width - 30; Top = a.Bottom - Height; SaveSettings(); }));
            menu.Items.Add(Item("隐藏到托盘", Hide));
            menu.Items.Add(Item("退出奶蛙", Close));
        }
        void RefreshStartupMenu()
        {
            bool enabled = false, available = true;
            try { enabled = StartupRegistration.IsEnabled(); }
            catch (System.Security.SecurityException) { available = false; }
            catch (UnauthorizedAccessException) { available = false; }
            catch (IOException) { available = false; }
            if (startupMenu != null) { startupMenu.IsChecked = enabled; startupMenu.IsEnabled = available; }
            if (trayStartupMenu != null) { trayStartupMenu.Checked = enabled; trayStartupMenu.Enabled = available; }
        }
        void SetStartupEnabled(bool enabled)
        {
            try
            {
                StartupRegistration.SetEnabled(enabled);
                RefreshStartupMenu();
                Say(enabled ? "下次登录电脑，我会来陪你。" : "开机自启动已关闭。", 3600);
            }
            catch (Exception error)
            {
                RefreshStartupMenu();
                MessageBox.Show("开机自启动设置没有保存成功。\n" + error.Message, "奶蛙", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WakeMessage) { Wake(); handled = true; }
            if (message == 0x0021) { handled = true; return new IntPtr(3); }
            if (message == 0x007E) { Dispatcher.BeginInvoke(new Action(Clamp)); }
            if (message == 0x0084 && !pointerDown && !menu.IsOpen)
            {
                NativePoint mouse; GetCursorPos(out mouse);
                Point p = PointFromScreen(new Point(mouse.X, mouse.Y));
                if (bubble.Visibility == Visibility.Visible && p.Y < 68) return IntPtr.Zero;
                int x = (int)((p.X - 18) * 192 / petSize), y = (int)((p.Y - 68) * 192 / petSize);
                if (x < 0 || x >= 192 || y < 0 || y >= 208 || alpha[row][frame][y * 192 + x] < 24)
                { handled = true; return new IntPtr(-1); }
            }
            return IntPtr.Zero;
        }
        void UpdateClickThrough()
        {
            if (handle == IntPtr.Zero || !IsVisible) return;
            bool pass = false;
            if (!pointerDown && !menu.IsOpen)
            {
                NativePoint mouse; GetCursorPos(out mouse);
                Point p = PointFromScreen(new Point(mouse.X, mouse.Y));
                int x = (int)((p.X - 18) * 192 / petSize), y = (int)((p.Y - 68) * 192 / petSize);
                pass = x < 0 || x >= 192 || y < 0 || y >= 208 || alpha[row][frame][y * 192 + x] < 24;
                if (bubble.Visibility == Visibility.Visible && p.X >= 6 && p.X < Width - 6 && p.Y >= 3 && p.Y < 66) pass = false;
            }
            int style = GetWindowLong(handle, -20);
            int changed = pass ? style | 0x20 : style & ~0x20;
            if (style != changed) SetWindowLong(handle, -20, changed);
        }
        public void Wake() { Show(); Clamp(); }
        void SaveSettings()
        {
            if (noSave) return;
            try { File.WriteAllLines(Path.Combine(folder, "settings.txt"), new string[] { Left.ToString(CultureInfo.InvariantCulture), Top.ToString(CultureInfo.InvariantCulture), petSize.ToString(CultureInfo.InvariantCulture), roaming.ToString() }); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        void LoadSettings()
        {
            try
            {
                string file = Path.Combine(folder, "settings.txt"); if (!File.Exists(file)) return;
                string[] values = File.ReadAllLines(file); double x, y, size; bool roam;
                if (values.Length < 4 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) || !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) || !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out size) || !bool.TryParse(values[3], out roam)) return;
                if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y) || double.IsNaN(size) || double.IsInfinity(size)) return;
                petSize = Math.Max(105, Math.Min(380, size)); ApplySize(); Left = x; Top = y; roaming = roam;
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        void BeginSmokeTest()
        {
            noSave = true; roaming = false; bubble.Visibility = Visibility.Collapsed;
            int tested = 0; DispatcherTimer probe = new DispatcherTimer(); probe.Interval = TimeSpan.FromMilliseconds(700);
            probe.Tick += delegate
            {
                if (tested < 11) { SetState(tested++, 0); }
                else
                {
                    probe.Stop(); timer.Stop();
                    SetState(0, 0); UpdateLayout();
                    RenderTargetBitmap snapshot = new RenderTargetBitmap((int)Math.Ceiling(Width), (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32);
                    snapshot.Render(canvas);
                    PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(snapshot));
                    using (FileStream file = File.Create(Path.Combine(folder, "desktop-render.png"))) encoder.Save(file);
                    File.WriteAllText(Path.Combine(folder, "smoke-test.txt"), "PASS: loaded all 73 RGBA frames; exercised 11 states in a live transparent WPF window. " + DateTime.Now.ToString("s"));
                    Close();
                }
            };
            probe.Start();
        }
    }
    public static class Entry
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);
        [STAThread] public static int Main(string[] args)
        {
            bool smoke = Array.IndexOf(args, "--smoke-test") >= 0;
            bool created;
            using (Mutex mutex = new Mutex(true, smoke ? "MilkFrogDesktopSmoke" : "MilkFrogDesktopCompanion", out created))
            {
                if (!created) { PostMessage(FindWindow(null, "MilkFrogDesktopCompanion"), PetWindow.WakeMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                try { Application app = new Application(); app.Run(new PetWindow(smoke)); return 0; }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), error.ToString());
                    MessageBox.Show("奶蛙启动失败，详细原因已保存到 error.log。\n" + error.Message, "奶蛙", MessageBoxButton.OK, MessageBoxImage.Error); return 1;
                }
            }
        }
    }
}
