# UI specification

## Shared rules

Keep MainWindow, LightTheme, branding, dialogs and vi-VN/en-US localization. No audio feedback.
No account/login/role UI is in scope.

## Jobs

Keep list, search, open and delete behavior. A Running Job blocks return to the list and cannot be
deleted. Start resumes a Stopped Job; Completed Jobs are read-only.

## Create Job

Wizard inputs three CSV files, previews headers, selects one code column for each, captures two
positive capacities, reports empty/duplicate rows and shows a summary before atomic creation.
Before selection, each CSV button shows the localized choose prompt. After selection, the button
shows the file name while its tooltip retains the full path. Parser details such as delimiter
characters are not shown beside the chooser.

## Settings

Provide scanner Serial settings/connection test plus printer IP/port and Carton/Pallet ZPL template
editing/test print. Settings remain locked while a Job is Running.

## Operator screen

Show current Job, workflow, scanner/printer status, current Carton/Pallet progress, last scan,
clear error/success feedback and current Product list. Commands are Start, Stop, Retry Print and
Trace/Export actions. There are no partial Carton/Pallet completion controls. Disable invalid or
busy commands. The ten most recent Products list stretches to consume the remaining vertical
space when the window is enlarged. Below Reprint, six live tiles show checked and remaining counts
for Product, Carton and Pallet codes. Checked Products include `Scanned` and `Packed`; checked
Cartons/Pallets are confirmed `Completed` units. Remaining is the imported total minus checked.

## Traceability and export

Traceability searches exact Product/Carton/Pallet code and groups duplicate codes by Job. Export is
a separate action supporting Excel and CSV ZIP. The operator chooses the complete Job or unused
codes, then any combination of Product, Carton and Pallet; all three are selected by default.

## Automation UI — 26/10/2026

Create Job adds semi-automatic mode and Use PLC (default on in the UI, mandatory for semi-automatic). Operator header shows the immutable mode. Settings has separate Camera and PLC tabs with their own endpoint fields, live connection statuses and messages. Apply saves/connects only the selected device without test actuation; draft changes in the other tab are not applied. All labels exist in vi-VN/en-US.

Camera/PLC forms use the ZRP visual style with CJ-specific scope. Camera framing and no-read token are basic settings. In the PLC tab, the locked operator view shows only the endpoint, connection state, and a read-only summary separating PC-to-PLC sends from PLC-to-PC receives. The protected technical section is absent from the visual/accessibility tree until unlocked, then exposes framing, compatibility mode, timeout, command payloads, and Sensor 1/Sensor 2 tabs with L/H/P/D/G/E settings. Sensor values persist locally and are not transmitted by Apply. Unlock uses a technician-created local code; it expires after five minutes, leaving the PLC tab, or closing Settings. Mandatory invalid/duplicate rejection has no disable switch.
