using System.Drawing.Drawing2D;

namespace Jarvis.Windows;

internal sealed partial class ModernJarvisForm
{
    private readonly Label dashboardClock = new();
    private readonly Label coreOverview = new();
    private readonly Label activityFeed = new();
    private readonly Label capabilityStatus = new();
    private readonly Label missionTimeline = new();
    private readonly Label memoryInsights = new();
    private readonly Label providerStatus = new();
    private readonly VoiceWaveControl voiceWave = new();

    private Control BuildCommandCenter()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Padding = new Padding(7) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildNavigation(), 0, 0);
        root.Controls.Add(BuildDashboardBody(), 1, 0);
        return root;
    }

    private Control BuildNavigation()
    {
        var shell = DashboardCard();
        shell.Dock = DockStyle.Fill;
        shell.Margin = new Padding(0, 0, 8, 0);
        shell.Padding = new Padding(12);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.Transparent };
        nav.Controls.Add(new Label { Text = "◉  J A R V I S", Width = 165, Height = 39, ForeColor = Cyan, Font = new Font("Segoe UI", 14F, FontStyle.Bold) });
        nav.Controls.Add(new Label { Text = "COMMAND CENTER", Width = 165, Height = 29, ForeColor = Muted, Font = new Font("Segoe UI", 8F, FontStyle.Bold) });
        nav.Controls.Add(NavButton("⌗  Command Center", () => commandBox.Focus()));
        nav.Controls.Add(NavButton("◎  AI Core", () => reactor.Focus()));
        nav.Controls.Add(NavButton("◈  Capabilities", ShowCapabilities));
        nav.Controls.Add(NavButton("☷  Tasks", ShowTasks));
        nav.Controls.Add(NavButton("▦  Calendar", ShowLocalSchedule));
        nav.Controls.Add(NavButton("◉  Memory", ShowHistory));
        nav.Controls.Add(NavButton("▤  Conversations", ShowHistorySearch));
        nav.Controls.Add(NavButton("⌁  Workflows", () => MessageBox.Show(this, "Configurable workflows are not available yet. Quick commands and local tasks are ready.", "Jarvis workflows")));
        nav.Controls.Add(NavButton("⚙  Settings", ShowProviderSettings));
        nav.Controls.Add(new Label { Text = "VOICE CONTROL", Width = 165, Height = 31, ForeColor = Cyan, Font = new Font("Segoe UI", 8F, FontStyle.Bold), Padding = new Padding(6, 12, 0, 0) });
        nav.Controls.Add(NavButton("◉  Speak to Jarvis", async () => await ListenAndExecuteAsync()));
        nav.Controls.Add(NavButton("◌  Toggle wake word", async () => await ToggleWakeAsync()));
        nav.Controls.Add(new Label { Text = $"BUILD {BuildInfo.ReleaseBuild}  •  WINDOWS X64", Width = 165, Height = 38, ForeColor = Muted, Font = new Font("Segoe UI", 7F, FontStyle.Bold), Padding = new Padding(6, 15, 0, 0) });
        shell.Controls.Add(nav);
        return shell;
    }

    private Button NavButton(string text, Action action)
    {
        var button = new Button { Text = text, Width = 162, Height = 37, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(11, 27, 41), ForeColor = TextMain, Font = new Font("Segoe UI", 9F), Margin = new Padding(0, 0, 0, 4), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = Color.FromArgb(27, 63, 85);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 60, 79);
        button.Click += (_, _) => action();
        return button;
    }

    private Control BuildDashboardBody()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.Transparent, Margin = new Padding(0) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 26));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        body.Controls.Add(BuildDashboardHeader(), 0, 0);
        body.Controls.Add(BuildCoreRow(), 0, 1);
        body.Controls.Add(BuildMissionRow(), 0, 2);
        body.Controls.Add(BuildInsightsRow(), 0, 3);
        body.Controls.Add(BuildVoiceBar(), 0, 4);
        return body;
    }

    private Control BuildDashboardHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        header.Controls.Add(new Label { Text = "●  SYSTEM STATUS  /  OPTIMAL", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(99, 234, 197), Font = new Font("Segoe UI", 8F, FontStyle.Bold) }, 0, 0);
        dashboardClock.Dock = DockStyle.Fill;
        dashboardClock.TextAlign = ContentAlignment.MiddleCenter;
        dashboardClock.ForeColor = Cyan;
        dashboardClock.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        header.Controls.Add(dashboardClock, 1, 0);
        var search = NavButton("⌕  Search history", ShowHistorySearch);
        search.Dock = DockStyle.Fill;
        header.Controls.Add(search, 2, 0);
        return header;
    }

    private Control BuildCoreRow()
    {
        var row = DashboardGrid(3, 19, 57, 24);
        var overview = DashboardCard();
        overview.Controls.Add(FillText(coreOverview, 9F));
        overview.Controls.Add(CardHeader("AI CORE OVERVIEW"));
        row.Controls.Add(overview, 0, 0);

        var core = DashboardCard();
        core.BorderColor = Color.FromArgb(41, 132, 166);
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Color.Transparent };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        stack.Controls.Add(new Label { Text = "J A R V I S    /    A I   C O R E", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Cyan, Font = new Font("Segoe UI", 17F, FontStyle.Bold) }, 0, 0);
        reactor.Dock = DockStyle.Fill;
        reactor.Margin = new Padding(0);
        stack.Controls.Add(reactor, 0, 1);
        stack.Controls.Add(new Label { Text = "LOCAL COMMANDS  •  OPTIONAL CLOUD INTELLIGENCE", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, Font = new Font("Segoe UI", 8F, FontStyle.Bold) }, 0, 2);
        core.Controls.Add(stack);
        row.Controls.Add(core, 1, 0);

        var feed = DashboardCard();
        var feedStack = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = Color.Transparent };
        feedStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        feedStack.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        feedStack.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        feedStack.Controls.Add(CardHeader("LIVE INTELLIGENCE FEED"), 0, 0);
        feedStack.Controls.Add(FillText(activityFeed, 8F), 0, 1);
        var answer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(9, 22, 35), Padding = new Padding(8) };
        responseBox.Dock = DockStyle.Fill;
        responseBox.BorderStyle = BorderStyle.None;
        responseBox.ReadOnly = true;
        responseBox.BackColor = answer.BackColor;
        responseBox.ForeColor = TextMain;
        responseBox.Font = new Font("Segoe UI", 9F);
        responseBox.Text = "Ready for your command.";
        answer.Controls.Add(responseBox);
        feedStack.Controls.Add(answer, 0, 2);
        feed.Controls.Add(feedStack);
        row.Controls.Add(feed, 2, 0);
        return row;
    }

    private Control BuildMissionRow()
    {
        var row = DashboardGrid(3, 43, 28, 29);
        var capabilities = DashboardCard();
        capabilities.Controls.Add(FillText(capabilityStatus, 9F));
        capabilities.Controls.Add(CardHeader("ACTIVE CAPABILITIES"));
        row.Controls.Add(capabilities, 0, 0);
        var timeline = DashboardCard();
        timeline.Controls.Add(FillText(missionTimeline, 9F));
        timeline.Controls.Add(CardHeader("MISSION TIMELINE / LOCAL TASKS"));
        row.Controls.Add(timeline, 1, 0);
        var quick = DashboardCard();
        var quickGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Color.Transparent, Padding = new Padding(8, 27, 8, 6) };
        quickGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        quickGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        quickGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        quickGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        quickGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        AddQuickAction(quickGrid, 0, 0, "⌕", "Search", "search Jarvis AI");
        AddQuickAction(quickGrid, 1, 0, "▣", "Screenshot", "screenshot");
        AddQuickAction(quickGrid, 0, 1, "◐", "Volume +", "volume up");
        AddQuickAction(quickGrid, 1, 1, "▶", "Play/Pause", "play pause");
        AddQuickAction(quickGrid, 0, 2, "☷", "Tasks", "show tasks");
        AddQuickAction(quickGrid, 1, 2, "◉", "System", "system info");
        quick.Controls.Add(quickGrid);
        quick.Controls.Add(CardHeader("QUICK COMMANDS"));
        row.Controls.Add(quick, 2, 0);
        return row;
    }

    private Control BuildInsightsRow()
    {
        var row = DashboardGrid(3, 35, 28, 37);
        var system = DashboardCard();
        monitorLabel.Dock = DockStyle.Fill;
        monitorLabel.ForeColor = TextMain;
        monitorLabel.Font = new Font("Segoe UI", 9F);
        monitorLabel.Padding = new Padding(10, 30, 10, 8);
        system.Controls.Add(monitorLabel);
        system.Controls.Add(CardHeader("SYSTEM MONITOR / LIVE"));
        row.Controls.Add(system, 0, 0);
        var memory = DashboardCard();
        memory.Controls.Add(FillText(memoryInsights, 9F));
        memory.Controls.Add(CardHeader("MEMORY INSIGHTS"));
        row.Controls.Add(memory, 1, 0);
        var provider = DashboardCard();
        var settings = NavButton("Manage provider  ›", ShowProviderSettings);
        settings.Dock = DockStyle.Bottom;
        settings.Height = 33;
        settings.Margin = new Padding(8);
        providerStatus.Dock = DockStyle.Fill;
        providerStatus.ForeColor = TextMain;
        providerStatus.Font = new Font("Segoe UI", 9F);
        providerStatus.Padding = new Padding(10, 30, 10, 4);
        provider.Controls.Add(providerStatus);
        provider.Controls.Add(settings);
        provider.Controls.Add(CardHeader("AI PROVIDER / CONNECTION"));
        row.Controls.Add(provider, 2, 0);
        return row;
    }

    private Control BuildVoiceBar()
    {
        var bar = DashboardCard();
        bar.Dock = DockStyle.Fill;
        bar.Margin = new Padding(4, 6, 4, 2);
        bar.BorderColor = Color.FromArgb(38, 125, 166);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Color.Transparent, Padding = new Padding(8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        voiceWave.Dock = DockStyle.Fill;
        grid.Controls.Add(voiceWave, 0, 0);
        commandBox.Dock = DockStyle.Fill;
        commandBox.Multiline = false;
        commandBox.BorderStyle = BorderStyle.FixedSingle;
        commandBox.BackColor = Color.FromArgb(9, 21, 33);
        commandBox.ForeColor = TextMain;
        commandBox.Font = new Font("Segoe UI", 11F);
        commandBox.PlaceholderText = "Ask Jarvis or enter a local command…";
        commandBox.Margin = new Padding(6, 7, 6, 7);
        grid.Controls.Add(commandBox, 1, 0);
        runButton.Text = "RUN COMMAND";
        StylePrimaryButton(runButton);
        runButton.Dock = DockStyle.Fill;
        runButton.Click += async (_, _) => await ExecuteCurrentCommandAsync();
        grid.Controls.Add(runButton, 2, 0);
        listenButton.Text = "◉  TALK";
        StyleSecondaryButton(listenButton);
        listenButton.Dock = DockStyle.Fill;
        listenButton.Click += async (_, _) => await ListenAndExecuteAsync();
        grid.Controls.Add(listenButton, 3, 0);
        listeningLabel.Dock = DockStyle.Fill;
        listeningLabel.TextAlign = ContentAlignment.MiddleCenter;
        listeningLabel.ForeColor = Cyan;
        listeningLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        listeningLabel.Text = "READY / LOCAL";
        grid.Controls.Add(listeningLabel, 4, 0);
        bar.Controls.Add(grid);
        return bar;
    }

    private static TableLayoutPanel DashboardGrid(int count, params float[] widths)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = count, RowCount = 1, BackColor = Color.Transparent };
        foreach (var width in widths) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        return grid;
    }

    private static RoundedPanel DashboardCard() => new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(11, 23, 38), BorderColor = Color.FromArgb(30, 81, 106), Radius = 12, Padding = new Padding(7), Margin = new Padding(4) };
    private static Label CardHeader(string title) => new() { Text = title, Dock = DockStyle.Top, Height = 26, ForeColor = Cyan, Font = new Font("Segoe UI", 8F, FontStyle.Bold), Padding = new Padding(9, 5, 0, 0) };
    private static Label FillText(Label label, float size)
    {
        label.Dock = DockStyle.Fill;
        label.ForeColor = TextMain;
        label.Font = new Font("Segoe UI", size);
        label.Padding = new Padding(10, 30, 8, 8);
        return label;
    }

    private void AddQuickAction(TableLayoutPanel grid, int column, int row, string symbol, string label, string command)
    {
        var button = CreateQuickButton(symbol, label);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(3);
        button.Click += async (_, _) => { commandBox.Text = command; await ExecuteCurrentCommandAsync(); };
        grid.Controls.Add(button, column, row);
    }

    private void UpdateDashboardData()
    {
        dashboardClock.Text = DateTime.Now.ToString("dddd, dd MMM  •  HH:mm:ss");
        var pending = tasks.Count(task => !task.Done);
        var providerName = assistantSettings.Endpoint.Contains("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) ? "GEMINI" : assistantSettings.Endpoint.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase) ? "OPENAI" : "CUSTOM HTTPS";
        var providerReady = !string.IsNullOrWhiteSpace(assistantSettings.ApiKey);
        coreOverview.Text = $"◎  Core            Ready\n\n◉  Voice          {(wake.IsRunning ? "Listening locally" : "Push to talk")}\n\n☷  Tasks          {pending} pending\n\n◈  Provider       {(providerReady ? "Configured" : "Not configured")}\n\n▤  History        {history.Count} turns";
        var recent = history.Where(turn => turn.Role == "user").TakeLast(3).Reverse().Select(turn => "›  " + (turn.Text.Length > 55 ? turn.Text[..55] + "…" : turn.Text));
        activityFeed.Text = history.Count == 0 ? "No recent activity.\n\nCommands and conversations appear here after use." : string.Join("\n\n", recent);
        capabilityStatus.Text = $"●  System commands     Ready\n●  Browser open/search   Ready\n●  Local tasks/reminders  Ready\n●  AI chat              {(providerReady ? "Configured" : "Needs API key")}\n○  Advanced agents     Unavailable";
        var upcoming = tasks.Where(task => !task.Done && task.DueAt is not null).OrderBy(task => task.DueAt).Take(3).Select(task => $"{task.DueAt:HH:mm}  •  {task.Title}");
        missionTimeline.Text = !upcoming.Any() ? $"{pending} pending tasks\n\nNo scheduled reminders." : string.Join("\n\n", upcoming);
        memoryInsights.Text = $"{history.Count} conversation turns saved\n{pending} pending tasks\n\nStored locally for this Windows user.";
        providerStatus.Text = $"{providerName}\n{(providerReady ? "Key saved securely; connection untested" : "No API key configured")}\nModel: {assistantSettings.Model}";
        voiceWave.Active = wake.IsRunning;
    }

    private void ShowCapabilities() => MessageBox.Show(this, "Ready: system commands, browser opening/search, local tasks/reminders, encrypted history. Cloud chat works after you configure a provider key. Advanced agents are not installed.", "Jarvis capabilities");

    private void ShowLocalSchedule()
    {
        var upcoming = tasks.Where(task => !task.Done && task.DueAt is not null).OrderBy(task => task.DueAt).Select(task => $"{task.DueAt:g}  •  {task.Title}").ToList();
        MessageBox.Show(this, upcoming.Count == 0 ? "No reminders scheduled. Say 'remind me in 10 minutes to stretch' or add one through Tasks." : string.Join("\n", upcoming), "Jarvis local schedule");
    }

    private void ShowHistorySearch()
    {
        using var dialog = new Form { Text = "Search Jarvis conversations", Size = new Size(650, 500), StartPosition = FormStartPosition.CenterParent, BackColor = Panel, ForeColor = TextMain };
        var query = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Search local conversation history" };
        var results = new ListBox { Dock = DockStyle.Fill, BackColor = Panel2, ForeColor = TextMain };
        void Refresh()
        {
            results.Items.Clear();
            foreach (var turn in history.Where(turn => turn.Text.Contains(query.Text, StringComparison.OrdinalIgnoreCase)).TakeLast(100)) results.Items.Add($"{turn.Role}: {turn.Text}");
        }
        query.TextChanged += (_, _) => Refresh();
        dialog.Controls.Add(results);
        dialog.Controls.Add(query);
        Refresh();
        dialog.ShowDialog(this);
    }
}

internal sealed class VoiceWaveControl : Control
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 65 };
    private int frame;
    private bool active;
    public bool Active { get => active; set { active = value; Invalidate(); } }
    public VoiceWaveControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        timer.Tick += (_, _) => { if (!Active) return; frame++; Invalidate(); };
        timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        for (var i = 0; i < 11; i++)
        {
            var pulse = Active ? Math.Abs(Math.Sin(frame * .24 + i * .7)) : .18;
            var height = 6 + (float)pulse * (Height * .64f);
            var x = 6 + i * 5;
            using var pen = new Pen(Color.FromArgb(80 + (int)(pulse * 175), 83, 233, 255), 2F);
            e.Graphics.DrawLine(pen, x, (Height - height) / 2, x, (Height + height) / 2);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
