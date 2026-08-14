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
