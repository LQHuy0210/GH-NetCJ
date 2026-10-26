# CJ camera and PLC integration

Decision confirmed 26/10/2026: reuse the familiar ZRP layout and TCP transports,
while keeping CJ packing rules. The PLC Settings tab includes two technician-only
ZRP-style sensor panels. Do not import optional error/duplicate rejection, queued
verification or automatic reconnect/replay.

## Operator settings

Camera and PLC have separate tabs, license gates, Apply commands and live status lines.
Each Apply saves only that device's fields and connects only that device. Draft edits
in the other tab are not saved or validated. No actuation or sensor configuration is
sent during Apply. Sensor values are persisted locally with protected PLC settings.
Settings are locked while a Job is running.

On the PLC tab, an operator sees the PLC IP/port, connection state, and a short
read-only summary of the PC-to-PLC and PLC-to-PC messages. Raw protocol options and
command payloads are shown only after a technician unlocks the technical section.
The sections are explicitly labeled **PC sends to PLC** and **PC receives from PLC**;
the receive field identifies the `PLC001` synchronization request and is read-only.
The operator can Apply an endpoint change while locked; this saves only the PLC IP/port
and connects the PLC, without changing protocol details or sending commands.

The first technician sets a private access code in the app (8–64 characters) before
handing the station to operators. There is no built-in/default code. The credential
record is protected with Windows DPAPI for the current Windows account; verification
uses a salted PBKDF2 hash. The section locks after five minutes, when leaving the PLC
tab, or when closing Settings. Five failed attempts trigger a 30-second cooldown.
Changing Windows accounts requires the technician to configure access for that account.
This is a local technical-settings gate, not a user/role or Windows authentication
system, so station setup must be done by a technician before operator handover.

Camera: IP, port, CR/LF/CRLF terminator and no-read token. An exact UTF-8 code is emitted
only when its configured terminator is complete; CR/LF choices also tolerate CRLF.
Split/coalesced packets and split UTF-8 are handled. Outer spaces are preserved for CJ's
exact CSV-code comparison. Empty frames and the exact no-read token are invalid inspections.
Invalid UTF-8 or frames exceeding 65536 characters close the camera connection. A no-read
token that conflicts with a Product code blocks semi-automatic Start.

PLC: IP, port and connection state are visible by default. Technical settings contain the ZRP
REJECT/Index compatibility selection, timeout and configurable CJ line commands.
REJECT is the default. Index is only for a PLC program that supports the ZRP/DLM sequence
contract; it is not required by the CJ model. There are no checkboxes to skip rejection
of duplicate/invalid Product codes. The locked technical section includes Sensor 1 and
Sensor 2 tabs with ZRP fields: delay distance (L), ignore distance (H), encoder pulses (P),
encoder diameter (D), gap (G), and error threshold (E). Values validate and persist in
SQLite. Applying settings does not send the sensor configuration message or activate
encoder setup; hardware protocol mapping remains subject to PLC program acceptance.

## Job modes and packing

- Manual: COM scanner reads Product, Carton and Pallet. PLC lamp/admission control is
  selected at creation (on by default in the UI); it may be disabled for a scanner/printer-only
  station. Camera input is ignored.
- SemiAutomatic: camera TCP reads Product; COM scanner confirms Carton/Pallet. PLC is
  mandatory. The scanner cannot bypass camera Product inspection, and the camera cannot
  confirm containers. Every duplicate, unknown, no-read or wrong-state camera result is
  rejected, excluded from aggregation and requests PLC rejection.
- Mode/UsePlc, source codes and capacities are immutable. Product uniqueness remains per Job.
  Exact-capacity Carton/Pallet printing, label confirmation and same-ID explicit print retry
  rules remain unchanged. This extension does not add Product printing, Parent Code generation,
  partial units, login or an operator-completion step.

## PLC protocol and workflow

Both camera and PLC are TCP servers; CJ is the TCP client. The existing Zebra printer is
separate and keeps its TCP/ZPL transport. PLC payloads are printable ASCII with CR, LF,
CRLF or STX/ETX framing, adapted from GHNet-ZRP.

