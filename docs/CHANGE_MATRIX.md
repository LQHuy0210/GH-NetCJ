# Current replacement matrix

| Area | Current implementation | Acceptance gate |
|---|---|---|
| WPF shell, localization, licensing | Retained and packing-specific | License and WPF smoke |
| Job creation | Three selected CSV columns, immutable capacities and full-unit validation | `CsvImportSmoke`, `PackingDomainSmoke` |
| Scanner | Zebra COM Serial adapter/configuration | `ScannerProtocolSmoke`, `DeviceConfigSmoke` |
| Printer | Zebra TCP/ZPL adapter and two templates | `ZebraPrinterSmoke`, `DeviceConfigSmoke` |
| Runtime/recovery | Atomic state machine with full-only print guard | `PackingRuntimeSmoke`, `PackingApplicationSmoke` |
| Trace/export | Exact hierarchy; full Job or selected Available codes in Excel/CSV-ZIP | `TraceExportSmoke` |
| Persistence | Single fresh packing migration | `PackingDomainSmoke` |

Template-only source, schema, UI, assets, tools and documentation were removed after every
replacement gate passed. Licensing bit value `2` remains compatible and is named `Scanner`.
