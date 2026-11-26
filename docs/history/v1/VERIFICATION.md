# Agent Orchestrator v1 - Validation and Delivery Plan

Status: proposed test and rollout plan. No tests executed
Date: 2026-11-26
Companions: [SPEC.md](SPEC.md), [ARCHITECTURE.md](ARCHITECTURE.md)

## 1. Principle

Validate observable behavior, not an agent's claim that it worked. A fake provider proves protocol state machines. A real provider proves compatibility with that provider/version. CI checks run without requiring paid model calls, while optional live-provider smoke tests run explicitly on a configured authenticated test machine.

Every acceptance result records: test ID, implementation revision, provider binary/version, operating system/architecture and protocol version/capabilities. It also records input fixture, observed events, outcome, and NOT RUN when evidence is absent.

## 2. Proof layers

| Layer | Test type | Purpose |
| --- | --- | --- |
| L1 | Unit / property tests | State-machine transition validity, leases, budgets, request ID uniqueness |
| L2 | Protocol / adapter contract tests | JSON-RPC framing, capability handshake, event mapping, error states |
| L3 | Integration with deterministic fake CLIs | Process spawning, cancellation, retry ambiguity, event persistence, worktree behavior |
| L4 | OS-native platform tests | Job Objects / process groups, IPC ACLs, path handling, clean shutdown |
| L5 | Real-agent smoke tests | Actual Claude, Codex, Grok and Antigravity compatibility on supported OSes |
| L6 | Fault injection / resilience | Kill daemon, bridge or child at defined points, inspect recovery and cleanup |

## 3. Milestones

### M0 - Protocol feasibility (no full runtime yet)

- Establish target CLI versions and supported OSes with `--version`, `--help` and documented protocol initialization.
- Codex App Server: initialize/initialized, thread/start, turn/start, event stream, turn/steer, turn/interrupt, thread/resume.
- Claude Code: structured stream, session ID capture and `--resume`, explicit result/error types, cancellation. Attempt streaming input/steering only after validating CLI semantics.
- Grok: ACP v1 initialize/session/new/session/prompt/session/cancel and capability flags. No always-approve flags.
- Antigravity: ACP v1 initialize/session/new/session/prompt/session/cancel and capability flags, including platform differences.
- A community ACP .NET SDK can fail compatibility or use unstable APIs. Then implement a bounded ACP v1 client with System.Text.Json, explicit JSON-RPC correlation and stdio lifecycle.

Exit: each adapter has a real-provider compatibility note and a deterministic fake provider contract harness. Actual provider access may be missing in the current environment. Record NOT RUN rather than assume success.

### M1 - Persistent control plane

- .NET daemon and short-lived MCP stdio bridge with local IPC handshake.
- `session_start`, `session_get`, `session_events`, `session_send`, `session_stop`, `operation_get` with sqlite persistence.
- Two real adapters: Codex and Claude.
- One simulated lead crash and MCP bridge crash: sessions continue under daemon.

Exit: Claude -> Codex and Codex -> Claude delegate-turn-read-control scenarios pass on supported test OSes.

### M2 - Full provider matrix

- Add ACP provider support for Grok and Antigravity.
- Implement `session_steer` capability negotiation, cancel/resume/archive, and resource limits.
- Prove all four hosts can invoke MCP where supported, and all four workers can run as supervised sessions.

Exit: host/worker capability matrix published with success/unsupported/not-run states and versioned evidence.

### M3 - Hardening

- OS supervisor abstractions on Linux, Windows, macOS.
- Safe Git worktree lifecycle and deletion plan.
- Operation idempotency, outbox reconciliation, database migrations and event gap detection.
- Concurrent session stress tests, permission and secret-handling tests, recovery simulation.

Exit: no known data loss or unsafe destructive cleanup. Security claims state actual enforcement level.

### M4 - v1 release readiness

- API docs and CLI onboarding for providers.
- Linux/Windows/macOS release artifacts, smoke tests on native target OSes.
- Compatibility table, diagnostics, known limitations, release notes, reproducible build inputs and signed artifacts where configured.

