using GH_Net.Repository.Devices;
using System.Text;

var decoder = new SerialCodeFrameDecoder();
Assert(decoder.Feed(Encoding.UTF8.GetBytes("PRO"), "CR").Count == 0, "Partial code must remain buffered.");
Assert(decoder.Feed(Encoding.UTF8.GetBytes("DUCT-001\r"), "CR").SequenceEqual(["PRODUCT-001"]), "Split CR code failed.");
Assert(decoder.Feed(Encoding.UTF8.GetBytes("C001\rC002\r"), "CR").SequenceEqual(["C001", "C002"]), "Coalesced codes failed.");
decoder.Reset();
Assert(decoder.Feed(Encoding.UTF8.GetBytes("PL001\r"), "CRLF").Count == 0, "CRLF must wait for LF.");
Assert(decoder.Feed(Encoding.UTF8.GetBytes("\n"), "CRLF").SequenceEqual(["PL001"]), "Split CRLF failed.");
decoder.Reset();
byte[] unicode = Encoding.UTF8.GetBytes("SẢN-PHẨM\n");
Assert(decoder.Feed(unicode.AsSpan(0, 3), "LF").Count == 0, "Split UTF-8 must remain buffered.");
Assert(decoder.Feed(unicode.AsSpan(3), "LF").SequenceEqual(["SẢN-PHẨM"]), "Split UTF-8 decode failed.");
Console.WriteLine("Scanner protocol smoke passed: CR/LF/CRLF/split/coalesced/UTF-8.");
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
