using System.Text;

namespace GH_Net.Repository.Devices;

/// <summary>Bounded ASCII decoder for DLM STX/ETX and line-delimited replies.</summary>
public sealed class PlcMessageDecoder
{
    public const int MaximumLength = 256;
    private readonly StringBuilder _buffer = new();
    private bool _discard;

    public IReadOnlyList<string> Feed(ReadOnlySpan<byte> bytes)
    {
        var messages = new List<string>();
        foreach (byte value in bytes)
        {
            if (value == 2) { _buffer.Clear(); _discard = false; continue; }
            if (value is 3 or 10 or 13)
            {
                if (!_discard && _buffer.Length > 0) messages.Add(_buffer.ToString());
                _buffer.Clear(); _discard = false; continue;
            }
            if (_discard) continue;
            if (value < 32 || value > 126 || _buffer.Length >= MaximumLength)
            { _buffer.Clear(); _discard = true; continue; }
            _buffer.Append((char)value);
        }
        return messages;
    }
}
