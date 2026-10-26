# Packing database

## Engine and lifecycle

- SQLite with EF Core 8 and WAL mode.
- Database resides beside the executable and is created by migrations.
- No upgrade/import of prior product data is required.
- The chain contains one fresh `PackingInitial` migration.

## Packing tables

| Table | Purpose |
|---|---|
| `Jobs` | Immutable input metadata, capacity and persisted workflow |
| `Products` | Product source codes and Carton association |
| `Cartons` | Ordered Carton codes, Product count, print/confirmation status |
| `Pallets` | Ordered Pallet codes, Carton count, print/confirmation status |
| `ScanEvents` | Audit for accepted and rejected scanner input |
| `PrintAttempts` | Every initial/reprint ZPL attempt and outcome |
| `ScannerConfigs` | Singleton Serial configuration |
| `PrinterConfigs` | TCP endpoint and Carton/Pallet ZPL templates |

## Integrity

- Product/Carton/Pallet codes are unique by `(JobId, Code)`.
- Carton/Pallet source order is unique per Job.
- Counts and source row indexes cannot be negative.
- A PrintAttempt targets exactly one Carton or one Pallet.
- Deleting a non-running Job cascades to its packing and audit data.
- Cross-Job associations are also validated transactionally by Service; EF foreign keys alone
  do not express composite Job ownership.

## Concurrency

One runtime transition writes at a time. Read-only trace/export uses no-tracking queries and a
consistent read transaction. Long-lived services use `IUnitOfWorkFactory`.

## Automation schema — 26/10/2026

The fresh PackingInitial includes Jobs.Mode/UsePlc/LastCameraSequence, ScanEvents.Source/CameraSequence, AutomationConfigs and cascading LineEvents. LineEvents records intent before network I/O; Pending becomes Unknown after crash and is never replayed. See [AUTOMATION.md](AUTOMATION.md).

AutomationConfigs also persists the selected CameraTerminator and the two ZRP-style PLC sensor panels as JSON (`Sensors`). Each panel stores L/H/P/D/G/E values with defaults in the fresh `PackingInitial` migration. These values are saved for technician setup; this release does not transmit a sensor configuration message to hardware.
