# Machine-readable state machines

These YAML files are the **single source of truth** for allowed transitions. They are derived from `docs/lean-pilot/04-state-machines.md`, limited to the DEMO 1 scope (`docs/lean-pilot/11-milestones-and-dod.md` §11.1).

- Backend tests load them. From M0, `SpecFilesTests` checks that they are internally consistent. From M1, each C# state machine is tested to implement **exactly** these transitions: every listed transition is allowed when its guards hold, and every unlisted (state, command) pair is rejected.
- Changing behaviour means changing the YAML **in the same PR**, and reviewers read the YAML diff.

M4 added `user.yaml` (offboarding: disable / enable) and `sync-quarantine.yaml` (held commands: apply / discard / apply failed), with `scope: m4`.

M5 added `stock-issue.yaml`, `stock-return.yaml` and `core-return.yaml` (material accountability, 04 §4.9–4.11) and extended `stock-ledger.yaml` (IN_TRANSIT, returns, CORE_BIN), with `scope: m5`.

Schema (see `_schema.md`): `machine`, `scope`, `states`, `initial`, `terminal`, `transitions[]` with `id`, `from[]`, `command`, `to`, `actor`, optional `policies[]`, `requires[]`, `effects[]`. `from: [_start]` means creation.
