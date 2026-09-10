using Vortice.Direct3D11;
using Microsoft.Extensions.Logging;

namespace Kadr.Recording;

/// <summary>
/// Ограниченный пул текстур для сэмплов кодера. Текстура занята, пока Media Foundation не сообщит Processed;
/// если свободных нет, переиспользуется самая старая занятая (память ограничена, кодер продолжает работать).
/// </summary>
public sealed class TexturePool : IDisposable
{
    private sealed class Slot
    {
        public ID3D11Texture2D Texture = null!;
        public bool Busy;
        public long Sequence;
    }

    private readonly List<Slot> _slots = new();
    private readonly Func<ID3D11Texture2D> _factory;
    private readonly int _capacity;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private long _sequence;
    private bool _warned;

    public TexturePool(int capacity, Func<ID3D11Texture2D> factory, ILogger logger)
    {
        _capacity = Math.Max(2, capacity);
        _factory = factory;
        _logger = logger;
    }

    public int Busy { get { lock (_sync) return _slots.Count(s => s.Busy); } }
    public int Count { get { lock (_sync) return _slots.Count; } }

    /// <summary>Свободная текстура (создаётся при необходимости) и токен для возврата.</summary>
    public (ID3D11Texture2D Texture, object Token) Rent()
    {
        lock (_sync)
        {
            var free = _slots.FirstOrDefault(s => !s.Busy);
            if (free is null && _slots.Count < _capacity)
            {
                free = new Slot { Texture = _factory() };
                _slots.Add(free);
            }
            if (free is null)
            {
                free = _slots.MinBy(s => s.Sequence)!;
                if (!_warned) { _warned = true; _logger.LogWarning("Кодер удерживает больше {Capacity} кадров, текстуры переиспользуются", _capacity); }
            }
            free.Busy = true;
            free.Sequence = ++_sequence;
            return (free.Texture, free);
        }
    }

    public void Return(object token)
    {
        lock (_sync) if (token is Slot s) s.Busy = false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var s in _slots) { try { s.Texture.Dispose(); } catch { } }
            _slots.Clear();
        }
    }
}
