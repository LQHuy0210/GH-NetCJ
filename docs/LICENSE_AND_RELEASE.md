# License and release

`GH-Net.Licensing`, Ed25519, hardware ID, DPAPI storage and the activation UI remain unchanged.
No user/login/role system is in scope.

The existing numeric feature bit `2` is retained and displayed as Scanner so previously issued
tokens remain compatible. Export continues to require the Export feature.

The target package is framework-dependent Windows x64 on .NET 8 Desktop Runtime. On 14/08/2026,
clean restore/build, all packing smoke suites, publish and the packaged `--self-test` passed.
Production release acceptance still requires the real Zebra scanner/printer checklist in
`TEST_PLAN.md`. Production endpoint and ZPL template values remain configurable settings and are
never hard-coded source values.
