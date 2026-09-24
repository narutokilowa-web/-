# Machine-readable state machines

These YAML files are the **single source of truth** for allowed transitions. They are derived from `docs/lean-pilot/04-state-machines.md`, limited to the DEMO 1 scope (`docs/lean-pilot/11-milestones-and-dod.md` §11.1).

- Backend tests load them. From M0, `SpecFilesTests` checks that they are internally consistent. From M1, each C# state machine is tested to implement **exactly** these transitions: every listed transition is allowed when its guards hold, and every unlisted (state, command) pair is rejected.
- Changing behaviour means changing the YAML **in the same PR**, and reviewers read the YAML diff.

Schema (see `_schema.md`): `machine`, `scope`, `states`, `initial`, `terminal`, `transitions[]` with `id`, `from[]`, `command`, `to`, `actor`, optional `policies[]`, `requires[]`, `effects[]`. `from: [_start]` means creation.
