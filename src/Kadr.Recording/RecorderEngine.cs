using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Vortice.Direct3D11;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace Kadr.Recording;

public enum RecorderState { Idle, Recording, Paused, Stopping, Finished, Canceled, Failed }

/// <summary>
/// Запись области экрана в MP4 (H.264 + AAC) через MediaStreamSource + MediaTranscoder.
/// Кадры приходят из Windows.Graphics.Capture, звук из WASAPI. Пауза не оставляет разрыва во времени файла.
/// </summary>
public sealed class RecorderEngine : IDisposable
{
    private readonly RecordingOptions _options;
    private readonly ILogger _logger;
    private readonly Stopwatch _clock = new();
    private readonly object _pauseSync = new();
    private readonly ManualResetEventSlim _resumeEvent = new(true);
    private readonly SemaphoreSlim _frameSignal = new(0);

    private ScreenSource? _screen;
    private AudioSource? _audio;
    private MouseHighlighter? _highlighter;
    private TexturePool? _pool;
    private VideoStreamDescriptor? _videoDescriptor;
    private AudioStreamDescriptor? _audioDescriptor;
    private TimeSpan _pausedTotal;
    private Stopwatch? _pauseClock;
    private long _videoFrameIndex;
    private long _audioSamplesSent;
    private volatile bool _stopRequested;
    private volatile bool _cancelRequested;
    private TimeSpan _lastVideoTimestamp;

    public RecorderState State { get; private set; } = RecorderState.Idle;
    public TimeSpan Elapsed => _clock.IsRunning || State == RecorderState.Paused ? _clock.Elapsed - _pausedTotal - (_pauseClock?.Elapsed ?? TimeSpan.Zero) : _lastVideoTimestamp;
    public TimeSpan MaxDuration => TimeSpan.FromMinutes(_options.MaxDurationMinutes);
    public bool IsPaused => State == RecorderState.Paused;

    public event Action<TimeSpan, TimeSpan>? Progress;

    public RecorderEngine(RecordingOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>Запускает запись и возвращает задачу, завершающуюся при остановке/отмене.</summary>
    public async Task RunAsync()
    {
        if (!ScreenSource.IsSupported)
            throw new RecorderException(RecorderErrorCode.CaptureUnsupported, "Windows.Graphics.Capture не поддерживается этой системой");

        var o = _options;
        _logger.LogInformation("Запись: монитор {Monitor}, область {X},{Y} {W}x{H}, {Fps} fps, макс. высота {MaxH}, файл {File}",
            o.MonitorName, o.X, o.Y, o.Width, o.Height, o.FrameRate, o.MaxOutputHeight, o.OutputFile);

        _screen = new ScreenSource((IntPtr)o.MonitorHandle, o.X, o.Y, o.Width, o.Height, o.ShowCursor, _logger)
        {
            MinFrameInterval = TimeSpan.FromSeconds(0.5 / Math.Max(1, o.FrameRate)),
        };
        _screen.FrameArrived += () => { if (_frameSignal.CurrentCount == 0) _frameSignal.Release(); };
        // Пул на ~1 с кадров: кодер не может удержать больше, память ограничена.
        _pool = new TexturePool(Math.Clamp(o.FrameRate, 8, 60), () => _screen.CreateTexture(o.Width, o.Height, Vortice.Direct3D11.BindFlags.ShaderResource | Vortice.Direct3D11.BindFlags.RenderTarget), _logger);
        if (o.HighlightPointer || o.HighlightClicks)
        {
            _highlighter = new MouseHighlighter(o.HighlightPointer, o.HighlightClicks, o.MonitorLeft + o.X, o.MonitorTop + o.Y, o.Width, o.Height, o.Scale, _logger);
            _screen.Highlighter = _highlighter;
        }
        _audio = new AudioSource(o.MicrophoneDeviceId, o.SystemAudioDeviceId, _logger);

        var videoProps = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)o.Width, (uint)o.Height);
        videoProps.FrameRate.Numerator = (uint)o.FrameRate;
        videoProps.FrameRate.Denominator = 1;
        _videoDescriptor = new VideoStreamDescriptor(videoProps);

