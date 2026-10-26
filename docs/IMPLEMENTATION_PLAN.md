# Incremental implementation plan

## Phase 0 — contract and documentation

Status: implemented 14/08/2026. Product requirements, architecture, flow, database,
protocols, UI, change matrix, tests and release documents now describe packing.

## Phase 1 — domain foundation

Status: implemented 14/08/2026. Core contains packing statuses, scan types/results,
workflow states and pure full-unit planning/expected-code rules with focused smoke coverage.

## Phase 2 — packing schema foundation

Status: implemented 14/08/2026. EF Core model contains Job packing metadata, Product,
Carton, Pallet, ScanEvent, PrintAttempt and ScannerConfig plus Zebra template fields.
The final repository now uses the consolidated `PackingInitial` migration produced in Phase 11.

## Phase 3 — CSV import and immutable Job creation

Status: implemented 14/08/2026. The CSV parser supports comma/semicolon/TAB/pipe detection,
quoted fields including embedded line breaks, header inspection, exact selected-column import,
empty/duplicate reporting and atomic Product/Carton/Pallet persistence. Create Job WPF now captures
the three files, selected columns and capacities. Creation requires Products to form full Cartons
and full Pallets, requires enough Carton/Pallet codes, and permits extra unused container codes.

## Phase 4 — Zebra scanner Serial foundation

Status: implemented 14/08/2026. `System.IO.Ports` adapter, UTF-8 CR/LF/CRLF decoder,
singleton configuration service, COM enumeration, Apply/connect and Settings UI are present.
Real-hardware acceptance remains pending.

## Phase 5 — Zebra TCP/ZPL foundation

Status: implemented 14/08/2026. A dedicated Zebra TCP adapter, application-managed Carton/Pallet
ZPL templates, strict `{{CODE}}` rendering, explicit test print and Settings UI are present.
No automatic retry is implemented. Real-label acceptance remains pending.

## Phase 6 — transactional packing runtime

Status: implemented 14/08/2026. Product, Carton and Pallet transitions are serialized and
committed atomically with ScanEvent auditing. Only exact-capacity units print; a runtime guard
blocks partial printing before any printer command or PrintAttempt. Failed printing remains pending
with explicit retry and never retries automatically.

## Phase 7 — coordinator and recovery

Status: implemented 14/08/2026. The singleton coordinator runs Packing jobs and enforces
one running Job application-wide, validates configured COM/TCP devices, preserves workflow state on
Stop/crash recovery, and resumes WaitingPrint/WaitingConfirmation without losing the active unit.

## Phase 8 — packing operator UI

Status: implemented 14/08/2026. Packing jobs open a dedicated MainView showing workflow, current
Carton/Pallet IDs and capacities, packed progress, recent scans, clear errors, Start/Stop, same-ID
reprint, Trace and Export controls. Partial completion controls were removed.

## Phase 9 — bidirectional traceability

Status: implemented 14/08/2026. Exact Product, Carton or Pallet searches run across Jobs and
return the complete Product–Carton–Pallet hierarchy in both directions, grouped by Job.

## Phase 10 — Job and unused-code export

Status: implemented 14/08/2026. Packing export uses one consistent SQLite read transaction and
produces either a styled six-sheet Excel workbook or six UTF-8 CSV datasets inside ZIP. Both
contain Job metadata, Products, Cartons, Pallets, scan results and every print attempt. The same
formats also export any selected combination of `Available` Product, Carton and Pallet codes.

## Phase 11 — replacement coverage and cleanup

Status: implemented 14/08/2026. Replacement smokes cover every retained behavior; template-only
source, schema, UI, assets, tools and resource keys are removed. Migration history is collapsed to
one fresh `PackingInitial` migration.

## Phase 12 — release verification

Status: automated and packaged verification completed 14/08/2026. Clean restore, Release build,
all replacement smoke suites, framework-dependent `win-x64` publish and packaged `--self-test`
passed. Real Zebra hardware acceptance remains a separate customer/operator activity.

## Camera/PLC extension — 26/10/2026

Implemented TCP camera/PLC adapters from ZRP, immutable modes, licensed Start preflight, line command/audit integration, settings/localization and AutomationSmoke. Transport verification is separate from physical acceptance; see [AUTOMATION.md](AUTOMATION.md).
