# GH-Net Product–Carton–Pallet

Windows WPF application for packing and tracing data through three levels:

```text
Product -> Carton -> Pallet
```

The target system imports three CSV files per immutable Job, reads exact codes from one
Zebra scanner over COM Serial, and prints Carton/Pallet ZPL labels through one Zebra printer
over TCP/IP.

Authoritative documentation: [docs/README.md](docs/README.md).
