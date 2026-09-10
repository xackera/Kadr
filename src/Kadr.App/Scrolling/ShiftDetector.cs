using System.Numerics;

namespace Kadr.App.Scrolling;

/// <summary>Предобработанный кадр: хэши строк по вертикальным полосам.</summary>
public sealed class PreprocessedFrame
{
    public const ulong InvalidKey = ulong.MaxValue;
    public readonly uint[] RowHash;   // [stripe * height + y]
    public readonly ulong[] Key;      // rowHash[y] | rowHash[y+3] << 32
    public readonly int Stripes;
    public readonly int Height;

    public PreprocessedFrame(int stripes, int height)
    {
        Stripes = stripes;
        Height = height;
        RowHash = new uint[stripes * height];
        Key = new ulong[stripes * height];
    }
}

/// <summary>
/// Определение вертикального сдвига между двумя кадрами области при прокрутке.
/// Полосы шириной ~64 px, хэш строки из 32 знаков градиента яркости, грубый поиск шагом 4, уточнение ±5,
/// поддержка по полосам, проверка резкости пика. Фиксированные шапки/подвалы отсекаются центром движущейся зоны.
/// </summary>
public sealed class ShiftDetector
{
    private const int StripesMin = 6, StripesMinNarrow = 3, StripesMax = 16, StripeTargetWidth = 64;
    private const int HashSamples = 33, KeyDelta = 3;
    private const int MaxShiftPercent = 75;
    private const int FineRowStep = 2, CoarseRowStep = 8, CoarseShiftStep = 4, CoarseHd0RowStep = 4;
    private const int CoarsePeakCount = 15, CoarsePeakMinSep = 8, FineHalfWindow = 5;
    private const double MinChangedCellsFraction = 0.05;
    private const int MinAbsoluteDiffScore = 100;
    private const double MinPeakSharpness = 1.1;
    private const int PeakNeighborDistance = 6;

    private readonly int _width, _height, _stripes, _maxShift;
    private readonly int[] _hd0;
    private readonly int _hd0Rows;
    private readonly int[] _fine;
    private readonly List<(int Shift, int Score)> _peaks = new();

    public int MovingCenter { get; private set; }

    public ShiftDetector(int width, int height)
    {
        _width = width;
        _height = height;
        _stripes = width / StripeTargetWidth;
        if (_stripes < StripesMin) _stripes = Math.Max(StripesMinNarrow, width / StripeTargetWidth);
        if (_stripes > StripesMax) _stripes = StripesMax;
        if (_stripes < 1) _stripes = 1;
        _maxShift = Math.Max(1, height * MaxShiftPercent / 100);
        _hd0Rows = (height + 1) / 2;
        _hd0 = new int[_hd0Rows * _stripes];
        _fine = new int[_maxShift + 1];
        MovingCenter = height / 2;
    }

    /// <summary>Хэши строк по полосам. Данные BGRA, stride в байтах.</summary>
    public PreprocessedFrame Preprocess(ReadOnlySpan<byte> bgra, int stride)
    {
        var f = new PreprocessedFrame(_stripes, _height);
        for (int s = 0; s < _stripes; s++)
        {
            int x0 = s * _width / _stripes, x1 = (s + 1) * _width / _stripes;
            int sw = Math.Max(1, x1 - x0);
            for (int y = 0; y < _height; y++)
            {
                int rowOffset = y * stride;
                int prev = -1;
                uint hash = 0;
                for (int k = 0; k < HashSamples; k++)
                {
                    int x = x0 + k * (sw - 1) / (HashSamples - 1);
                    int x2 = Math.Min(x + 1, x1 - 1);
                    int luma = (Luma(bgra, rowOffset + x * 4) + Luma(bgra, rowOffset + x2 * 4)) >> 1;
                    if (k > 0 && luma > prev) hash |= 1u << (k - 1);
                    prev = luma;
                }
                f.RowHash[s * _height + y] = hash;
            }
            for (int y = 0; y < _height; y++)
                f.Key[s * _height + y] = y + KeyDelta < _height
                    ? f.RowHash[s * _height + y] | ((ulong)f.RowHash[s * _height + y + KeyDelta] << 32)
                    : PreprocessedFrame.InvalidKey;
        }
        return f;
    }

