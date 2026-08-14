# Test and acceptance plan

## Automated suites

| Suite | Coverage |
|---|---|
| `LicenseSmoke` | Existing activation/token/hardware behavior |
| `PackingDomainSmoke` | Full-unit planning, shortage/divisibility rules, expected scan type, migration, constraints, cascade |
| `CsvImportSmoke` | CSV detection/quotes/multiline, selected column, empty/duplicate reporting, atomic creation |
| `ScannerProtocolSmoke` | Serial CR/LF/CRLF framing, partial/coalesced input and split UTF-8 |
| `ZebraPrinterSmoke` | ZPL validation/rendering and exact TCP payload |
| `DeviceConfigSmoke` | Scanner/Zebra persistence, Apply, test print and Running-Job lock |
| `PackingRuntimeSmoke` | Product duplicate rejection, full-only printing guard, confirmations, audit and completion |
| `PackingApplicationSmoke` | Coordinator Start/Stop, one-running-Job policy and crash recovery |
| `DirectWpfSmoke` | Packing Create/Settings contracts, no partial controls, selectable export UI and localization |
| `TraceExportSmoke` | Exact hierarchy plus full/selectable Available-only Excel and CSV-ZIP parity |

## Automated verification evidence — 14/08/2026

- Clean restore completed using the repository `NuGet.Config`.
- `dotnet build GH-Net.slnx -c Release --no-restore` completed with 0 warnings and 0 errors.
- All ten smoke projects above passed.
- Framework-dependent `win-x64` publish completed with 43 files.
- The published `GH-Net.exe --self-test` exited with code 0.
- Static legacy-symbol, localization-resource and dependency-injection audits passed.
- `git diff --check` passed.
- NuGet vulnerability-feed lookup emitted `NU1900` while network access was unavailable; package
  restore/build/publish still completed from resolved dependencies.

## Phase 0–2 acceptance

- Product requirements and all current documents describe only the packing application.
- Pure packing policy boundary/error scenarios pass.
- Fresh SQLite migration creates all packing tables and constraints.
- Duplicate Product code is rejected per Job.
- Deleting a Job cascades to packing and audit rows.
- Solution Release build has zero warnings/errors.

## Real-hardware acceptance (deferred)

1. Connect/configure actual Zebra scanner and printer.
2. Create a representative CSV Job.
3. Pack full Carton/Pallet scenarios and verify incompatible Job counts are rejected.
4. Verify wrong, duplicate and busy scans do not mutate state.
5. Disconnect each device and exercise explicit recovery/reprint.
6. Interrupt/restart at WaitingPrint and WaitingConfirmation.
7. Compare physical labels, traceability, full-Job export and selected unused-code export in both formats.
