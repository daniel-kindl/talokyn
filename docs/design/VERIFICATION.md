# Agent Orchestrator v1 - Validation and Delivery Plan

Status: revised after adversarial review. No production runtime or real-provider tests executed
Date: 2026-11-26
Companions: [SPEC.md](SPEC.md), [ARCHITECTURE.md](ARCHITECTURE.md), [RED_TEAM_REPORT.md](RED_TEAM_REPORT.md)

## 1. Principle

Validate observable behavior, not an agent's claim that it worked. A fake provider proves protocol state machines. A real provider proves compatibility with that provider/version. CI checks run without requiring paid model calls, while optional live-provider smoke tests run explicitly on a configured authenticated test machine.

Every acceptance result records: test ID, implementation revision, provider binary/version, operating system/architecture and protocol version/capabilities. It also records input fixture, observed events, outcome, and NOT RUN when evidence is absent. For native external side effects, UNKNOWN is distinct from FAIL and PASS. Negative tests must prove a native provider was NOT contacted after rejection, using a fake provider call counter.

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

Exit: each adapter has a deterministic contract harness and a provider compatibility record. Real-provider calls that could not be executed remain NOT RUN. A v1 release claiming that provider is blocked until target cells pass.

### M1 - Persistent control plane

- .NET daemon and short-lived MCP stdio bridge with local IPC handshake.
- `session_start`, `session_get`, `session_events`, `session_send`, `session_stop`, `operation_get` with sqlite persistence.
- Two real adapters: Codex and Claude.
- Include lease epoch fencing, uncertain-delivery handling, default-denied client-side callbacks, and stop-queue invalidation BEFORE enabling mutating workers. Do not postpone these to M3.
- Reject new dispatch on journal failure or unknown provider state.
- One simulated lead crash and MCP bridge crash: sessions continue under daemon.

Exit: Claude -> Codex and Codex -> Claude scenarios pass, including crash-after-provider-acceptance without duplicate replay. No release/merge tools exposed by the orchestrator. Block M2 if lease, permission, or journal safety tests fail.

### M2 - Full provider matrix

- Add ACP provider support for Grok and Antigravity.
- Implement `session_steer` capability negotiation, cancel/resume/archive, and resource limits.
- Prove all four hosts can invoke MCP where supported, and all four workers can run as supervised sessions.

Exit: host/worker capability matrix published with success/unsupported/not-run states and versioned evidence. Host MCP support is measured separately from worker ACP/native support. Native provider support is not inferred from docs alone.

### M3 - Hardening

- OS supervisor abstractions on Linux, Windows, macOS.
- Safe Git worktree lifecycle and **plan-only** deletion. The daemon never recursively removes workspaces in v1.
- Operation idempotency, outbox reconciliation, database migrations and event gap detection.
- Concurrent session stress tests, permission and secret-handling tests, recovery simulation.

Exit: no known silent loss of committed audit/state events under supported storage conditions. Uncertain external effects and incomplete evidence are explicitly reported. No daemon automatic workspace deletion exists. Security claims state actual enforcement level.

### M4 - v1 release readiness

- API docs and CLI onboarding for providers.
- Linux/Windows/macOS release artifacts, smoke tests on native target OSes.
- Compatibility table, diagnostics, known limitations, release notes, reproducible build inputs and signed artifacts where configured.

Exit: all P0 safety invariants pass on each release target, all advertised host/worker combinations have real evidence, and unavailable capabilities are explicitly UNSUPPORTED. No skipped failing tests. P0 NOT RUN entries block claiming a platform/provider. No claim of complete crash-time transcripts or OS sandboxing without evidence.

## 4. Acceptance scenarios

### API and semantics

