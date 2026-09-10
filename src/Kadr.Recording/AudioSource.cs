using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Microsoft.Extensions.Logging;

namespace Kadr.Recording;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsInput);

/// <summary>Перечисление аудиоустройств и разрешение специальных идентификаторов.</summary>
public static class AudioDevices
{
    public const string NoSound = "NoSound";
    public const string DefaultConsole = "DefaultConsole";
    public const string DefaultCommunications = "DefaultCommunications";

    public static IReadOnlyList<AudioDeviceInfo> List(bool input)
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            using var e = new MMDeviceEnumerator();
            foreach (var d in e.EnumerateAudioEndPoints(input ? DataFlow.Capture : DataFlow.Render, DeviceState.Active))
                list.Add(new AudioDeviceInfo(d.ID, d.FriendlyName, input));
        }
        catch { }
        return list;
    }

    public static MMDevice? Resolve(string? id, bool input)
    {
        if (string.IsNullOrEmpty(id) || id == NoSound) return null;
        var e = new MMDeviceEnumerator();
        var flow = input ? DataFlow.Capture : DataFlow.Render;
        try
        {
            if (id == DefaultConsole) return e.GetDefaultAudioEndpoint(flow, Role.Console);
            if (id == DefaultCommunications) return e.GetDefaultAudioEndpoint(flow, Role.Communications);
            try
            {
                var d = e.GetDevice(id);
                if (d.State == DeviceState.Active) return d;
            }
            catch { }
            return e.GetDefaultAudioEndpoint(flow, Role.Console);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Микрофон + системный звук (loopback) → микс PCM 16 бит стерео. Пустые входы дают тишину,
/// поэтому дорожка непрерывна даже без воспроизведения.
/// </summary>
public sealed class AudioSource : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BytesPerSample = 2;

    private readonly ILogger _logger;
    private readonly List<IWaveIn> _captures = new();
    private readonly List<BufferedWaveProvider> _buffers = new();
    private readonly MixingSampleProvider _mixer;
    private readonly float[] _floatBuffer = new float[SampleRate * Channels]; // до 1 с

    public bool HasSources => _captures.Count > 0;

    public AudioSource(string? micId, string? sysId, ILogger logger)
    {
        _logger = logger;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
        _mixer = new MixingSampleProvider(format) { ReadFully = true };

        var sys = AudioDevices.Resolve(sysId, input: false);
        if (sys is not null)
        {
            try { Add(new WasapiLoopbackCapture(sys), "системный звук"); }
            catch (Exception ex) { _logger.LogWarning(ex, "Не удалось открыть системный звук"); }
        }

        var mic = AudioDevices.Resolve(micId, input: true);
        if (mic is not null)
        {
            var micMode = Environment.GetEnvironmentVariable("KADR_MIC_MODE"); // poll | nostart
            _logger.LogInformation("Микрофон: {Name} (режим {Mode})", mic.FriendlyName, micMode ?? "event");
            try { Add(micMode == "poll" ? new WasapiCapture(mic) : new WasapiCapture(mic, true, 20), "микрофон"); }
            catch (UnauthorizedAccessException ex) { throw new RecorderException(RecorderErrorCode.MicrophoneAccessDenied, "Доступ к микрофону запрещён в настройках Windows", ex); }
            catch (System.Runtime.InteropServices.COMException ex) when ((uint)ex.HResult == 0x8000FFFF || (uint)ex.HResult == 0x80070005)
            { throw new RecorderException(RecorderErrorCode.MicrophoneAccessDenied, "Доступ к микрофону запрещён в настройках Windows", ex); }
            catch (Exception ex) { _logger.LogWarning(ex, "Не удалось открыть микрофон"); }
        }
    }

    private void Add(IWaveIn capture, string name)
    {
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(4),
            ReadFully = true, // пустой буфер даёт тишину, а не «конец потока» для микшера
        };
        capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        capture.RecordingStopped += (_, e) => { if (e.Exception is not null) _logger.LogWarning(e.Exception, "Захват {Name} остановлен с ошибкой", name); };

        ISampleProvider samples = buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
        else if (samples.WaveFormat.Channels > 2) samples = new MultiplexingSampleProvider(new[] { samples }, 2);
        if (samples.WaveFormat.SampleRate != SampleRate) samples = new WdlResamplingSampleProvider(samples, SampleRate);
        _mixer.AddMixerInput(samples);

        _captures.Add(capture);
        _buffers.Add(buffer);
        _logger.LogInformation("Аудио: {Name}, формат {Format}", name, capture.WaveFormat);
    }

    public void Start()
    {
        if (Environment.GetEnvironmentVariable("KADR_MIC_MODE") == "nostart") { _logger.LogWarning("Отладка: захват аудио не запущен"); return; }
        foreach (var c in _captures) c.StartRecording();
    }

    public void Pause()
    {
        foreach (var c in _captures) { try { c.StopRecording(); } catch { } }
        foreach (var b in _buffers) b.ClearBuffer();
    }

    public void Resume()
    {
        foreach (var c in _captures) { try { c.StartRecording(); } catch (Exception ex) { _logger.LogWarning(ex, "Не удалось возобновить аудио"); } }
    }

    /// <summary>Следующие sampleCount сэмплов (на канал) в виде PCM16 stereo; нехватка данных заполняется тишиной.</summary>
    public byte[] ReadPcm16(int sampleCount)
    {
        int floats = sampleCount * Channels;
        int read = _mixer.Read(_floatBuffer, 0, Math.Min(floats, _floatBuffer.Length));
        var result = new byte[floats * BytesPerSample];
        for (int i = 0; i < read; i++)
        {
            var v = Math.Clamp(_floatBuffer[i] * 1.5f, -1f, 1f); // +3.5 дБ, как усиление в оригинале
            var s = (short)(v * short.MaxValue);
            result[i * 2] = (byte)s;
            result[i * 2 + 1] = (byte)(s >> 8);
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var c in _captures)
        {
            try { c.StopRecording(); } catch { }
            c.Dispose();
        }
        _captures.Clear();
    }
}
