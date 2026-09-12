using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Jarvis.Windows;

internal static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Mhd-Azeem/Jarvis/releases/latest";
    private const string WindowsAssetName = "Jarvis-Windows.exe";

    internal static async Task CheckAndOfferUpdateAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Jarvis-Windows-Updater");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await client.GetStringAsync(LatestReleaseApi);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var latestBuild = ParseBuildNumber(tag);
            if (latestBuild <= BuildInfo.ReleaseBuild) return;

            string? downloadUrl = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (string.Equals(asset.GetProperty("name").GetString(), WindowsAssetName, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl)) return;

            var answer = MessageBox.Show(
                $"Jarvis Build #{latestBuild} is available.\n\nUpdate now? Jarvis will download the new version, restart itself, and continue automatically.",
                "Jarvis update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);

            if (answer != DialogResult.Yes) return;

            await DownloadAndReplaceAsync(client, downloadUrl, latestBuild);
        }
        catch
        {
            // Update checks must never stop Jarvis from launching.
        }
    }

    private static async Task DownloadAndReplaceAsync(HttpClient client, string downloadUrl, int latestBuild)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
        {
            MessageBox.Show("Jarvis couldn't locate its current executable, so the automatic update could not continue.", "Jarvis updater");
            return;
        }

        var currentDirectory = Path.GetDirectoryName(currentExe)!;
        if (!CanWriteToDirectory(currentDirectory))
        {
            MessageBox.Show(
                "Jarvis cannot update itself in this folder because Windows does not allow write access. Move Jarvis-Windows.exe to a folder you own, such as Desktop or Documents, and run it again.",
                "Jarvis updater",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var tempExe = Path.Combine(Path.GetTempPath(), $"Jarvis-Windows-build-{latestBuild}-{Guid.NewGuid():N}.exe");
        await using (var source = await client.GetStreamAsync(downloadUrl))
        await using (var destination = File.Create(tempExe))
        {
            await source.CopyToAsync(destination);
        }

        if (new FileInfo(tempExe).Length < 5_000_000L)
        {
            try { File.Delete(tempExe); } catch { }
            MessageBox.Show("The downloaded update did not look valid. Jarvis kept the current version.", "Jarvis updater");
            return;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"jarvis-update-{Guid.NewGuid():N}.ps1");
        var target64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(currentExe));
        var source64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(tempExe));
        var script64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(scriptPath));
        var pid = Environment.ProcessId;

        var script = $$"""
$ErrorActionPreference = 'Stop'
$target = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{{target64}}'))
$source = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{{source64}}'))
$script = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('{{script64}}'))
Wait-Process -Id {{pid}} -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700
Copy-Item -LiteralPath $source -Destination $target -Force
Start-Process -FilePath $target
Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Remove-Item -LiteralPath $script -Force -ErrorAction SilentlyContinue
""";

        await File.WriteAllTextAsync(scriptPath, script, Encoding.UTF8);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Application.Exit();
    }

    private static int ParseBuildNumber(string tag)
    {
        const string prefix = "build-";
        if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 0;
        return int.TryParse(tag[prefix.Length..], out var build) ? build : 0;
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".jarvis-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
