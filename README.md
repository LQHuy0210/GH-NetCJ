# GH-Net Product–Carton–Pallet

Windows WPF application for packing and tracing data through three levels:

```text
Product -> Carton -> Pallet
```

The target system imports three CSV files per immutable Job, reads exact codes from one
Zebra scanner over COM Serial, and prints Carton/Pallet labels through one Zebra printer over
TCP/IP. The CJ Food workflow extension is being implemented incrementally; printer settings
now include Product ZPL configuration and a test-print control, while production Product-label
sequencing is still in progress.

Manual and semi-automatic Jobs support optional PLC line control and TCP camera input
using the GHNet-ZRP transport/framing. See [camera/PLC integration](docs/AUTOMATION.md).

Authoritative documentation: [docs/README.md](docs/README.md).
