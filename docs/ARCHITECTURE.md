# Target architecture

```text
GH-Net (WPF/MVVM/DI)
  -> GH-Net.Service
       -> GH-Net.Repository
            -> GH-Net.Core
  -> GH-Net.Licensing
```

| Layer | Ownership |
|---|---|
| Core | Packing statuses, state rules and pure validation |
| Repository | SQLite/EF Core, COM Serial scanner, Zebra TCP transport |
| Service | CSV Job creation, atomic workflow, recovery, traceability, export |
| WPF | Settings, Job creation, operator workflow and reporting |
| Licensing | Activation, hardware identity, feature gates |

## Runtime boundaries

- SQLite is authoritative. Each accepted/rejected scan and state transition is persisted.
- `PackingRuntimeService` will be a singleton with a gate but creates a UnitOfWork per transition.
- Scanner events contain raw complete codes only; business classification belongs in Service.
- Printer transport sends rendered ZPL only; it does not mark domain objects Completed.
- UI observes committed updates and never owns workflow state.

## Current state

The packing implementation is the only application pipeline. Stale-symbol, DI and resource
audits prevent removed template behavior from being reintroduced.
