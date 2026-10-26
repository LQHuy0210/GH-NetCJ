# Master execution checklist

## Completed foundation — 14/08/2026

- [x] Confirm Product–Carton–Pallet requirements.
- [x] Reject login/roles, scan queue, audio and old-data migration.
- [x] Establish Keep/Modify/Replace/Remove matrix and deletion gates.
- [x] Add pure packing enums and policy.
- [x] Add packing entities, constraints, indexes and migration.
- [x] Add `PackingDomainSmoke`.

## Remaining vertical slices

- [x] CSV import and immutable Job creation.
- [x] Serial scanner adapter/configuration and protocol smoke.
- [x] Zebra TCP/ZPL adapter/templates/test print foundation.
- [x] Transactional packing runtime and coordinator recovery.
- [x] Replace Create Job, Settings and MainView for Packing jobs.
- [x] Traceability.
- [x] Excel and CSV-ZIP export.
- [x] Replace template smoke coverage.
- [x] Remove template-only source, schema, assets, DI, tools and stale docs.
- [x] Collapse migration history to one fresh packing initialization migration.
- [x] Clean restore/build/test/publish/packaged self-test.
- [ ] Real-device acceptance.

## Cleanup gate

For every template artifact, record retained behavior, replacement code/test and evidence. Search C#,
XAML, DI, resources, migration/snapshot, project files, tools, packaging and documentation before
explicit removal. Legacy tables remain until scanner, printer, runtime and WPF replacements pass.

## Automation extension — 26/10/2026

- [x] Reuse ZRP TCP camera/PLC framing and reject codecs.
- [x] Add manual/semi-automatic modes, source routing, line commands and audit/export.
- [x] Add localized settings and mode selection.
- [x] Add AutomationSmoke.
- [ ] Physical camera/PLC/line acceptance per AUTOMATION.md.