| ID | Setup/action | Expected evidence | Spec |
| --- | --- | --- | --- |
| ACC-001 | Spawn same request ID twice concurrently | One **local intent**, same ID returned; payload mismatch CONFLICT; unknown provider side effect not blindly retried | FR-010/011/066 |
| ACC-002 | Start agent with no executable | PROVIDER_UNAVAILABLE; no orphan worktree | FR-001/010 |
| ACC-003 | Send to idle session, disconnect caller | One durable local intent; provider execution may be unknown, never auto-replayed | FR-014/030/066 |
| ACC-004 | Attempt steer without capability | UNSUPPORTED; no follow-up injected silently | FR-015 |
| ACC-005 | Steer stale turn after new turn starts | CONFLICT/INVALID_STATE; no impact on new turn | FR-015 |
| ACC-006 | Request cancellation mid-turn | Provider cancel attempted; interrupted event or timed-out escalation recorded | FR-016 |
| ACC-007 | Stop session with dirty workspace | Queue fenced; true process exit observed or NeedsReconciliation; files, branch and transcript preserved | FR-017/044/065 |
| ACC-008 | Resume provider with native state | New process and conversation state linked; no duplicate start | FR-018 |
| ACC-009 | Resume non-resumable provider | NOT_RESUMABLE; local history retained | FR-018 |
| ACC-010 | Archive, request deletion through MCP | MCP returns plan only; separate operator CLI can purge eligible metadata; no worktree/branch deletion | FR-019/020/044 |
| ACC-011 | Two leads acquire control; old lead already queued steer | One active lease; old queued operation fenced at dispatch with **zero provider calls** | FR-021/065 |
| ACC-012 | Exceed max session/depth/turn limits | RESOURCE_LIMIT, no worker launched | FR-023/050 |
| ACC-013 | Read paged events across restart | Ordered, stable sequence; no gaps silently ignored | FR-013/030/033 |
| ACC-014 | Daemon crash after provider receives prompt, before ack persisted | EffectUnknown, no automatic retry; read-only reconciliation where supported | FR-031/066 |
| ACC-015 | Large output storm | Bounded memory and spool during healthy disk; disk-full causes no new native dispatch and evidence-incomplete flag | FR-034/069 |

### Recovery and supervision

| ID | Fault injection | Expected evidence |
| --- | --- | --- |
| RES-001 | Kill orchestrating agent | Daemon, worker and operations remain accessible via another host |
| RES-002 | Kill MCP bridge process | Same session IDs and events recoverable from restarted bridge |
| RES-003 | Kill daemon during idle session | Previously committed state available; worker detected or safely marked lost, with Windows Job Object behavior documented |
| RES-004 | Kill daemon during active prompt | Operation classified recovery_required/unknown; no false completion or unsafe retry |
| RES-005 | Kill worker during tool execution | OS subtree contained per platform capability; error and partial artifacts retained |
| RES-006 | Replace worker with another PID | No accidental kill of unrelated process via PID reuse |
| RES-007 | SQLite busy/locked injection | Bounded retry; consistent transactional state and diagnostic error |
| RES-008 | Corrupt or incompatible DB schema | Refuse unsafe operation; diagnostic and recovery instructions, no silent reset |
| RES-009 | Disconnect during permission request | Default deny without independent trusted approval channel; no sibling or self-approval |
| RES-010 | Disable local disk while output streams | Backpressure/error; no unbounded memory increase or false journal durability |

### Safety and workspaces

| ID | Attempt | Expected evidence |
| --- | --- | --- |
| SEC-001 | Session path escapes managed root through `..` or symlink | Reject before spawn/delete |
| SEC-002 | Review another session's work as original author | Independent role not falsely certified; review source SHA fixed |
| SEC-003 | Request workspace removal under any dirty/ignored/untracked state | Daemon produces plan only; no recursive delete path exists |
| SEC-004 | Agent message claims owner approved release | No privileged operation exposed or authorization granted |
| SEC-005 | Provider output contains fake MCP/JSON-RPC command | Treated as untrusted text/event, not executed as control instruction |
| SEC-006 | Caller connects across OS account boundary | Rejected by socket/pipe permissions |
| SEC-007 | Same OS-user process spoofs actor/owner label | No owner-only operation becomes available; documentation explicitly denies strong same-user authentication |
| SEC-008 | Seed secrets in CLI output, environment and stderr | Redacted from ordinary logs; no secret logged by orchestrator instrumentation |
| SEC-009 | Run without actual OS sandbox | Workspace separation is reported, but enforced read-only/security isolation is not claimed |
| SEC-010 | Start with dangerous provider flags | Configuration rejected unless separately authorized and documented |
| SEC-011 | Launch daemon as root/elevated admin | Default startup refuses; no elevated MCP bridge exposed |

### Cross-platform and performance