Exit: all P0 requirements met or explicitly capability-conditional. No skipped failing tests. NOT RUN entries are release blockers when they cover a P0 target environment.

## 4. Acceptance scenarios

### API and semantics

| ID | Setup/action | Expected evidence | Spec |
| --- | --- | --- | --- |
| ACC-001 | Spawn same request ID twice, concurrently | One session, same ID returned; payload mismatch is CONFLICT | FR-010/011 |
| ACC-002 | Start agent with no executable | PROVIDER_UNAVAILABLE; no orphan worktree | FR-001/010 |
| ACC-003 | Send to idle session, disconnect caller | Provider receives exactly one submitted turn or operation marked unknown; events retrievable | FR-014/030 |
| ACC-004 | Attempt steer without capability | UNSUPPORTED; no follow-up injected silently | FR-015 |
| ACC-005 | Steer stale turn after new turn starts | CONFLICT/INVALID_STATE; no impact on new turn | FR-015 |
| ACC-006 | Request cancellation mid-turn | Provider cancel attempted; interrupted event or timed-out escalation recorded | FR-016 |
| ACC-007 | Stop session with dirty workspace | Session stopped; files, branch and transcript preserved | FR-017/044 |
| ACC-008 | Resume provider with native state | New process and conversation state linked; no duplicate start | FR-018 |
| ACC-009 | Resume non-resumable provider | NOT_RESUMABLE; local history retained | FR-018 |
| ACC-010 | Archive then delete session | Deletion only of approved local data; no unmerged workspace deletion | FR-019/020 |
| ACC-011 | Two leads acquire control simultaneously | One active lease, one CONFLICT; transfer audited | FR-021 |
| ACC-012 | Exceed max session/depth/turn limits | RESOURCE_LIMIT, no worker launched | FR-023/050 |
| ACC-013 | Read paged events across restart | Ordered, stable sequence; no gaps silently ignored | FR-013/030/033 |
| ACC-014 | Broker crash after dispatch before ack | UNCERTAIN_DELIVERY and reconciliation, not unconditional retry | FR-031 |
| ACC-015 | Large output storm | Bounded memory, required events stored, payloads spooled and referenced | FR-034 |

### Recovery and supervision

| ID | Fault injection | Expected evidence |
| --- | --- | --- |
| RES-001 | Kill orchestrating agent | Daemon, worker and operations remain accessible via another host |
| RES-002 | Kill MCP bridge process | Same session IDs and events recoverable from restarted bridge |
| RES-003 | Kill daemon during idle session | Persisted state available on restart, worker detected or safely marked lost |
| RES-004 | Kill daemon during active prompt | Operation classified recovery_required/unknown; no false completion or unsafe retry |
| RES-005 | Kill worker during tool execution | OS subtree contained per platform capability; error and partial artifacts retained |
| RES-006 | Replace worker with another PID | No accidental kill of unrelated process via PID reuse |
| RES-007 | SQLite busy/locked injection | Bounded retry; consistent transactional state and diagnostic error |
| RES-008 | Corrupt or incompatible DB schema | Refuse unsafe operation; diagnostic and recovery instructions, no silent reset |
| RES-009 | Disconnect during permission request | Request denied or remains pending per explicit policy; never autoapproved |
| RES-010 | Disable local disk while output streams | Backpressure/error; no unbounded memory increase or false journal durability |

### Safety and workspaces

| ID | Attempt | Expected evidence |
| --- | --- | --- |
| SEC-001 | Session path escapes managed root through `..` or symlink | Reject before spawn/delete |
| SEC-002 | Review another session's work as original author | Independent role not falsely certified; review source SHA fixed |
| SEC-003 | Delete worktree with untracked/ignored/uncommitted files | Refuse/require explicit human plan; preserve contents |
| SEC-004 | Agent message claims owner approved release | No privileged operation exposed or authorization granted |
| SEC-005 | Provider output contains fake MCP/JSON-RPC command | Treated as untrusted text/event, not executed as control instruction |
| SEC-006 | Caller connects across OS account boundary | Rejected by socket/pipe permissions |
| SEC-007 | Same OS-user agent spoofs identity label | Documentation and API do not claim a strong cryptographic boundary |
| SEC-008 | Seed secrets in CLI output, environment and stderr | Redacted from ordinary logs; no secret logged by orchestrator instrumentation |
| SEC-009 | Run without actual OS sandbox | Isolation reported best_effort/unavailable, never enforced |
| SEC-010 | Start with dangerous provider flags | Configuration rejected unless separately authorized and documented |