        var audioDebug = Environment.GetEnvironmentVariable("KADR_AUDIO_DEBUG"); // off | silence
        if (audioDebug is not null) _logger.LogWarning("Отладочный режим аудио: {Mode}", audioDebug);
        MediaStreamSource source;
        if (_audio.HasSources && audioDebug != "off")
        {
            _audioDescriptor = new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm(AudioSource.SampleRate, AudioSource.Channels, 16));
            source = new MediaStreamSource(_videoDescriptor, _audioDescriptor);
        }
        else
        {
            source = new MediaStreamSource(_videoDescriptor);
        }
        source.BufferTime = TimeSpan.Zero;
        source.CanSeek = false;
        source.Starting += (_, e) => { _logger.LogInformation("MediaStreamSource: старт"); e.Request.SetActualStartPosition(TimeSpan.Zero); };
        source.Closed += (_, e) => _logger.LogInformation("MediaStreamSource: закрыт ({Reason})", e.Request.Reason);
        source.SampleRequested += OnSampleRequested;

        var (outW, outH) = o.OutputSize();
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)outW;
        profile.Video.Height = (uint)outH;
        profile.Video.FrameRate.Numerator = (uint)o.FrameRate;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.Bitrate = (uint)EstimateBitrate(outW, outH, o.FrameRate);
        if (_audioDescriptor is null) profile.Audio = null;
        else { profile.Audio.SampleRate = AudioSource.SampleRate; profile.Audio.ChannelCount = AudioSource.Channels; profile.Audio.Bitrate = 128000; }

        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        Directory.CreateDirectory(Path.GetDirectoryName(o.OutputFile)!);
        using var file = new FileStream(o.OutputFile, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        using var stream = file.AsRandomAccessStream();

        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
        if (!prepared.CanTranscode)
            throw new RecorderException(RecorderErrorCode.EncoderFailed, $"Кодер недоступен: {prepared.FailureReason}");

        _screen.Start();
        _audio.Start();
        _clock.Start();
        State = RecorderState.Recording;
        var monitor = new Thread(() =>
        {
            while (State is RecorderState.Recording or RecorderState.Paused or RecorderState.Stopping)
            {
                Thread.Sleep(1500);
                using var proc = Process.GetCurrentProcess();
                _logger.LogInformation("Состояние: {State}, кадров захвачено {Frames}, видеосэмплов {Video}, аудиосэмплов {Audio}, в обработке: {Req}, текстур у кодера {Live}/{Pool}, память {Private} МБ",
                    State, _screen.FrameCounter, _videoFrameIndex, _audioSamplesSent, _inRequest ?? "нет", _liveTextures, _pool?.Count, proc.PrivateMemorySize64 / 1024 / 1024);
            }
        }) { IsBackground = true, Name = "RecorderMonitor" };
        monitor.Start();
        _logger.LogInformation("Кодирование {W}x{H} @ {Fps}, битрейт {Bitrate} кбит/с, звук: {Audio}", outW, outH, o.FrameRate, profile.Video.Bitrate / 1000, _audioDescriptor is not null);

        try
        {
            await prepared.TranscodeAsync();
            State = _cancelRequested ? RecorderState.Canceled : RecorderState.Finished;
        }
        catch (Exception ex)
        {
            State = RecorderState.Failed;
            throw new RecorderException(RecorderErrorCode.EncoderFailed, "Ошибка кодирования: " + ex.Message, ex);
        }
        finally
        {
            _clock.Stop();
            _screen.Dispose();
            _audio.Dispose();
            _highlighter?.Dispose();
            _pool?.Dispose();
        }
        stream.Dispose();
        file.Dispose();
        if (_cancelRequested)
        {
            try { File.Delete(o.OutputFile); } catch (Exception ex) { _logger.LogWarning(ex, "Не удалось удалить файл при отмене"); }
        }
        _logger.LogInformation("Запись завершена: {State}, длительность {Duration}", State, _lastVideoTimestamp);
    }

    private static int EstimateBitrate(int w, int h, int fps)
    {
        // ~0.1 бит на пиксель в секунду, в пределах 1.5..40 Мбит/с
        var bits = (long)w * h * fps * 0.1;
        return (int)Math.Clamp(bits, 1_500_000, 40_000_000);
    }

    private int _requestLogCount;
    private volatile string? _inRequest;
    private int _liveTextures;

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        bool isVideo = args.Request.StreamDescriptor is VideoStreamDescriptor;
        bool verbose = _requestLogCount < 8 || (_videoFrameIndex % 100 == 0 && isVideo);
        if (verbose) { _requestLogCount++; _logger.LogInformation("Запрос сэмпла: {Kind}, кадров захвачено {Frames}, видео {V}, аудио {A}, t={T}", isVideo ? "видео" : "аудио", _screen?.FrameCounter, _videoFrameIndex, _audioSamplesSent, Timeline); }
        try
        {
            var sw = Stopwatch.StartNew();
            _inRequest = (isVideo ? "видео" : "аудио") + " с " + Timeline;
            if (isVideo) args.Request.Sample = NextVideoSample();
            else args.Request.Sample = NextAudioSample();
            _inRequest = null;
            if (verbose) _logger.LogInformation("Сэмпл готов: {Kind} за {Ms} мс", isVideo ? "видео" : "аудио", sw.ElapsedMilliseconds);
            if (args.Request.Sample is null) _logger.LogInformation("Конец потока: {Kind}", isVideo ? "видео" : "аудио");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка подготовки сэмпла");
            args.Request.Sample = null;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private TimeSpan Timeline => _clock.Elapsed - _pausedTotal;

    private MediaStreamSample? NextVideoSample()
    {
        var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / _options.FrameRate);
        while (true)
        {
            if (_stopRequested) return null;
            WaitIfPaused();
            if (_stopRequested) return null;

            var target = TimeSpan.FromTicks(frameDuration.Ticks * _videoFrameIndex);
            if (target >= MaxDuration) { _logger.LogInformation("Достигнут лимит длительности"); _stopRequested = true; return null; }

            var wait = target - Timeline;
            if (wait > TimeSpan.Zero) Thread.Sleep(wait);

            var rented = _screen!.TakeFrameForEncoder(_pool!);
            if (rented is null)
            {
                // кадров ещё не было: ждём первый
                _frameSignal.Wait(200);
                continue;
            }
            var (texture, token) = rented.Value;
            var surface = Direct3DInterop.CreateWinRTSurface(texture);
            var sample = MediaStreamSample.CreateFromDirect3D11Surface(surface, target);
            sample.Duration = frameDuration;
            Interlocked.Increment(ref _liveTextures);
            sample.Processed += (_, _) => { _pool?.Return(token); Interlocked.Decrement(ref _liveTextures); };
            _videoFrameIndex++;
            _lastVideoTimestamp = target;
            if (_videoFrameIndex % _options.FrameRate == 0) Progress?.Invoke(target, MaxDuration);
            return sample;
        }
    }

    private MediaStreamSample? NextAudioSample()
    {
        const int chunkSamples = AudioSource.SampleRate / 50; // 20 мс
        if (_stopRequested) return null;
        WaitIfPaused();
        if (_stopRequested) return null;

        var timestamp = TimeSpan.FromTicks(_audioSamplesSent * TimeSpan.TicksPerSecond / AudioSource.SampleRate);
        var chunkDuration = TimeSpan.FromTicks(chunkSamples * TimeSpan.TicksPerSecond / AudioSource.SampleRate);
        // Ждём, пока реальное время не обгонит этот кусок, чтобы данные захвата успели прийти.
        var wait = timestamp + chunkDuration + TimeSpan.FromMilliseconds(60) - Timeline;
        if (wait > TimeSpan.Zero) Thread.Sleep(wait);
        if (_stopRequested) return null;

        var bytes = Environment.GetEnvironmentVariable("KADR_AUDIO_DEBUG") == "silence"
            ? new byte[chunkSamples * AudioSource.Channels * AudioSource.BytesPerSample]
            : _audio!.ReadPcm16(chunkSamples);
        var sample = MediaStreamSample.CreateFromBuffer(bytes.AsBuffer(), timestamp);
        sample.Duration = chunkDuration;
        _audioSamplesSent += chunkSamples;
        return sample;
    }

    private void WaitIfPaused()
    {
        while (!_resumeEvent.IsSet && !_stopRequested) _resumeEvent.Wait(100);
    }

    public bool TogglePause()
    {
        lock (_pauseSync)
        {
            if (State == RecorderState.Recording) { Pause(); return true; }
            if (State == RecorderState.Paused) { Resume(); return false; }
            return false;
        }
    }

    public void Pause()
    {
        lock (_pauseSync)
        {
            if (State != RecorderState.Recording) return;
            State = RecorderState.Paused;
            _pauseClock = Stopwatch.StartNew();
            _resumeEvent.Reset();
            _audio?.Pause();
            _highlighter?.SetPaused(true);
            _logger.LogInformation("Пауза");
        }
    }

    public void Resume()
    {
        lock (_pauseSync)
        {
            if (State != RecorderState.Paused) return;
            _pausedTotal += _pauseClock?.Elapsed ?? TimeSpan.Zero;
            _pauseClock = null;
            State = RecorderState.Recording;
            _audio?.Resume();
            _highlighter?.SetPaused(false);
            _resumeEvent.Set();
            _logger.LogInformation("Продолжение записи");
        }
    }

    public void Stop()
    {
        _logger.LogInformation("Запрошена остановка");
        _stopRequested = true;
        State = RecorderState.Stopping;
        _resumeEvent.Set();
    }

    public void Cancel()
    {
        _cancelRequested = true;
        Stop();
    }

    public void Dispose()
    {
        _stopRequested = true;
        _resumeEvent.Set();
        _screen?.Dispose();
        _audio?.Dispose();
        _frameSignal.Dispose();
        _resumeEvent.Dispose();
    }
}