    private static int Luma(ReadOnlySpan<byte> p, int i) => (77 * p[i + 2] + 150 * p[i + 1] + 29 * p[i]) >> 8;

    private static int CellHd(PreprocessedFrame a, int ya, PreprocessedFrame b, int yb, int s)
    {
        int h = a.Height;
        var ka = a.Key[s * h + ya]; var kb = b.Key[s * h + yb];
        if (ka != PreprocessedFrame.InvalidKey && kb != PreprocessedFrame.InvalidKey) return BitOperations.PopCount(ka ^ kb);
        return BitOperations.PopCount(a.RowHash[s * h + ya] ^ b.RowHash[s * h + yb]) * 2;
    }

    /// <summary>Сдвиг вниз (в пикселях) между first и second или null, если сдвиг не найден.</summary>
    public int? FindVerticalShift(PreprocessedFrame first, PreprocessedFrame second)
    {
        Array.Fill(_fine, -1);
        _peaks.Clear();
        MovingCenter = _height / 2;

        int changed = 0;
        for (int r = 0; r < _hd0Rows; r++)
        {
            int y = 2 * r;
            for (int s = 0; s < _stripes; s++)
            {
                int d = CellHd(first, y, second, y, s);
                _hd0[r * _stripes + s] = d;
                if (d > 0) changed++;
            }
        }
        if (changed < MinChangedCellsFraction * _hd0Rows * _stripes) return null;

        for (int shift = 1; shift <= _maxShift; shift += CoarseShiftStep)
            InsertPeak(shift, DiffScore(first, second, shift, CoarseRowStep, CoarseHd0RowStep));
        if (_peaks.Count == 0) return null;

        var candidates = new List<(int Shift, int Score, int Support, double Norm)>();
        foreach (var (p, _) in _peaks)
        {
            int bestShift = 0, bestScore = -1;
            for (int s = Math.Max(1, p - FineHalfWindow); s <= Math.Min(_maxShift, p + FineHalfWindow); s++)
            {
                if (_fine[s] < 0) _fine[s] = DiffScore(first, second, s, FineRowStep, 1);
                if (_fine[s] > bestScore) { bestScore = _fine[s]; bestShift = s; }
            }
            if (bestShift == 0 || bestScore < MinAbsoluteDiffScore) continue;
            int support = Support(first, second, bestShift);
            double rowsFull = Math.Ceiling((_height - 1 - KeyDelta) / 2.0);
            double rowsShift = Math.Max(1, Math.Ceiling((_height - bestShift - KeyDelta) / 2.0));
            candidates.Add((bestShift, bestScore, support, bestScore * rowsFull / rowsShift));
        }
        if (candidates.Count == 0) return null;
        candidates.Sort((a, b) => a.Support != b.Support ? b.Support.CompareTo(a.Support) : b.Norm.CompareTo(a.Norm));

        foreach (var c in candidates)
        {
            int shift = c.Shift;
            if (c.Support > 0) shift = Refine(first, second, shift);
            if (!IsPeakSharp(first, second, shift, c.Score)) continue;
            MovingCenter = ComputeMovingCenter(first, second, shift);
            return shift;
        }
        return null;
    }

    private void InsertPeak(int shift, int score)
    {
        if (score <= 0) return;
        for (int i = 0; i < _peaks.Count; i++)
        {
            if (Math.Abs(_peaks[i].Shift - shift) < CoarsePeakMinSep)
            {
                if (score > _peaks[i].Score) _peaks[i] = (shift, score);
                return;
            }
        }
        if (_peaks.Count < CoarsePeakCount) { _peaks.Add((shift, score)); return; }
        int worst = 0;
        for (int i = 1; i < _peaks.Count; i++) if (_peaks[i].Score < _peaks[worst].Score) worst = i;
        if (score > _peaks[worst].Score) _peaks[worst] = (shift, score);
    }

