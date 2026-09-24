# YAML schema for state machines

```yaml
machine: <PascalCase name>
scope: demo1 | deferred          # deferred machines are specified in 04 but not built yet
spec_ref: <doc section>
states: [..]                      # every state used below must be listed
initial: <state>                  # state entered by the creation transition
terminal: [..]                    # no transition may leave a terminal state
deferred_exits:                   # optional: non-terminal states whose exits are deferred to a later milestone
  <state>: <where the exits are specified>
transitions:
  - id: <unique id>               # e.g. W5 (matches the tables in 04)
    from: [<state> | _start]
    command: <PascalCase command>
    to: <state>
    actor: <role code | assignee | registrar | system>  or a list of these
    policies: [..]                # record-level rules, e.g. approver_not_registrar
    requires: [..]                # guard/evidence requirements (names are stable ids)
    effects: [..]                 # side effects on other machines (documentation + test hooks)
```

Validation rules enforced by `SpecFilesTests`:
- `id` values are unique within a machine.
- Every `from`/`to` state is in `states` (`_start` is allowed only in `from`).
- `initial` is in `states` and is the `to` of at least one `_start` transition.
- No transition has a terminal state in `from`.
- Every non-terminal state has at least one outgoing transition, unless it is listed in `deferred_exits` with a reference.
- Every state is reachable from `_start`.
- Actors are roles listed in `docs/lean-pilot/permissions.yaml`, or one of the relational actors `assignee`, `registrar`, `system`.
