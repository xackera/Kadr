using System.IO;
using System.Media;

namespace Kadr.App.Services;

/// <summary>
/// Звуковые сигналы Kadr. Синтезируются кодом при первом обращении, внешних файлов нет.
/// Затвор — двойной механический щелчок, как у зеркальной камеры; старт и стоп записи — короткие тоны.
/// </summary>
public static class SoundService
{
    private const int Rate = 44100;

    private static readonly Lazy<byte[]> ShutterWav = new(() => LoadResource("Assets/shutter.wav") ?? BuildShutter());
    private static readonly Lazy<byte[]> RecordStartWav = new(BuildRecordStart);
    private static readonly Lazy<byte[]> RecordStopWav = new(BuildRecordStop);

    /// <param name="customPath">Свой .wav; если пусто или файла нет, играет встроенный звук.</param>
    public static void PlayShutter(string? customPath = null) => Play(customPath, ShutterWav.Value);
    public static void PlayRecordStart(string? customPath = null) => Play(customPath, RecordStartWav.Value);
    public static void PlayRecordStop(string? customPath = null) => Play(customPath, RecordStopWav.Value);

    private static void Play(string? customPath, byte[] builtin)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
            {
                using var custom = new SoundPlayer(customPath);
                custom.Play();
                return;
            }
            using var ms = new MemoryStream(builtin);
            using var player = new SoundPlayer(ms);
            player.Play();
        }
        catch { /* звук не критичен */ }
    }

    /// <summary>Звук из ресурсов приложения; null, если ресурса нет (тогда играет синтезированный).</summary>
    private static byte[]? LoadResource(string relativePath)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info is null) return null;
            using var stream = info.Stream;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ синтез

    /// <summary>Двойной щелчок: подъём зеркала и закрытие шторки через 72 мс.</summary>
    public static byte[] BuildShutter()
    {
        var buf = new float[(int)(Rate * 0.20)];
        AddClick(buf, 0.000, 1.00);
        AddClick(buf, 0.072, 0.72);
        return Encode(buf, 0.55f);
    }

    public static byte[] BuildRecordStart()
    {
        var buf = new float[(int)(Rate * 0.26)];
        AddTone(buf, 0.00, 0.075, 784, 0.9);   // соль
        AddTone(buf, 0.09, 0.095, 1175, 1.0);  // ре октавой выше
        return Encode(buf, 0.42f);
    }

    public static byte[] BuildRecordStop()
    {
        var buf = new float[(int)(Rate * 0.28)];
        AddTone(buf, 0.00, 0.080, 1175, 1.0);
        AddTone(buf, 0.095, 0.115, 784, 0.9);
        return Encode(buf, 0.42f);
    }

    /// <summary>Механический щелчок: короткий шумовой всплеск плюс затухающие резонансы корпуса.</summary>
    private static void AddClick(float[] buf, double atSec, double gain)
    {
        int start = (int)(atSec * Rate);
        var rnd = new Random(1337 + start);
        // резонансы: частота, время затухания, вес
        (double f, double decay, double a)[] modes =
        {
            (1_950, 0.016, 0.55),
            (3_100, 0.011, 0.40),
            (5_400, 0.006, 0.25),
            (760, 0.022, 0.30),
        };

        int len = (int)(0.055 * Rate);
        for (int i = 0; i < len && start + i < buf.Length; i++)
        {
            double t = (double)i / Rate;
            double v = 0;
            foreach (var (f, decay, a) in modes)
                v += a * Math.Sin(2 * Math.PI * f * t) * Math.Exp(-t / decay);
            // сухой удар в самом начале
            v += (rnd.NextDouble() * 2 - 1) * 0.9 * Math.Exp(-t / 0.0022);
            buf[start + i] += (float)(v * gain);
        }
    }

    /// <summary>Мягкий тон с плавными атакой и затуханием, без щелчков на краях.</summary>
    private static void AddTone(float[] buf, double atSec, double durSec, double freq, double gain)
    {
        int start = (int)(atSec * Rate);
        int len = (int)(durSec * Rate);
        for (int i = 0; i < len && start + i < buf.Length; i++)
        {
            double p = (double)i / len;
            double env = 0.5 * (1 - Math.Cos(2 * Math.PI * Math.Min(p, 1) * 0.5)); // мягкая атака
            env *= Math.Exp(-p * 2.2);                                             // и спад
            double t = (double)i / Rate;
            double v = Math.Sin(2 * Math.PI * freq * t)
                     + 0.18 * Math.Sin(2 * Math.PI * freq * 2 * t);                // лёгкая вторая гармоника
            buf[start + i] += (float)(v * env * gain);
        }
    }

    /// <summary>Нормализация к заданному пику и упаковка в WAV, 16 бит, моно.</summary>
    private static byte[] Encode(float[] buf, float peak)
    {
        float max = 0;
        foreach (var v in buf) max = Math.Max(max, Math.Abs(v));
        float k = max > 1e-6f ? peak / max : 0;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataSize = buf.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataSize); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataSize);
        foreach (var v in buf) w.Write((short)(Math.Clamp(v * k, -1f, 1f) * short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }
}
