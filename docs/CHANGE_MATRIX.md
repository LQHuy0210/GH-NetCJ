# Current replacement matrix

| Area | Current implementation | Acceptance gate |
|---|---|---|
| WPF shell, localization, licensing | Retained and packing-specific | License and WPF smoke |
| Job creation | Three selected CSV columns, immutable capacities and full-unit validation | `CsvImportSmoke`, `PackingDomainSmoke` |
| Scanner | Zebra COM Serial adapter/configuration | `ScannerProtocolSmoke`, `DeviceConfigSmoke` |
| Printer | Zebra TCP/ZPL adapter and two templates | `ZebraPrinterSmoke`, `DeviceConfigSmoke` |
| Runtime/recovery | Atomic state machine with full-only print guard | `PackingRuntimeSmoke`, `PackingApplicationSmoke` |
| Trace/export | Exact hierarchy; full Job or selected Available codes in Excel/CSV-ZIP | `TraceExportSmoke` |
| Persistence | Fresh packing schema including PLC sensor settings | `PackingDomainSmoke`, `AutomationSmoke` |

Template-only source, schema, UI, assets, tools and documentation were removed after every
replacement gate passed. License feature assignments remain Printer=1, Camera=2, Export=4, Plc=8 and Scanner=16.

## Camera/PLC extension — 26/10/2026

ZRP camera/PLC transports and framing are reused with CJ exact-code comparison. AutomationSmoke covers both Job modes, source isolation, fixed/Index rejection, line commands, failures and exports. This is a newly authorized packing integration; removed template printing/queue/reconnect behavior is not restored.

The current PLC UI adds two protected ZRP-style Sensor tabs with validated L/H/P/D/G/E settings. Values persist in AutomationConfigs; the screen does not emit ZRP sensor-configuration payloads.
