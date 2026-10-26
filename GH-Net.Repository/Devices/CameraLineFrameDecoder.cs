using System.Buffers;
using System.Text;

namespace GH_Net.Repository.Devices;

/// <summary>
/// Incrementally decodes UTF-8 camera data and emits frames terminated by CR,
/// LF, or CRLF. Incomplete data is retained until a terminator arrives.
/// </summary>
public sealed class CameraLineFrameDecoder
{
    /// <summary>
    /// Default maximum number of characters allowed in one camera frame.
    /// </summary>
    public const int DefaultMaximumFrameLength = 65_536;

    private readonly object _sync = new();
    private readonly UTF8Encoding _encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly int _maximumFrameLength;
    private readonly StringBuilder _buffer = new();
    private Decoder _decoder;
    private bool _skipLeadingLf;
    private string? _terminator;

    public void SetTerminator(string? terminator)
    {
        if (terminator is not (null or "CR" or "LF" or "CRLF")) throw new ArgumentException("Invalid camera terminator.");
        lock (_sync) { ResetCore(); _terminator = terminator; }
    }

    /// <summary>
    /// Creates a decoder with a bounded frame buffer.
    /// </summary>
    public CameraLineFrameDecoder(int maximumFrameLength = DefaultMaximumFrameLength)
    {
        if (maximumFrameLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumFrameLength),
                "Maximum frame length must be greater than zero.");
        }

        _maximumFrameLength = maximumFrameLength;
        _decoder = _encoding.GetDecoder();
    }

    /// <summary>
    /// Gets the count of decoded characters waiting for a frame terminator.
    /// </summary>
    public int BufferedCharacterCount
    {
        get
        {
            lock (_sync)
            {
                return _buffer.Length;
            }
        }
    }

    /// <summary>
    /// Appends one TCP byte chunk and returns every completed frame in receive order.
    /// Empty completed frames are returned so the caller can record an error.
    /// </summary>
    /// <exception cref="DecoderFallbackException">
    /// The input contains an invalid UTF-8 sequence.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// A frame exceeds the configured maximum length.
    /// </exception>
    public IReadOnlyList<string> Append(ReadOnlySpan<byte> bytes)
    {
        lock (_sync)
        {
            if (bytes.IsEmpty)
            {
                return Array.Empty<string>();
            }

            int requiredCharacters = _encoding.GetMaxCharCount(bytes.Length);
            char[] rentedCharacters = ArrayPool<char>.Shared.Rent(requiredCharacters);

            try
            {
                int decodedCount = _decoder.GetChars(
                    bytes,
                    rentedCharacters.AsSpan(0, requiredCharacters),
                    flush: false);

                _buffer.Append(rentedCharacters.AsSpan(0, decodedCount));
                return ExtractCompletedFrames();
            }
            catch (DecoderFallbackException)
            {
                ResetCore();
                throw;
            }
            catch (InvalidDataException)
            {
                ResetCore();
                throw;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rentedCharacters);
            }
        }
    }

    /// <summary>
    /// Clears pending bytes and characters when a connection is closed or replaced.
    /// </summary>
    public void Reset()
    {
        lock (_sync)
        {
            ResetCore();
        }
    }

    private IReadOnlyList<string> ExtractCompletedFrames()
    {
        if (_skipLeadingLf && _buffer.Length > 0)
        {
            if (_buffer[0] == '\n')
            {
                _buffer.Remove(0, 1);
            }

            _skipLeadingLf = false;
        }

        var frames = new List<string>();
        int frameStart = 0;

        for (int index = 0; index < _buffer.Length; index++)
        {
            char current = _buffer[index];
            if (_terminator == "CRLF" && (current != '\n' || index == frameStart || _buffer[index - 1] != '\r')) continue;
            if (_terminator == "CR" && current != '\r') continue;
            if (_terminator == "LF" && current != '\n') continue;
            if (current is not ('\r' or '\n'))
            {
                continue;
            }

            int frameLength = index - frameStart;
            if (current == '\n' && frameLength > 0 && _buffer[index - 1] == '\r') frameLength--;
            EnsureWithinLimit(frameLength);
            // CJ imports exact codes: preserve leading/trailing spaces just like the COM scanner.
            frames.Add(_buffer.ToString(frameStart, frameLength));

            if (current == '\r')
            {
                if (index + 1 < _buffer.Length && _buffer[index + 1] == '\n')
                {
                    index++;
                }
                else if (index + 1 == _buffer.Length)
                {
                    _skipLeadingLf = true;
                }
            }

            frameStart = index + 1;
        }

        if (frameStart > 0)
        {
            _buffer.Remove(0, frameStart);
        }

        EnsureWithinLimit(_buffer.Length - (_terminator == "CRLF" && _buffer.Length > 0 && _buffer[^1] == '\r' ? 1 : 0));
        return frames;
    }

    private void EnsureWithinLimit(int characterCount)
    {
        if (characterCount > _maximumFrameLength)
        {
            throw new InvalidDataException(
                $"Camera frame exceeds the {_maximumFrameLength}-character limit.");
        }
    }

    private void ResetCore()
    {
        _decoder.Reset();
        _buffer.Clear();
        _skipLeadingLf = false;
    }
}
