# Device protocols

## Zebra scanner — COM Serial

Target configuration is global: PortName, BaudRate, DataBits, Parity, StopBits, terminator and
read timeout. The adapter buffers partial input and publishes one complete unmodified code per
terminator. It does not parse barcode fields or classify Product/Carton/Pallet.

Exact model, firmware defaults and terminator are deferred to real-device acceptance.
The repository uses the official `System.IO.Ports` .NET package; no vendor SDK is required for
the configured Serial transport.

## Zebra printer — TCP/IP and ZPL

The application is a TCP client using configured IP, port and timeouts. It renders application-
managed Carton/Pallet templates by replacing the required `{{CODE}}` placeholder and sends the
resulting ZPL bytes. A successful socket write is a print-attempt outcome, not packing confirmation.

There is no automatic retry. Each explicit Retry creates another PrintAttempt with the same
Carton/Pallet ID. Exact printer port, encoding, label dimensions and status-query policy remain
deferred until the Zebra model is available.

Codes containing ZPL command control characters (`^`, `~`, CR, LF or NUL) are rejected by the
current renderer rather than inserted into `^FD` unsafely.

## Camera and PLC — 26/10/2026

See [AUTOMATION.md](AUTOMATION.md) for ZRP UTF-8 camera framing, PLC ASCII REJECT/Index, PLC001 synchronization, configurable CJ lamp/admission payloads and Hercules instructions. Both devices are TCP servers. No Modbus dependency or vendor SDK has been introduced. SentToSocket is not physical actuation acknowledgement.