- Fixed rejection sends REJECT.
- Index rejection sends the unchanged PlcCommandCodec.DlmErrorCommand plus a seven-digit
  camera sequence. Start/resume sends 0 plus persisted LastCameraSequence in seven digits.
- Exact PLC001 replies request the current persisted sequence on the same live connection;
  unrelated replies are ignored. Counter range is 1–9999999; synchronization also accepts 0.
  It never wraps or clamps. Sequence is reserved durably before processing, so a failed
  transition may leave a gap; it is not a source-row or log ID.

There is no invented ACK protocol. SentToSocket confirms a write only, not physical
actuation. Defaults below are test values that must match the actual PLC program:

| State | Default | PLC behavior |
|---|---|---|
| Ready | READY | Permit one Product inspection; clear normal full/error indication |
| Hold | HOLD | Prevent another Product entering during the active operation |
| Full | FULL | Stop admission; light green for packing/label confirmation |
| Fault | FAULT | Stop admission; indicate fault |
| Stopped | STOP | Stop Job admission |

Start connects required endpoints, sends HOLD, then the Index baseline if selected, then
permits READY only when a Product is expected. Each scan transition sends HOLD. A full
unit sends FULL before printing and remains FULL while awaiting its label confirmation.
A failed print leaves the unit pending with FAULT; no automatic print retry occurs.
Container confirmation enables READY only when another Product is expected. Stop and
final completion send STOP. There are no automatic sensor-configuration messages.

The PLC program owns physical tracking, reject timing, one-product admission and a local
watchdog/timeout for unavailable PC/network/camera results. PC commands are not an emergency
stop circuit. A continuously feeding conveyor needs a separate buffering/routing design;
this version serializes inspections and rejects busy input rather than queueing it.

## Failure, recovery and persistence

Busy scanner scans are rejected. Camera overrun records one bounded incident, attempts
rejection, faults/pauses the Job and suppresses further ingress until restart; it cannot
create an unbounded queue. Required camera/PLC loss also pauses the Job without discarding
packing associations. Remove/reconcile all unaccepted products and uncertain actuation
before explicit restart. There is no automatic reconnect or replay of reject/print commands.

Start creates a fresh camera TCP stream, and Stop closes it, so partial/buffered data from
an earlier session cannot be accepted in a later one. SQLite persists mode, sequence,
scan source and command intent/outcome. Command outcomes are Pending, SentToSocket,
NotSent or Unknown; interrupted Pending rows become Unknown on recovery and are not replayed.
Full Job Excel/CSV-ZIP export includes scan source/sequence and LineEvents for PLC Jobs.

License bit assignments are unchanged: Printer=1, Camera=2, Export=4, Plc=8, Scanner=16.
Packing Start needs Scanner and Printer; UsePlc adds Plc; SemiAutomatic adds Camera.
Job creation does not require device readiness. The fresh PackingInitial schema includes
the sensor configuration field with defaults for both sensors; no earlier customer-data
upgrade path is part of this release.

## Verification and acceptance

Run tools/Run-PackingSmoke.ps1 for the Release build, all 12 suites and WPF self-test.
AutomationSmoke uses actual localhost TCP sockets with simulated devices and SQLite.
It covers both modes, all PLC framing variants, selected camera framing/UTF-8, mandatory
rejection, source isolation, capacity printing, PLC001, configuration isolation/licenses,
Stop/resume/partial-frame fencing, loss/overrun, audit/export and no replay.

For Hercules, open separate TCP servers at camera 127.0.0.1:9001 and PLC 127.0.0.1:9002.
Apply each tab independently: neither server should receive commands. Start a compatible
Job and verify HOLD, optional Index synchronization, then READY. Send one Product frame
at a time with the selected camera terminator. Check duplicate/unknown/no-read rejects
without count changes; fill a Carton, observe FULL and Zebra output, confirm Carton then
Pallet with the scanner, and verify STOP. Drop each required device and test explicit
recovery with no replay. Hardware acceptance separately verifies lamp mappings, admission
interlock, watchdog, actual rejection, sequence alignment and recovery at operating speed.
