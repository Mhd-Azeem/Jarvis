using System.IO.Compression;
using System.Net;

namespace Jarvis.Windows;

internal static class WakeModelStore
{
    private const string ModelName = "vosk-model-small-en-us-0.15";
    private const string ModelUrl = "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";
    private static readonly string BasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
    internal static string ModelPath => Path.Combine(BasePath, ModelName);
    internal static bool Installed => File.Exists(Path.Combine(ModelPath, "am", "final.mdl"));

    internal static async Task InstallAsync(Action<string> progress, CancellationToken cancellationToken = default)
    {
        if (Installed) return;
        Directory.CreateDirectory(BasePath);
        var archivePath = Path.Combine(BasePath, "wake-model.download");
        var staging = Path.Combine(BasePath, "wake-model-staging");
        try
        {
            progress("Downloading the 40 MB English offline model…");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(archivePath))
                await input.CopyToAsync(output, cancellationToken);
            var size = new FileInfo(archivePath).Length;
            if (size is < 20_000_000 or > 100_000_000) throw new InvalidDataException("Model archive has an unexpected size.");

            progress("Installing offline speech model…");
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            using var zip = ZipFile.OpenRead(archivePath);
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                if (!target.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe model archive entry.");
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                expanded += entry.Length;
                if (expanded > 500_000_000) throw new InvalidDataException("Model archive is too large.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, true);
            }
            var extracted = Path.Combine(staging, ModelName);
            if (!File.Exists(Path.Combine(extracted, "am", "final.mdl")))
                throw new InvalidDataException("Downloaded model is incomplete.");
            if (Directory.Exists(ModelPath)) Directory.Delete(ModelPath, true);
            Directory.Move(extracted, ModelPath);
            progress("Offline wake model ready.");
        }
        finally
        {
            try { File.Delete(archivePath); } catch { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }
}
