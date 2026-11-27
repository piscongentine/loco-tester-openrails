// Loco Tester - main window logic.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LocoTester
{
    public static class Gui
    {
        public static int Run(string[] args)
        {
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Loco Tester", MessageBoxButton.OK, MessageBoxImage.Warning);
                e.Handled = true;
            };
            Window w;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LocoTester.MainWindow.xaml"))
                w = (Window)XamlReader.Load(s);
            MainController c = new MainController(w, args);
            return app.Run(w);
        }

        public static BitmapImage Resource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LocoTester." + name))
            {
                if (s == null) return null;
                BitmapImage b = new BitmapImage();
                b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = s; b.EndInit(); b.Freeze();
                return b;
            }
        }
    }

    // ---------------------------------------------------------------- view models

    public class LocoItem : INotifyPropertyChanged
    {
        public Loco Loco;
        public List<Issue> Issues;
        public int Errors = -1, Warnings;
        public string Name { get { return Loco.DisplayName; } }
        public string Folder { get { return Loco.FolderName + "  ·  " + System.IO.Path.GetFileName(Loco.EngPath); } }
        public string Type { get { return Loco.EngineType; } }
        public Brush TypeBrush { get { return Theme.TypeBrush(Loco.EngineType); } }
        public string Extra
        {
            get
            {
                List<string> p = new List<string>();
                if (Loco.MaxSpeedMps > 0) p.Add(Math.Round(Loco.MaxSpeedMps * 3.6) + " km/h");
                if (Loco.MaxPowerW > 0) p.Add(Theme.Power(Loco.MaxPowerW));
                return string.Join("  ·  ", p.ToArray());
            }
        }
        // clean list row: badge (E / D / S), name, folder, top speed, health dot; grouped by Group
        public string Group
        {
            get
            {
                string t = (Loco.EngineType ?? "").ToLowerInvariant();
                return t.Contains("electric") ? "ELECTRIC" : t.Contains("diesel") ? "DIESEL" : t.Contains("steam") ? "STEAM" : "OTHER";
            }
        }
        public string Badge { get { string g = Group; return g == "OTHER" ? "?" : g.Substring(0, 1); } }
        public Brush BadgeBack
        {
            get
            {
                Color c = ((SolidColorBrush)Theme.TypeBrush(Loco.EngineType)).Color;
                SolidColorBrush b = new SolidColorBrush(Color.FromArgb(0x26, c.R, c.G, c.B)); b.Freeze(); return b;
            }
        }
        public string Sub { get { return Loco.FolderName; } }
        public string SpeedText { get { return Loco.MaxSpeedMps > 0 ? Math.Round(Loco.MaxSpeedMps * 3.6) + " km/h" : ""; } }
        public string Tip { get { return Loco.DisplayName + "\n" + Folder + (Extra.Length > 0 ? "\n" + Extra : ""); } }
        public Brush HealthBrush { get { return Errors < 0 ? Theme.B("#3A4A66") : Errors > 0 ? Theme.Red : Warnings > 0 ? Theme.Amber : Theme.Green; } }
        public string HealthText { get { return Errors < 0 ? "Checking…" : Errors > 0 ? Errors + " problem(s) — open Overview" : Warnings > 0 ? Warnings + " warning(s)" : "All good"; } }
        public event PropertyChangedEventHandler PropertyChanged;
        public void Changed() { if (PropertyChanged != null) { PropertyChanged(this, new PropertyChangedEventArgs("HealthBrush")); PropertyChanged(this, new PropertyChangedEventArgs("HealthText")); } }
    }

    public class IssueRow { public Brush Brush { get; set; } public string Text { get; set; } }

    public class StreamRow : INotifyPropertyChanged
    {
        public StreamPlayer P;
        public string Index { get; set; }
        public string What { get; set; }
        string file = ""; double bar; Brush row = Theme.B("#0E1828");
        public string File { get { return file; } set { if (file != value) { file = value; N("File"); } } }
        public double Bar { get { return bar; } set { if (Math.Abs(bar - value) > 0.5) { bar = value; N("Bar"); } } }
        public Brush RowBrush { get { return row; } set { if (row != value) { row = value; N("RowBrush"); } } }
        public event PropertyChangedEventHandler PropertyChanged;
        void N(string p) { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(p)); }
    }

    public static class Theme
    {
        public static readonly Brush Red = B("#F05252"), Amber = B("#F5B83D"), Green = B("#3DD68C"), Teal = B("#22C3B0"), Accent = B("#F7811E"), Muted = B("#8C9DBA");
        public static Brush B(string hex) { SolidColorBrush b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        public static Brush TypeBrush(string t)
        {
            string l = (t ?? "").ToLowerInvariant();
            if (l.Contains("electric")) return B("#5AB4FF");
            if (l.Contains("diesel")) return B("#F5B83D");
            if (l.Contains("steam")) return B("#C9A7FF");
            return B("#8C9DBA");
        }
        public static string Power(double w) { return w >= 1e6 ? (w / 1e6).ToString("0.0#") + " MW" : Math.Round(w / 1000) + " kW"; }
    }

    // ---------------------------------------------------------------- deck

    class DeckSpec
    {
        public string Id, Glyph, Label, Key;
        public bool Hold, Toggle;
        public int[] Triggers = new int[0];
        public ButtonBase Button;
        public TextBlock LabelText;
    }

    // ---------------------------------------------------------------- controller

    public class MainController
    {
        readonly Window w;
        readonly AudioEngine audio = new AudioEngine();
        readonly ObservableCollection<LocoItem> items = new ObservableCollection<LocoItem>();
        ICollectionView view;
        string trainset, orRoot;
        LocoItem current;
        SoundSim sim;
        bool simCab;
        readonly SimState state = new SimState();
        LocoViewer viewer;
        CabFile cab;
        int cabView;
        CancellationTokenSource loadCts;
        readonly Dictionary<string, BuiltModel> modelCache = new Dictionary<string, BuiltModel>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> modelOrder = new List<string>();
        readonly ObservableCollection<StreamRow> streamRows = new ObservableCollection<StreamRow>();
        readonly ObservableCollection<string> eventLog = new ObservableCollection<string>();
        readonly List<DeckSpec> deck = new List<DeckSpec>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastT, simAccum, uiAccum;
        readonly AnimState anim = new AnimState();
        Settings settings = Settings.Load();
        int healthDone;

        // live controls
        double speed;            // m/s
        double throttle;         // 0..1
        double brake;            // 0..1
        double rpm;              // 0..1, follows the throttle like a diesel revving up / down
        bool brakeHeld, panto1, panto2, wipers, doors, mirrors, cabLight, power, hornHeld, sanderHeld, emergency, dynHeld, coupled;
        // engine power: off when a loco is chosen; Power starts the engine (cranking, then idle), Stop / Power shut it down
        bool simReady, startPending, starting, shuttingDown;
        DispatcherTimer toastTimer;
        int reverser = 1;        // 1 forward, 0 neutral, -1 reverse
        int lastDir = 1;
        double dyn;              // dynamic brake 0..1
        int headlight;           // 0 off 1 dim 2 bright
        int runPhase;            // 0 idle, 1 accel, 2 cruise, 3 brake
        double runTimer, runTarget;
        bool manualVars;
        bool sliderLock;
        Slider vSpeed, v1, v2, v3, vBrake;

        public MainController(Window window, string[] args)
        {
            w = window;
            w.Icon = Gui.Resource("app.ico");
            BitmapImage logo = Gui.Resource("logo.png");
            Img("HeaderLogo").Source = logo;
            Img("AboutLogo").Source = logo;
            Txt("HeaderVersion").Text = "v" + AppInfo.Version + " · " + AppInfo.VersionName + " · by " + AppInfo.Author;
            Txt("AboutVersion").Text = "Version " + AppInfo.Version + "  “" + AppInfo.VersionName + "”";
            Txt("FeatureText").Text = Features;
            Txt("KeysText").Text = Keys;
            if (audio.InitError != null) Status("Sound output unavailable: " + audio.InitError);
            if (LocoViewer.RenderTier < 2) Status("Your graphics card / driver gives only limited 3D acceleration (WPF tier " + LocoViewer.RenderTier + "). Update the graphics driver for smooth HD 3D.");

            viewer = new LocoViewer((Border)F("ViewHost"));
            ListBox list = (ListBox)F("LocoList");
            view = CollectionViewSource.GetDefaultView(items);
            view.Filter = Filter;
            view.SortDescriptions.Add(new SortDescription("Group", ListSortDirection.Ascending));
            view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            view.GroupDescriptions.Add(new PropertyGroupDescription("Group"));
            list.ItemsSource = view;
            list.SelectionChanged += delegate { LocoItem it = list.SelectedItem as LocoItem; if (it != null && it != current) Select(it); };
            ((ItemsControl)F("StreamList")).ItemsSource = streamRows;
            ((ListBox)F("EventLog")).ItemsSource = eventLog;

            TextBox search = (TextBox)F("SearchBox");
            search.TextChanged += delegate { Txt("SearchHint").Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; view.Refresh(); UpdateCount(); };
            foreach (string f in new[] { "FAll", "FElectric", "FDiesel", "FSteam", "FProblems" }) ((RadioButton)F(f)).Checked += delegate { view.Refresh(); UpdateCount(); };

            TextBox folder = (TextBox)F("FolderBox");
            folder.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter) LoadFolder(folder.Text); };
            Btn("BrowseBtn").Click += delegate { Browse(); };
            Btn("RescanBtn").Click += delegate { LoadFolder(folder.Text); };
            Btn("OpenFolderBtn").Click += delegate { if (current != null) Process.Start("explorer.exe", "/select,\"" + current.Loco.EngPath + "\""); };
            Btn("OpenEngBtn").Click += delegate { if (current != null) Process.Start("notepad.exe", "\"" + current.Loco.EngPath + "\""); };
            Btn("RepairBtn").Click += delegate { Repair(); };
            Btn("SwapBtn").Click += delegate { OpenSwap(); };
            Btn("SysCheckBtn").Click += delegate { RunSysCheck(); };
            Btn("UpdateBtn").Click += delegate { CheckForUpdates(); };

            foreach (string t in new[] { "TabOverview", "Tab3D", "TabCab", "TabSound", "TabAbout" }) ((RadioButton)F(t)).Checked += delegate { ShowPage(); };

            Btn("CamFront").Click += delegate { viewer.Preset("front"); };
            Btn("CamSide").Click += delegate { viewer.Preset("side"); };
            Btn("CamRear").Click += delegate { viewer.Preset("rear"); };
            Btn("CamTop").Click += delegate { viewer.Preset("top"); };
            Btn("Cam34").Click += delegate { viewer.Preset("3/4"); };
            Btn("CamPov").Click += delegate { if (!viewer.CabPov()) Status("No cab file: using an estimated driver position"); };
            OnToggle("HdTgl", delegate { LocoViewer.HighDetail = Tgl("HdTgl").IsChecked == true; modelCache.Clear(); modelOrder.Clear(); if (current != null) LoadModel(current.Loco); });
            OnToggle("NightTgl", delegate { viewer.Night = Tgl("NightTgl").IsChecked == true; });
            OnToggle("ShedTgl", delegate { viewer.Environment = Tgl("ShedTgl").IsChecked == true ? "shed" : "open"; viewer.BuildEnvironment(true); });
            OnToggle("SpinTgl", delegate { viewer.Turntable = Tgl("SpinTgl").IsChecked == true; });
            OnToggle("RearUnitTgl", delegate { anim.RearUnit = Tgl("RearUnitTgl").IsChecked == true; });

            foreach (string c in new[] { "CabFront", "CabLeft", "CabRight", "CabRear" }) ((RadioButton)F(c)).Checked += delegate { CabViewChanged(); };
            OnToggle("CabNightTgl", delegate { ShowCabImage(); });
            Tgl("CabLightTgl").PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (current != null && !NeedPower()) e.Handled = true; };
            Tgl("CabLightTgl").Click += delegate { SetCabLight(Tgl("CabLightTgl").IsChecked == true); };
            OnToggle("CabHotTgl", delegate { BuildHotspots(); });

            ((RadioButton)F("EarOutside")).Checked += delegate { if (!IsTab("TabCab")) StartSound(false); };
            ((RadioButton)F("EarCab")).Checked += delegate { StartSound(true); };

            Slider vol = (Slider)F("MasterVol");
            vol.Value = settings.Volume;
            audio.MasterVolume = (float)vol.Value;
            vol.ValueChanged += delegate { audio.MasterVolume = (float)vol.Value; settings.Volume = vol.Value; };

            Slider sp = (Slider)F("SpeedSlider");
            sp.ValueChanged += delegate
            {
                if (sliderLock) return;
                if (!power && sp.Value > 0 && current != null) { sliderLock = true; sp.Value = 0; sliderLock = false; NeedPower(); return; }
                runPhase = 0;
            };
            Slider th = (Slider)F("ThrottleSlider");
            th.ValueChanged += delegate
            {
                if (sliderLock) return;
                if (!power && th.Value > 0 && current != null) { sliderLock = true; th.Value = 0; sliderLock = false; NeedPower(); return; }
                double old = throttle; throttle = th.Value / 100.0;
                if (Math.Abs(old - throttle) > 0.04 && sim != null) sim.Fire(TriggerNames.ThrottleChange);
                manualVars = false;
            };
            Btn("RunBtn").Click += delegate { StartRun(); };
            Btn("StopBtn").Click += delegate { StopAll(); };

            BuildDeck();
            BuildVarPanel();

            w.PreviewKeyDown += OnKeyDown;
            w.PreviewKeyUp += OnKeyUp;
            w.SourceInitialized += delegate { DarkTitleBar(); };
            w.Closing += delegate
            {
                settings.Width = w.ActualWidth; settings.Height = w.ActualHeight; settings.Maximized = w.WindowState == WindowState.Maximized;
                settings.Save();
                audio.Dispose();
            };
            if (settings.Width > 600) { w.Width = settings.Width; w.Height = settings.Height; }
            if (settings.Maximized) w.WindowState = WindowState.Maximized;
            CompositionTarget.Rendering += OnFrame;

            // command line: [folder] [--page=overview|3d|cab|sound|about] [--throttle=0-100]
            bool runCheck = false, runUpdate = false;
            string start = null, page = null, startCam = null; double startThrottle = -1, startSpeed = 0; bool startNight = false, startPanto = false, startReverse = false, startPower = false; int startLights = 0;
            foreach (string a in args)
            {
                if (a.StartsWith("--page=")) page = a.Substring(7).ToLowerInvariant();
                else if (a.StartsWith("--throttle=")) startThrottle = Stf.ParseNumber(a.Substring(11), -1);
                else if (a == "--night") startNight = true;
                else if (a == "--power") startPower = true;
                else if (a == "--syscheck") { page = "about"; runCheck = true; }
                else if (a == "--updates") { page = "about"; runUpdate = true; }
                else if (a == "--panto") startPanto = true;
                else if (a.StartsWith("--lights=")) startLights = (int)Stf.ParseNumber(a.Substring(9), 0);
                else if (a.StartsWith("--speed=")) startSpeed = Stf.ParseNumber(a.Substring(8), 0);
                else if (a == "--reverse") startReverse = true;
                else if (a.StartsWith("--camera=")) startCam = a.Substring(9);
                else if (Directory.Exists(a)) start = a;
            }
            bool fromArgs = start != null;
            if (start == null) start = settings.Folder;
            if (string.IsNullOrEmpty(start) && Directory.Exists(@"C:\OR\TRAINS\TRAINSET")) start = @"C:\OR";
            if (string.IsNullOrEmpty(start))
            {
                // first start: look for an Open Rails / MSTS folder on this PC
                foreach (SysCheck.Result r in SysCheck.Run()) { }
                if (SysCheck.OpenRailsFolders.Count > 0) start = SysCheck.OpenRailsFolders[0];
            }
            folder.Text = start ?? "";
            w.Loaded += delegate
            {
                if (!string.IsNullOrEmpty(start)) LoadFolder(start, !fromArgs); else Status("Choose your Open Rails folder with Browse…");
                string[] names = { "overview", "3d", "cab", "sound", "about" };
                string[] tabs = { "TabOverview", "Tab3D", "TabCab", "TabSound", "TabAbout" };
                int pi = page != null ? Array.IndexOf(names, page) : -1;
                if (pi >= 0) ((RadioButton)F(tabs[pi])).IsChecked = true;
                if (runCheck) RunSysCheck();
                if (runUpdate) CheckForUpdates();
                // test / shortcut options: --power --night --panto --lights=1|2 --speed=kmh --throttle=% --reverse --camera=front|side|rear|top|pov
                // (anything that drives the loco switches the power on first)
                DispatcherTimer once = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                once.Tick += delegate
                {
                    once.Stop();
                    if (current == null) return;
                    if (startNight) Tgl("NightTgl").IsChecked = true;
                    if ((startPower || startReverse || startPanto || startLights > 0 || startSpeed > 0 || startThrottle >= 0) && !power) DeckDown("power");
                    if (startThrottle >= 0) ((Slider)F("ThrottleSlider")).Value = startThrottle;
                    if (startReverse) DeckDown("reverser");
                    if (startReverse) DeckDown("reverser");
                    if (startPanto) DeckDown("panto1");
                    for (int i = 0; i < startLights; i++) DeckDown("headlight");
                    if (startSpeed > 0) ((Slider)F("SpeedSlider")).Value = startSpeed;
                    if (startCam == "pov") viewer.CabPov(); else if (startCam != null) viewer.Preset(startCam);
                };
                once.Start();
                ShowPage();
            };
        }

        // ---------------------------------------------------------------- helpers

        object F(string n) { return w.FindName(n); }
        void OnToggle(string n, Action a) { ToggleButton t = Tgl(n); t.Checked += delegate { a(); }; t.Unchecked += delegate { a(); }; }
        TextBlock Txt(string n) { return (TextBlock)w.FindName(n); }
        Button Btn(string n) { return (Button)w.FindName(n); }
        ToggleButton Tgl(string n) { return (ToggleButton)w.FindName(n); }
        Image Img(string n) { return (Image)w.FindName(n); }
        bool IsTab(string n) { return ((RadioButton)F(n)).IsChecked == true; }
        void Status(string s) { Txt("StatusText").Text = s; }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        void DarkTitleBar()
        {
            try { int on = 1; IntPtr h = new WindowInteropHelper(w).Handle; DwmSetWindowAttribute(h, 20, ref on, 4); int cap = 0x002D1A11; DwmSetWindowAttribute(h, 35, ref cap, 4); } catch { }
        }

        // ---------------------------------------------------------------- folder + list

        void Browse()
        {
            using (System.Windows.Forms.FolderBrowserDialog d = new System.Windows.Forms.FolderBrowserDialog())
            {
                d.Description = "Choose your Open Rails / MSTS folder (or its TRAINS\\TRAINSET folder)";
                d.SelectedPath = ((TextBox)F("FolderBox")).Text;
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { ((TextBox)F("FolderBox")).Text = d.SelectedPath; LoadFolder(d.SelectedPath); }
            }
        }

        bool saveFolder = true;

        void LoadFolder(string picked) { LoadFolder(picked, true); }

        void LoadFolder(string picked, bool remember)
        {
            string ts, root;
            if (!LocoScanner.ResolveFolders(picked, out ts, out root)) { Status("Folder not found: " + picked); return; }
            trainset = ts; orRoot = root;
            saveFolder = remember;
            if (remember) settings.Folder = picked;
            Status("Scanning " + ts + " …");
            Stopwatch sw = Stopwatch.StartNew();
            string keep = current != null ? current.Loco.EngPath : settings.LastLoco;
            Task.Factory.StartNew(delegate { return LocoScanner.Scan(ts, root, null); }).ContinueWith(delegate(Task<List<Loco>> t)
            {
                items.Clear();
                if (t.Exception != null) { Status("Scan failed: " + t.Exception.InnerException.Message); return; }
                LocoItem sel = null;
                foreach (Loco l in t.Result) { LocoItem it = new LocoItem(); it.Loco = l; items.Add(it); if (l.EngPath == keep) sel = it; }
                UpdateCount();
                Status("Found " + items.Count + " locomotives in " + sw.ElapsedMilliseconds + " ms");
                if (sel == null && items.Count > 0) sel = items[0];
                if (sel != null) { ((ListBox)F("LocoList")).SelectedItem = sel; ((ListBox)F("LocoList")).ScrollIntoView(sel); }
                CheckHealthAll();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void CheckHealthAll()
        {
            healthDone = 0;
            LocoItem[] arr = new LocoItem[items.Count];
            items.CopyTo(arr, 0);
            string root = orRoot;
            Dispatcher disp = w.Dispatcher;
            Task.Factory.StartNew(delegate
            {
                Parallel.ForEach(arr, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, delegate(LocoItem it)
                {
                    List<Issue> iss = Doctor.Diagnose(it.Loco, root);
                    disp.BeginInvoke(new Action(delegate { SetIssues(it, iss); healthDone++; Txt("StatusRight").Text = items.Count + " locos · health checked " + healthDone + "/" + items.Count; }), DispatcherPriority.Background);
                });
            });
        }

        void SetIssues(LocoItem it, List<Issue> iss)
        {
            it.Issues = iss;
            it.Errors = 0; it.Warnings = 0;
            foreach (Issue i in iss) { if (i.Severity == Severity.Error) it.Errors++; else if (i.Severity == Severity.Warning) it.Warnings++; }
            it.Changed();
            if (it == current) ShowIssues();
            if (((RadioButton)F("FProblems")).IsChecked == true) view.Refresh();
        }

        bool Filter(object o)
        {
            LocoItem it = (LocoItem)o;
            string q = ((TextBox)F("SearchBox")).Text.Trim();
            if (q.Length > 0)
            {
                string hay = (it.Loco.DisplayName + " " + it.Loco.FolderName + " " + System.IO.Path.GetFileName(it.Loco.EngPath) + " " + it.Loco.EngineType).ToLowerInvariant();
                foreach (string part in q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) if (!hay.Contains(part)) return false;
            }
            string t = (it.Loco.EngineType ?? "").ToLowerInvariant();
            if (((RadioButton)F("FElectric")).IsChecked == true) return t.Contains("electric");
            if (((RadioButton)F("FDiesel")).IsChecked == true) return t.Contains("diesel");
            if (((RadioButton)F("FSteam")).IsChecked == true) return t.Contains("steam");
            if (((RadioButton)F("FProblems")).IsChecked == true) return it.Errors > 0;
            return true;
        }

        void UpdateCount()
        {
            int n = 0; foreach (object o in view) n++;
            Txt("ListCount").Text = n == items.Count ? items.Count + " locomotives" : n + " of " + items.Count + " locomotives";
        }

        // ---------------------------------------------------------------- selection

        void Select(LocoItem it)
        {
            StopRun();
            current = it;
            if (saveFolder) settings.LastLoco = it.Loco.EngPath;
            Loco l = it.Loco;
            Txt("LocoTitle").Text = l.DisplayName;
            Txt("LocoSub").Text = l.EngineType + "  ·  " + l.FolderName + "\\" + System.IO.Path.GetFileName(l.EngPath);
            ((Slider)F("SpeedSlider")).Maximum = Math.Max(60, Math.Ceiling((l.MaxSpeedMps > 1 ? l.MaxSpeedMps * 3.6 : 160) / 10) * 10);
            ResetControls();
            ApplyDeckForType(l);
            ShowStats();
            ShowIssues();
            if (it.Issues == null)
            {
                string root = orRoot;
                Task.Factory.StartNew(delegate { return Doctor.Diagnose(l, root); }).ContinueWith(delegate(Task<List<Issue>> t) { if (t.Exception == null) SetIssues(it, t.Result); }, TaskScheduler.FromCurrentSynchronizationContext());
            }
            cab = l.CabViewPath(0) != null ? CabFile.Load(l.CabViewPath(0)) : null;
            cabView = 0;
            ((RadioButton)F("CabFront")).IsChecked = true;
            ((RadioButton)F("CabRear")).IsEnabled = l.CabViewPath(1) != null;
            UpdateCabLightAvailability();
            StartSound(IsTab("TabCab") || ((RadioButton)F("EarCab")).IsChecked == true);
            LoadModel(l);
            if (IsTab("TabCab")) CabViewChanged();
            Status("Engine is off. Press ⏻ Power (O) to start it.");
        }

        void ResetControls()
        {
            speed = 0; throttle = 0; brake = 0; rpm = 0; dyn = 0; brakeHeld = false; runPhase = 0; emergency = false; dynHeld = false; coupled = false; reverser = 1; lastDir = 1;
            panto1 = panto2 = wipers = doors = mirrors = cabLight = false; headlight = 0; power = false; hornHeld = false; sanderHeld = false;
            startPending = starting = shuttingDown = false;
            anim.Panto1 = anim.Panto2 = anim.Wiper = anim.Doors = anim.Mirrors = 0; anim.WheelAngle = 0;
            sliderLock = true;
            ((Slider)F("SpeedSlider")).Value = 0; ((Slider)F("ThrottleSlider")).Value = 0;
            sliderLock = false;
            foreach (DeckSpec d in deck) { ToggleButton tb = d.Button as ToggleButton; if (tb != null) tb.IsChecked = false; }
            UpdateDeckLabels();
            Tgl("CabLightTgl").IsChecked = false;
        }

        void ShowStats()
        {
            Loco l = current.Loco;
            UniformGrid g = (UniformGrid)F("StatGrid");
            g.Children.Clear();
            int lead = 0; foreach (LightDef d in l.Lights) if (d.Unit == 0 || d.Unit == 2 || d.Unit == 3) lead++;
            int sounds = 0; foreach (SoundRef s in l.Sounds) if (s.ResolvedPath != null) sounds++;
            g.Children.Add(Stat("TYPE", l.EngineType, l.WagonName ?? ""));
            g.Children.Add(Stat("TOP SPEED", l.MaxSpeedMps > 0 ? Math.Round(l.MaxSpeedMps * 3.6) + " km/h" : "—", l.MaxSpeedMps > 0 ? Math.Round(l.MaxSpeedMps * 2.23694) + " mph" : ""));
            g.Children.Add(Stat("POWER", l.MaxPowerW > 0 ? Theme.Power(l.MaxPowerW) : "—", l.MaxPowerW > 0 ? Math.Round(l.MaxPowerW / 745.7) + " hp" : ""));
            g.Children.Add(Stat("WHEEL Ø", (l.WheelRadius * 2).ToString("0.00") + " m", "radius " + l.WheelRadius.ToString("0.000") + " m"));
            g.Children.Add(Stat("LIGHTS", l.Lights.Count.ToString(), lead + " on the leading end"));
            g.Children.Add(Stat("CAB VIEWS", l.CabViews.Count.ToString(), l.CabViews.Count > 0 ? l.CabViews[0] : "none"));
            g.Children.Add(Stat("SOUND FILES", sounds + " / " + l.Sounds.Count, sounds == l.Sounds.Count && sounds > 0 ? "all found" : "some missing"));
            g.Children.Add(Stat("3D SHAPE", l.ShapePath != null ? "found" : "missing", l.ShapeFile ?? ""));

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Engine file:  " + System.IO.Path.GetFileName(l.EngPath));
            sb.AppendLine("Shape:  " + (l.ShapeFile ?? "—") + (l.ShapePath == null ? "  ✗" : "  ✓"));
            foreach (SoundRef s in l.Sounds) sb.AppendLine((s.Section == "Engine" ? "Cab sound:  " : "Outside sound:  ") + s.FileName + (s.ResolvedPath == null ? "  ✗" : "  ✓"));
            for (int i = 0; i < l.CabViews.Count; i++) sb.AppendLine("Cab view " + (i + 1) + ":  " + l.CabViews[i] + (l.CabViewPath(i) == null ? "  ✗" : "  ✓"));
            sb.AppendLine("Modified:  " + l.Modified.ToString("dd MMM yyyy HH:mm"));
            Txt("FilesText").Text = sb.ToString().TrimEnd();
            Txt("RepairReport").Text = "";
        }

        Border Stat(string title, string value, string sub)
        {
            Border b = new Border();
            b.Style = (Style)w.FindResource("Card");
            b.Margin = new Thickness(5);
            b.Padding = new Thickness(16, 12, 16, 12);
            StackPanel s = new StackPanel();
            s.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted });
            s.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            s.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = Theme.B("#6F82A3"), TextTrimming = TextTrimming.CharacterEllipsis });
            b.Child = s;
            return b;
        }

        void ShowIssues()
        {
            List<IssueRow> rows = new List<IssueRow>();
            if (current == null) return;
            if (current.Issues == null) { Txt("HealthSummary").Text = "checking…"; ((ItemsControl)F("IssueList")).ItemsSource = rows; return; }
            foreach (Issue i in current.Issues)
                rows.Add(new IssueRow { Brush = i.Severity == Severity.Error ? Theme.Red : i.Severity == Severity.Warning ? Theme.Amber : Theme.Muted, Text = i.Message + (i.Fixable ? "" : "  (not auto-fixable)") });
            if (rows.Count == 0) rows.Add(new IssueRow { Brush = Theme.Green, Text = "No problems found. Sounds, shape and cab files are all in place." });
            ((ItemsControl)F("IssueList")).ItemsSource = rows;
            Txt("HealthSummary").Text = current.Errors + " errors · " + current.Warnings + " warnings";
            int fixable = 0; foreach (Issue i in current.Issues) if (i.Fixable) fixable++;
            Btn("RepairBtn").IsEnabled = fixable > 0;
        }

        // ---------------------------------------------------------------- repair

        void Repair()
        {
            if (current == null) return;
            LocoItem it = current;
            if (MessageBox.Show(w, "Loco Tester will back up this loco's .eng and .sms files, then fix the sound problems it found.\n\nThe backup goes into:\n" + System.IO.Path.Combine(it.Loco.Folder, "LocoTester_Backup") + "\n\nRepair now?",
                "Repair sounds", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            StopAll();
            Status("Repairing " + it.Loco.DisplayName + " …");
            Btn("RepairBtn").IsEnabled = false;
            List<Loco> donors = new List<Loco>(); foreach (LocoItem x in items) donors.Add(x.Loco);
            string root = orRoot;
            Task.Factory.StartNew(delegate { return Doctor.Repair(it.Loco, root, donors); }).ContinueWith(delegate(Task<RepairResult> t)
            {
                if (t.Exception != null) { Status("Repair failed: " + t.Exception.InnerException.Message); Btn("RepairBtn").IsEnabled = true; return; }
                RepairResult r = t.Result;
                WavData.ClearCache();
                it.Loco = Loco.Load(it.Loco.EngPath, root);
                Doctor.ForgetHealth(it.Loco);
                SetIssues(it, r.After);
                if (it == current) { Select(it); }
                Txt("RepairReport").Text = "REPAIR REPORT\n" + string.Join("\n", r.Actions.ToArray()) + "\n\nErrors: " + r.ErrorsBefore + " → " + r.ErrorsAfter;
                Status("Repair finished: errors " + r.ErrorsBefore + " → " + r.ErrorsAfter);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void OpenSwap()
        {
            if (current == null) { Status("Choose a locomotive first"); return; }
            SwapWindow sw;
            try { sw = new SwapWindow(w, current.Loco, orRoot, audio); sw.ShowDialog(); }
            catch (Exception ex) { Status("Replace sounds failed: " + ex.Message); Log("Replace sounds failed: " + ex); return; }
            if (!sw.Changed) return;
            // reload the sound files; keep the engine running if it was
            bool wasOn = power;
            StartSound(simCab);
            if (wasOn) { power = true; SetToggle("power", true); }
            Doctor.ForgetHealth(current.Loco);
            Log("Sounds changed: reloaded " + System.IO.Path.GetFileName(current.Loco.SmsPath(simCab) ?? ""));
            Status("Sounds changed. Try them with the Horn / Bell / Sander buttons.");
        }

        // ---------------------------------------------------------------- pages

        void ShowPage()
        {
            string[] pages = { "PageOverview", "Page3D", "PageCab", "PageSound", "PageAbout" };
            string[] tabs = { "TabOverview", "Tab3D", "TabCab", "TabSound", "TabAbout" };
            for (int i = 0; i < pages.Length; i++) ((UIElement)F(pages[i])).Visibility = IsTab(tabs[i]) ? Visibility.Visible : Visibility.Collapsed;
            ((UIElement)F("Deck")).Visibility = IsTab("TabAbout") ? Visibility.Collapsed : Visibility.Visible;
            if (current == null) return;
            bool wantCab = IsTab("TabCab") || ((RadioButton)F("EarCab")).IsChecked == true;
            if (wantCab != simCab) StartSound(wantCab);
            if (IsTab("TabCab")) CabViewChanged();
        }

        // ---------------------------------------------------------------- 3D

        void LoadModel(Loco l)
        {
            if (loadCts != null) loadCts.Cancel();
            loadCts = new CancellationTokenSource();
            CancellationToken ct = loadCts.Token;
            viewer.Clear();
            Txt("ViewInfo").Text = "";
            if (l.ShapePath == null) { Txt("ViewLoading").Text = "No 3D shape for this loco"; Txt("ViewLoading").Visibility = Visibility.Visible; return; }
            BuiltModel cached;
            if (modelCache.TryGetValue(l.ShapePath, out cached)) { ShowModel(l, cached, true); Prefetch(); return; }
            Txt("ViewLoading").Text = "Loading 3D model…";
            Txt("ViewLoading").Visibility = Visibility.Visible;
            Task.Factory.StartNew(delegate { return LocoViewer.Build(l, ct); }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ContinueWith(delegate(Task<BuiltModel> t)
            {
                if (t.IsCanceled || ct.IsCancellationRequested) return;
                if (t.Exception != null) { Txt("ViewLoading").Text = "3D model failed: " + t.Exception.InnerException.Message; return; }
                Remember(l.ShapePath, t.Result);
                if (current != null && current.Loco == l) { ShowModel(l, t.Result, false); Prefetch(); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Remember(string key, BuiltModel b)
        {
            if (!modelCache.ContainsKey(key)) modelOrder.Add(key);
            modelCache[key] = b;
            while (modelOrder.Count > 8) { modelCache.Remove(modelOrder[0]); modelOrder.RemoveAt(0); }
        }

        /// <summary>Builds the next loco in the list in the background so its 3D view opens instantly.</summary>
        void Prefetch()
        {
            ListBox list = (ListBox)F("LocoList");
            int i = list.SelectedIndex;
            if (i < 0 || i + 1 >= list.Items.Count) return;
            LocoItem next = list.Items[i + 1] as LocoItem;
            if (next == null || next.Loco.ShapePath == null || modelCache.ContainsKey(next.Loco.ShapePath)) return;
            Loco nl = next.Loco;
            Task.Factory.StartNew(delegate { return LocoViewer.Build(nl, CancellationToken.None); }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)
                .ContinueWith(delegate(Task<BuiltModel> t) { if (t.Exception == null && t.Result.Shape.Error == null) Remember(nl.ShapePath, t.Result); }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowModel(Loco l, BuiltModel b, bool fromCache)
        {
            if (b.Shape.Error != null) { Txt("ViewLoading").Text = b.Shape.Error; Txt("ViewLoading").Visibility = Visibility.Visible; return; }
            Txt("ViewLoading").Visibility = Visibility.Collapsed;
            viewer.Show(l, b, cab);
            Txt("ViewInfo").Text = b.Shape.Triangles.ToString("N0") + " triangles · " + b.Textures + " textures" + (b.MissingTextures > 0 ? " (" + b.MissingTextures + " missing)" : "") +
                " · " + (fromCache ? "from cache" : "built in " + b.BuildMs + " ms") + "\n" + viewer.Capabilities();
        }

        // ---------------------------------------------------------------- cab

        void CabViewChanged()
        {
            if (current == null) return;
            Loco l = current.Loco;
            bool rear = ((RadioButton)F("CabRear")).IsChecked == true;
            string cvf = l.CabViewPath(rear ? 1 : 0);
            cab = cvf != null ? CabFile.Load(cvf) : null;
            cabView = rear ? 0 : ((RadioButton)F("CabLeft")).IsChecked == true ? 1 : ((RadioButton)F("CabRight")).IsChecked == true ? 2 : 0;
            if (cab != null && cabView >= cab.Views.Count) cabView = 0;
            ShowCabImage();
        }

        void ShowCabImage()
        {
            Image img = Img("CabImage");
            bool none = cab == null || cab.Views.Count == 0;
            Txt("CabEmpty").Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            if (none) { img.Source = null; ((Canvas)F("CabHotspots")).Children.Clear(); Txt("CabInfo").Text = ""; return; }
            string p = cab.ImagePath(cabView, Tgl("CabNightTgl").IsChecked == true, cabLight);
            img.Source = p != null ? AceReader.LoadCached(p, 2048) : null;
            BuildHotspots();
            bool hasLight = cab.HasCabLight(cabView);
            Txt("CabInfo").Text = System.IO.Path.GetFileName(cab.Path) + " · view " + (cabView + 1) + "/" + cab.Views.Count + " · " + cab.Controls.Count + " controls · cab light images: " + (hasLight ? "yes" : "none") +
                (cabLight && !hasLight ? "  ⚠ cab light has no CABLIGHT image, so it shows no change in game" : "");
        }

        bool hasCabLight;

        /// <summary>Checks the loco's cab views (front and rear) for CABLIGHT images. Without them the cab light
        /// would show no change in Open Rails, so the Cab light buttons are greyed out.</summary>
        void UpdateCabLightAvailability()
        {
            hasCabLight = false;
            if (current != null)
                for (int i = 0; i < Math.Max(1, current.Loco.CabViews.Count); i++)
                {
                    string p = current.Loco.CabViewPath(i);
                    if (p == null) continue;
                    CabFile c = CabFile.Load(p);
                    if (c.HasAnyCabLight()) { hasCabLight = true; break; }
                }
            string tip = hasCabLight ? "Cab light: turns the lit cab on and off  (C)" : "This cab has no cab light (no CABLIGHT images in its cab view folder)";
            foreach (DeckSpec d in deck)
                if (d.Id == "cablight")
                {
                    d.Button.IsEnabled = hasCabLight;
                    d.Button.ToolTip = tip;
                    ToolTipService.SetShowOnDisabled(d.Button, true);
                    d.LabelText.Text = hasCabLight ? "Cab light" : "No cab light";
                }
            ToggleButton t = Tgl("CabLightTgl");
            t.IsEnabled = hasCabLight;
            t.ToolTip = tip;
            ToolTipService.SetShowOnDisabled(t, true);
            if (!hasCabLight && cabLight) SetCabLight(false);
        }

        void SetCabLight(bool on)
        {
            if (on && !hasCabLight) { Toast("No cab light", "This loco's cab has no CABLIGHT images, so the cab light shows no change."); on = false; }
            cabLight = on;
            Tgl("CabLightTgl").IsChecked = on;
            foreach (DeckSpec d in deck) if (d.Id == "cablight") ((ToggleButton)d.Button).IsChecked = on;
            if (cab != null) ShowCabImage();
            Log(on ? "Cab light ON" : "Cab light OFF");
        }

        void BuildHotspots()
        {
            Canvas c = (Canvas)F("CabHotspots");
            c.Children.Clear();
            if (cab == null || cabView != 0) return;   // controls belong to the front view
            bool show = Tgl("CabHotTgl").IsChecked == true;
            foreach (CabControl cc in cab.Controls)
            {
                string action = CabAction(cc.Type);
                Rectangle r = new Rectangle();
                r.Width = Math.Max(6, cc.W); r.Height = Math.Max(6, cc.H);
                Canvas.SetLeft(r, cc.X); Canvas.SetTop(r, cc.Y);
                r.RadiusX = 4; r.RadiusY = 4;
                r.Fill = Brushes.Transparent;
                r.StrokeThickness = 1.2;
                r.Stroke = show ? (action != null ? Theme.B("#AAF7811E") : Theme.B("#4422C3B0")) : Brushes.Transparent;
                r.ToolTip = cc.Kind + ": " + cc.Type + (action != null ? "\nClick to test" : "");
                r.Cursor = action != null ? Cursors.Hand : Cursors.Arrow;
                if (action != null)
                {
                    string a = action;
                    r.MouseEnter += delegate { r.Fill = Theme.B("#33F7811E"); };
                    r.MouseLeave += delegate { r.Fill = Brushes.Transparent; if (IsHold(a)) DeckUp(a); };
                    r.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { DeckDown(a); e.Handled = true; };
                    r.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { DeckUp(a); e.Handled = true; };
                    r.MouseRightButtonDown += delegate(object s, MouseButtonEventArgs e) { if (a == "throttle") SetThrottle(throttle - 0.1); e.Handled = true; };
                }
                c.Children.Add(r);
            }
        }

        static string CabAction(string type)
        {
            string t = (type ?? "").ToUpperInvariant();
            if (t.Contains("HORN")) return "horn";
            if (t.Contains("BELL")) return "bell";
            if (t.Contains("SAND")) return "sander";
            if (t.Contains("WIPER")) return "wipers";
            if (t.Contains("PANTOGRAPH2") || t.Contains("PANTO2")) return "panto2";
            if (t.Contains("PANTO")) return "panto1";
            if (t.Contains("HEADLIGHT") || t.Contains("HLIGHT")) return "headlight";
            if (t.Contains("CAB_LIGHT") || t.Contains("CABLIGHT")) return "cablight";
            if (t.Contains("THROTTLE")) return "throttle";
            if (t.Contains("BRAKE") && !t.Contains("_DISPLAY") && !t.Contains("PRESSURE") && !t.Contains("GAUGE")) return "brake";
            if (t.Contains("DIRECTION") || t.Contains("REVERSER")) return "reverser";
            if (t.Contains("ALERTER") || t.Contains("VIGILANCE")) return "alerter";
            if (t.Contains("DOOR")) return "doors";
            if (t.Contains("RADIO")) return "radio";
            if (t.Contains("CIRCUIT_BREAKER") || t.Contains("POWER")) return "power";
            if (t.Contains("COMPRESSOR")) return "compressor";
            return null;
        }

        // ---------------------------------------------------------------- sound

        void StartSound(bool cabEar)
        {
            if (sim != null) sim.Stop();
            audio.StopAll();
            sim = null;
            simReady = false;
            simCab = cabEar;
            streamRows.Clear();
            WrapPanel tp = (WrapPanel)F("TriggerPanel");
            tp.Children.Clear();
            if (current == null) return;
            string p = current.Loco.SmsPath(cabEar);
            if (p == null) { Txt("SmsName").Text = "No sound file found for this loco."; UpdateDeckAvailability(); return; }
            SmsFile f = SmsFile.Load(p);
            sim = new SoundSim(audio, f, cabEar, orRoot);
            sim.Log += delegate(string m) { Log(m); };
            SmsGroup g = sim.Group;
            Txt("SmsName").Text = System.IO.Path.GetFileName(p) + (g != null ? "  ·  " + g.Streams.Count + " streams" + (g.CabCam ? " · cab group" : g.ExternalCam ? " · outside group" : "") : "  ·  no usable group");
            foreach (StreamPlayer sp in sim.Players)
            {
                StreamRow r = new StreamRow();
                r.P = sp;
                r.Index = (sp.Stream.Index + 1).ToString();
                r.What = sp.Stream.Describe();
                List<string> files = new List<string>(sp.Stream.AllFiles());
                r.File = files.Count > 0 ? files[0] : "";
                streamRows.Add(r);
            }
            // every discrete trigger in the file
            SortedDictionary<int, bool> trig = new SortedDictionary<int, bool>();
            foreach (StreamPlayer sp in sim.Players) foreach (SmsTrigger t in sp.Stream.Triggers) if (t.Kind == TrigKind.Discrete) trig[t.Discrete] = true;
            foreach (int n in trig.Keys)
            {
                int num = n;
                Button b = new Button { Content = num + "  ·  " + TriggerNames.Name(num), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 5, 10, 5), FontSize = 12 };
                b.Click += delegate { if (!NeedPower()) return; int k = sim != null ? sim.Fire(num) : 0; if (k == 0) Log("Trigger " + num + ": no stream reacted"); };
                tp.Children.Add(b);
            }
            UpdateDeckAvailability();
            // decode every .wav of this group on a worker thread, then start: the click stays instant
            SoundSim mine = sim;
            List<string> paths = new List<string>();
            if (g != null) foreach (SmsStream st in g.Streams) foreach (string wf in st.AllFiles()) { string wp = f.ResolveWav(wf, orRoot); if (wp != null && !paths.Contains(wp)) paths.Add(wp); }
            Task.Factory.StartNew(delegate { Parallel.ForEach(paths, delegate(string wp) { WavData.Get(wp); }); }).ContinueWith(delegate
            {
                if (sim != mine) return;
                simReady = true;
                Log("Sound ready: " + paths.Count + " sound files");
                if (startPending) BeginStartup();                                  // Power was pressed while loading
                else if (power && !shuttingDown) { sim.Spool = 1; sim.Start(StateNow()); }   // ear switched while running
                startPending = false;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---------------------------------------------------------------- engine power

        void PowerOn()
        {
            if (current == null) return;
            power = true; SetToggle("power", true);
            Log("Power ON: starting engine");
            Status("Starting engine…");
            if (sim == null) { startPending = true; StartSound(simCab); return; }
            if (!simReady) { startPending = true; return; }
            BeginStartup();
        }

        void BeginStartup()
        {
            if (sim == null) return;
            shuttingDown = false;
            if (sim.Running) sim.Stop();
            audio.StopAll();
            sim.Spool = 0;
            starting = true;
            sim.Start(StateNow());
            // the loco's own start-up sounds, if its .sms has them (circuit breaker, power on)
            Fire(TriggerNames.CircuitBreakerClosing, TriggerNames.CircuitBreakerClosed, TriggerNames.EnginePowerOn);
        }

        void PowerOff()
        {
            if (!power && !starting) return;
            StopRun();
            if (hornHeld) DeckUp("horn");
            if (sanderHeld) DeckUp("sander");
            if (brakeHeld) DeckUp("brake");
            if (dynHeld) DeckUp("dynbrake");
            throttle = 0;
            sliderLock = true; ((Slider)F("ThrottleSlider")).Value = 0; sliderLock = false;
            Fire(TriggerNames.CircuitBreakerOpen, TriggerNames.EnginePowerOff);
            power = false; SetToggle("power", false);
            startPending = false; starting = false;
            shuttingDown = sim != null && sim.Running;
            Log("Power OFF: engine shutting down");
            Status(shuttingDown ? "Engine shutting down…" : "Engine is off");
        }

        /// <summary>True when the engine has power; otherwise shows "Power is off" and returns false.</summary>
        bool NeedPower()
        {
            if (power) return true;
            Toast("⏻  Power is off", "Start the engine with the Power button (key O) first.");
            return false;
        }

        void Toast(string title, string text)
        {
            Txt("ToastTitle").Text = title;
            Txt("ToastText").Text = text;
            Border t = (Border)F("Toast");
            t.Visibility = Visibility.Visible;
            t.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            if (toastTimer == null)
            {
                toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
                toastTimer.Tick += delegate
                {
                    toastTimer.Stop();
                    System.Windows.Media.Animation.DoubleAnimation a = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
                    a.Completed += delegate { if (!toastTimer.IsEnabled) t.Visibility = Visibility.Collapsed; };
                    t.BeginAnimation(UIElement.OpacityProperty, a);
                };
            }
            toastTimer.Stop(); toastTimer.Start();
        }

        /// <summary>Called every frame: start-up (crank ~1.6 s, then rise to idle by ~4.5 s) and run-down (~3 s).</summary>
        void SpoolEngine(double dt)
        {
            if (sim == null || !sim.Running) return;
            sim.SinceStart += dt;
            if (starting)
            {
                double t = sim.SinceStart;
                if (t < 1.6) sim.Spool = 0.12 + 0.1 * t;
                else { double x = Math.Min(1, (t - 1.6) / 2.9); sim.Spool = 0.28 + 0.72 * x * x * (3 - 2 * x); }
                if (sim.Spool >= 0.999) { sim.Spool = 1; starting = false; Log("Engine running"); Status("Engine running"); }
            }
            else if (shuttingDown)
            {
                sim.Spool = Math.Max(0, sim.Spool - dt / 3.0);
                if (sim.Spool <= 0.001) { sim.Stop(); audio.StopAll(); shuttingDown = false; Log("Engine stopped"); Status("Engine stopped"); }
            }
        }

        SimState StateNow()
        {
            state.Speed = speed;
            if (!manualVars)
            {
                state.Variable1 = throttle;                 // throttle 0..1
                state.Variable2 = power ? rpm : 0;          // engine RPM / load 0..1 (lags the throttle)
                state.Variable3 = dyn;                      // dynamic brake
                state.BrakeCyl = brake * 50;
            }
            return state;
        }

        void Log(string m)
        {
            eventLog.Insert(0, DateTime.Now.ToString("HH:mm:ss.f") + "  " + m);
            while (eventLog.Count > 300) eventLog.RemoveAt(eventLog.Count - 1);
        }

        void Fire(params int[] t)
        {
            if (sim == null) return;
            foreach (int n in t) sim.Fire(n);
        }

        // ---------------------------------------------------------------- deck

        void BuildDeck()
        {
            AddDeck("horn", "📯", "Horn", "H", true, false, TriggerNames.HornOn);
            AddDeck("bell", "🔔", "Bell", "B", true, false, TriggerNames.BellOn);
            AddDeck("panto1", "⚡", "Panto 1", "P", false, true, TriggerNames.Pantograph1Up, TriggerNames.PantographToggle);
            AddDeck("panto2", "⚡", "Panto 2", "⇧P", false, true, TriggerNames.Pantograph2Up, TriggerNames.PantographToggle);
            AddDeck("headlight", "💡", "Lights: Off", "L", false, false);
            AddDeck("cablight", "🔦", "Cab light", "C", false, true);
            AddDeck("wipers", "🌧", "Wipers", "W", false, true, TriggerNames.WiperOn);
            AddDeck("sander", "⏬", "Sander", "S", true, false, TriggerNames.SanderOn);
            AddDeck("compressor", "🌀", "Compressor", "K", false, true, TriggerNames.CompressorOn);
            AddDeck("brake", "🛑", "Brake", "Space", true, false, TriggerNames.TrainBrakePressureIncrease, TriggerNames.EngineBrakePressureIncrease, 53, TriggerNames.TrainBrakePressureDecrease);
            AddDeck("doors", "🚪", "Doors", "D", false, true, TriggerNames.DoorOpen);
            AddDeck("mirrors", "🪞", "Mirrors", "M", false, true);
            AddDeck("power", "⏻", "Power", "O", false, true, TriggerNames.EnginePowerOn, TriggerNames.CircuitBreakerClosing);
            AddDeck("alerter", "⚠", "Alerter", "A", true, false, TriggerNames.VigilanceAlarmOn);
            AddDeck("emergency", "🚨", "Emergency", "E", false, true, 51, TriggerNames.TrainBrakePressureIncrease);
            AddDeck("dynbrake", "🧲", "Dyn brake", "Y", true, false, 2, 20);
            AddDeck("reverser", "⇅", "Forward", "V", false, false, TriggerNames.ReverserChange);
            AddDeck("coupler", "🔗", "Couple", "J", false, false, 58, 61);
            AddDeck("radio", "📻", "Radio", "—", false, false, TriggerNames.CabRadioOn);
            // steam locos only
            AddDeck("injector1", "💧", "Injector 1", "—", false, true, 30);
            AddDeck("injector2", "💧", "Injector 2", "—", false, true, 27);
            AddDeck("blower", "🌬", "Blower", "—", false, false, 33);
            AddDeck("cocks", "♨", "Cyl cocks", "—", false, false, 34);
            AddDeck("firedoor", "🔥", "Fire door", "—", false, false, 36, 40);
            AddDeck("safety", "⛲", "Safety valve", "—", false, false, 42);
        }

        void AddDeck(string id, string glyph, string label, string key, bool hold, bool toggle, params int[] triggers)
        {
            DeckSpec d = new DeckSpec { Id = id, Glyph = glyph, Label = label, Key = key, Hold = hold, Toggle = toggle, Triggers = triggers };
            ButtonBase b = toggle ? (ButtonBase)new ToggleButton() : new Button();
            b.Style = (Style)w.FindResource("Deck");
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = glyph, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol") });
            d.LabelText = new TextBlock { Text = label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            sp.Children.Add(d.LabelText);
            b.Content = sp;
            b.ToolTip = label + "  (" + key + ")" + (hold ? " — hold" : "");
            d.Button = b;
            // no power: show the message and swallow the press (so toggles do not flip and holds do not start)
            b.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (id != "power" && current != null && !NeedPower()) e.Handled = true; };
            if (hold)
            {
                b.PreviewMouseLeftButtonDown += delegate { DeckDown(id); };
                b.PreviewMouseLeftButtonUp += delegate { DeckUp(id); };
                b.MouseLeave += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed) DeckUp(id); };
            }
            else b.Click += delegate { DeckDown(id); };   // toggle buttons flip IsChecked themselves; DeckDown keeps state in sync
            deck.Add(d);
            ((WrapPanel)F("DeckButtons")).Children.Add(b);
        }

        /// <summary>Only show the tests that make sense for this kind of loco.</summary>
        void ApplyDeckForType(Loco l)
        {
            string ty = (l.EngineType ?? "").ToLowerInvariant();
            bool electric = ty.Contains("electric"), steam = ty.Contains("steam");
            HashSet<string> steamOnly = new HashSet<string> { "injector1", "injector2", "blower", "cocks", "firedoor", "safety" };
            foreach (DeckSpec d in deck)
            {
                bool show = true;
                if (d.Id == "panto1" || d.Id == "panto2") show = electric;
                else if (steamOnly.Contains(d.Id)) show = steam;
                else if (d.Id == "dynbrake") show = !steam;
                d.Button.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void UpdateDeckAvailability()
        {
            foreach (DeckSpec d in deck)
            {
                if (d.Triggers.Length == 0 || d.Id == "power") { d.Button.Opacity = 1; continue; }   // power always works (it starts the engine)
                bool has = false;
                if (sim != null) foreach (int t in d.Triggers) if (sim.Has(t)) has = true;
                d.Button.Opacity = has ? 1 : 0.45;
                d.Button.ToolTip = d.Label + "  (" + d.Key + ")" + (d.Hold ? " — hold" : "") + (has ? "" : "\nThis sound file has no sound for it (animation / lights still work)");
            }
        }

        void UpdateDeckLabels()
        {
            foreach (DeckSpec d in deck)
            {
                if (d.Id == "headlight") d.LabelText.Text = "Lights: " + (headlight == 0 ? "Off" : headlight == 1 ? "Dim" : "Bright");
                if (d.Id == "reverser") d.LabelText.Text = reverser == 1 ? "Forward" : reverser == 0 ? "Neutral" : "Reverse";
                if (d.Id == "coupler") d.LabelText.Text = coupled ? "Uncouple" : "Couple";
            }
        }

        static bool IsHold(string id) { return id == "horn" || id == "bell" || id == "sander" || id == "brake" || id == "alerter" || id == "dynbrake"; }

        void SetToggle(string id, bool on) { foreach (DeckSpec d in deck) if (d.Id == id) { ToggleButton tb = d.Button as ToggleButton; if (tb != null) tb.IsChecked = on; } }

        void DeckDown(string id)
        {
            if (id != "power" && !NeedPower()) return;
            switch (id)
            {
                case "horn": hornHeld = true; Fire(TriggerNames.HornOn); Log("Horn ON"); break;
                case "bell": Fire(TriggerNames.BellOn); Log("Bell ON"); break;
                case "alerter": Fire(TriggerNames.VigilanceAlarmOn); Log("Alerter"); break;
                case "brake":
                    if (!brakeHeld) { brakeHeld = true; Fire(TriggerNames.TrainBrakeChange, TriggerNames.TrainBrakePressureIncrease, TriggerNames.EngineBrakePressureIncrease, 53); Log("Brake APPLIED"); }
                    break;
                case "panto1": panto1 = !panto1; SetToggle("panto1", panto1); Fire(panto1 ? TriggerNames.Pantograph1Up : TriggerNames.Pantograph1Down, TriggerNames.PantographToggle); Log("Pantograph 1 " + (panto1 ? "UP" : "DOWN")); break;
                case "panto2": panto2 = !panto2; SetToggle("panto2", panto2); Fire(panto2 ? TriggerNames.Pantograph2Up : TriggerNames.Pantograph2Down, TriggerNames.PantographToggle); Log("Pantograph 2 " + (panto2 ? "UP" : "DOWN")); break;
                case "headlight": headlight = (headlight + 1) % 3; Fire(TriggerNames.LightSwitchToggle); UpdateDeckLabels(); Log("Headlights " + (headlight == 0 ? "OFF" : headlight == 1 ? "DIM" : "BRIGHT")); break;
                case "cablight": SetCabLight(!cabLight); break;
                case "wipers": wipers = !wipers; SetToggle("wipers", wipers); Fire(wipers ? TriggerNames.WiperOn : TriggerNames.WiperOff); Log("Wipers " + (wipers ? "ON" : "OFF")); break;
                case "sander": if (!sanderHeld) { sanderHeld = true; Fire(TriggerNames.SanderOn); Log("Sander ON"); } break;
                case "compressor": { bool on = IsOn("compressor", true); Fire(on ? TriggerNames.CompressorOn : TriggerNames.CompressorOff); Log("Compressor " + (on ? "ON" : "OFF")); break; }
                case "doors": doors = !doors; SetToggle("doors", doors); Fire(doors ? TriggerNames.DoorOpen : TriggerNames.DoorClose); Log("Doors " + (doors ? "OPEN" : "CLOSED")); break;
                case "mirrors": mirrors = !mirrors; SetToggle("mirrors", mirrors); Log("Mirrors " + (mirrors ? "OUT" : "IN")); break;
                case "power":
                    if (power) PowerOff(); else PowerOn();
                    SetToggle("power", power);
                    break;
                case "radio": Fire(TriggerNames.CabRadioOn); Log("Cab radio"); break;
                case "emergency":
                    emergency = !emergency; SetToggle("emergency", emergency);
                    if (emergency) { StopRun(); throttle = 0; Fire(51, TriggerNames.TrainBrakePressureIncrease, TriggerNames.EngineBrakePressureIncrease, 53); Log("EMERGENCY BRAKE: penalty lights on, throttle cut"); }
                    else { Fire(TriggerNames.TrainBrakePressureDecrease, TriggerNames.EngineBrakePressureDecrease); Log("Emergency released"); }
                    break;
                case "dynbrake": if (!dynHeld) { dynHeld = true; Fire(2, 20); Log("Dynamic brake ON"); } break;
                case "coupler": coupled = !coupled; Fire(coupled ? 58 : 61); Log(coupled ? "Coupled" : "Uncoupled"); UpdateDeckLabels(); break;
                case "injector1": { bool on = IsOn("injector1", true); Fire(on ? 30 : 31); Log("Injector 1 " + (on ? "ON" : "OFF")); break; }
                case "injector2": { bool on = IsOn("injector2", true); Fire(on ? 27 : 28); Log("Injector 2 " + (on ? "ON" : "OFF")); break; }
                case "blower": Fire(33); Log("Blower"); break;
                case "cocks": Fire(34); Log("Cylinder cocks"); break;
                case "firedoor": Fire(36, 40); Log("Fire door"); break;
                case "safety": Fire(42); Log("Safety valve"); break;
                case "reverser":
                    if (Math.Abs(speed) > 0.3) { Log("Stop the loco before changing direction"); break; }
                    reverser = reverser == 1 ? 0 : reverser == 0 ? -1 : 1;
                    Fire(TriggerNames.ReverserChange); UpdateDeckLabels(); Log("Reverser: " + (reverser == 1 ? "FORWARD" : reverser == 0 ? "NEUTRAL" : "REVERSE"));
                    break;
                case "throttle": SetThrottle(throttle + 0.1); break;
            }
        }

        bool IsOn(string id, bool flipIfFromKey)
        {
            foreach (DeckSpec d in deck) if (d.Id == id) { ToggleButton tb = (ToggleButton)d.Button; return tb.IsChecked == true; }
            return false;
        }

        void DeckUp(string id)
        {
            if (!power) return;
            switch (id)
            {
                case "horn": hornHeld = false; Fire(TriggerNames.HornOff); Log("Horn OFF"); break;
                case "bell": Fire(TriggerNames.BellOff); Log("Bell OFF"); break;
                case "sander": if (sanderHeld) { sanderHeld = false; Fire(TriggerNames.SanderOff); Log("Sander OFF"); } break;
                case "brake": if (brakeHeld) { brakeHeld = false; Fire(TriggerNames.TrainBrakePressureDecrease, TriggerNames.EngineBrakePressureDecrease); Log("Brake RELEASED"); } break;
                case "dynbrake": if (dynHeld) { dynHeld = false; Fire(3); Log("Dynamic brake OFF"); } break;
            }
        }

        void SetThrottle(double v)
        {
            if (!NeedPower()) return;
            throttle = Math.Max(0, Math.Min(1, v));
            sliderLock = true; ((Slider)F("ThrottleSlider")).Value = throttle * 100; sliderLock = false;
            Fire(TriggerNames.ThrottleChange);
        }

        // ---------------------------------------------------------------- run test

        void StartRun()
        {
            if (current == null || !NeedPower()) return;
            if (emergency) DeckDown("emergency");
            if (reverser != 1) { reverser = 1; UpdateDeckLabels(); }
            runTarget = 100 / 3.6;
            if (current.Loco.MaxSpeedMps > 1) runTarget = Math.Min(runTarget, current.Loco.MaxSpeedMps);
            runPhase = 1; runTimer = 0; manualVars = false;
            if (brakeHeld) DeckUp("brake");
            Log("RUN TEST: 0 → " + Math.Round(runTarget * 3.6) + " km/h, then brake to a stop");
            Status("Run test in progress…");
        }

        void StopRun() { if (runPhase != 0) { runPhase = 0; if (brakeHeld) DeckUp("brake"); } }

        void StopAll()
        {
            StopRun();
            speed = 0; throttle = 0; brake = 0;
            sliderLock = true; ((Slider)F("SpeedSlider")).Value = 0; ((Slider)F("ThrottleSlider")).Value = 0; sliderLock = false;
            // Stop = stop the loco and shut the engine down (it runs down instead of cutting off)
            if (power || starting) PowerOff();
            else Status("Engine is off");
        }

        // ---------------------------------------------------------------- frame loop

        void OnFrame(object sender, EventArgs e)
        {
            double t = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.1, t - lastT);
            lastT = t;
            if (dt <= 0) return;

            // drive
            Slider sp = (Slider)F("SpeedSlider");
            if (runPhase > 0)
            {
                runTimer += dt;
                if (runPhase == 1)
                {
                    throttle = Math.Min(1, throttle + dt * 0.5);
                    double a = 0.9 * throttle * (1 - 0.5 * speed / Math.Max(1, runTarget));
                    speed = Math.Min(runTarget, speed + a * dt);
                    if (speed >= runTarget - 0.05) { runPhase = 2; runTimer = 0; Log("Reached " + Math.Round(speed * 3.6) + " km/h"); }
                }
                else if (runPhase == 2)
                {
                    throttle = Math.Max(0.3, throttle - dt * 0.5);
                    if (runTimer > 4) { runPhase = 3; runTimer = 0; throttle = 0; DeckDown("brake"); }
                }
                else if (runPhase == 3)
                {
                    brake = Math.Min(1, brake + dt * 0.6);
                    speed = Math.Max(0, speed - (0.3 + 1.0 * brake) * dt);
                    if (speed <= 0) { DeckUp("brake"); runPhase = 0; Log("RUN TEST finished: stopped"); Status("Run test finished"); }
                }
                sliderLock = true; sp.Value = speed * 3.6; ((Slider)F("ThrottleSlider")).Value = throttle * 100; sliderLock = false;
            }
            else
            {
                // follow the speed slider smoothly
                double want = sp.Value / 3.6;
                double rate = brakeHeld ? 2.0 : 1.4;
                if (brakeHeld) { want = Math.Max(0, speed - rate * dt); sliderLock = true; sp.Value = want * 3.6; sliderLock = false; }
                speed += Math.Max(-rate * dt, Math.Min(rate * dt, want - speed));
            }
            if (emergency) { throttle = 0; double v = Math.Max(0, speed - 2.2 * dt); if (v != speed) { speed = v; sliderLock = true; sp.Value = speed * 3.6; sliderLock = false; } }
            if (dynHeld && runPhase == 0) { double v = Math.Max(0, speed - 0.7 * dt); if (v != speed) { speed = v; sliderLock = true; sp.Value = speed * 3.6; sliderLock = false; } }
            if (reverser == 0 && runPhase == 0 && speed > 0) { speed = Math.Max(0, speed - 0.5 * dt); sliderLock = true; sp.Value = speed * 3.6; sliderLock = false; }
            dyn = dynHeld ? Math.Min(1, dyn + dt * 0.7) : Math.Max(0, dyn - dt * 0.7);
            brake = (brakeHeld || emergency) ? Math.Min(1, brake + dt * (emergency ? 2.0 : 0.8)) : Math.Max(0, brake - dt * 0.6);
            // engine revs up ~12 %/s and down ~9 %/s, so every notch of the engine sound plays through
            double wantRpm = power ? throttle : 0;
            rpm = rpm < wantRpm ? Math.Min(wantRpm, rpm + dt * 0.12) : Math.Max(wantRpm, rpm - dt * 0.09);

            // animation state
            double r = Math.Max(0.2, current != null ? current.Loco.WheelRadius : 0.5);
            anim.WheelAngle += (reverser == -1 ? -1 : 1) * speed / r * dt;
            anim.Penalty = emergency;
            anim.Rain = wipers;
            if (reverser != 0) lastDir = reverser;
            anim.Reverser = lastDir;      // neutral keeps the lights of the last direction
            anim.Panto1 = Ease(anim.Panto1, panto1 ? 1 : 0, dt / 2.5);
            anim.Panto2 = Ease(anim.Panto2, panto2 ? 1 : 0, dt / 2.5);
            anim.Doors = Ease(anim.Doors, doors ? 1 : 0, dt / 1.5);
            anim.Mirrors = Ease(anim.Mirrors, mirrors ? 1 : 0, dt / 1.5);
            if (wipers) anim.Wiper = 0.5 - 0.5 * Math.Cos(t * 2 * Math.PI / 1.6);
            else anim.Wiper = Ease(anim.Wiper, 0, dt / 0.8);
            anim.Headlight = headlight;
            anim.Time = t;
            anim.Throttle = throttle;
            anim.Speed = (reverser == -1 ? -1 : 1) * speed;
            anim.Power = power;
            anim.Horn = hornHeld;
            anim.Smoke = Tgl("SmokeTgl").IsChecked == true;
            if (IsTab("Tab3D")) viewer.Apply(anim, dt);
            if (IsTab("TabCab")) DrawCabWipers(t);

            // sound (30 Hz)
            SpoolEngine(dt);
            simAccum += dt;
            if (simAccum >= 0.033 && sim != null) { simAccum = 0; sim.Update(StateNow()); }

            // UI (10 Hz)
            uiAccum += dt;
            if (uiAccum >= 0.1)
            {
                uiAccum = 0;
                Txt("SpeedText").Text = Math.Round(speed * 3.6) + " km/h";
                Txt("ThrottleText").Text = Math.Round(throttle * 100) + " %";
                ((Border)F("LevelBar")).Height = Math.Min(28, audio.PeakLevel * 28);
                if (IsTab("TabSound")) RefreshSoundLab();
            }
        }

        static double Ease(double v, double target, double step) { return v < target ? Math.Min(target, v + step) : Math.Max(target, v - step); }

        void DrawCabWipers(double t)
        {
            Canvas fx = (Canvas)F("CabFx");
            if (!wipers && anim.Wiper <= 0.001) { if (fx.Children.Count > 0) fx.Children.Clear(); return; }
            if (cab == null || cabView != 0) return;
            // simple wiper blades sweeping across the windscreen area of the 2D cab
            if (fx.Children.Count != 2)
            {
                fx.Children.Clear();
                for (int i = 0; i < 2; i++) fx.Children.Add(new Line { Stroke = Theme.B("#CC111111"), StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
            double ang = (-70 + 140 * anim.Wiper) * Math.PI / 180;
            double[] px = { 220, 420 };
            for (int i = 0; i < 2; i++)
            {
                Line l = (Line)fx.Children[i];
                l.X1 = px[i]; l.Y1 = 210; l.X2 = px[i] + Math.Sin(ang) * 150; l.Y2 = 210 - Math.Cos(ang) * 150;
            }
        }

        void RefreshSoundLab()
        {
            foreach (StreamRow r in streamRows)
            {
                Voice v = r.P.Current;
                bool on = v != null && !v.Stopped && v.Volume * v.Fade > 0.005;
                r.Bar = on ? Math.Min(110, v.Volume * v.Fade * 110) : 0;
                r.RowBrush = on ? Theme.B("#13283D") : Theme.B("#0E1828");
                if (r.P.LastFile != null) r.File = r.P.LastFile + (on ? "" : "  (idle)");
            }
            sliderLock = true;
            if (vSpeed != null) vSpeed.Value = speed * 3.6;
            if (!manualVars && v1 != null) { v1.Value = state.Variable1 * 100; v2.Value = state.Variable2 * 100; v3.Value = state.Variable3 * 100; vBrake.Value = state.BrakeCyl; }
            sliderLock = false;
        }

        void BuildVarPanel()
        {
            StackPanel p = (StackPanel)F("VarPanel");
            vSpeed = AddVar(p, "Speed (km/h)", 0, 200, delegate(double v) { ((Slider)F("SpeedSlider")).Value = v; });
            v1 = AddVar(p, "Variable 1 (throttle %)", 0, 100, delegate(double v) { manualVars = true; state.Variable1 = v / 100; });
            v2 = AddVar(p, "Variable 2 (engine RPM / load %)", 0, 100, delegate(double v) { manualVars = true; state.Variable2 = v / 100; });
            v3 = AddVar(p, "Variable 3 (dynamic brake %)", 0, 100, delegate(double v) { manualVars = true; state.Variable3 = v / 100; });
            vBrake = AddVar(p, "Brake cylinder (psi)", 0, 100, delegate(double v) { manualVars = true; state.BrakeCyl = v; });
            TextBlock hint = new TextBlock { Text = "Moving a variable slider takes manual control of it; moving the throttle or starting a run hands it back.", FontSize = 11, Foreground = Theme.B("#6F82A3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            p.Children.Add(hint);
        }

        Slider AddVar(StackPanel p, string label, double min, double max, Action<double> set)
        {
            Grid g = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            TextBlock lb = new TextBlock { Text = label, FontSize = 12, Foreground = Theme.B("#C9D5E8") };
            TextBlock val = new TextBlock { Text = "0", FontSize = 12, Foreground = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Right };
            g.Children.Add(lb); g.Children.Add(val);
            Slider s = new Slider { Minimum = min, Maximum = max, Margin = new Thickness(0, 2, 0, 6) };
            s.ValueChanged += delegate { val.Text = Math.Round(s.Value).ToString(); if (!sliderLock) set(s.Value); };
            p.Children.Add(g); p.Children.Add(s);
            return s;
        }

        // ---------------------------------------------------------------- system check

        /// <summary>Same or older than this copy: just say so. Newer on GitHub: open the release page.</summary>
        void CheckForUpdates()
        {
            Button b = Btn("UpdateBtn");
            TextBlock t = Txt("UpdateText");
            b.IsEnabled = false;
            t.Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xB0, 0xCC));
            t.Text = "Checking GitHub for the latest version…";
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                Updater.Result r = Updater.Check(AppInfo.Version);
                w.Dispatcher.BeginInvoke(new Action(delegate
                {
                    b.IsEnabled = true;
                    if (r.Error != null)
                    {
                        t.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A));
                        t.Text = "⚠ " + r.Error;
                    }
                    else if (r.Newer)
                    {
                        t.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x6B));
                        t.Text = "⬆ Update available: v" + r.Latest + " (you have v" + r.Current + "). Opening the download page on GitHub…";
                        Toast("Update available", "v" + r.Latest + " is on GitHub");
                        try { Process.Start(r.Url); } catch { t.Text += "  Could not open the browser: " + r.Url; }
                    }
                    else
                    {
                        t.Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0xE7, 0xA0));
                        t.Text = "✔ You have the latest version: v" + r.Current + (Updater.Compare(r.Latest, r.Current) != 0 ? " (GitHub: v" + r.Latest + ")" : "") + ". Nothing to download.";
                        Toast("Up to date", "v" + r.Current + " is the latest");
                    }
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        void RunSysCheck()
        {
            StackPanel rows = (StackPanel)F("SysCheckRows");
            rows.Children.Clear();
            Btn("SysCheckBtn").IsEnabled = false;
            System.Threading.Thread th = new System.Threading.Thread(delegate()
            {
                foreach (SysCheck.Result r in SysCheck.Run())
                {
                    SysCheck.Result rr = r;
                    w.Dispatcher.BeginInvoke(new Action(delegate { rows.Children.Add(CheckRow(rr)); }));
                    System.Threading.Thread.Sleep(90);
                }
                w.Dispatcher.BeginInvoke(new Action(delegate { Btn("SysCheckBtn").IsEnabled = true; }));
            });
            th.IsBackground = true;
            th.Start();
        }

        UIElement CheckRow(SysCheck.Result r)
        {
            Grid g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            string icon = r.Level == 0 ? "✓" : r.Level == 1 ? "!" : r.Level == 2 ? "✕" : "i";
            Brush c = r.Level == 0 ? Theme.Green : r.Level == 1 ? Theme.Amber : r.Level == 2 ? Theme.Red : Theme.B("#5AB4FF");
            Border dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = c, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            dot.Child = new TextBlock { Text = icon, Foreground = Theme.B("#0A111D"), FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            StackPanel t = new StackPanel();
            t.Children.Add(new TextBlock { Text = r.Name + ":  " + r.Detail, TextWrapping = TextWrapping.Wrap, Foreground = Theme.B("#D5DEEC") });
            if (!string.IsNullOrEmpty(r.Advice)) t.Children.Add(new TextBlock { Text = r.Advice, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Muted, FontSize = 11.5 });
            Grid.SetColumn(t, 1);
            g.Children.Add(dot); g.Children.Add(t);
            return g;
        }

        // ---------------------------------------------------------------- keyboard

        static readonly Dictionary<Key, string> keyMap = new Dictionary<Key, string>
        {
            { Key.H, "horn" }, { Key.B, "bell" }, { Key.L, "headlight" }, { Key.C, "cablight" }, { Key.W, "wipers" }, { Key.S, "sander" },
            { Key.K, "compressor" }, { Key.Space, "brake" }, { Key.D, "doors" }, { Key.M, "mirrors" }, { Key.E, "emergency" }, { Key.Y, "dynbrake" }, { Key.V, "reverser" }, { Key.J, "coupler" }, { Key.O, "power" }, { Key.A, "alerter" }
        };
        readonly HashSet<Key> held = new HashSet<Key>();

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { ((TextBox)F("SearchBox")).Focus(); e.Handled = true; return; }
            if (e.Key == Key.F5) { LoadFolder(((TextBox)F("FolderBox")).Text); e.Handled = true; return; }
            if (Keyboard.FocusedElement is TextBox) return;
            if (current == null) return;
            Key k = e.Key;
            if (k == Key.Escape) { StopAll(); e.Handled = true; return; }
            if (k == Key.R) { StartRun(); e.Handled = true; return; }
            if (k == Key.Up) { SetThrottle(throttle + 0.1); e.Handled = true; return; }
            if (k == Key.Down) { SetThrottle(throttle - 0.1); e.Handled = true; return; }
            if (k == Key.PageUp) { if (NeedPower()) ((Slider)F("SpeedSlider")).Value += 10; e.Handled = true; return; }
            if (k == Key.PageDown) { if (NeedPower()) ((Slider)F("SpeedSlider")).Value -= 10; e.Handled = true; return; }
            if (k >= Key.D1 && k <= Key.D5) { string[] tabs = { "TabOverview", "Tab3D", "TabCab", "TabSound", "TabAbout" }; ((RadioButton)F(tabs[k - Key.D1])).IsChecked = true; e.Handled = true; return; }
            if (k == Key.P) { DeckDown(Keyboard.Modifiers == ModifierKeys.Shift ? "panto2" : "panto1"); e.Handled = true; return; }
            string id;
            if (keyMap.TryGetValue(k, out id))
            {
                e.Handled = true;
                if (id != "power" && !power) { if (!e.IsRepeat) NeedPower(); return; }
                if (IsHold(id)) { if (held.Add(k)) DeckDown(id); }
                else if (!e.IsRepeat)
                {
                    foreach (DeckSpec d in deck) if (d.Id == id && d.Toggle && id != "panto1" && id != "panto2" && id != "wipers" && id != "doors" && id != "mirrors" && id != "emergency" && id != "power" && id != "cablight") ((ToggleButton)d.Button).IsChecked = !((ToggleButton)d.Button).IsChecked;
                    DeckDown(id);
                }
            }
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
            string id;
            if (keyMap.TryGetValue(e.Key, out id) && IsHold(id) && held.Remove(e.Key)) { DeckUp(id); e.Handled = true; }
        }

        // ---------------------------------------------------------------- texts

        const string Features =
            "•  Lists every locomotive in your Open Rails / MSTS folder in a fraction of a second, with search and type filters\n" +
            "•  Health check of every loco in the background: missing or broken .sms files, missing .wav files, bad brackets, wrong counts, missing shapes and cab files\n" +
            "•  One-click sound repair with an automatic backup: rebuilds missing or empty sound files from a similar healthy loco, fixes brackets, headers and counts, finds renamed sounds\n" +
            "•  HD 3D view of the real model and textures in a loco shed (or on open track) on the real DB Tracks track: rotate, pan, zoom, preset cameras, turntable and a driver's-eye view\n" +
            "•  Animated wheels, radiator fans, steam rods, pantographs (with sparks at the wire), wipers, doors and mirrors\n" +
            "•  Realistic lights from the .eng: dim / bright headlights at the end you drive toward, red tail lights, marker and blinking lights, emergency (penalty) lights; glare, beams and a real night mode\n" +
            "•  Diesel exhaust smoke that thickens with the throttle, steam chimney, whistle and cylinder steam\n" +
            "•  2D cab with day, night and cab-light images; click horn, bell, sander, wipers, pantograph, brake and more right on the cab\n" +
            "•  Sound Lab: plays the .sms the way Open Rails does (cue-point loops, speed and engine curves, every trigger as a button) with live meters and an event log\n" +
            "•  Tests: horn, bell, pantographs, lights, wipers, sander, compressor, brake, emergency brake, dynamic brake, reverser, doors, mirrors, coupling, power, alerter, radio, steam controls\n" +
            "•  Power: the engine starts (cranking, then idle) when you press Power and runs down when you press Stop; every control asks for power first\n" +
            "•  Horn, bell and sander play like MSTS: the sound sustains while you hold the button and plays its ending (or fades out) when you let go\n" +
            "•  Replace sounds: swap a loco's horn, bell or sander for your own .wav in a few clicks, with the original kept and one-click restore\n" +
            "•  Cab light check: the Cab light button works only on cabs that have lit (CABLIGHT) images, and is greyed out on the rest\n" +
            "•  Run 0 → 100 km/h: accelerates, cruises and brakes to a stop while you listen to every sound stage\n" +
            "•  'Check this PC': requirements, graphics card and driver, sound, Open Rails folders and DB Tracks";
        const string Keys =
            "O power (start / stop the engine first)   H horn (hold)   B bell (hold)   S sander (hold)\n" +
            "Space brake (hold)   A alerter   P / Shift+P pantograph 1 / 2   L headlights   C cab light\n" +
            "W wipers   E emergency   Y dynamic brake (hold)   V reverser   J couple\n" +
            "K compressor   D doors   M mirrors   R run 0→100   Esc stop the loco and the engine\n" +
            "↑ ↓ throttle   PgUp PgDn speed   1-5 pages   Ctrl+F search   F5 rescan";
    }

    // ---------------------------------------------------------------- settings

    public class Settings
    {
        public string Folder, LastLoco;
        public double Volume = 0.7, Width, Height;
        public bool Maximized;
        static string FilePath { get { return System.IO.Path.Combine(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocoTester"), "settings.txt"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    double d;
                    bool num = double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
                    switch (k)
                    {
                        case "folder": s.Folder = v; break;
                        case "last": s.LastLoco = v; break;
                        case "volume": if (num) s.Volume = d; break;
                        case "width": if (num) s.Width = d; break;
                        case "height": if (num) s.Height = d; break;
                        case "max": s.Maximized = v == "1"; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
                File.WriteAllLines(FilePath, new[] { "folder=" + Folder, "last=" + LastLoco, "volume=" + Volume.ToString(ci), "width=" + Width.ToString(ci), "height=" + Height.ToString(ci), "max=" + (Maximized ? "1" : "0") });
            }
            catch { }
        }
    }
}