- PLAT-001: Linux x64: daemon/IPC/process/SQLite/worktree tests.
- PLAT-002: Linux ARM64: native release smoke and representative protocol tests.
- PLAT-003: Windows x64: named-pipe ACL, Job Object child tree, path/quote handling, native release smoke.
- PLAT-004: macOS x64: Unix socket, process-group and native release smoke.
- PLAT-005: macOS ARM64: same as PLAT-004.
- PLAT-006: Validate provider binaries only where those providers actually support the OS. Missing installed provider is NOT RUN, not pass.
- PERF-001: 20 deterministic fake concurrent agents for 30 minutes. Stable bounded queue and no lost required events.
- PERF-002: Measure cold start, idle RSS, peak RSS, event ingest throughput, shutdown time and disk growth on each platform. Set release budgets only after pilot data.

## 4A. Release-blocking adversarial tests (added)

Every test below uses an injected scheduling point, a recorded command sequence and a fake native-provider counter. A mock-only success does not establish real-provider compatibility.

| ID | Adversarial sequence | Required result | Maps to |
| --- | --- | --- | --- |
| ADV-001 | A lease admits a steer; it expires; B takes control; A command reaches dispatcher | A fenced before provider call | FR-021/065 |
| ADV-002 | B receives lease; A command races provider send already in progress | Outcome classified dispatched/unknown; no false promise of rollback | FR-065/066 |
| ADV-003 | Provider executes prompt; RPC response lost; client retries key | No duplicate local intent or auto-replayed native effect | FR-011/066 |
| ADV-004 | Native `session/new` succeeds but response lost | Quarantine uncertain native session; no blind second session | FR-011/066 |
| ADV-005 | Stop arrives with queued follow-up and in-flight turn | Queue fenced, no new turns, process confirmation or NeedsReconciliation | FR-017/064 |
| ADV-006 | Cancel races natural turn completion | Completed not overwritten by false Interrupted; no second turn dispatched inadvertently | FR-016/064 |
| ADV-007 | Archive while provider still running or unknown | INVALID_STATE/NEEDS_RECONCILIATION, not Archived | FR-019/064 |
| ADV-008 | MCP re-registers worker under fake human owner label | No owner-only command exposed; no privileged callback succeeds | FR-055/067 |
| ADV-009 | ACP agent requests terminal, file write, or MCP-server grant | Deny by default; no side effect under daemon privileges | FR-067 |
| ADV-010 | ACP resume changes additional directories/MCP tools | Require fresh explicit authorization or reject | FR-068 |
| ADV-011 | Git worktree path swapped to junction/symlink before cleanup | No automatic destructive operation exists; plan warns/does not prove safety | FR-044/045 |
| ADV-012 | Read-only reviewer writes into ordinary snapshot | Cannot claim enforced read-only; source manifest remains unchanged or reviewer fails | FR-041/072 |
| ADV-013 | Daemon dies with Windows kill-on-close job | Worker may die; committed state and recovery classification survive | FR-070/075 |
| ADV-014 | Daemon dies leaving Unix orphan; process ID reused | No unrelated process termination; NeedsReconciliation | FR-070/075 |
| ADV-015 | Provider emits forged MCP JSON/tool call in text delta | Stored as untrusted data, never executed as runtime command | FR-032/067 |
| ADV-016 | Disk fills between temporary blob and SQLite reference | Incomplete/temporary blobs reconciled; no fabricated durable event | FR-034/069 |
| ADV-017 | SQLite WAL placed on unsupported network filesystem | Refuse startup or disable unsupported mode with explicit error | FR-071 |
| ADV-018 | Cursor requests events older than retention floor | CURSOR_EXPIRED and snapshot/recovery information | FR-069 |
| ADV-019 | Provider advertises no ACP resume, cancel delivered as notification | No resume attempt, cancellation remains pending until terminal evidence | FR-016/018/074 |
| ADV-020 | Parent/child cap reached; worker spoofs parent session ID | Reservation rejects extra managed child; admit same-user bypass limitation | FR-023/050 |
| ADV-021 | Client leaves while permission request awaits answer | Fail closed; preserve request status and no silent tool approval | FR-067 |
| ADV-022 | Daemon killed at each acceptance/dispatch/ack/persist point | State never indicates confirmed success without evidence; operator reconciliation uses positive evidence or AbandonedUnknown, no blind replay | FR-030/066/075/076 |
| ADV-023 | Publisher-like GitHub credential exists in worker environment | No claim that orchestrator can enforce owner-only operations outside its API; require credential isolation for such claims | FR-055 |
| ADV-024 | All four hosts/workers matrix on each supported OS | PASS only when actually tested, UNSUPPORTED and NOT RUN not successes | FR-074 |

