# License and release

`GH-Net.Licensing`, Ed25519, hardware ID, DPAPI storage and the activation UI remain unchanged.
No user/login/role system is in scope.

GHNet-Base and its `GH-Net.LicenseGenerator` are authoritative for feature assignments:
Printer = 1, Camera = 2, Export = 4, Plc = 8, Scanner = 16. CJ requires Scanner and Printer
to start packing; Camera cannot satisfy the Scanner requirement. Generate CJ licenses with
the Scanner checkbox in the Base generator (plus Printer and Export as needed).

Existing named Scanner claims remain Scanner. Legacy numeric bit 2 means Camera under the
Base contract; CJ licenses issued using numeric bit 2 must be reissued with Scanner = 16.
Legacy named All claims retain the Base mask 15 and do not grant Scanner; reissue them for
CJ if Scanner is needed. New tokens enumerate individual feature names, including Scanner
when selected, so future feature additions cannot expand their permissions.
Export continues to require the Export feature.

The target package is framework-dependent Windows x64 on .NET 8 Desktop Runtime. On 14/08/2026,
clean restore/build, all packing smoke suites, publish and the packaged `--self-test` passed.
Production release acceptance still requires the real Zebra scanner/printer checklist in
`TEST_PLAN.md`. Production endpoint and ZPL template values remain configurable settings and are
never hard-coded source values.

Camera/PLC Jobs additionally enforce Plc=8 when UsePlc is selected and Camera=2 for SemiAutomatic. Existing bit assignments are preserved. Test transport compatibility with Hercules and complete the additional physical acceptance checklist in AUTOMATION.md before production release.
