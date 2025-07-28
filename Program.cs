using System;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Application = System.Windows.Forms.Application;
using static System.Console;
using static System.Environment;
using static Utils;

class Program {
#if DEBUG
    [DllImport("kernel32.dll")]
    static extern bool AllocConsole();
#endif
    
    [STAThread]
    static void Main() {
#if DEBUG
        AllocConsole();
        TextWriter? dbg = Out;
#else
        TextWriter? dbg = null;
#endif

        ApplicationConfiguration.Initialize();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var context = new AppContext(dbg);

        Application.Run(context);
    }
}

// Results of VLC library initialization attempt, including user interface 
// components

record InitVlcResult(
    string Directory, 
    LibVLC? Lib, 
    MediaPlayer? Player, 
    FileVersionInfo? Version, 
    ContextMenuStrip Menu,
    ToolStripMenuItem? PauseResumeMenuItem
);

// Main application class

class AppContext : ApplicationContext {
    readonly string homePth;
    readonly Config config;
    InitVlcResult initVlc;
    readonly NotifyIcon trayIcon;
    readonly FolderBrowserDialog initialDirBrowser;
    string? currentFile = null;
    
    TextWriter? Dbg { get; init; }

    public AppContext(TextWriter? dbg = null) {
        Dbg = dbg;
        
        homePth = Path.Combine(GetFolderPath(SpecialFolder.UserProfile),
            ".strei");
        
        string configPth = Path.Combine(homePth, "config.ini");
        config = new(configPth) { Dbg = dbg };
        
        EnsureHomeDir();
        
        initialDirBrowser = new() {
            Description = "Select a directory",
            ShowNewFolderButton = false
        };
        
        AppDomain.CurrentDomain.UnhandledException += (s, e) => {
            Dbg?.WriteLine($"""
                Unhandled exception:
                {e.ExceptionObject}
                """, Application.ProductName);
            OnExit(this, EventArgs.Empty);
        };
        
        Dbg?.WriteLine("Read configuration");
        
        try {
            config.Read();
        }
        catch (Exception ex) {
            Dbg?.WriteLine($"    {ex.GetType().Name}: {ex.Message}");
            Dbg?.WriteLine(ex.StackTrace);
            
            MessageBox.Show($"""
                Error reading configuration:
                
                {ex.GetType().Name}: {ex.Message}
                
                The application will now exit.
                """);
                
            OnExit(this, EventArgs.Empty);
            return;
        }
        Dbg?.WriteLine($"    {config.SectionCount} sections, {
            config.KeyCount} keys read");
        
        string vlcDir = config.GetValue("general", "VlcDir") ?? 
            GetFolderPath(SpecialFolder.ProgramFiles);
        
        initVlc = TryInitializeVlc(vlcDir);
        
        Dbg?.WriteLine("Load tray icon");
        try {
            trayIcon = new NotifyIcon() {
                Icon = strei.Resources.AppIcon,
                ContextMenuStrip = initVlc.Menu,
                Visible = true,
                Text = Application.ProductName
            };
        }
        catch (Exception ex) {
            Dbg?.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            OnExit(this, EventArgs.Empty);
        }
    }
    
    void EnsureHomeDir() {
        if (!Directory.Exists(homePth)) Directory.CreateDirectory(homePth);
    }
    
    InitVlcResult TryInitializeVlc(string vlcDir) {
        Dbg?.WriteLine($"Initialize VLC to '{vlcDir}'");
        
        var menu = new ContextMenuStrip();
        ToolStripMenuItem pauseResumeMenuItem = null!;
        
        void FinishMenu(bool vlcOk) {            
            if (!vlcOk) {
                menu.Items.Add($"VLC initialization error. Set directory", null, 
                    OnVlcErrorMenuClick);
            }
            else {
                menu.Items.Add("Choose file", null, OnChooseFile);
                menu.Items.Add("Change initial directory...", null, 
                    OnChangeInitialDirectory);
        
                pauseResumeMenuItem = new ToolStripMenuItem("Pause", null, 
                    OnPauseResume) 
                {
                    Enabled = false
                };
                menu.Items.Add(pauseResumeMenuItem);
        
                menu.Items.Add(new ToolStripSeparator());
            }
        
            menu.Items.Add("About", null, OnAboutMenuClick);
            menu.Items.Add("Exit", null, OnExit);
        }
        
        return Try<InitVlcResult, Exception>(() => {
            if (!Directory.Exists(vlcDir)) {
                Dbg?.WriteLine("    Directory doesn't exist");
                
                MessageBox.Show($"VLC initialization: the directory '{vlcDir
                    }' doesn't exist", Application.ProductName);
                    
                FinishMenu(vlcOk: false);
                
                return new(
                    Directory: vlcDir,
                    Lib: null,
                    Player: null,
                    Version: null,
                    Menu: menu,
                    PauseResumeMenuItem: null
                );
            }
        
            Dbg?.WriteLine("    Core.Initialize()");
            Core.Initialize(vlcDir);
            
            Dbg?.WriteLine("    Read VLC version");
            string vlcDllPth = Path.Combine(vlcDir, "libvlc.dll");
            // Assume `libvlc.dll` exists
            var ver = FileVersionInfo.GetVersionInfo(vlcDllPth);
        
            Dbg?.WriteLine("    libVLC()");
#if DEBUG
            var lib = new LibVLC("--no-video", "--verbose", "2");
#else
            var lib = new LibVLC("--no-video");
#endif
            
            Dbg?.WriteLine("    MediaPlayer()");
            var player = new MediaPlayer(lib);
            
            player.EndReached += (sender, args) => 
                ThreadPool.QueueUserWorkItem(_ => {
                    if (player?.Media != null) {
                        player.Stop();
                        player.Play();
                    }
                });
            
            FinishMenu(vlcOk: true);
            
            return new(
                Directory: vlcDir,
                Lib: lib,
                Player: player,
                Version: ver,
                Menu: menu,
                PauseResumeMenuItem: pauseResumeMenuItem
            );
        }, ex => {
            Dbg?.WriteLine($"    {ex.GetType().Name}: {ex.Message}");
            
            MessageBox.Show($"""
                VLC initialization error:
                
                {ex.GetType().Name}: {ex.Message}
                """);
            
            FinishMenu(vlcOk: false);
            
            return new(
                Directory: vlcDir,
                Lib: null,
                Player: null,
                Version: null,
                Menu: menu,
                PauseResumeMenuItem: null
            );
        });
    }
    
#region Event handlers
        
