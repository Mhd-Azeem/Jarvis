using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jarvis.Windows;

internal sealed record AssistantTurn(string Role, string Text);
internal sealed record JarvisTask(Guid Id, string Title, bool Done, DateTime? DueAt, bool Notified);

internal sealed class AssistantSettings
{
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = "";
    public bool SpeakReplies { get; set; } = true;
    public bool WakeEnabled { get; set; } = false;
    public float WakeSensitivity { get; set; } = 0.55f;
}

internal static class AssistantData
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
    private static string SettingsFile => Path.Combine(Folder, "settings.dat");
    private static string HistoryFile => Path.Combine(Folder, "history.dat");
    private static string TaskFile => Path.Combine(Folder, "tasks.dat");

    internal static AssistantSettings LoadSettings() => Read<AssistantSettings>(SettingsFile) ?? new();
    internal static List<AssistantTurn> LoadHistory() => Read<List<AssistantTurn>>(HistoryFile) ?? new();
    internal static List<JarvisTask> LoadTasks() => Read<List<JarvisTask>>(TaskFile) ?? new();

    internal static void SaveSettings(AssistantSettings value) => Write(SettingsFile, value);
    internal static void SaveHistory(List<AssistantTurn> value) => Write(HistoryFile, value.TakeLast(100).ToList());
    internal static void SaveTasks(List<JarvisTask> value) => Write(TaskFile, value);
    internal static void ClearHistory() { if (File.Exists(HistoryFile)) File.Delete(HistoryFile); }

    private static T? Read<T>(string path)
    {
        try
        {
            if (!File.Exists(path)) return default;
            var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(plaintext);
        }
        catch { return default; }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Folder);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value);
        var ciphertext = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, ciphertext);
        File.Move(temporary, path, true);
    }
}

internal static class AssistantCloud
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };

    internal static async Task<string> ReplyAsync(
        AssistantSettings settings,
        IReadOnlyList<AssistantTurn> turns,
        Action<string> onProgress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) return "Configure an API key in Settings to use cloud chat. Local commands work without one.";
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            return "The provider endpoint must be an HTTPS URL.";

        var messages = new List<object>
        {
            new { role = "system", content = "You are Jarvis, a concise personal assistant. Never claim to have performed a computer action. Local commands are executed separately." }
        };
        messages.AddRange(turns.TakeLast(16).Select(turn => (object)new { role = turn.Role, content = turn.Text }));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Content = new StringContent(JsonSerializer.Serialize(new { model = settings.Model, stream = true, messages }), Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            return $"Provider error {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 300)]}";
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var answer = new StringBuilder();
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var payload = line[6..].Trim();
            if (payload == "[DONE]") break;
            try
            {
                using var document = JsonDocument.Parse(payload);
                var delta = document.RootElement.GetProperty("choices")[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    answer.Append(content.GetString());
                    onProgress(answer.ToString());
                }
            }
            catch (JsonException) { }
        }
        return answer.Length == 0 ? "The provider returned an empty response." : answer.ToString();
    }
}
