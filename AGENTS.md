# GH-Net packing system — current project instructions

Updated: 14/08/2026. This file supersedes the read-only historical
`.agents/AGENTS.md`, which still describes the retired GHNet India target.

Read `README.md` and `docs/README.md` first. The target product packs and traces
`Product -> Carton -> Pallet` using one Zebra scanner over COM Serial and one Zebra
printer over TCP/IP. Preserve the WPF shell, design, localization and licensing.

```text
GH-Net (WPF/MVVM/DI) -> GH-Net.Service -> GH-Net.Repository -> GH-Net.Core
                    \-> GH-Net.Licensing
```

- Pure packing rules belong in Core; transactions/workflow/recovery in Service;
  SQLite and device I/O in Repository; presentation in WPF.
- SQLite is authoritative. Runtime singletons use `IUnitOfWorkFactory` and each scan
  transition is atomic.
- One Job may run. Stop waits for the active operation. Busy scans are rejected, not queued.
- Never auto-retry printing. Reprint preserves the current Carton/Pallet ID.
- Job input is immutable and Product uniqueness is scoped to one Job.
- Keep licensing, DPAPI, hardware ID, LightTheme and vi-VN/en-US localization.
- There is no login/user/role system in scope.
- Removed-template behavior must not be reintroduced; replacement coverage is recorded in
  `docs/CHANGE_MATRIX.md`.
- Migration history contains one fresh packing migration. No prior customer-data upgrade is required.
- Follow `docs/TEST_PLAN.md`; do not claim completion while relevant checks fail.