Minimum failure exploration: enumerate all short interleavings of lease transfer, queued steer, stop, cancel and dispatch. Run randomized/property tests with fixed seeds. Inject process/storage faults at each side-effect boundary. The included `tests/test_reference_model.py` models two representative races only. It is NOT a production implementation or provider test.

## 5. Provider matrix format

Each cell must be measured and labeled `PASS`, `FAIL`, `UNSUPPORTED` or `NOT RUN`, not inferred from another provider. `Hosted as lead` means a real MCP client in that agent can invoke the session tools.

| Lead -> Worker | Codex | Claude | Grok | Antigravity |
| --- | --- | --- | --- | --- |
| Codex | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Claude | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Grok | NOT RUN | NOT RUN | NOT RUN | NOT RUN |
| Antigravity | NOT RUN | NOT RUN | NOT RUN | NOT RUN |

For every tested cell, record: OS, CLI version/hash, authentication mode (not token), MCP registration, adapter protocol and negotiated capability flags. Also record spawn, send, read, cancel, stop, resume availability, elapsed time, trace and full test evidence. Host MCP and worker compatibility are **separate** matrices. A provider protocol unsupported on a platform is UNSUPPORTED, never PASS. NOT RUN blocks claims about that target. Initial simulated-provider and unit tests do not replace real-provider smoke tests.

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

No authenticated Claude/Codex/Grok/Antigravity calls have been executed for this design. Protocol support is based on published documentation. This revision includes a runnable **reference model** of control-plane invariants, not a native implementation. All live-provider compatibility and OS process-supervision claims remain NOT RUN until prototype testing. `EffectUnknown` is a legitimate terminal review state when external delivery cannot be established.

## 9. Useful upstream references

- https://learn.chatgpt.com/docs/app-server
- https://github.com/modelcontextprotocol/csharp-sdk
- https://github.com/agentclientprotocol/agent-client-protocol
- https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/15-agent-mode.md
- https://antigravity.google/docs/ide/extensions/acp/
- https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill
- https://learn.microsoft.com/dotnet/standard/data/sqlite/database-errors

- https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/versioning.md
- https://github.com/agentclientprotocol/acp-tck
- https://sqlite.org/wal.html
- https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects

## 10. Additional performance/security acceptance gates (audit revision 1.2)

The 20-agent stress fixtures explicitly raise the test instance worker cap (default production limit is four). They do not disable quotas or backpressure. These tests are release-blocking **on the production .NET 11 implementation**. The included Python cases are only reference contract examples. **NOT RUN** in .NET until an implementation exists.

### Performance (PERF-Axx)

| ID | Attack/load case | Acceptance criterion |
| --- | --- | --- |
| PERF-A01 | 20 fake agents, one noisy agent, sustained 30 minutes | Per-session/global byte and item limits respected; other sessions make progress; no required event silently dropped |
| PERF-A02 | Fill ordinary command queue and block provider RPC | STOP/CANCEL remain serviceable via independent control lane; safe state classification |
| PERF-A03 | 64 KiB on stdout and stderr concurrently, repeated and delayed reads | Both pipes drained; no deadlock; bounded buffers; cancellation works |
| PERF-A04 | Gigabyte-equivalent advertised IPC frame; partial and deeply nested input | Frame rejected before allocation; no OOM, stack exhaustion or stalled daemon |
| PERF-A05 | SQLite commands at rate above writer capacity | Bounded writer queue and busy timeout; new admissions rejected without dispatch; control still operates |
| PERF-A06 | Long-lived read transaction pins WAL during writes | Checkpoint stall detected; WAL/disk growth alarmed; new work fenced at safety threshold |
| PERF-A07 | Power-loss model for WAL `NORMAL` versus `FULL` | Command/dispatch writes configure and verify FULL; no normal-mode durability claim |
| PERF-A08 | 100k event seek after long history | Composite index used; no `OFFSET`/full-history scan; result page count and bytes bounded |
| PERF-A09 | Start daemon with 100k archived and 20 active sessions | Only active/unknown states scanned for reconciliation; startup memory bounded |
| PERF-A10 | 10k tiny text-delta events and slow/no clients | Bounded coalescing/spooling, cursor correctness, no per-client push backlog |
| PERF-A11 | 20 sessions where one produces 90% of all events | Bounded writer fairness and STOP latency; no starvation across sessions |
| PERF-A12 | Repeated load for 30 minutes and 8-hour soak in release qualification | Stable handles/RSS, bounded queue/spool, reproducible P50/P95/P99 metrics |
| PERF-A13 | Native x64/ARM64 releases and multiple OSes | Collect cold start, RSS, CPU, disk, WAL, IPC latency and throughput evidence per target |