    void OnVlcErrorMenuClick(object sender, EventArgs e) {
        FolderBrowserDialog fbd = new() {
            Description = "Select a directory:",
            InitialDirectory = GetFolderPath(SpecialFolder.ProgramFiles),
            ShowNewFolderButton = false,
        };
    
        if (fbd.ShowDialog() != DialogResult.OK) return;
        
        config.SetValue("general", "VlcDir", fbd.SelectedPath);
        
        string vlcDir = fbd.SelectedPath;
        
        initVlc = TryInitializeVlc(vlcDir);
        
        trayIcon.ContextMenuStrip = initVlc.Menu;
    }

    void OnPauseResume(object sender, EventArgs e) {
        if (initVlc.Player == null) return;

        if (initVlc.Player.IsPlaying) {
            initVlc.Player.Pause();
            initVlc.PauseResumeMenuItem!.Text = "Resume";
        }
        else {
            initVlc.Player.Play();
            initVlc.PauseResumeMenuItem!.Text = "Pause";
        }
    }

    void OnChooseFile(object sender, EventArgs e) {
        if (initVlc.Player == null) return;
        
        string initialDir = config.GetValue("general", "InitialDir") ?? 
            GetFolderPath(SpecialFolder.MyMusic);
        
        using var dialog = new OpenFileDialog {
            Filter = "MP4 files (*.mp4)|*.mp4",
            Title = "Select MP4 File",
            InitialDirectory = initialDir
        };

        if (dialog.ShowDialog() == DialogResult.OK) {
            currentFile = dialog.FileName;
            try {
                initVlc.Player.Stop();
            
                var media = new Media(initVlc.Lib, currentFile, 
                    FromType.FromPath);
                media.AddOption(":no-video");

                initVlc.Player.Media = media;
                initVlc.Player.Play();

                initVlc.PauseResumeMenuItem!.Enabled = true;
                initVlc.PauseResumeMenuItem!.Text = "Pause";

                trayIcon.Text = $"Playing: {Path.GetFileName(currentFile)}";
            }
            catch (Exception ex) {
                MessageBox.Show($"""
                    Playback error.
                    {ex.GetType().Name}: {ex.Message}
                    """, Application.ProductName);
            }
        }
    }
    
    void OnChangeInitialDirectory(object sender, EventArgs e) {
        string initialDir = config.GetValue("general", "InitialDir") ?? 
            GetFolderPath(SpecialFolder.MyMusic);
        
        initialDirBrowser.RootFolder = SpecialFolder.MyMusic;

        if (initialDirBrowser.ShowDialog() == DialogResult.OK) 
            initialDir = initialDirBrowser.SelectedPath;
        
        config.SetValue("general", "InitialDir", initialDir);
    }
    
    void OnAboutMenuClick(object sender, EventArgs e) {
        MessageBox.Show($"""
            {Application.ProductName} {Application.ProductVersion}
            VLC {(initVlc.Version?.FileVersion.ToString() ?? "unitialized")}
            """, 
            Application.ProductName);
    }

    void OnExit(object sender, EventArgs e) {
        try {
            initVlc?.Player?.Stop();
        }
        finally {        
            initVlc?.Player?.Dispose();
            initVlc?.Lib?.Dispose();
        }
        if (trayIcon != null) trayIcon.Visible = false;
        Application.Exit();
    }
    
#endregion Event handlers
}