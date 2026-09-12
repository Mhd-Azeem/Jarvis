using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Jarvis.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new JarvisForm());
    }
}

internal sealed class JarvisForm : Form
{
    private readonly TextBox commandBox = new();
    private readonly TextBox responseBox = new();
    private readonly Button runButton = new();
    private readonly Button listenButton = new();

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

    public JarvisForm()
    {
        Text = "JARVIS for Windows";
        Width = 820;
        Height = 620;
        MinimumSize = new Size(680, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(14, 18, 24);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 10F);

        var title = new Label
        {
            Text = "JARVIS",
            AutoSize = true,
            Font = new Font("Segoe UI", 28F, FontStyle.Bold),
            ForeColor = Color.DeepSkyBlue,
            Margin = new Padding(0, 0, 0, 4)
        };

        var subtitle = new Label
        {
            Text = "Windows control assistant • text + voice commands",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Margin = new Padding(0, 0, 0, 16)
        };

        commandBox.Multiline = true;
        commandBox.Height = 82;
        commandBox.Dock = DockStyle.Top;
        commandBox.BackColor = Color.FromArgb(27, 33, 43);
        commandBox.ForeColor = Color.White;
        commandBox.BorderStyle = BorderStyle.FixedSingle;
        commandBox.Font = new Font("Segoe UI", 11F);
        commandBox.PlaceholderText = "Try: open YouTube, screenshot, system info, volume up, play pause...";

        runButton.Text = "Run command";
        runButton.AutoSize = true;
        runButton.Padding = new Padding(8, 4, 8, 4);
        runButton.Click += async (_, _) => await ExecuteCurrentCommandAsync();

        listenButton.Text = "🎙 Listen";
        listenButton.AutoSize = true;
        listenButton.Padding = new Padding(8, 4, 8, 4);
        listenButton.Click += async (_, _) => await ListenAndExecuteAsync();

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 10)
        };
        buttonRow.Controls.Add(listenButton);
        buttonRow.Controls.Add(runButton);

        responseBox.Multiline = true;
        responseBox.ReadOnly = true;
        responseBox.Height = 105;
        responseBox.Dock = DockStyle.Top;
        responseBox.BackColor = Color.FromArgb(21, 26, 34);
        responseBox.ForeColor = Color.WhiteSmoke;
        responseBox.BorderStyle = BorderStyle.FixedSingle;
        responseBox.Text = "Ready. Type a command or press Listen.";

        var commands = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            ForeColor = Color.LightGray,
            Margin = new Padding(0, 16, 0, 0),
            Text = "Commands available now:\n" +
                   "• open Chrome / Edge / Notepad / Calculator / Settings\n" +
                   "• open YouTube / GitHub / Gmail\n" +
                   "• search Blender tutorial\n" +
                   "• screenshot\n" +
                   "• system info / time / date\n" +
                   "• volume up / volume down / mute\n" +
                   "• play pause / next track / previous track\n" +
                   "• lock computer\n" +
                   "• restart computer / shut down computer (confirmation required)"
        };

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(28),
            BackColor = BackColor
        };

        root.Controls.Add(title);
        root.Controls.Add(subtitle);
        root.Controls.Add(commandBox);
        root.Controls.Add(buttonRow);
        root.Controls.Add(responseBox);
        root.Controls.Add(commands);

        root.SizeChanged += (_, _) =>
        {
            var width = Math.Max(300, root.ClientSize.Width - 70);
            commandBox.Width = width;
            responseBox.Width = width;
        };

        Controls.Add(root);
        AcceptButton = runButton;
    }

    private async Task ExecuteCurrentCommandAsync()
    {
        var command = commandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            SetResponse("Enter a command first.");
            return;
        }

        var result = ExecuteCommand(command);
        SetResponse(result);
        _ = SpeakAsync(result);
        await Task.CompletedTask;
    }

    private async Task ListenAndExecuteAsync()
    {
        listenButton.Enabled = false;
        SetResponse("Listening for up to 8 seconds...");
        try
        {
            var heard = await ListenOnceAsync();
            if (string.IsNullOrWhiteSpace(heard))
            {
                SetResponse("I didn't hear a command. Check the Windows microphone and speech-language settings.");
                return;
            }

            commandBox.Text = heard;
            var result = ExecuteCommand(heard);
            SetResponse(result);
            _ = SpeakAsync(result);
        }
        finally
        {
            listenButton.Enabled = true;
        }
    }

    private string ExecuteCommand(string rawCommand)
    {
        var command = rawCommand.Trim();
        var lower = command.ToLowerInvariant();

        try
        {
            if (lower is "time" or "what time is it" || lower.Contains("current time"))
                return $"It is {DateTime.Now:h:mm tt}.";

            if (lower is "date" or "what is the date" || lower.Contains("today's date"))
                return $"Today is {DateTime.Now:dddd, d MMMM yyyy}.";

            if (lower.Contains("system info") || lower.Contains("computer info"))
                return GetSystemInfo();

            if (lower.Contains("screenshot"))
                return TakeScreenshot();

            if (lower is "volume up" || lower.Contains("increase volume"))
            {
                PressMediaKey(VkVolumeUp);
                return "Volume increased.";
            }

            if (lower is "volume down" || lower.Contains("decrease volume"))
            {
                PressMediaKey(VkVolumeDown);
                return "Volume decreased.";
            }

            if (lower.Contains("mute"))
            {
                PressMediaKey(VkVolumeMute);
                return "Toggled mute.";
            }

            if (lower.Contains("play pause") || lower is "pause" || lower is "play")
            {
                PressMediaKey(VkMediaPlayPause);
                return "Toggled media playback.";
            }

            if (lower.Contains("next track") || lower.Contains("next song"))
            {
                PressMediaKey(VkMediaNext);
                return "Skipping to the next track.";
            }

            if (lower.Contains("previous track") || lower.Contains("previous song"))
            {
                PressMediaKey(VkMediaPrev);
                return "Going to the previous track.";
            }

            if (lower.Contains("lock computer") || lower == "lock pc")
            {
                LockWorkStation();
                return "Locking the computer.";
            }

            if (lower.Contains("restart computer") || lower == "restart pc")
                return ConfirmPowerAction(restart: true);

            if (lower.Contains("shut down computer") || lower.Contains("shutdown computer") || lower == "shutdown pc")
                return ConfirmPowerAction(restart: false);

            if (lower.StartsWith("search "))
            {
                var query = command[7..].Trim();
                if (query.Length == 0) return "Tell me what to search for.";
                OpenTarget($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
                return $"Searching for {query}.";
            }

            if (lower.StartsWith("open "))
                return OpenRequestedTarget(command[5..].Trim());

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
            ["chrome"] = "chrome.exe",
            ["google chrome"] = "chrome.exe",
            ["edge"] = "msedge.exe",
            ["microsoft edge"] = "msedge.exe",
            ["notepad"] = "notepad.exe",
            ["calculator"] = "calc.exe",
            ["settings"] = "ms-settings:",
            ["file explorer"] = "explorer.exe",
            ["explorer"] = "explorer.exe",
            ["youtube"] = "https://www.youtube.com",
            ["github"] = "https://github.com",
            ["gmail"] = "https://mail.google.com",
            ["google"] = "https://www.google.com"
        };

        if (targets.TryGetValue(key, out var target))
        {
            OpenTarget(target);
            return $"Opening {requested}.";
        }

        if (Uri.TryCreate(requested, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            OpenTarget(uri.ToString());
            return $"Opening {requested}.";
        }

        if (requested.Contains('.') && !requested.Contains(' '))
        {
            var url = requested.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? requested
                : $"https://{requested}";
            OpenTarget(url);
            return $"Opening {requested}.";
        }

        return $"I don't have a safe launcher for {requested} yet.";
    }

    private static void OpenTarget(string target)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private string TakeScreenshot()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault();
        if (screen is null) return "I couldn't find a display to capture.";

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Jarvis Screenshots");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Jarvis_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        using var bitmap = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);
        }
        bitmap.Save(path, ImageFormat.Png);
        return $"Screenshot saved to {path}.";
    }

    private static string GetSystemInfo()
    {
        return $"Computer {Environment.MachineName}. Windows {Environment.OSVersion.Version}. " +
               $"{Environment.ProcessorCount} logical processors. " +
               $"64-bit operating system: {Environment.Is64BitOperatingSystem}. " +
               $"User: {Environment.UserName}.";
    }

    private string ConfirmPowerAction(bool restart)
    {
        var action = restart ? "restart" : "shut down";
        var answer = MessageBox.Show(
            $"Are you sure you want Jarvis to {action} this computer now?",
            "Jarvis confirmation",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes) return $"Cancelled {action}.";

        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = restart ? "/r /t 0" : "/s /t 0",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        return restart ? "Restarting the computer." : "Shutting down the computer.";
    }

    private static void PressMediaKey(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private void SetResponse(string text)
    {
        responseBox.Text = text;
    }

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
if ($result) {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    Write-Output $result.Text
}
$recognizer.Dispose()
""";

        var path = Path.Combine(Path.GetTempPath(), $"jarvis-listen-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(path, script, Encoding.UTF8);
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -File \"{path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(start);
            if (process is null) return null;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = (await outputTask).Trim();
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
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
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -File \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is not null) await process.WaitForExitAsync();
        }
        catch
        {
            // TTS is optional; command execution remains usable if Windows speech is unavailable.
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
