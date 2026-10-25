using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Windows;

internal static class ModernProgram
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return AssistantData.LoadSettings().Endpoint.StartsWith("https://", StringComparison.Ordinal) ? 0 : 1;
        var smoke = args.Contains("--ui-smoke-test");
        try
        {
            ApplicationConfiguration.Initialize();
            using var form = new ModernJarvisForm();
            if (smoke)
            {
                form.Shown += (_, _) =>
                {
                    var closeTimer = new System.Windows.Forms.Timer { Interval = 3000 };
                    closeTimer.Tick += (_, _) => { closeTimer.Stop(); closeTimer.Dispose(); form.Close(); };
                    closeTimer.Start();
                };
            }
            Application.Run(form);
            return 0;
        }
        catch (Exception error)
        {
            if (smoke)
            {
                var log = Environment.GetEnvironmentVariable("JARVIS_SMOKE_LOG");
                if (!string.IsNullOrWhiteSpace(log)) File.WriteAllText(log, error.ToString());
            }
            return 2;
        }
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
    private AssistantSettings assistantSettings = AssistantData.LoadSettings();
    private readonly List<AssistantTurn> history = AssistantData.LoadHistory();
    private readonly List<JarvisTask> tasks = AssistantData.LoadTasks();
    private CancellationTokenSource? pendingRequest;
    private readonly NotifyIcon trayIcon = new();
    private bool exitRequested;
    private readonly Label monitorLabel = new();
    private readonly System.Windows.Forms.Timer monitorTimer = new() { Interval = 2500 };
    private readonly WakeWordEngine wake = new();

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
        monitorTimer.Tick += (_, _) => monitorLabel.Text = SystemMonitor.Snapshot();
        monitorTimer.Tick += (_, _) => CheckReminders();
        monitorLabel.Text = SystemMonitor.Snapshot();
        monitorTimer.Start();
        wake.StatusChanged += status => { if (IsHandleCreated) BeginInvoke((Action)(() => { listeningLabel.Text = status; reactor.Listening = wake.IsRunning; })); };
        wake.CommandHeard += command => { if (IsHandleCreated) BeginInvoke((Action)(async () => {
            commandBox.Text = command;
            try { var result = await ExecuteWithCloudAsync(command); SetResponse(result); if (assistantSettings.SpeakReplies) await SpeakAsync(result); }
            catch (Exception error) { SetResponse(error.Message); }
            finally { wake.Resume(); }
        })); };
        Shown += async (_, _) => { if (assistantSettings.WakeEnabled && WakeModelStore.Installed) await StartWakeAsync(); };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open Jarvis", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Exit Jarvis", null, (_, _) => { exitRequested = true; Close(); });
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "Jarvis Assistant";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        trayIcon.Visible = false;
        monitorTimer.Stop();
        wake.Dispose();
        trayIcon.Dispose();
        base.OnFormClosing(e);
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
        content.Controls.Add(CreateInfoRow("Voice", "Push to talk"));
        content.Controls.Add(CreateInfoRow("Updates", "Checks on launch"));

        var updateButton = CreateButton("Check for updates", false);
        updateButton.Width = 230;
        updateButton.Margin = new Padding(0, 14, 0, 0);
        updateButton.Click += async (_, _) => await UpdateService.CheckAndOfferUpdateAsync();
        content.Controls.Add(updateButton);
        var settingsButton = CreateButton("AI provider settings", false);
        settingsButton.Width = 230;
        settingsButton.Margin = new Padding(0, 8, 0, 0);
        settingsButton.Click += (_, _) => ShowProviderSettings();
        content.Controls.Add(settingsButton);
        var historyButton = CreateButton("Conversation history", false);
        historyButton.Width = 230;
        historyButton.Margin = new Padding(0, 8, 0, 0);
        historyButton.Click += (_, _) => ShowHistory();
        content.Controls.Add(historyButton);
        var tasksButton = CreateButton("Tasks and reminders", false);
        tasksButton.Width = 230;
        tasksButton.Margin = new Padding(0, 8, 0, 0);
        tasksButton.Click += (_, _) => ShowTasks();
        content.Controls.Add(tasksButton);
        var cancelButton = CreateButton("Cancel AI response", false);
        cancelButton.Width = 230;
        cancelButton.Margin = new Padding(0, 8, 0, 0);
        cancelButton.Click += (_, _) => pendingRequest?.Cancel();
        content.Controls.Add(cancelButton);
        var installWakeButton = CreateButton("Install offline wake model", false);
        installWakeButton.Width = 230;
        installWakeButton.Margin = new Padding(0, 8, 0, 0);
        installWakeButton.Click += async (_, _) => await InstallWakeModelAsync();
        content.Controls.Add(installWakeButton);
        var wakeButton = CreateButton("Toggle Hey Jarvis", false);
        wakeButton.Width = 230;
        wakeButton.Margin = new Padding(0, 8, 0, 0);
        wakeButton.Click += async (_, _) => await ToggleWakeAsync();
        content.Controls.Add(wakeButton);

        sidebar.Controls.Add(content);
        return sidebar;
    }

    private Control BuildMainPanel()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.Transparent,
            Padding = new Padding(6, 0, 0, 0)
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

        var monitorCard = CreateCardPanel();
        monitorCard.Dock = DockStyle.Top;
        monitorCard.Height = 84;
        monitorCard.Margin = new Padding(0, 0, 0, 12);
        monitorCard.Padding = new Padding(18);
        var monitorTitle = new Label { Text = "LIVE SYSTEM MONITOR", Dock = DockStyle.Top, Height = 24, ForeColor = Cyan, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        monitorLabel.Dock = DockStyle.Fill;
        monitorLabel.ForeColor = TextMain;
        monitorLabel.TextAlign = ContentAlignment.MiddleLeft;
        monitorCard.Controls.Add(monitorLabel);
        monitorCard.Controls.Add(monitorTitle);

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
        main.Controls.Add(monitorCard, 0, 3);
        main.Controls.Add(responseCard, 0, 4);
        main.Controls.Add(footer, 0, 5);
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
            var result = await ExecuteWithCloudAsync(command);
            SetResponse(result);
            if (assistantSettings.SpeakReplies) _ = SpeakAsync(result);

        }
        finally
        {
            runButton.Enabled = true;
        }
    }

    private async Task ListenAndExecuteAsync()
    {
        if (wake.IsRunning) { SetResponse("Turn off Hey Jarvis before using push-to-talk."); return; }
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
            var result = await ExecuteWithCloudAsync(heard);
            SetResponse(result);
            if (assistantSettings.SpeakReplies) _ = SpeakAsync(result);
        }
        finally
        {
            reactor.Listening = false;
            listeningLabel.Text = "READY";
            listeningLabel.ForeColor = Muted;
            listenButton.Enabled = true;
        }
    }

    private async Task InstallWakeModelAsync()
    {
        if (WakeModelStore.Installed) { SetResponse("Offline wake model is ready."); return; }
        try
        {
            await WakeModelStore.InstallAsync(status => { if (IsHandleCreated) BeginInvoke((Action)(() => SetResponse(status))); });
            SetResponse("Offline wake model installed. Select Toggle Hey Jarvis to enable listening.");
        }
        catch (Exception error) { SetResponse($"Model installation failed: {error.Message}"); }
    }

    private async Task ToggleWakeAsync()
    {
        if (wake.IsRunning)
        {
            wake.Stop();
            assistantSettings.WakeEnabled = false;
            AssistantData.SaveSettings(assistantSettings);
            return;
        }
        await StartWakeAsync();
    }

    private async Task StartWakeAsync()
    {
        if (!WakeModelStore.Installed) { SetResponse("Install the offline wake model first."); return; }
        try
        {
            wake.Sensitivity = assistantSettings.WakeSensitivity;
            await wake.StartAsync();
            assistantSettings.WakeEnabled = true;
            AssistantData.SaveSettings(assistantSettings);
        }
        catch (Exception error)
        {
            assistantSettings.WakeEnabled = false;
            AssistantData.SaveSettings(assistantSettings);
            SetResponse($"Could not start Hey Jarvis: {error.Message}");
        }
    }
    private async Task<string> ExecuteWithCloudAsync(string command)
    {
        var local = ExecuteCommand(command);
        history.Add(new AssistantTurn("user", command));
        string result;
        if (local.StartsWith("I don't know that command yet.", StringComparison.Ordinal))
        {
            pendingRequest = new CancellationTokenSource();
            try
            {
                SetResponse("Contacting AI provider…");
                result = await AssistantCloud.ReplyAsync(assistantSettings, history,
                    partial => { if (IsHandleCreated) BeginInvoke((Action)(() => SetResponse(partial))); },
                    pendingRequest.Token);
            }
            catch (OperationCanceledException) { result = "AI response cancelled."; }
            catch (Exception ex) { result = $"Provider request failed: {ex.Message}"; }
            finally { pendingRequest.Dispose(); pendingRequest = null; }
        }
        else result = local;
        history.Add(new AssistantTurn("assistant", result));
        AssistantData.SaveHistory(history);
        return result;
    }

    private void ShowProviderSettings()
    {
        using var dialog = new Form {
            Text = "Jarvis settings", Size = new Size(560, 430),
            StartPosition = FormStartPosition.CenterParent, BackColor = Panel, ForeColor = TextMain,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        var endpointBox = new TextBox { Text = assistantSettings.Endpoint, Width = 490 };
        var modelBox = new TextBox { Text = assistantSettings.Model, Width = 490 };
        var keyBox = new TextBox { Text = assistantSettings.ApiKey, Width = 490, UseSystemPasswordChar = true };
        var speakBox = new CheckBox { Text = "Speak responses", Checked = assistantSettings.SpeakReplies, AutoSize = true, ForeColor = TextMain };
        var sensitivityBox = new NumericUpDown { Minimum = 35, Maximum = 90, Value = (decimal)(Math.Clamp(assistantSettings.WakeSensitivity, 0.35f, 0.90f) * 100), Width = 100 };
        var save = new Button { Text = "Save settings", Width = 150, DialogResult = DialogResult.OK };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), AutoScroll = true };
        foreach (var pair in new[] { ("HTTPS chat endpoint", endpointBox), ("Model", modelBox), ("API key, encrypted for this Windows user", keyBox) })
        {
            stack.Controls.Add(new Label { Text = pair.Item1, Width = 490, Height = 24, ForeColor = Muted });
            stack.Controls.Add(pair.Item2);
        }
        stack.Controls.Add(speakBox);
        stack.Controls.Add(new Label { Text = "Hey Jarvis sensitivity (35–90%)", Width = 490, Height = 24, ForeColor = Muted });
        stack.Controls.Add(sensitivityBox);
        stack.Controls.Add(new Label { Text = "Audio is handled by Windows speech services. Only typed or recognized text is sent to the configured provider.", Width = 490, Height = 42, ForeColor = Muted });
        stack.Controls.Add(save);
        dialog.Controls.Add(stack);
        dialog.AcceptButton = save;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!Uri.TryCreate(endpointBox.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(this, "The provider endpoint must use HTTPS.", "Jarvis settings");
            return;
        }
        assistantSettings = new AssistantSettings { Endpoint = endpointBox.Text.Trim(), Model = modelBox.Text.Trim(), ApiKey = keyBox.Text.Trim(), SpeakReplies = speakBox.Checked, WakeEnabled = assistantSettings.WakeEnabled, WakeSensitivity = (float)sensitivityBox.Value / 100f };
        wake.Sensitivity = assistantSettings.WakeSensitivity;
        AssistantData.SaveSettings(assistantSettings);
        SetResponse(assistantSettings.ApiKey.Length == 0 ? "Local commands ready. No AI provider key configured." : "AI provider settings saved.");
    }

    private void ShowHistory()
    {
        using var dialog = new Form { Text = "Jarvis conversation history", Size = new Size(660, 520), StartPosition = FormStartPosition.CenterParent, BackColor = Panel, ForeColor = TextMain };
        var display = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Panel, ForeColor = TextMain, Text = string.Join(Environment.NewLine + Environment.NewLine, history.Select(turn => $"{turn.Role.ToUpperInvariant()}: {turn.Text}")) };
        var clear = new Button { Text = "Clear history", Dock = DockStyle.Bottom, Height = 40 };
        clear.Click += (_, _) => {
            if (MessageBox.Show(dialog, "Delete local conversation history?", "Jarvis", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            history.Clear(); AssistantData.ClearHistory(); display.Clear();
        };
        dialog.Controls.Add(display);
        dialog.Controls.Add(clear);
        dialog.ShowDialog(this);
    }
    private void ShowTasks()
    {
        using var dialog = new Form { Text = "Jarvis tasks and reminders", Size = new Size(620, 500), StartPosition = FormStartPosition.CenterParent, BackColor = Panel, ForeColor = TextMain };
        var list = new ListBox { Dock = DockStyle.Fill, BackColor = Panel2, ForeColor = TextMain, Font = new Font("Segoe UI", 10F) };
        void Refresh()
        {
            list.Items.Clear();
            foreach (var task in tasks) list.Items.Add($"{(task.Done ? "✓" : "○")} {task.Title}{(task.DueAt is null ? "" : $"  ·  {task.DueAt:g}")}");
        }
        var entry = new TextBox { Dock = DockStyle.Top, PlaceholderText = "New task" };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.LeftToRight };
        var add = CreateButton("Add task", false);
        add.Click += (_, _) => { if (string.IsNullOrWhiteSpace(entry.Text)) return; tasks.Add(new JarvisTask(Guid.NewGuid(), entry.Text.Trim(), false, null, false)); AssistantData.SaveTasks(tasks); entry.Clear(); Refresh(); };
        var complete = CreateButton("Complete selected", false);
        complete.Click += (_, _) => { var index = list.SelectedIndex; if (index < 0 || index >= tasks.Count) return; tasks[index] = tasks[index] with { Done = true }; AssistantData.SaveTasks(tasks); Refresh(); };
        var remove = CreateButton("Delete selected", false);
        remove.Click += (_, _) => { var index = list.SelectedIndex; if (index < 0 || index >= tasks.Count) return; if (MessageBox.Show(dialog, "Delete this task?", "Jarvis", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return; tasks.RemoveAt(index); AssistantData.SaveTasks(tasks); Refresh(); };
        actions.Controls.AddRange(new Control[] { add, complete, remove });
        dialog.Controls.Add(list);
        dialog.Controls.Add(entry);
        dialog.Controls.Add(actions);
        Refresh();
        dialog.ShowDialog(this);
    }

    private void CheckReminders()
    {
        var due = tasks.Where(task => !task.Done && !task.Notified && task.DueAt is not null && task.DueAt <= DateTime.Now).ToList();
        if (due.Count == 0) return;
        foreach (var task in due)
        {
            var index = tasks.FindIndex(item => item.Id == task.Id);
            tasks[index] = task with { Notified = true };
            trayIcon.ShowBalloonTip(5000, "Jarvis reminder", task.Title, ToolTipIcon.Info);
        }
        AssistantData.SaveTasks(tasks);
    }

    private string ExecuteCommand(string rawCommand)
    {
        var command = rawCommand.Trim();
        var lower = command.ToLowerInvariant();
        try
        {
            if (lower.StartsWith("add task "))
            {
                var title = command[9..].Trim();
                if (title.Length == 0) return "Tell me what task to add.";
                tasks.Add(new JarvisTask(Guid.NewGuid(), title, false, null, false));
                AssistantData.SaveTasks(tasks);
                return $"Added task: {title}.";
            }
            if (lower is "show tasks" or "list tasks")
            {
                var pending = tasks.Where(task => !task.Done).Take(10).Select(task => task.Title).ToList();
                return pending.Count == 0 ? "You have no pending tasks." : "Pending tasks: " + string.Join("; ", pending);
            }
            if (lower.StartsWith("complete task "))
            {
                var title = command[14..].Trim();
                var index = tasks.FindIndex(task => !task.Done && task.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return $"I couldn't find a pending task named {title}.";
                tasks[index] = tasks[index] with { Done = true };
                AssistantData.SaveTasks(tasks);
                return $"Completed task: {tasks[index].Title}.";
            }
            var reminder = Regex.Match(command, @"^remind me in (\d+) (minute|minutes|hour|hours) to (.+)$", RegexOptions.IgnoreCase);
            if (reminder.Success)
            {
                if (!int.TryParse(reminder.Groups[1].Value, out var amount) || amount is < 1 or > 10080) return "Choose a reminder from 1 to 10080 minutes or hours.";
                var due = reminder.Groups[2].Value.StartsWith("hour", StringComparison.OrdinalIgnoreCase) ? DateTime.Now.AddHours(amount) : DateTime.Now.AddMinutes(amount);
                var title = reminder.Groups[3].Value.Trim();
                tasks.Add(new JarvisTask(Guid.NewGuid(), title, false, due, false));
                AssistantData.SaveTasks(tasks);
                return $"I'll remind you to {title} at {due:g}.";
            }
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
