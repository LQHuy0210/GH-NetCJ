# Product requirements

## Camera/PLC extension — 26/10/2026

The user authorized manual and semi-automatic operation using GHNet-ZRP camera/PLC TCP
connections. [AUTOMATION.md](AUTOMATION.md) defines the implemented transport, framing,
mode/source policy, configurable line commands, failure behavior and hardware acceptance.
Manual Jobs may use PLC lamps; semi-automatic Jobs require camera plus PLC. Scanner
continues confirming Carton/Pallet labels. Device models are not fixed; protocol conformance
and physical acceptance remain required. Product printing remains out of scope.

## Confirmed scope

- Modify this repository in place and retain its architecture, branding and licensing.
- Only one Job runs at a time. A Running Job blocks navigation to the Job list.
- Start begins or resumes; Stop waits for the current atomic operation and preserves state.
- A Job imports Product, Carton and Pallet CSV files and selects one exact-code column per file.
- Empty or duplicate selected codes block Job creation and identify the source row.
- Job data and capacity settings are immutable after creation.
- `ProductsPerCarton` and `CartonsPerPallet` apply to the whole Job.
- Product count must divide exactly into full Cartons, and the required Carton count must divide
  exactly into full Pallets. Job creation rejects incompatible counts or insufficient codes.
- Any valid Product in the Job may enter the currently open Carton.
- Carton and Pallet IDs are consumed once in CSV row order.
- Extra Carton/Pallet codes are allowed and remain Available until used.
- No data can be appended to a started Job; the operator creates another Job.
- Duplicate Product control is scoped to a Job, not global.
- A Carton or Pallet prints only at exact capacity; partial units can never be printed.
- Both Carton and Pallet labels must be scanned for confirmation.
- Failed/unreadable labels may be reprinted with the same ID only while awaiting confirmation.
- Failed print commands are not retried automatically.
- Scanner input while busy is rejected and never queued.
- Crash/restart restores the exact persisted workflow, associations and pending confirmation.
- Traceability searches Product, Carton or Pallet in both hierarchy directions.
- Export covers a whole Job in Excel and CSV files inside ZIP, with statuses, timestamps,
  scan results and print attempts. No operator/user field is required.
- Export can instead output any selected combination of unused Product, Carton and Pallet codes.
  Unused means exact `Available` status; `Scanned` and `Filling` are treated as used.
- Initial quality priority is correctness, recovery and stability rather than throughput.

## Devices

- One Zebra scanner via COM Serial, globally configured and connection-tested.
- One Zebra printer via TCP/IP for both Carton and Pallet labels.
- Application-managed Carton/Pallet ZPL templates with `{{CODE}}` are the current design;
  final protocol details remain subject to real-device acceptance.

## Rejected

- Product label printing.
- Concurrent running Jobs.
- Scan queue while processing.
- Audio feedback.
- Editing/importing additional data after Job creation.
- User login, accounts or roles.
- Migration of prior product production data.

## Deferred production decisions

- Exact Zebra scanner model, firmware and validated serial framing.
- Exact Zebra printer model, port and final ZPL label dimensions.
- Database retention and signing/installer policy.

## Definition of done

Implementation, automated verification, package verification and real-device acceptance are
reported separately. Production acceptance requires the customer/operator to complete the
scenarios in `TEST_PLAN.md` on actual Zebra hardware.

## Confirmed settings scope — 26/10/2026

Use the ZRP visual style with CJ requirements: independent camera/PLC tabs; mandatory rejection of invalid/duplicate camera results; two protected ZRP-style sensor setting panels; no optional error bypass. Sensor settings persist locally, while their transmission to the PLC remains out of scope until the PLC program contract is confirmed. Index compatibility and CJ line payloads are advanced settings. See AUTOMATION.md.