    /// <summary>Насколько сдвиг лучше нулевого: сумма положительных улучшений по ячейкам.</summary>
    private int DiffScore(PreprocessedFrame first, PreprocessedFrame second, int shift, int rowStep, int hd0Step)
    {
        int rows = _height - shift - KeyDelta;
        if (rows <= 0) return 0;
        int score = 0, r = 0;
        for (int i = 0; i < rows; i += rowStep)
        {
            if (r >= _hd0Rows) break;
            for (int s = 0; s < _stripes; s++)
            {
                int d = CellHd(first, i + shift, second, i, s);
                int imp = _hd0[r * _stripes + s] - d;
                if (imp > 0) score += imp;
            }
            r += hd0Step;
        }
        return score;
    }

    private int StripeSignedSum(PreprocessedFrame first, PreprocessedFrame second, int shift, int stripe)
    {
        int rows = _height - shift - KeyDelta;
        int sum = 0, r = 0;
        for (int i = 0; i < rows; i += FineRowStep, r++)
        {
            if (r >= _hd0Rows) break;
            sum += _hd0[r * _stripes + stripe] - CellHd(first, i + shift, second, i, stripe);
        }
        return sum;
    }

    private int Support(PreprocessedFrame first, PreprocessedFrame second, int shift)
    {
        int n = 0;
        for (int s = 0; s < _stripes; s++) if (StripeSignedSum(first, second, shift, s) > 0) n++;
        return n;
    }

    private int Refine(PreprocessedFrame first, PreprocessedFrame second, int shift)
    {
        var supporting = new List<int>();
        for (int s = 0; s < _stripes; s++) if (StripeSignedSum(first, second, shift, s) > 0) supporting.Add(s);
        if (supporting.Count == 0) return shift;
        int best = shift; long bestSum = long.MinValue;
        for (int s = Math.Max(1, shift - FineHalfWindow); s <= Math.Min(_maxShift, shift + FineHalfWindow); s++)
        {
            long sum = 0;
            foreach (var st in supporting) sum += StripeSignedSum(first, second, s, st);
            if (sum > bestSum) { bestSum = sum; best = s; }
        }
        return best;
    }

    private bool IsPeakSharp(PreprocessedFrame first, PreprocessedFrame second, int shift, int score)
    {
        long sum = 0; int n = 0;
        for (int d = 1; d <= PeakNeighborDistance; d++)
        {
            foreach (var s in new[] { shift - d, shift + d })
            {
                if (s < 1 || s > _maxShift) continue;
                if (_fine[s] < 0) _fine[s] = DiffScore(first, second, s, FineRowStep, 1);
                sum += _fine[s]; n++;
            }
        }
        if (n == 0) return true;
        double navg = (double)sum / n;
        return navg < 1 || score >= MinPeakSharpness * navg;
    }

    private int ComputeMovingCenter(PreprocessedFrame first, PreprocessedFrame second, int shift)
    {
        int firstRow = -1, lastRow = -1;
        int rows = _height - shift - KeyDelta;
        for (int i = 0; i < rows; i += 2)
        {
            int r = i / 2;
            if (r >= _hd0Rows) break;
            bool moving = false;
            for (int s = 0; s < _stripes && !moving; s++)
                if (CellHd(first, i + shift, second, i, s) < _hd0[r * _stripes + s]) moving = true;
            if (moving) { if (firstRow < 0) firstRow = i; lastRow = i; }
        }
        if (firstRow < 0) return _height / 2;
        return (firstRow + Math.Min(lastRow + shift, _height - 1)) / 2;
    }
}
