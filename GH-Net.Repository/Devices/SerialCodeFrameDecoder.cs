using System.Text;

namespace GH_Net.Repository.Devices;

public sealed class SerialCodeFrameDecoder
{
    private const int MaximumCodeLength = 4096;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _buffer = new();

    public IReadOnlyList<string> Feed(ReadOnlySpan<byte> bytes, string terminator)
    {
        string marker = terminator switch
        {
            "CR" => "\r",
            "LF" => "\n",
            "CRLF" => "\r\n",
            _ => throw new ArgumentOutOfRangeException(nameof(terminator), "Terminator must be CR, LF or CRLF.")
        };

        int characterCount = _decoder.GetCharCount(bytes, flush: false);
        Span<char> characters = characterCount <= 1024 ? stackalloc char[characterCount] : new char[characterCount];
        int written = _decoder.GetChars(bytes, characters, flush: false);
        _buffer.Append(characters[..written]);
        if (_buffer.Length > MaximumCodeLength && !_buffer.ToString().Contains(marker, StringComparison.Ordinal))
        {
            _buffer.Clear();
            _decoder.Reset();
            throw new InvalidOperationException($"Scanner code exceeds the {MaximumCodeLength}-character limit.");
        }

        var frames = new List<string>();
        while (true)
        {
            int index = _buffer.ToString().IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) break;
            string frame = _buffer.ToString(0, index);
            _buffer.Remove(0, index + marker.Length);
            if (frame.Length > MaximumCodeLength)
                throw new InvalidOperationException($"Scanner code exceeds the {MaximumCodeLength}-character limit.");
            if (frame.Length > 0) frames.Add(frame);
        }
        return frames;
    }

    public void Reset()
    {
        _buffer.Clear();
        _decoder.Reset();
    }
}
