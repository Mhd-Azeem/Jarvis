using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.Windows;

internal static class ModernProgram
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ModernJarvisForm());
    }
}

internal sealed class ModernJarvisForm : Form
{
    private static readonly Color Bg = Color.FromArgb(5, 9, 17);
    private static readonly Color Panel = Color.FromArgb(20, 29, 43);
    private static readonly Color Panel2 = Color.FromArgb(16, 24, 37);
    private static readonly Color Cyan = Color.FromArgb(83, 233, 255);
    private static readonly Color Blue = Color.FromArgb(74, 125, 255);
    private static readonly Color Muted = Color.FromArgb(137, 155, 181);
    private static readonly Color TextMain = Color.FromArgb(241, 247, 255);

    private readonly TextBox commandBox = new();
    private readonly RichTextBox responseBox = new();
    private readonly Button runButton = new();
    private readonly Button listenButton = new();
    private readonly Label listeningLabel = new();
    private readonly ArcReactorControl reactor = new();

    private const byte VkVolumeMute = 0xAD;
    private const byte VkVolumeDown = 0xAE;
    private const byte VkVolumeUp = 0xAF;
    private const byte VkMediaNext = 0xB0;
    private const byte VkMediaPrev = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    public ModernJarvisForm()
    {
        Text = "JARVIS • Windows Assistant";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 660);
        Size = new Size(1180, 760);
        BackColor = Bg;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;

        var shell = new GradientPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            StartColor = Color.FromArgb(5, 9, 17),
            EndColor = Color.FromArgb(8, 21, 36)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        layout.Controls.Add(BuildSidebar(), 0, 0);
        layout.Controls.Add(BuildMainPanel(), 1, 0);
        shell.Controls.Add(layout);
        Controls.Add(shell);