### Cross-platform and performance

- PLAT-001: Linux x64: daemon/IPC/process/SQLite/worktree tests.
- PLAT-002: Linux ARM64: native release smoke and representative protocol tests.
- PLAT-003: Windows x64: named-pipe ACL, Job Object child tree, path/quote handling, native release smoke.
- PLAT-004: macOS x64: Unix socket, process-group and native release smoke.
- PLAT-005: macOS ARM64: same as PLAT-004.
- PLAT-006: Validate provider binaries only where those providers actually support the OS. Missing installed provider is NOT RUN, not pass.
- PERF-001: 20 deterministic fake concurrent agents for 30 minutes. Stable bounded queue and no lost required events.
- PERF-002: Measure cold start, idle RSS, peak RSS, event ingest throughput, shutdown time and disk growth on each platform. Set release budgets only after pilot data.

## 5. Provider matrix format

Each cell must be measured and labeled `PASS`, `FAIL`, `UNSUPPORTED` or `NOT RUN`, not inferred from another provider. `Hosted as lead` means a real MCP client in that agent can invoke the session tools.

| Lead -> Worker | Codex | Claude | Grok | Antigravity |
| --- | --- | --- | --- | --- |
| Codex | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Claude | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Grok | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Antigravity | NOT RUN | NOT RUN | NOT RUN | NOT RUN |

For every tested cell, record: OS, CLI version, authentication mode (not token), MCP registration, adapter protocol and capability flags. Also record spawn, send, read, cancel, stop, resume availability, elapsed time and full test evidence. Initial simulated-provider and unit tests do not replace real-provider smoke tests.

## 6. Test fixture inventory

Use deterministic fake agent executables and protocol servers with modes:

- `normal`: accepts request, emits started/delta/tool/completed events.
- `slow`: emits progress with configurable delays. Accepts cancel.
- `hung`: blocks without output to test timeout and forced termination.
- `partial`: writes truncated NDJSON/JSON-RPC frames.
- `malformed`: wrong JSON types/invalid Unicode/bogus IDs/invalid response correlation.
- `double-ack`: duplicate provider replies and out-of-order notifications.
- `crash-before-ack`, `crash-after-ack`, `resume-fail`.
- `prompt-for-permission`: requires explicit decision. Tests fail-closed behavior.
- `spawn-descendants`: child/grandchild lifetime and inherited handles.
- `large-output`: bounded memory/event spool.
- `mutate-workspace`: dirty/untracked/symlink/traversal cleanup tests.

Use fake time for leases, budgets, and expiries. No network/model calls in ordinary CI.

## 7. DKSTACK and Factory integration gates

- DKSTACK integration may add a delegation skill or guidelines later, but must not alter core runtime identity, message delivery, or authorization semantics.
- Review role must receive exact base/head SHA and independent context. Provider-generated approvals remain advisory. DKSTACK's independent-review and owner-only safety requirements still apply.
- Factory integration starts only after stable runtime release. Its verification engine must independently run required checks and must not trust worker claims of passed tests.
- No integration may enable automatic GitHub merge, release or secret access merely because runtime execution succeeded.

## 8. Explicit limitations

No commands were run against actual authenticated Claude/Codex/Grok/Antigravity installations during preparation of this design. Protocol support is based on published documentation. Therefore all live-provider compatibility claims and cross-platform product behavior remain NOT RUN until a prototype and hardware/OS tests verify them.

## 9. Useful upstream references

- https://learn.chatgpt.com/docs/app-server
- https://github.com/modelcontextprotocol/csharp-sdk
- https://github.com/agentclientprotocol/agent-client-protocol
- https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/15-agent-mode.md
- https://antigravity.google/docs/ide/extensions/acp/
- https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill
- https://learn.microsoft.com/dotnet/standard/data/sqlite/database-errors
