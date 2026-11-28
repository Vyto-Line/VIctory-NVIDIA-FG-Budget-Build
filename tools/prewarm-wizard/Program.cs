// VIctory GPU Prewarm wizard.
// Guides the user through picking the VIctory folder and the game dump folder, translates every shader and
// makes every recorded pipeline (victory.exe shaders + warm), then writes a launcher .cmd with the settings
// tested on RTX 30 cards (Series S, DLSS, frame generation through the unlocker, skip big copy-backs).
// Plain C# 5 / .NET Framework 4 so it runs on any Windows 10/11 PC without installing anything.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace VictoryPrewarm
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Wizard());
        }
    }

    class Wizard : Form
    {
        const string GameExeName = "game_scarlett_master.exe";
        static readonly Color Good = Color.FromArgb(0, 128, 60);
        static readonly Color Bad = Color.FromArgb(190, 30, 30);
        static readonly Color Warn = Color.FromArgb(170, 110, 0);

        // Pages
        readonly List<Panel> pages = new List<Panel>();
        int page;
        readonly Label header = new Label();
        readonly Label subHeader = new Label();
        readonly Button backBtn = new Button();
        readonly Button nextBtn = new Button();
        readonly Button cancelBtn = new Button();

        // Page 2: VIctory folder
        readonly TextBox victoryBox = new TextBox();
        readonly Label victoryStatus = new Label();
        string victoryRoot, victoryExe, playScript;
        bool hasStreamline, hasUnlocker, hasFsr;
        string proxyName;   // dxgi.dll or winmm.dll beside victory.exe: the unlocker as Windows loads it
        string builtinDll;  // %LOCALAPPDATA%\victory\mfg-unlocker\MFGUnlocker.dll (F1, MFG UNLOCKER)

        // Page 3: game folder
        readonly TextBox gameBox = new TextBox();
        readonly Label gameStatus = new Label();
        string gameDir;

        // Page 4: launch settings
        readonly ComboBox consoleBox = new ComboBox();
        readonly ComboBox dlssBox = new ComboBox();
        readonly ComboBox fgBox = new ComboBox();
        readonly ComboBox fgTypeBox = new ComboBox();
        readonly Button unlockBtn = new Button();
        readonly CheckBox noBackBox = new CheckBox();
        readonly CheckBox desktopBox = new CheckBox();
        readonly Label settingsNote = new Label();
        bool defaultsApplied;
        string gpuName = "";
        int gpuMb;

        // Page 5: prewarm
        readonly Label stepLabel = new Label();
        readonly ProgressBar bar = new ProgressBar();
        readonly Label detailLabel = new Label();
        readonly TextBox logBox = new TextBox();
        Process proc;
        Thread worker;
        volatile bool cancelled;
        bool running, finished;
        string logPath;
        int warmMade, warmTotal;

        // Page 6: done
        readonly Label doneText = new Label();
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 1000 };
        DateTime runStart;
        string launcherPath;

        public Wizard()
        {
            Text = "VIctory GPU Prewarm";
            ClientSize = new Size(760, 620);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.FromArgb(24, 28, 36) };
            header.SetBounds(20, 10, 720, 32);
            header.Font = new Font("Segoe UI Semibold", 15f);
            header.ForeColor = Color.White;
            subHeader.SetBounds(22, 44, 720, 24);
            subHeader.ForeColor = Color.FromArgb(180, 200, 220);
            top.Controls.Add(header);
            top.Controls.Add(subHeader);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56 };
            var line = new Label { BorderStyle = BorderStyle.Fixed3D, Height = 2, Dock = DockStyle.Top };
            bottom.Controls.Add(line);
            backBtn.Text = "< Back"; backBtn.SetBounds(440, 14, 95, 30);
            nextBtn.Text = "Next >"; nextBtn.SetBounds(545, 14, 95, 30);
            cancelBtn.Text = "Cancel"; cancelBtn.SetBounds(650, 14, 95, 30);
            backBtn.Click += delegate { Go(page - 1); };
            nextBtn.Click += delegate { OnNext(); };
            cancelBtn.Click += delegate { Close(); };
            bottom.Controls.AddRange(new Control[] { backBtn, nextBtn, cancelBtn });

            pages.Add(BuildWelcome());
            pages.Add(BuildVictoryPage());
            pages.Add(BuildGamePage());
            pages.Add(BuildSettingsPage());
            pages.Add(BuildRunPage());
            pages.Add(BuildDonePage());
            foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Padding = new Padding(24, 16, 24, 8); p.Visible = false; Controls.Add(p); }
            Controls.Add(top);
            Controls.Add(bottom);
            AcceptButton = nextBtn;

            DetectGpu();
            AutoDetectFolders();
            Go(0);
        }

        // ---------- page building ----------

        static Label Para(string text, int y, int h)
        {
            var l = new Label { Text = text, AutoSize = false };
            l.SetBounds(24, y, 700, h);
            return l;
        }

        Panel BuildWelcome()
        {
            var p = new Panel();
            p.Controls.Add(Para(
                "This tool gets VIctory ready so the game does not stutter while you play.\r\n\r\n" +
                "What it does:\r\n" +
                "   1.  Translates every shader of the game into VIctory's cache (a few minutes).\r\n" +
                "   2.  Builds every GPU pipeline that earlier runs recorded (about 15-30 minutes the first time,\r\n" +
                "        much faster on later runs because finished work is kept).\r\n" +
                "   3.  Writes a launcher file (.cmd) that starts the game with the right settings.\r\n\r\n" +
                "Before you start:\r\n" +
                "   \u2022  Close the game if it is running. Do not play while the prewarm runs.\r\n" +
                "   \u2022  Close GPU-heavy apps you do not need (games, video editors, browsers with many tabs).\r\n" +
                "   \u2022  You can stop at any time: the next run continues where this one stopped.\r\n\r\n" +
                "Run it again after updating VIctory or your NVIDIA driver.\r\n\r\n" +
                "Tip: the pipeline list grows as you play. If this is your very first time, play for a few\r\n" +
                "minutes first, then prewarm, so there is more to build ahead of time.", 8, 380));
            return p;
        }

        Panel BuildVictoryPage()
        {
            var p = new Panel();
            p.Controls.Add(Para(
                "Pick the VIctory folder: the one you downloaded or built VIctory into.\r\n\r\n" +
                "The right folder contains:   victory.exe   (or   target\\release\\victory.exe)\r\n" +
                "and usually also the   scripts   folder.\r\n\r\n" +
                "Examples:   C:\\Games\\VIctory-NVIDIA      D:\\VIctory\r\n" +
                "Not the game folder, and not a folder inside VIctory. If you pick  target  or  release,\r\n" +
                "this tool finds the main folder for you.", 8, 150));
            victoryBox.SetBounds(24, 168, 590, 28);
            victoryBox.TextChanged += delegate { CheckVictory(); };
            var browse = new Button { Text = "Browse..." };
            browse.SetBounds(622, 166, 100, 30);
            browse.Click += delegate { Browse(victoryBox, "Pick the VIctory folder (it holds victory.exe or target\\release\\victory.exe)"); };
            victoryStatus.SetBounds(24, 208, 700, 150);
            p.Controls.AddRange(new Control[] { victoryBox, browse, victoryStatus });
            return p;
        }

        Panel BuildGamePage()
        {
            var p = new Panel();
            p.Controls.Add(Para(
                "Pick your game folder: your own dump of the game, the folder that holds\r\n" +
                "      " + GameExeName + "\r\n\r\n" +
                "Example:   D:\\Dumps\\GTA6        (inside it:  D:\\Dumps\\GTA6\\" + GameExeName + ")\r\n\r\n" +
                "If you pick a folder above it, this tool looks one or two levels down and picks the right one.\r\n" +
                "Avoid folders that sync to the cloud (OneDrive, Dropbox): they can slow down or lock files.", 8, 150));
            gameBox.SetBounds(24, 168, 590, 28);
            gameBox.TextChanged += delegate { CheckGame(); };
            var browse = new Button { Text = "Browse..." };
            browse.SetBounds(622, 166, 100, 30);
            browse.Click += delegate { Browse(gameBox, "Pick the game folder (it holds " + GameExeName + ")"); };
            gameStatus.SetBounds(24, 208, 700, 150);
            p.Controls.AddRange(new Control[] { gameBox, browse, gameStatus });
            return p;
        }

        Panel BuildSettingsPage()
        {
            var p = new Panel();
            p.Controls.Add(Para("These go into the launcher file made at the end. The recommended choices are already picked\r\nfor your graphics card.", 8, 44));
            int y = 64;
            AddRow(p, "Console profile:", consoleBox, y,
                new[] { "Series S  (720p inside, faster - best for 8-12 GB cards)", "Series X  (1440p, sharper - needs 16 GB+)" });
            y += 44;
            AddRow(p, "DLSS upscaling:", dlssBox, y,
                new[] { "Performance  (fastest)", "Balanced", "Quality  (sharpest)", "Off  (game's own upscaling)" });
            y += 44;
            AddRow(p, "Frame generation:", fgBox, y,
                new[] { "Off", "2x  (one made-up frame per real one)", "3x", "4x" });
            y += 44;
            AddRow(p, "Frame gen type:", fgTypeBox, y,
                new[] { "Automatic  (NVIDIA DLSS where it runs, else AMD FSR 3)", "NVIDIA DLSS Frame Generation", "AMD FSR 3  (2x only, any card)" });
            y += 48;
            noBackBox.Text = "Skip big copy-backs  (VICTORY_WC_NO_BACK=1, about +3 fps on RTX 30 cards)";
            noBackBox.Checked = true;
            noBackBox.SetBounds(24, y, 700, 26);
            y += 30;
            desktopBox.Text = "Also put a copy of the launcher on my Desktop";
            desktopBox.Checked = true;
            desktopBox.SetBounds(24, y, 700, 26);
            y += 36;
            settingsNote.SetBounds(24, y, 700, 110);
            y += 112;
            unlockBtn.Text = "Set up MFG Unlocker for this card";
            unlockBtn.SetBounds(24, y, 280, 30);
            unlockBtn.Visible = false;
            unlockBtn.Click += delegate { SetUpUnlocker(); };
            fgBox.SelectedIndexChanged += delegate { UpdateSettingsNote(); };
            fgTypeBox.SelectedIndexChanged += delegate { UpdateSettingsNote(); };
            consoleBox.SelectedIndexChanged += delegate { UpdateSettingsNote(); };
            dlssBox.SelectedIndexChanged += delegate { UpdateSettingsNote(); };
            p.Controls.AddRange(new Control[] { noBackBox, desktopBox, settingsNote, unlockBtn });
            return p;
        }

        static void AddRow(Panel p, string label, ComboBox box, int y, string[] items)
        {
            var l = new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft };
            l.SetBounds(24, y, 150, 28);
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.AddRange(items);
            box.SelectedIndex = 0;
            box.SetBounds(180, y, 540, 28);
            p.Controls.Add(l);
            p.Controls.Add(box);
        }

        Panel BuildRunPage()
        {
            var p = new Panel();
            stepLabel.SetBounds(24, 8, 700, 26);
            stepLabel.Font = new Font("Segoe UI Semibold", 11f);
            bar.SetBounds(24, 40, 700, 26);
            detailLabel.SetBounds(24, 72, 700, 44);
            logBox.SetBounds(24, 120, 700, 250);
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Font = new Font("Consolas", 8.5f);
            logBox.BackColor = Color.FromArgb(250, 250, 250);
            p.Controls.AddRange(new Control[] { stepLabel, bar, detailLabel, logBox });
            return p;
        }

        Panel BuildDonePage()
        {
            var p = new Panel();
            doneText.SetBounds(24, 8, 700, 300);
            var launch = new Button { Text = "Start the game now" };
            launch.SetBounds(24, 320, 180, 34);
            launch.Click += delegate { if (launcherPath != null) Process.Start(new ProcessStartInfo(launcherPath) { UseShellExecute = true, WorkingDirectory = victoryRoot }); };
            var open = new Button { Text = "Show the launcher file" };
            open.SetBounds(214, 320, 180, 34);
            open.Click += delegate { if (launcherPath != null) Process.Start("explorer.exe", "/select,\"" + launcherPath + "\""); };
            var log = new Button { Text = "Open the log" };
            log.SetBounds(404, 320, 140, 34);
            log.Click += delegate { if (logPath != null && File.Exists(logPath)) Process.Start("notepad.exe", "\"" + logPath + "\""); };
            p.Controls.AddRange(new Control[] { doneText, launch, open, log });
            return p;
        }

        // ---------- navigation ----------

        void Go(int i)
        {
            if (i < 0 || i >= pages.Count) return;
            page = i;
            for (int k = 0; k < pages.Count; k++) pages[k].Visible = k == i;
            string[] titles = { "Welcome", "Step 1 of 4: the VIctory folder", "Step 2 of 4: the game folder",
                "Step 3 of 4: launch settings", "Step 4 of 4: prewarming the GPU", "All done" };
            string[] subs = { "Prewarm the shader and pipeline cache, then get a ready-made launcher.",
                "Where VIctory itself lives.", "Where your own copy of the game lives.",
                "How the launcher will start the game.", "Leave this window open. Do not play meanwhile.",
                "Your launcher is ready." };
            header.Text = titles[i];
            subHeader.Text = subs[i];
            backBtn.Enabled = i > 0 && i < 4;
            backBtn.Visible = i < 5;
            nextBtn.Text = i == 3 ? "Start prewarm" : (i == 5 ? "Finish" : "Next >");
            cancelBtn.Text = i == 4 ? "Stop" : "Cancel";
            cancelBtn.Visible = i != 5;
            if (i == 1) CheckVictory();
            if (i == 2) CheckGame();
            if (i == 3) { if (!defaultsApplied) { ApplyDefaultsFromCard(); defaultsApplied = true; } UpdateSettingsNote(); }
            UpdateNext();
            if (i == 4) StartPrewarm();
        }

        void UpdateNext()
        {
            if (page == 1) nextBtn.Enabled = victoryExe != null;
            else if (page == 2) nextBtn.Enabled = gameDir != null;
            else if (page == 4) nextBtn.Enabled = finished;
            else nextBtn.Enabled = true;
        }

        void OnNext()
        {
            if (page == 5) { Close(); return; }
            if (page == 4) { Go(5); return; }
            Go(page + 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running)
            {
                var r = MessageBox.Show(this,
                    "The prewarm is still running.\r\n\r\nStop it now? Finished work is kept, and the next run continues from there.",
                    "Stop the prewarm?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                StopPrewarm();
            }
            base.OnFormClosing(e);
        }

        void Browse(TextBox box, string description)
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = description;
                d.ShowNewFolderButton = false;
                if (Directory.Exists(box.Text)) d.SelectedPath = box.Text;
                if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
            }
        }

        // ---------- detection and checks ----------

        void DetectGpu()
        {
            try
            {
                var psi = new ProcessStartInfo("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader,nounits")
                { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadLine();
                    p.WaitForExit(5000);
                    if (!string.IsNullOrEmpty(s))
                    {
                        var parts = s.Split(',');
                        gpuName = parts[0].Trim();
                        if (parts.Length > 1) int.TryParse(parts[1].Trim(), out gpuMb);
                    }
                }
            }
            catch { }
            // Big cards (16 GB+) can take Series X; the rest are better on Series S.
            consoleBox.SelectedIndex = gpuMb >= 15000 ? 1 : 0;
        }

        bool IsRtx40OrLater()
        {
            var m = Regex.Match(gpuName, @"RTX\s*(\d{2})\d{2}");
            return m.Success && int.Parse(m.Groups[1].Value) >= 40;
        }

        void AutoDetectFolders()
        {
            var tries = new List<string>();
            string here = Path.GetDirectoryName(Application.ExecutablePath);
            for (string d = here; d != null; d = Path.GetDirectoryName(d)) tries.Add(d);
            tries.Add(Environment.CurrentDirectory);
            foreach (var t in tries)
                if (ResolveVictory(t) != null) { victoryBox.Text = ResolveVictory(t); break; }

            var gameTries = new List<string>();
            string env = Environment.GetEnvironmentVariable("VICTORY_GAME_DIR");
            if (!string.IsNullOrEmpty(env)) gameTries.Add(env);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            gameTries.Add(Path.Combine(user, "Downloads"));
            gameTries.Add(Path.Combine(user, "Desktop"));
            gameTries.Add(Path.Combine(user, "Documents"));
            if (!string.IsNullOrEmpty(victoryBox.Text)) gameTries.Add(Path.GetDirectoryName(victoryBox.Text));
            foreach (var t in gameTries)
            {
                string g = FindGameDir(t, 2);
                if (g != null) { gameBox.Text = g; break; }
            }
        }

        // Returns the VIctory root folder for a picked path (walks up from target\release etc.), or null.
        static string ResolveVictory(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return null;
            try
            {
                string d = Path.GetFullPath(picked.Trim().Trim('"'));
                for (int up = 0; up < 3 && d != null; up++, d = Path.GetDirectoryName(d))
                {
                    if (File.Exists(Path.Combine(d, "target", "release", "victory.exe"))) return d;
                    // Inside a build tree (target\release) the main folder two levels up is the one to use.
                    bool buildTree = Path.GetFileName(d).Equals("release", StringComparison.OrdinalIgnoreCase) &&
                        Path.GetFileName(Path.GetDirectoryName(d) ?? "").Equals("target", StringComparison.OrdinalIgnoreCase);
                    if (!buildTree && File.Exists(Path.Combine(d, "victory.exe"))) return d;
                }
            }
            catch { }
            return null;
        }

        // A full dump has the game's config next to the exe; a folder with only the exe is not enough.
        static bool IsFullDump(string dir)
        {
            return File.Exists(Path.Combine(dir, GameExeName)) && File.Exists(Path.Combine(dir, "MicrosoftGame.Config"));
        }

        static string FindGameDir(string start, int depth)
        {
            try
            {
                if (string.IsNullOrEmpty(start) || !Directory.Exists(start)) return null;
                if (IsFullDump(start)) return start;
                if (depth <= 0) return null;
                foreach (var sub in Directory.GetDirectories(start))
                {
                    string name = Path.GetFileName(sub);
                    if (name.StartsWith(".") || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                    string g = FindGameDir(sub, depth - 1);
                    if (g != null) return g;
                }
            }
            catch { }
            return null;
        }

        void CheckVictory()
        {
            victoryRoot = victoryExe = playScript = null;
            hasStreamline = hasUnlocker = hasFsr = false;
            proxyName = builtinDll = null;
            string t = victoryBox.Text.Trim().Trim('"');
            if (t.Length == 0)
            {
                SetStatus(victoryStatus, Warn, "Click Browse... and pick the VIctory folder.");
            }
            else if (!Directory.Exists(t))
            {
                SetStatus(victoryStatus, Bad, "\u2716  This folder does not exist. Check the spelling or use Browse...");
            }
            else
            {
                string root = ResolveVictory(t);
                if (root == null)
                {
                    string hint = FindGameDir(t, 0) != null
                        ? "This is the GAME folder. Pick the VIctory folder here, and the game folder on the next page."
                        : "No victory.exe here (and none in target\\release). Pick the folder you extracted or built VIctory into.\r\n" +
                          "If you only have the source code, build it first:  cargo build --release --locked -p victory";
                    SetStatus(victoryStatus, Bad, "\u2716  " + hint);
                }
                else
                {
                    victoryRoot = root;
                    string rel = Path.Combine(root, "target", "release", "victory.exe");
                    victoryExe = File.Exists(rel) ? rel : Path.Combine(root, "victory.exe");
                    string ps = Path.Combine(root, "scripts", "play-foxhound.ps1");
                    playScript = File.Exists(ps) ? ps : null;
                    hasStreamline = File.Exists(Path.Combine(root, "nvidia-sdk", "Streamline", "bin", "x64", "sl.interposer.dll")) ||
                                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VICTORY_STREAMLINE_DIR"));
                    string exeDir = Path.GetDirectoryName(victoryExe);
                    if (!running) RestoreUnlocker(); // left renamed by a prewarm that was stopped hard
                    DetectUnlocker();
                    hasFsr = FsrDir() != null;
                    var sb = new StringBuilder();
                    if (!root.Equals(Path.GetFullPath(t).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine("Using the main folder:  " + root);
                    sb.AppendLine("\u2714  Found " + victoryExe.Substring(root.Length).TrimStart('\\'));
                    sb.AppendLine(playScript != null ? "\u2714  Found the play script (scripts\\play-foxhound.ps1)"
                        : "\u26A0  No scripts\\play-foxhound.ps1: the launcher will start victory.exe directly (fewer settings).");
                    sb.AppendLine(hasStreamline ? "\u2714  NVIDIA Streamline found: DLSS and frame generation can be used"
                        : "\u26A0  NVIDIA Streamline not found: DLSS and frame generation stay off (run Get-NVIDIA-DLSS.cmd to add them).");
                    if (proxyName != null) sb.AppendLine("\u2714  MFG Unlocker found beside victory.exe (" + proxyName + ")");
                    else if (builtinDll != null) sb.AppendLine("\u2714  MFG Unlocker downloaded by VIctory (F1 menu); the next page can set it up for the launcher");
                    sb.AppendLine(hasFsr ? "\u2714  AMD FSR 3 frame generation files found"
                        : "\u2022  No AMD FSR 3 files (only needed for FSR 3 frame generation; Get-AMD-FSR.cmd fetches them)");
                    SetStatus(victoryStatus, Good, sb.ToString());
                }
            }
            UpdateNext();
        }

        void CheckGame()
        {
            gameDir = null;
            string t = gameBox.Text.Trim().Trim('"');
            if (t.EndsWith(GameExeName, StringComparison.OrdinalIgnoreCase)) t = Path.GetDirectoryName(t);
            if (t.Length == 0)
            {
                SetStatus(gameStatus, Warn, "Click Browse... and pick the folder that holds " + GameExeName + ".");
            }
            else if (!Directory.Exists(t))
            {
                SetStatus(gameStatus, Bad, "\u2716  This folder does not exist. Check the spelling or use Browse...");
            }
            else
            {
                string found = FindGameDir(t, 2);
                if (found == null)
                {
                    string hint = File.Exists(Path.Combine(t, GameExeName))
                        ? "This folder has " + GameExeName + " but not the rest of the game (no MicrosoftGame.Config)." + Environment.NewLine +
                          "Pick the full dump folder, the one with all the game's files next to the exe."
                        : ResolveVictory(t) != null
                        ? "This is the VICTORY folder. Pick your game dump here instead."
                        : "No " + GameExeName + " in this folder or the two levels below it.\r\nPick the folder of your own game dump that holds that file.";
                    SetStatus(gameStatus, Bad, "\u2716  " + hint);
                }
                else
                {
                    gameDir = Path.GetFullPath(found);
                    var sb = new StringBuilder();
                    if (!gameDir.Equals(Path.GetFullPath(t).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine("Found it one level down, using:  " + gameDir);
                    sb.AppendLine("\u2714  Found " + GameExeName);
                    string low = gameDir.ToLowerInvariant();
                    if (low.Contains("onedrive") || low.Contains("dropbox") || low.Contains("google drive"))
                        sb.AppendLine("\u26A0  This folder looks cloud-synced. It works, but a local folder is faster.");
                    SetStatus(gameStatus, Good, sb.ToString());
                }
            }
            UpdateNext();
        }

        void UpdateSettingsNote()
        {
            var sb = new StringBuilder();
            if (gpuName.Length > 0) sb.AppendLine("Your graphics card: " + gpuName + (gpuMb > 0 ? "  (" + (gpuMb / 1024) + " GB)" : ""));
            if (!hasStreamline)
            {
                sb.AppendLine("\u26A0  DLSS and frame generation need NVIDIA Streamline, which was not found; they will be off.");
            }
            else if (fgBox.SelectedIndex > 0)
            {
                int t = fgTypeBox.SelectedIndex; // 0 auto, 1 DLSS, 2 FSR 3
                if (FrameGenX() == 0)
                    sb.AppendLine("\u26A0  This frame generation needs DLSS on; pick a DLSS mode (or FSR 3 with Series S).");
                bool dlssFgRuns = IsRtx40OrLater() || hasUnlocker;
                if (t != 2 && !dlssFgRuns)
                    sb.AppendLine(t == 1
                        ? "\u26A0  DLSS Frame Generation on this card needs MFG Unlocker: click the button below."
                        : "\u2022  This card gets AMD FSR 3 (2x), or DLSS Frame Generation once MFG Unlocker is set up (button below).");
                bool fsrUsed = t == 2 || (t == 0 && !dlssFgRuns);
                if (fsrUsed)
                {
                    if (!hasFsr) sb.AppendLine("\u26A0  FSR 3 files are missing: run Get-AMD-FSR.cmd in the VIctory folder first (about 30 MB).");
                    if (fgBox.SelectedIndex > 1) sb.AppendLine("\u26A0  FSR 3 makes 2x only.");
                }
                else if (fgBox.SelectedIndex > 1 && !IsRtx40OrLater())
                    sb.AppendLine("Note: on RTX 30 cards 2x is the tested choice; 3x/4x may not work.");
            }
            sb.AppendLine("In the game, F1 opens VIctory's menu. What you save there is used over these settings at every start.");
            settingsNote.Text = sb.ToString();
            unlockBtn.Visible = hasStreamline && !IsRtx40OrLater() && proxyName == null && fgBox.SelectedIndex > 0 && fgTypeBox.SelectedIndex != 2;
        }

        void ApplyDefaultsFromCard()
        {
            // Defaults: frame generation 2x when it can work, else off.
            bool fgPossible = hasStreamline && (IsRtx40OrLater() || hasUnlocker || hasFsr);
            fgBox.SelectedIndex = fgPossible ? 1 : 0;
            dlssBox.SelectedIndex = hasStreamline ? 0 : 3;
        }

        static void SetStatus(Label l, Color c, string text)
        {
            l.ForeColor = c;
            l.Text = text;
        }

        // ---------- prewarm ----------

        void StartPrewarm()
        {
            if (running || finished) return;
            running = true;
            cancelled = false;
            logBox.Clear();
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "victory", "logs");
            Directory.CreateDirectory(logDir);
            logPath = Path.Combine(logDir, "prewarm.log");
            runStart = DateTime.Now;
            clock.Tick += delegate { if (running) subHeader.Text = "Leave this window open. Do not play meanwhile.   Running for " + FormatSpan(DateTime.Now - runStart); };
            clock.Start();
            worker = new Thread(PrewarmThread) { IsBackground = true };
            worker.Start();
        }

        void StopPrewarm()
        {
            cancelled = true;
            KillTree();
            if (worker != null) worker.Join(8000);
            RestoreUnlocker();
        }

        void KillTree()
        {
            try
            {
                var p = proc;
                if (p != null && !p.HasExited)
                {
                    var k = Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { UseShellExecute = false, CreateNoWindow = true });
                    if (k != null) k.WaitForExit(5000);
                }
            }
            catch { }
        }

        // Names Windows loads MFG Unlocker under beside victory.exe. scripts\play-foxhound.ps1 runs `victory run`,
        // which does not load the F1 option's copy itself, so the launcher needs one of these.
        static readonly string[] ProxyNames = { "winmm.dll", "dxgi.dll" };

        string ExeDir { get { return Path.GetDirectoryName(victoryExe); } }

        static string VictoryHome()
        {
            string h = Environment.GetEnvironmentVariable("VICTORY_HOME");
            return string.IsNullOrEmpty(h)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "victory") : h;
        }

        void DetectUnlocker()
        {
            proxyName = null;
            foreach (string n in ProxyNames)
                if (File.Exists(Path.Combine(ExeDir, n)) || File.Exists(Path.Combine(ExeDir, n + ".off"))) { proxyName = n; break; }
            string b = Path.Combine(VictoryHome(), "mfg-unlocker", "MFGUnlocker.dll");
            builtinDll = File.Exists(b) ? b : null;
            hasUnlocker = proxyName != null;
        }

        string FsrDir()
        {
            const string dll = "amd_fidelityfx_framegeneration_dx12.dll";
            var tries = new List<string>();
            string env = Environment.GetEnvironmentVariable("VICTORY_FIDELITYFX_DIR");
            if (!string.IsNullOrEmpty(env)) tries.Add(env);
            tries.Add(Path.Combine(VictoryHome(), "fidelityfx"));
            tries.Add(Path.Combine(victoryRoot, "amd-sdk", "FidelityFX", "bin"));
            tries.Add(Path.Combine(victoryRoot, "fidelityfx"));
            foreach (string d in tries) if (File.Exists(Path.Combine(d, dll))) return d;
            return null;
        }

        // Downloads MFG Unlocker with VIctory's own pinned, checksum-checked fetcher (scripts\get-mfg-unlocker.ps1)
        // when it is not on this PC yet, then puts a copy beside victory.exe as winmm.dll for the launcher.
        void SetUpUnlocker()
        {
            if (MessageBox.Show(this,
                    "MFG Unlocker is someone else's mod (github.com/dashdogy/MFG-Unlocker, MIT license). It lets DLSS Frame " +
                    "Generation run on RTX 20 and 30 cards, which NVIDIA does not support.\r\n\r\n" +
                    (builtinDll == null ? "It will be downloaded (about 5 MB, release v1.7.2, checked against its author's SHA-256) and " : "It will be ") +
                    "copied beside victory.exe as winmm.dll.\r\n\r\nIf the game crashes or frame generation fails, delete that winmm.dll.\r\n\r\nSet it up?",
                    "MFG Unlocker", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            UseWaitCursor = true;
            unlockBtn.Enabled = false;
            Application.DoEvents();
            string err = null;
            try
            {
                if (builtinDll == null)
                {
                    string script = Path.Combine(victoryRoot, "scripts", "get-mfg-unlocker.ps1");
                    if (!File.Exists(script)) throw new Exception("scripts\\get-mfg-unlocker.ps1 is missing: update VIctory to a newer version first.");
                    string to = Path.Combine(VictoryHome(), "mfg-unlocker");
                    var psi = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -To \"" + to + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var pr = Process.Start(psi))
                    {
                        string errText = null;
                        var errThread = new Thread(delegate () { errText = pr.StandardError.ReadToEnd(); });
                        errThread.Start();
                        string output = pr.StandardOutput.ReadToEnd();
                        pr.WaitForExit();
                        errThread.Join();
                        DetectUnlocker();
                        if (pr.ExitCode != 0 || builtinDll == null)
                            throw new Exception("The download did not work:\r\n" + (output + errText).Trim());
                    }
                }
                File.Copy(builtinDll, Path.Combine(ExeDir, "winmm.dll"), true);
                DetectUnlocker();
            }
            catch (Exception ex) { err = ex.Message; }
            UseWaitCursor = false;
            unlockBtn.Enabled = true;
            if (err != null) MessageBox.Show(this, err, "MFG Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else MessageBox.Show(this, "MFG Unlocker is set up (winmm.dll beside victory.exe).", "MFG Unlocker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            UpdateSettingsNote();
        }

        void RestoreUnlocker()
        {
            if (victoryExe == null) return;
            foreach (string n in ProxyNames)
            {
                try
                {
                    string on = Path.Combine(ExeDir, n), off = on + ".off";
                    if (File.Exists(off) && !File.Exists(on)) File.Move(off, on);
                }
                catch { }
            }
        }

        void Ui(Action a)
        {
            try { if (!IsDisposed) BeginInvoke(a); } catch { }
        }

        void PrewarmThread()
        {
            int code = -1;
            string failure = null;
            try
            {
                File.WriteAllText(logPath, "=== VIctory GPU Prewarm " + DateTime.Now + "\r\n");
                // The frame generation unlocker is kept out of the many warm processes.
                foreach (string n in ProxyNames)
                {
                    string on = Path.Combine(ExeDir, n);
                    if (File.Exists(on) && !File.Exists(on + ".off")) File.Move(on, on + ".off");
                }

                Ui(delegate
                {
                    stepLabel.Text = "Part 1 of 2: translating the game's shaders";
                    bar.Style = ProgressBarStyle.Marquee;
                    detailLabel.Text = "Usually 2-5 minutes. Already translated shaders are skipped.";
                });
                code = RunStep("shaders \"" + gameDir + "\"", "shaders");
                if (cancelled) return;
                if (code != 0) { failure = "The shader step stopped with error code " + code + "."; return; }

                Ui(delegate
                {
                    stepLabel.Text = "Part 2 of 2: building GPU pipelines";
                    bar.Style = ProgressBarStyle.Continuous;
                    bar.Value = 0;
                    detailLabel.Text = "Counting the pipelines earlier runs recorded...";
                });
                code = RunStep("warm", "warm");
                if (cancelled) return;
                if (code != 0) { failure = "The pipeline step stopped with error code " + code + "."; return; }

                Invoke((Action)WriteLauncher);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            finally
            {
                RestoreUnlocker();
                try { File.AppendAllText(logPath, "=== end " + DateTime.Now + " code " + code + (cancelled ? " (stopped by user)" : "") + "\r\n"); } catch { }
                bool ok = failure == null && !cancelled;
                string why = failure;
                Ui(delegate { OnPrewarmEnded(ok, why); });
            }
        }

        static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;]*[A-Za-z]");
        static readonly Regex ShaderCount = new Regex(@"^(\d+) shaders in the cache(?:, (\d+) do not translate)?, (\d+) s");
        static readonly Regex WarmKnown = new Regex(@"^(\d+) pipelines known, (\d+) made before.*?; (\d+) to make");
        static readonly Regex WarmProgress = new Regex(@"^(\d+) of (\d+), (\d+) s");
        static readonly Regex WarmDone = new Regex(@"^(\d+) made in (\d+) s");

        int RunStep(string args, string name)
        {
            File.AppendAllText(logPath, "=== " + name + " " + DateTime.Now + "\r\n");
            var psi = new ProcessStartInfo(victoryExe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = victoryRoot,
            };
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["VICTORY_GAME_DIR"] = gameDir;
            var start = DateTime.Now;
            using (var log = new StreamWriter(logPath, true, new UTF8Encoding(false)))
            using (proc = new Process { StartInfo = psi })
            {
                object gate = new object();
                DataReceivedEventHandler h = (s, e) =>
                {
                    if (e.Data == null) return;
                    string l = Ansi.Replace(e.Data, "");
                    lock (gate) log.WriteLine(l);
                    OnLine(l, start);
                };
                proc.OutputDataReceived += h;
                proc.ErrorDataReceived += h;
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit(); // also waits for the async readers to drain
                int c = proc.ExitCode;
                proc = null;
                return c;
            }
        }

        void OnLine(string l, DateTime start)
        {
            string t = l.Trim();
            bool show = !t.Contains(" INFO ") && !t.Contains(" DEBUG ") && t.Length > 0;
            Match m;
            if ((m = ShaderCount.Match(t)).Success)
            {
                string txt = string.Format("{0:N0} shaders in the cache{1}  -  {2}",
                    int.Parse(m.Groups[1].Value),
                    m.Groups[2].Success ? string.Format(" ({0} cannot be translated; that is normal)", m.Groups[2].Value) : "",
                    Elapsed(start));
                Ui(delegate { detailLabel.Text = txt; });
                show = m.Groups[2].Success || int.Parse(m.Groups[1].Value) % 5000 == 0;
            }
            else if ((m = WarmKnown.Match(t)).Success)
            {
                warmTotal = int.Parse(m.Groups[3].Value);
                warmMade = 0;
                int before = int.Parse(m.Groups[2].Value);
                string txt = warmTotal == 0
                    ? "Every recorded pipeline is already built. Nothing to do."
                    : string.Format("{0:N0} pipelines to build ({1:N0} were already built earlier).", warmTotal, before);
                if (int.Parse(m.Groups[1].Value) == 0)
                    txt = "No pipelines recorded yet. Play for a few minutes, then run this tool again for the full effect.";
                Ui(delegate { detailLabel.Text = txt; if (warmTotal == 0) { bar.Value = 100; } });
            }
            else if ((m = WarmProgress.Match(t)).Success)
            {
                int made = int.Parse(m.Groups[1].Value), total = int.Parse(m.Groups[2].Value);
                warmMade = made; warmTotal = total;
                double secs = (DateTime.Now - start).TotalSeconds;
                string eta = made > 0 ? FormatSpan(TimeSpan.FromSeconds(secs / made * (total - made))) : "?";
                int pct = total > 0 ? (int)Math.Min(100, made * 100L / total) : 0;
                string txt = string.Format("{0:N0} of {1:N0} pipelines built ({2}%)  -  {3}  -  about {4} left",
                    made, total, pct, Elapsed(start), eta);
                Ui(delegate { bar.Value = pct; detailLabel.Text = txt; });
                show = made % 10000 == 0;
            }
            else if ((m = WarmDone.Match(t)).Success)
            {
                Ui(delegate { bar.Value = 100; });
            }
            if (show)
            {
                string add = t.Length > 220 ? t.Substring(0, 220) + "..." : t;
                Ui(delegate
                {
                    if (logBox.TextLength > 60000) logBox.Text = logBox.Text.Substring(30000);
                    logBox.AppendText(add + "\r\n");
                });
            }
        }

        static string Elapsed(DateTime start) { return FormatSpan(DateTime.Now - start) + " so far"; }

        static string FormatSpan(TimeSpan s)
        {
            if (s.TotalHours >= 1) return string.Format("{0}h {1:00}m", (int)s.TotalHours, s.Minutes);
            if (s.TotalMinutes >= 1) return string.Format("{0}m {1:00}s", (int)s.TotalMinutes, s.Seconds);
            return string.Format("{0}s", (int)s.TotalSeconds);
        }

        void OnPrewarmEnded(bool ok, string failure)
        {
            running = false;
            clock.Stop();
            if (cancelled) return;
            if (!ok)
            {
                bar.Style = ProgressBarStyle.Continuous;
                stepLabel.Text = "The prewarm stopped with a problem";
                stepLabel.ForeColor = Bad;
                detailLabel.Text = failure + "  The full log is at " + logPath;
                cancelBtn.Text = "Close";
                var r = MessageBox.Show(this,
                    failure + "\r\n\r\nCommon causes: the game or another VIctory window is still open, the folders were moved, " +
                    "or the graphics driver needs an update.\r\n\r\nOpen the log now?",
                    "Prewarm problem", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes) Process.Start("notepad.exe", "\"" + logPath + "\"");
                return;
            }
            finished = true;
            stepLabel.Text = "Finished";
            stepLabel.ForeColor = Good;
            bar.Style = ProgressBarStyle.Continuous;
            bar.Value = 100;
            var sb = new StringBuilder();
            sb.AppendLine("The GPU cache is warm. The game should now start and play with far less stutter.\r\n");
            sb.AppendLine("Your launcher file:");
            sb.AppendLine("      " + launcherPath);
            if (desktopBox.Checked) sb.AppendLine("      (a copy is also on your Desktop)");
            sb.AppendLine();
            sb.AppendLine("Double-click it to play. It starts the game with:");
            sb.AppendLine("      " + DescribeSettings());
            sb.AppendLine();
            sb.AppendLine("Run this prewarm again after you update VIctory or your NVIDIA driver, and after");
            sb.AppendLine("your first few play sessions (new pipelines are recorded while you play).");
            doneText.Text = sb.ToString();
            Go(5);
        }

        // ---------- launcher ----------

        string DlssMode()
        {
            if (!hasStreamline) return "off";
            switch (dlssBox.SelectedIndex) { case 0: return "performance"; case 1: return "balanced"; case 2: return "quality"; default: return "off"; }
        }

        string FgType() { switch (fgTypeBox.SelectedIndex) { case 1: return "dlss"; case 2: return "fsr"; default: return "auto"; } }

        int FrameGenX()
        {
            if (fgBox.SelectedIndex == 0) return 0;
            // Without DLSS only FSR 3 on Series S runs (the title's own upscaling takes Series S sizes only).
            if (DlssMode() == "off" && (FgType() == "dlss" || !SeriesS())) return 0;
            if (FgType() == "fsr") return 2;
            return fgBox.SelectedIndex + 1;
        }

        bool SeriesS() { return consoleBox.SelectedIndex == 0; }

        string DescribeSettings()
        {
            return (SeriesS() ? "Series S" : "Series X 1440p") +
                   ", DLSS " + DlssMode() +
                   ", frame generation " + (FrameGenX() == 0 ? "off" : FrameGenX() + "x " + FgType()) +
                   (noBackBox.Checked ? ", skip big copy-backs" : "");
        }

        static string Ps(string s) { return "'" + s.Replace("'", "''") + "'"; }
        static string Cmd(string s) { return s.Replace("%", "%%"); }

        void WriteLauncher()
        {
            string gameExe = Path.Combine(gameDir, GameExeName);
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("rem Made by VIctory GPU Prewarm on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ". Double-click to play.");
            sb.AppendLine("rem Settings: " + DescribeSettings() + ".");
            sb.AppendLine("rem In the game, F1 opens VIctory's menu; what it saves (victory-settings.txt) is used over these settings.");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("setlocal");
            sb.AppendLine("set \"VICTORY_DIR=" + Cmd(victoryRoot) + "\"");
            sb.AppendLine("set \"GAME_EXE=" + Cmd(gameExe) + "\"");
            sb.AppendLine("if not exist \"%GAME_EXE%\" (");
            sb.AppendLine("  echo The game was not found at %GAME_EXE%");
            sb.AppendLine("  echo If you moved it, run VIctory-Prewarm.exe again to make a new launcher.");
            sb.AppendLine("  pause");
            sb.AppendLine("  exit /b 1");
            sb.AppendLine(")");
            sb.AppendLine("cd /d \"%VICTORY_DIR%\"");
            if (playScript != null)
            {
                var a = new StringBuilder();
                a.Append("& .\\scripts\\play-foxhound.ps1 -Exe ").Append(Ps(gameExe));
                if (SeriesS()) a.Append(" -SeriesS");
                a.Append(" -Dlss ").Append(DlssMode());
                if (FrameGenX() > 0) a.Append(" -FrameGen -FrameGenX ").Append(FrameGenX()).Append(" -FgType ").Append(FgType());
                a.Append(" -More @{VICTORY_WC_NO_BACK=").Append(noBackBox.Checked ? "'1'" : "'0'").Append("}");
                sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"" + Cmd(a.ToString()) + "\"");
            }
            else
            {
                if (noBackBox.Checked) sb.AppendLine("set \"VICTORY_WC_NO_BACK=1\"");
                string rel = victoryExe.Substring(victoryRoot.Length).TrimStart('\\');
                sb.AppendLine("\"" + Cmd(rel) + "\" run \"%GAME_EXE%\" --console " + (SeriesS() ? "series-s" : "series-x") + " --execute");
            }
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("  echo.");
            sb.AppendLine("  echo The game stopped with an error. Logs: %LOCALAPPDATA%\\victory\\logs");
            sb.AppendLine("  pause");
            sb.AppendLine(")");

            var utf8 = new UTF8Encoding(false);
            launcherPath = Path.Combine(victoryRoot, "Launch VIctory (prewarmed).cmd");
            File.WriteAllText(launcherPath, sb.ToString(), utf8);
            if (desktopBox.Checked)
            {
                string d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                try { File.WriteAllText(Path.Combine(d, "Launch VIctory (prewarmed).cmd"), sb.ToString(), utf8); } catch { }
            }
        }

    }
}
