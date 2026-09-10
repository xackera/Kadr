using System.Drawing;
using System.Drawing.Imaging;

namespace Kadr.App.Scrolling;

/// <summary>Растущее изображение BGRA фиксированной ширины, до 32 768 строк. Нижний край перезаписывается свежими строками.</summary>
public sealed class ScrollingImage
{
    public const int MaxHeight = 32768;

    private byte[] _buffer;
    private readonly object _sync = new();

    public int Width { get; }
    public int Stride { get; }
    public int Height { get; private set; }

    /// <summary>Строка, с которой изменилось изображение (для перерисовки превью).</summary>
    public event Action<int>? Updated;

    public ScrollingImage(int width)
    {
        Width = width;
        Stride = width * 4;
        _buffer = new byte[Stride * 512];
    }

    /// <summary>Срезать overwrite строк снизу и дописать lineCount строк кадра начиная с top. Возвращает число добавленных строк.</summary>
    public int AddFragment(ReadOnlySpan<byte> frame, int frameStride, int top, int lineCount, int overwrite)
    {
        lock (_sync)
        {
            Height -= Math.Min(overwrite, Height);
            int from = Height;
            int canAdd = Math.Min(lineCount, MaxHeight - Height);
            if (canAdd <= 0) return 0;
            EnsureCapacity(Height + canAdd);
            for (int i = 0; i < canAdd; i++)
                frame.Slice((top + i) * frameStride, Stride).CopyTo(_buffer.AsSpan((Height + i) * Stride, Stride));
            Height += canAdd;
            Updated?.Invoke(from);
            return canAdd;
        }
    }

    private void EnsureCapacity(int rows)
    {
        long need = (long)rows * Stride;
        if (need <= _buffer.Length) return;
        long size = _buffer.Length;
        while (size < need) size *= 2;
        Array.Resize(ref _buffer, (int)Math.Min(size, (long)MaxHeight * Stride));
    }

    /// <summary>Копия строк [from, to) для превью.</summary>
    public byte[] CopyRows(int from, int to)
    {
        lock (_sync)
        {
            to = Math.Min(to, Height);
            if (to <= from) return Array.Empty<byte>();
            var result = new byte[(to - from) * Stride];
            Buffer.BlockCopy(_buffer, from * Stride, result, 0, result.Length);
            return result;
        }
    }

    public Bitmap ToBitmap()
    {
        lock (_sync)
        {
            var bmp = new Bitmap(Width, Math.Max(1, Height), PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(_buffer, y * Stride, data.Scan0 + y * data.Stride, Stride);
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }
    }
}
