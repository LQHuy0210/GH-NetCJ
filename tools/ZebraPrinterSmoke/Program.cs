using GH_Net.Repository.Devices;
using GH_Net.Service.Implements;
using System.Net;
using System.Net.Sockets;
using System.Text;

var renderer = new ZplTemplateRenderer();
string rendered = renderer.Render("^XA^FO10,10^FD{{CODE}}^FS^XZ", "CARTON-001");
Assert(rendered == "^XA^FO10,10^FDCARTON-001^FS^XZ", "ZPL placeholder rendering failed.");
const string percentCode = "rjwhrkiu2101999d91%dsjdf1unf1o1";
string percentRendered = renderer.Render("^XA^FO10,10^FD{{CODE}}^FS^XZ", percentCode);
Assert(percentRendered == $"^XA^FO10,10^FD{percentCode}^FS^XZ", "ZPL rendering must preserve the literal 1%d sequence.");
AssertThrows<InvalidOperationException>(() => renderer.Render("^XA^FD{{CODE}}^FS^XZ", "BAD^CODE"));
AssertThrows<InvalidOperationException>(() => renderer.Validate("^XA^FDNO_PLACEHOLDER^FS^XZ"));

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
Task<string> receive = Task.Run(async () =>
{
    using TcpClient client = await listener.AcceptTcpClientAsync();
    byte[] buffer = new byte[1024];
    int count = await client.GetStream().ReadAsync(buffer);
    return Encoding.UTF8.GetString(buffer, 0, count);
});
await using (var printer = new ZebraPrinterDevice())
{
    Assert(await printer.ConnectAsync("127.0.0.1", port, TimeSpan.FromSeconds(2)), "Zebra TCP connection failed.");
    await printer.SendZplAsync(percentRendered, TimeSpan.FromSeconds(2));
}
listener.Stop();
Assert(await receive == percentRendered, "Zebra TCP payload must preserve the literal 1%d sequence.");
Console.WriteLine("Zebra printer smoke passed: template validation/rendering/literal 1%d/TCP send.");
static void AssertThrows<TException>(Action action) where TException : Exception
{
    try { action(); } catch (TException) { return; }
    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
