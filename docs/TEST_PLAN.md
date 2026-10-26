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

## Camera/PLC extension — 26/10/2026

Run AutomationSmoke and TechnicalSettingsSmoke in addition to the ten original suites. AutomationSmoke covers real localhost TCP camera/PLC transport, both modes/all framing variants, fixed/Index reject, PLC001, input isolation, no-read/duplicate/overrun, device loss, Stop/resume, licenses, locked settings and command audit/export. TechnicalSettingsSmoke covers encrypted local access setup and lifecycle. Additional Hercules and physical-line scenarios are documented in [AUTOMATION.md](AUTOMATION.md).

## Executed verification — 26/10/2026

- Release solution build: 0 warnings, 0 errors.
- All 12 suites in tools/Run-PackingSmoke.ps1 passed, including AutomationSmoke and TechnicalSettingsSmoke.
- WPF --self-test passed with Create Job and Settings window construction.
- Framework-dependent win-x64 publish and published --self-test passed.
- EF model matches the single fresh PackingInitial migration; git diff --check passed.
- Some existing smoke projects emit CS0067 warnings for unused fake-device events.
- Camera/PLC checks used real localhost TCP sockets and simulated devices, not physical
  hardware and not a manually operated Hercules session. Physical acceptance remains open.

Camera/PLC Settings separation: AutomationSmoke verifies independent partial saves, per-device licensing, offline isolation, sensor-value persistence, locked-field preservation and no sensor/actuation send on Apply. DirectWpfSmoke checks the two protected sensor tabs and localized L/H/P/D/G/E fields. Technical PLC access checks cover user-defined encrypted credential storage, expiry, cooldown, restart lockout and fail-closed storage; the WPF smoke checks that protected controls exist only in the unlocked template and that the screen distinguishes send from receive. Physical PLC acceptance and operator handover remain to be completed on site.
