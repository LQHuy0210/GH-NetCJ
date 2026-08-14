# Packing workflow

## Create Job

1. Select Product, Carton and Pallet CSV files.
2. Select one exact-code column from each header.
3. Enter Products per Carton and Cartons per Pallet.
4. Reject empty/duplicate selected codes with source row details.
5. Reject Product counts that cannot form full Cartons/full Pallets and reject insufficient
   Carton/Pallet codes. Extra Carton/Pallet codes are accepted.
6. Persist the Job and all source codes atomically; input becomes immutable.

## Start and Product scan

Start validates license, scanner, printer and ZPL templates, then resumes the persisted state.
A Product scan is accepted only while filling a Carton, must exist in the Job and must still be
Available. It becomes Scanned and is associated with the open Carton in one transaction.

## Carton

When capacity is reached, allocate the next Carton by CSV row order, enter WaitingPrint and send
its ZPL. A successful send enters Printed/WaitingConfirmation. Only the exact current Carton scan
completes it, changes its Products to Packed and associates the Carton with the open Pallet.

## Pallet

When capacity is reached, allocate the next Pallet, print and require an exact Pallet confirmation
scan. Cartons and Pallets must reach exact capacity before any print command is sent. Confirmation
of the final full Pallet completes the Job.

## Failure and recovery

- Busy scans are rejected and not queued.
- A runtime capacity guard prevents any partial print and shows a clear error without sending a
  printer command or recording a print attempt.
- Print failure remains WaitingPrint; only an explicit Retry sends again.
- Unreadable labels may reprint the same ID while awaiting confirmation.
- Stop waits for the active transition, persists Stopped and permits a later Start.
- Startup converts an interrupted Running Job to Stopped without changing packing workflow.
- No print is automatically resent after a crash.

## Export

The operator may export the complete Job or unused codes. Unused export accepts any combination
of Product, Carton and Pallet and includes only exact `Available` rows. Excel and CSV-ZIP use the
same read-only snapshot and selection rules.