### Security (SEC-Axx)

| ID | Attack case | Acceptance criterion |
| --- | --- | --- |
| SEC-A01 | Oversized frame including impossible length declaration | Reject before allocation; avoid partial state transition |
| SEC-A02 | Duplicate JSON `role`, `leaseEpoch`, nested structures, invalid UTF-8 | Reject entire request; no side effects |
| SEC-A03 | Malformed/native ACP JSON-RPC message or spoofed turn/session ID | Session adapter quarantined; no unauthorized lifecycle transition; other sessions continue |
| SEC-A04 | Same-user agent claims it is human | No privileged owner operation exposed or approved; no false authentication claim |
| SEC-A05 | Different-user connects or local endpoint path is replaced | Peer restricted by OS credentials and endpoint ACL; startup/reconnect fail closed |
| SEC-A06 | Attempt remote Windows named-pipe access/elevated client | Rejected; ACL/elevation behavior tested natively |
| SEC-A07 | PATH/argument/environment manipulation to launch unwanted binary | Absolute trusted executable; ArgumentList only; no shell; no inherited unrelated credentials |
| SEC-A08 | Malicious Git hooks, `includeIf`, filter/smudge, LFS/submodule config | No unapproved helper execution under daemon identity |
| SEC-A09 | Inject JSON-RPC or tool instructions inside model output | Data only; no administrative command or permission grant |
| SEC-A10 | Force ACP permission callback, terminal/file tool or MCP registration | Denied by default; no daemon-privileged side effect |
| SEC-A11 | Open transcript/export/diagnostics with seeded secrets | Confidential artifact policy enforced; ordinary logs never include control/credential tokens |
| SEC-A12 | Disk fills mid-commit or WAL checkpoint becomes blocked | New native dispatch denied; no false success or durable ack |
| SEC-A13 | Attempt enforced read-only on unsupported OS sandbox | Refused or expressly downgraded only through trusted-worker opt-in; no false enforcement claim |
| SEC-A14 | Compromised provider executable attempts to access HOME credentials | Residual risk recorded if OS sandbox/credential restrictions absent; security claim not granted |
| SEC-A15 | Tamper with stored user-owned DB/logs and import falsified approvals | No external human authority inferred; corruption/integrity errors fail closed |
| SEC-A16 | Fuzz internal IPC parser, ACP adapter, MCP bridge and path handling | No daemon panic/crash, escalation, malformed-success or unbounded memory |
| SEC-A17 | Dependency substitution or untrusted agent executable update | Lockfile/signature/version policy enforced and compatibility invalidated until re-probed |
| SEC-A18 | Caller guesses unrelated session ID and calls list/get/events/export | NOT_AUTHORIZED, no record presence revealed, no transcript or artifact data |
| SEC-A19 | Caller has observe grant but attempts STOP/CANCEL, or old controller acts after revoke | NOT_AUTHORIZED/STALE_LEASE before native action; operator emergency path separate |

### Evidence collection

Every case must record: test ID, native OS/RID, exact build/commit, provider binary hash/version (if used), installed .NET SDK/runtime and timestamp. It also records input seed/fixture, measured latency/bytes/handles, observed native effects and pass/fail/NOT RUN. No simulated performance result may be labeled a measured .NET benchmark. Record a known hardware baseline, then establish numeric SLOs and regression thresholds. Proposed defaults in SPEC.md are not release promises.

The Python `benchmarks/benchmark_reference.py` illustrates list-vs-deque queueing, SQLite WAL FULL/NORMAL tradeoff, seek pagination and WAL checkpoint starvation. The `tests/test_perf_security_contract.py` models bounded frames/queues and SQLite safety conditions. Neither verifies the daemon, native agent providers, Windows/macOS isolation or security sandboxing.
