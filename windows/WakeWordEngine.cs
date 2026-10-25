using System.Text.Json;
using NAudio.Wave;
using Vosk;

namespace Jarvis.Windows;

internal sealed class WakeWordEngine : IDisposable
{
    private readonly object gate = new();
    private Model? model;
    private VoskRecognizer? recognizer;
    private WaveInEvent? microphone;
    private string phase = "off";
    private bool recording;
    private bool resumeWanted;
    private DateTime lastTrigger = DateTime.MinValue;

    public float Sensitivity { get; set; } = 0.55f;
    public bool IsRunning { get { lock (gate) return phase != "off"; } }
    public event Action<string>? StatusChanged;
    public event Action<string>? CommandHeard;

    public async Task StartAsync()
    {
        if (!WakeModelStore.Installed) throw new InvalidOperationException("Install the offline model first.");
        lock (gate) if (phase != "off") return;
        StatusChanged?.Invoke("Loading offline model…");
        model = await Task.Run(() => new Model(WakeModelStore.ModelPath));
        lock (gate)
        {
            recognizer = NewWakeRecognizer();
            microphone = new WaveInEvent { DeviceNumber = 0, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 200 };
            microphone.DataAvailable += OnAudio;
            microphone.RecordingStopped += OnRecordingStopped;
            phase = "wake";
            recording = true;
        }
        try
        {
            microphone.StartRecording();
            StatusChanged?.Invoke("Listening locally for Hey Jarvis");
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private VoskRecognizer NewWakeRecognizer()
    {
        var wake = new VoskRecognizer(model!, 16000f, "[\"hey jarvis\", \"[unk]\"]");
        wake.SetWords(true);
        return wake;
    }

    private void OnAudio(object? sender, WaveInEventArgs args)
    {
        string? command = null;
        string? status = null;
        lock (gate)
        {
            if (phase is not ("wake" or "command") || recognizer is null) return;
            try
            {
                if (!recognizer.AcceptWaveform(args.Buffer, args.BytesRecorded)) return;
                using var document = JsonDocument.Parse(recognizer.Result());
                var root = document.RootElement;
                var text = root.TryGetProperty("text", out var result) ? result.GetString()?.Trim() ?? "" : "";
                if (phase == "wake")
                {
                    var confidence = 0d;
                    if (root.TryGetProperty("result", out var words) && words.ValueKind == JsonValueKind.Array && words.GetArrayLength() > 0)
                    {
                        foreach (var word in words.EnumerateArray())
                            confidence += word.TryGetProperty("conf", out var value) ? value.GetDouble() : 0;
                        confidence /= words.GetArrayLength();
                    }
                    if (text.Equals("hey jarvis", StringComparison.OrdinalIgnoreCase) &&
                        confidence >= Sensitivity && DateTime.UtcNow - lastTrigger > TimeSpan.FromSeconds(4))
                    {
                        lastTrigger = DateTime.UtcNow;
                        recognizer.Dispose();
                        recognizer = new VoskRecognizer(model!, 16000f);
                        phase = "command";
                        status = "Hey Jarvis heard — listening for your request";
                    }
                }
                else if (text.Length > 0)
                {
                    command = text;
                    phase = "answering";
                    recording = false;
                    microphone?.StopRecording();
                    status = "Processing your request";
                }
            }
            catch (Exception error)
            {
                status = $"Offline voice error: {error.Message}";
            }
        }
        if (status is not null) StatusChanged?.Invoke(status);
        if (command is not null) CommandHeard?.Invoke(command);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        bool restart;
        lock (gate)
        {
            recording = false;
            restart = resumeWanted && phase == "wake";
            resumeWanted = false;
        }
        if (args.Exception is not null) StatusChanged?.Invoke($"Microphone stopped: {args.Exception.Message}");
        if (restart) StartRecording();
    }

    public void Resume()
    {
        bool startNow;
        lock (gate)
        {
            if (phase == "off" || model is null) return;
            recognizer?.Dispose();
            recognizer = NewWakeRecognizer();
            phase = "wake";
            startNow = !recording;
            resumeWanted = !startNow;
        }
        if (startNow) StartRecording();
    }

    private void StartRecording()
    {
        try
        {
            lock (gate) recording = true;
            microphone?.StartRecording();
            StatusChanged?.Invoke("Listening locally for Hey Jarvis");
        }
        catch (Exception error)
        {
            StatusChanged?.Invoke($"Microphone unavailable: {error.Message}");
            Stop();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            phase = "off";
            resumeWanted = false;
            try { microphone?.StopRecording(); } catch { }
            microphone?.Dispose();
            microphone = null;
            recognizer?.Dispose();
            recognizer = null;
            model?.Dispose();
            model = null;
            recording = false;
        }
        StatusChanged?.Invoke("Hey Jarvis is off");
    }

    public void Dispose() => Stop();
}