        AcceptButton = runButton;
    }

    private Control BuildSidebar()
    {
        var sidebar = CreateCardPanel();
        sidebar.Dock = DockStyle.Fill;
        sidebar.Margin = new Padding(0, 0, 14, 0);
        sidebar.Padding = new Padding(20);

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent
        };

        var title = new Label
        {
            Text = "JARVIS",
            AutoSize = true,
            Font = new Font("Segoe UI", 28F, FontStyle.Bold),
            ForeColor = TextMain,
            Margin = new Padding(0, 0, 0, 0)
        };
        var subtitle = new Label
        {
            Text = "PERSONAL ASSISTANT",
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Muted,
            Margin = new Padding(2, 0, 0, 12)
        };

        var status = CreateStatusPill("●  SYSTEM ONLINE", Color.FromArgb(55, 240, 177));
        status.Margin = new Padding(0, 4, 0, 12);

        reactor.Size = new Size(230, 230);
        reactor.Margin = new Padding(10, 4, 0, 8);

        listeningLabel.Text = "READY";
        listeningLabel.AutoSize = false;
        listeningLabel.Size = new Size(230, 28);
        listeningLabel.TextAlign = ContentAlignment.MiddleCenter;
        listeningLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        listeningLabel.ForeColor = Muted;
        listeningLabel.Margin = new Padding(0, 0, 0, 16);

        content.Controls.Add(title);
        content.Controls.Add(subtitle);
        content.Controls.Add(status);
        content.Controls.Add(reactor);
        content.Controls.Add(listeningLabel);
        content.Controls.Add(CreateSectionLabel("SYSTEM"));
        content.Controls.Add(CreateInfoRow("Build", $"#{BuildInfo.ReleaseBuild}"));
        content.Controls.Add(CreateInfoRow("Platform", "Windows x64"));
        content.Controls.Add(CreateInfoRow("Voice", "Enabled"));
        content.Controls.Add(CreateInfoRow("Updates", "Automatic"));

        var updateButton = CreateButton("Check for updates", false);
        updateButton.Width = 230;
        updateButton.Margin = new Padding(0, 14, 0, 0);
        updateButton.Click += async (_, _) => await UpdateService.CheckAndOfferUpdateAsync();
        content.Controls.Add(updateButton);

        sidebar.Controls.Add(content);
        return sidebar;
    }

    private Control BuildMainPanel()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent,
            Padding = new Padding(6, 0, 0, 0)
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Panel { Height = 72, Dock = DockStyle.Top, BackColor = Color.Transparent };
        var headline = new Label
        {
            Text = "COMMAND CENTER",
            AutoSize = true,
            Font = new Font("Segoe UI", 20F, FontStyle.Bold),
            ForeColor = TextMain,
            Location = new Point(4, 5)
        };
        var hint = new Label
        {
            Text = "Control your PC with voice or natural commands",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F),
            ForeColor = Muted,
            Location = new Point(7, 43)
        };
        header.Controls.Add(headline);
        header.Controls.Add(hint);

        var commandCard = CreateCardPanel();
        commandCard.Dock = DockStyle.Top;
        commandCard.Height = 155;
        commandCard.Margin = new Padding(0, 0, 0, 12);
        commandCard.Padding = new Padding(18);

        commandBox.Multiline = true;
        commandBox.BorderStyle = BorderStyle.None;
        commandBox.BackColor = Color.FromArgb(11, 18, 29);
        commandBox.ForeColor = TextMain;
        commandBox.Font = new Font("Segoe UI", 12F);
        commandBox.PlaceholderText = "Ask Jarvis…  e.g. open YouTube, screenshot, volume up";
        commandBox.Dock = DockStyle.Top;
        commandBox.Height = 64;

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0)
        };

        runButton.Text = "EXECUTE  ›";
        StylePrimaryButton(runButton);
        runButton.Width = 140;
        runButton.Click += async (_, _) => await ExecuteCurrentCommandAsync();

        listenButton.Text = "◉  LISTEN";
        StyleSecondaryButton(listenButton);
        listenButton.Width = 130;
        listenButton.Click += async (_, _) => await ListenAndExecuteAsync();

        actionRow.Controls.Add(runButton);
        actionRow.Controls.Add(listenButton);
        commandCard.Controls.Add(actionRow);
        commandCard.Controls.Add(commandBox);

        var quickCard = CreateCardPanel();
        quickCard.Dock = DockStyle.Top;
        quickCard.Height = 120;
        quickCard.Margin = new Padding(0, 0, 0, 12);
        quickCard.Padding = new Padding(14);

        var quickGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        for (var i = 0; i < 6; i++) quickGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.6667F));

        AddQuickAction(quickGrid, 0, "◈", "YouTube", "open YouTube");
        AddQuickAction(quickGrid, 1, "⌕", "Search", "search Jarvis AI assistant");
        AddQuickAction(quickGrid, 2, "▣", "Screenshot", "screenshot");
        AddQuickAction(quickGrid, 3, "◐", "Volume +", "volume up");
        AddQuickAction(quickGrid, 4, "▶", "Play/Pause", "play pause");
        AddQuickAction(quickGrid, 5, "⌘", "System", "system info");
        quickCard.Controls.Add(quickGrid);

        var responseCard = CreateCardPanel();
        responseCard.Dock = DockStyle.Fill;
        responseCard.Margin = new Padding(0, 0, 0, 12);
        responseCard.Padding = new Padding(18);

        var responseTitle = new Label
        {
            Text = "●  JARVIS RESPONSE",
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = Cyan,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 8)
        };

        responseBox.Dock = DockStyle.Fill;
        responseBox.BorderStyle = BorderStyle.None;
        responseBox.ReadOnly = true;
        responseBox.BackColor = Panel;
        responseBox.ForeColor = TextMain;
        responseBox.Font = new Font("Segoe UI", 12F);
        responseBox.Text = "Systems online. Ready for your command.";
        responseBox.DetectUrls = true;

        responseCard.Controls.Add(responseBox);
        responseCard.Controls.Add(responseTitle);

        var footer = new Label
        {
            Text = "VOICE • AUTOMATION • MEDIA • SYSTEM CONTROL • AUTO UPDATE",
            Dock = DockStyle.Fill,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(83, 101, 126),
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            Padding = new Padding(0, 4, 0, 0)
        };

        main.Controls.Add(header, 0, 0);
        main.Controls.Add(commandCard, 0, 1);
        main.Controls.Add(quickCard, 0, 2);
        main.Controls.Add(responseCard, 0, 3);
        main.Controls.Add(footer, 0, 4);
        return main;
    }

    private void AddQuickAction(TableLayoutPanel grid, int column, string symbol, string label, string command)
    {
        var button = CreateQuickButton(symbol, label);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(5);
        button.Click += async (_, _) =>
        {
            commandBox.Text = command;
            await ExecuteCurrentCommandAsync();
        };
        grid.Controls.Add(button, column, 0);
    }

    private static Button CreateQuickButton(string symbol, string label)
    {
        var button = new Button
        {
            Text = $"{symbol}\n{label}",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 27, 41),
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderColor = Color.FromArgb(35, 58, 80), BorderSize = 1, MouseDownBackColor = Color.FromArgb(26, 62, 82) }
        };
        button.MouseEnter += (_, _) => button.BackColor = Color.FromArgb(22, 47, 64);
        button.MouseLeave += (_, _) => button.BackColor = Color.FromArgb(16, 27, 41);
        return button;
    }

    private static RoundedPanel CreateCardPanel() => new()
    {
        BackColor = Panel,
        BorderColor = Color.FromArgb(31, 53, 74),
        Radius = 20
    };

    private static Label CreateSectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 8F, FontStyle.Bold),
        ForeColor = Muted,
        Margin = new Padding(0, 4, 0, 8)
    };

    private static Control CreateStatusPill(string text, Color color)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = color,
            BackColor = Color.FromArgb(14, 48, 43),
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            Padding = new Padding(10, 6, 10, 6)
        };
    }

    private static Control CreateInfoRow(string key, string value)
    {
        var row = new Panel { Width = 230, Height = 30, BackColor = Color.Transparent };
        row.Controls.Add(new Label { Text = key, ForeColor = Muted, AutoSize = true, Location = new Point(0, 5) });
        row.Controls.Add(new Label { Text = value, ForeColor = TextMain, AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Location = new Point(125, 5) });
        return row;
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button { Text = text };
        if (primary) StylePrimaryButton(button); else StyleSecondaryButton(button);
        return button;
    }

    private static void StylePrimaryButton(Button button)
    {
        button.Height = 38;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Cyan;
        button.ForeColor = Color.FromArgb(0, 18, 24);
        button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(121, 241, 255);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(46, 187, 213);
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.Height = 38;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(13, 27, 42);
        button.ForeColor = Cyan;
        button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderColor = Color.FromArgb(49, 91, 116);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 48, 65);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(18, 60, 78);
    }

    private async Task ExecuteCurrentCommandAsync()
    {
        var command = commandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            SetResponse("Enter a command first.");
            return;
        }

        runButton.Enabled = false;
        try
        {
            var result = ExecuteCommand(command);
            SetResponse(result);
            _ = SpeakAsync(result);
            await Task.CompletedTask;
        }
        finally
        {
            runButton.Enabled = true;
        }
    }

    private async Task ListenAndExecuteAsync()
    {
        listenButton.Enabled = false;
        listeningLabel.Text = "●  LISTENING";
        listeningLabel.ForeColor = Cyan;
        reactor.Listening = true;
        SetResponse("Listening… Speak naturally.");
        try
        {
            var heard = await ListenOnceAsync();
            if (string.IsNullOrWhiteSpace(heard))
            {
                SetResponse("I didn't hear a command. Check your microphone and Windows speech settings.");
                return;
            }

            commandBox.Text = heard;
            var result = ExecuteCommand(heard);
            SetResponse(result);
            _ = SpeakAsync(result);
        }
        finally
        {
            reactor.Listening = false;
            listeningLabel.Text = "READY";
            listeningLabel.ForeColor = Muted;
            listenButton.Enabled = true;
        }
    }

    private string ExecuteCommand(string rawCommand)
    {
        var command = rawCommand.Trim();
        var lower = command.ToLowerInvariant();
        try
        {
            if (lower is "time" or "what time is it" || lower.Contains("current time")) return $"It is {DateTime.Now:h:mm tt}.";
            if (lower is "date" or "what is the date" || lower.Contains("today's date")) return $"Today is {DateTime.Now:dddd, d MMMM yyyy}.";
            if (lower.Contains("system info") || lower.Contains("computer info")) return GetSystemInfo();
            if (lower.Contains("screenshot")) return TakeScreenshot();
            if (lower is "volume up" || lower.Contains("increase volume")) { PressMediaKey(VkVolumeUp); return "Volume increased."; }
            if (lower is "volume down" || lower.Contains("decrease volume")) { PressMediaKey(VkVolumeDown); return "Volume decreased."; }
            if (lower.Contains("mute")) { PressMediaKey(VkVolumeMute); return "Toggled mute."; }
            if (lower.Contains("play pause") || lower is "pause" || lower is "play") { PressMediaKey(VkMediaPlayPause); return "Toggled media playback."; }
            if (lower.Contains("next track") || lower.Contains("next song")) { PressMediaKey(VkMediaNext); return "Skipping to the next track."; }
            if (lower.Contains("previous track") || lower.Contains("previous song")) { PressMediaKey(VkMediaPrev); return "Going to the previous track."; }
            if (lower.Contains("lock computer") || lower == "lock pc") { LockWorkStation(); return "Locking the computer."; }
            if (lower.Contains("restart computer") || lower == "restart pc") return ConfirmPowerAction(true);
            if (lower.Contains("shut down computer") || lower.Contains("shutdown computer") || lower == "shutdown pc") return ConfirmPowerAction(false);
            if (lower.StartsWith("search "))
            {
                var query = command[7..].Trim();
                if (query.Length == 0) return "Tell me what to search for.";
                OpenTarget($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
                return $"Searching for {query}.";
            }
            if (lower.StartsWith("open ")) return OpenRequestedTarget(command[5..].Trim());
            return "I don't know that command yet. Try open YouTube, search something, screenshot, system info, volume up, play pause, or lock computer.";
        }
        catch (Exception ex)
        {
            return $"I couldn't complete that command: {ex.Message}";
        }
    }

    private string OpenRequestedTarget(string requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return "Tell me what to open.";
        var key = requested.Trim().ToLowerInvariant();
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"] = "chrome.exe", ["google chrome"] = "chrome.exe", ["edge"] = "msedge.exe",
            ["microsoft edge"] = "msedge.exe", ["notepad"] = "notepad.exe", ["calculator"] = "calc.exe",
            ["settings"] = "ms-settings:", ["file explorer"] = "explorer.exe", ["explorer"] = "explorer.exe",
            ["youtube"] = "https://www.youtube.com", ["github"] = "https://github.com",
            ["gmail"] = "https://mail.google.com", ["google"] = "https://www.google.com"
        };
        if (targets.TryGetValue(key, out var target)) { OpenTarget(target); return $"Opening {requested}."; }
        if (Uri.TryCreate(requested, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) { OpenTarget(uri.ToString()); return $"Opening {requested}."; }
        if (requested.Contains('.') && !requested.Contains(' ')) { OpenTarget(requested.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? requested : $"https://{requested}"); return $"Opening {requested}."; }
        return $"I don't have a safe launcher for {requested} yet.";
    }

    private static void OpenTarget(string target) => Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });

    private string TakeScreenshot()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        if (screen is null) return "I couldn't find a display to capture.";
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Jarvis Screenshots");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Jarvis_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        using var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
        return $"Screenshot saved to {path}.";
    }

    private static string GetSystemInfo() => $"Computer {Environment.MachineName}. Windows {Environment.OSVersion.Version}. {Environment.ProcessorCount} logical processors. 64-bit OS: {Environment.Is64BitOperatingSystem}. User: {Environment.UserName}.";

    private string ConfirmPowerAction(bool restart)
    {
        var action = restart ? "restart" : "shut down";
        var answer = MessageBox.Show($"Are you sure you want Jarvis to {action} this computer now?", "Jarvis confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return $"Cancelled {action}.";
        Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = restart ? "/r /t 0" : "/s /t 0", UseShellExecute = false, CreateNoWindow = true });
        return restart ? "Restarting the computer." : "Shutting down the computer.";
    }

    private static void PressMediaKey(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private void SetResponse(string text) => responseBox.Text = text;

    private static async Task<string?> ListenOnceAsync()
    {
        const string script = """
Add-Type -AssemblyName System.Speech
$info = [System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers() | Select-Object -First 1
if (-not $info) { exit 2 }
$recognizer = New-Object System.Speech.Recognition.SpeechRecognitionEngine($info)
$recognizer.SetInputToDefaultAudioDevice()
$recognizer.LoadGrammar((New-Object System.Speech.Recognition.DictationGrammar))
$result = $recognizer.Recognize([TimeSpan]::FromSeconds(8))
if ($result) { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Write-Output $result.Text }
$recognizer.Dispose()
""";
        var path = Path.Combine(Path.GetTempPath(), $"jarvis-listen-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(path, script, Encoding.UTF8);
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = "powershell.exe", Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -File \"{path}\"", UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            if (process is null) return null;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = (await outputTask).Trim();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private static async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        var script = $"""
Add-Type -AssemblyName System.Speech
$text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{encoded}'))
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.Speak($text)
$synth.Dispose()
""";
        var path = Path.Combine(Path.GetTempPath(), $"jarvis-speak-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(path, script, Encoding.UTF8);
        try
        {
            using var process = Process.Start(new ProcessStartInfo { FileName = "powershell.exe", Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -File \"{path}\"", UseShellExecute = false, CreateNoWindow = true });
            if (process is not null) await process.WaitForExitAsync();
        }
        catch { }
        finally { try { File.Delete(path); } catch { } }
    }
}

internal sealed class GradientPanel : Panel
{
    public Color StartColor { get; set; } = Color.Black;
    public Color EndColor { get; set; } = Color.Navy;
    public GradientPanel() => DoubleBuffered = true;
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, StartColor, EndColor, 35F);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

internal sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 18;
    public Color BorderColor { get; set; } = Color.FromArgb(40, 60, 80);
    public RoundedPanel() => DoubleBuffered = true;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(ClientRectangle, Radius);
        using var pen = new Pen(BorderColor, 1F);
        e.Graphics.DrawPath(pen, path);
    }
    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using var path = Rounded(ClientRectangle, Radius);
        Region = new Region(path);
    }
    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        var r = Rectangle.Inflate(bounds, -1, -1);
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ArcReactorControl : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 35 };
    private float angle;
    private bool listening;
    public bool Listening
    {
        get => listening;
        set { listening = value; Invalidate(); }
    }

    public ArcReactorControl()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        timer.Tick += (_, _) => { angle = (angle + (Listening ? 5.4F : 2.0F)) % 360F; Invalidate(); };
        timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = Math.Min(Width, Height) - 24;
        var rect = new RectangleF((Width - size) / 2F, (Height - size) / 2F, size, size);

        using var glow = new Pen(Color.FromArgb(Listening ? 125 : 75, 83, 233, 255), Listening ? 9F : 6F);
        e.Graphics.DrawEllipse(glow, rect);

        var inner = RectangleF.Inflate(rect, -18, -18);
        using var ring = new Pen(Color.FromArgb(180, 83, 233, 255), 2F);
        e.Graphics.DrawArc(ring, inner, angle, 105F);
        e.Graphics.DrawArc(ring, inner, angle + 180F, 105F);

        var core = RectangleF.Inflate(inner, -27, -27);
        using var brush = new LinearGradientBrush(core, Color.FromArgb(125, 244, 255), Color.FromArgb(19, 92, 128), 45F);
        e.Graphics.FillEllipse(brush, core);
        using var corePen = new Pen(Color.FromArgb(210, 130, 248, 255), 2F);
        e.Graphics.DrawEllipse(corePen, core);

        using var font = new Font("Segoe UI", 24F, FontStyle.Bold);
        var label = Listening ? "●" : "J";
        var measured = e.Graphics.MeasureString(label, font);
        e.Graphics.DrawString(label, font, Brushes.White, Width / 2F - measured.Width / 2F, Height / 2F - measured.Height / 2F);
    }
}
