# Agent Orchestrator v1 - Product Specification

Status: red-team revision 1.1; proposed, not implementation-approved
Date: 2026-11-26
Baseline: v1 original; see [RED_TEAM_REPORT.md](RED_TEAM_REPORT.md) for discovered design failures and fixes.
Name: Agent Orchestrator (working title)
Owner: repository owner; no repository has been selected for this new project

## 1. Mission

Provide a local, vendor-neutral session control plane for coding agents. Any supported MCP-capable coding agent may be a lead and may start, observe, message, steer where supported, cancel, stop, resume, archive, and safely dispose of managed worker sessions. Agents can change roles between tasks. The service owns sessions independently of its MCP clients. The guarantees are about the daemon's managed commands and evidence, not the trustworthiness of the agent process. Native provider side effects are not exactly-once without provider idempotency.

This product is infrastructure, not a model wrapper, development workflow engine, code review service, or autonomous software factory. It must function without DKSTACK or Factory installed.

### Terminology

- **Provider**: installed coding agent such as Codex, Claude Code, Grok Build, or Antigravity CLI.
- **Host**: a coding agent (or human CLI) invoking the orchestrator.
- **Adapter**: provider-specific implementation of session control and event normalization.
- **Session**: a logical conversation with a durable runtime ID and optional provider-native ID.
- **Turn**: a single prompt/execution cycle in a session; a session can have many turns.
- **Task**: the desired work result and acceptance criteria; can own multiple sessions.
- **Operation**: asynchronous runtime command with durable status and idempotency key.
- **Event**: immutable, ordered record of a state change or normalized worker output.
- **Workspace**: a repository checkout, Git worktree, or managed temporary directory.
- **Control lease**: temporary exclusive right to mutate a session's control state.
- **Orchestrator**: a host operating a task or session through the public API; not necessarily the parent process.

## 2. Architectural and product decisions

| Decision | v1 selection |
| --- | --- |
| Implementation | C# 15 / .NET 11, modular monolith |
| Scope | Local, single-user instance, with explicit API for any supported host |
| External API | MCP stdio bridge and human CLI, both using the same daemon contract |
| Runtime | Long-lived per-user daemon started on demand or by user service |
| IPC | Unix domain sockets on Linux/macOS; named pipes on Windows |
| Worker protocols | Codex App Server, Claude Code structured CLI, ACP v1 |
| Providers | Codex, Claude Code, Grok Build, Antigravity CLI |
| Persistence | SQLite state tables + append-only event journal |
| Isolation | Git worktrees for mutating tasks, with separately reported sandbox strength |
| Security | Trusted same-OS-user control plane, no verified human identity over MCP; OS-enforced sandbox only when tested |
| Config | Versioned YAML; validated against a schema; defaults are conservative |
| Distribution | Self-contained native-target .NET executables; AOT only after compatibility tests |
| Interfaces | No browser dashboard, remote server, hosted storage, or cloud control plane in v1 |

### Platform support

P0: Linux x64 and ARM64, Windows x64, macOS x64 and ARM64. P1: Windows ARM64. A provider is usable on a platform only if its own executable supports that platform. No Bash/PowerShell/WSL, Docker, Node, or Python requirement for the orchestrator binary itself; installed workers may have their own dependencies.

## 3. User stories

1. As a Claude Code user, I can create a Codex implementation session, read its events, and send a follow-up turn.
2. As a Codex user, I can create a Claude Code reviewer session on an exact commit and read its verdict and evidence.
3. As a Grok or Antigravity user with MCP tool support, I can invoke the same public tools and manage sessions within my authorization scope.
4. As the user, I can list sessions, see their owner/task/workspace/state, interrupt a hung session, resume eligible sessions, and retain unmerged code.
5. As an integrator, I can query capabilities and decline or explicitly queue a request rather than incorrectly assuming live steering support.
6. As a process owner, I can restart the orchestrating agent or MCP bridge without losing the managed workers.
7. As a future Factory developer, I can call the documented API and consume ordered durable events without importing DKSTACK rules or provider-specific CLIs into Factory.

## 4. Functional requirements

IDs are stable acceptance references. P0 is mandatory for v1 release unless explicitly called out as conditional capability.

### Discovery and configuration

- FR-001 P0: Enumerate configured/discovered agent executables, versions, authentication readiness (when testable without credentials exposure), active adapters, supported platforms, and capability flags.
- FR-002 P0: Do not hardcode provider model identifiers; discover where supported, otherwise use provider defaults or user configuration.
- FR-003 P0: Resolve absolute executable paths without shell interpolation. Pin provider protocol version/capability negotiation per session.
- FR-004 P0: Runtime and CLI both show explicit disabled/degraded capability reasons.

### Session control

- FR-010 P0: Start a managed session using a durable, canonicalized request record, explicit workspace, provider, role, permission profile, limits, task, lineage, and client request key. Atomically reserve session/limits before any native spawn; reject conflicting retries.
- FR-011 P0: Return a durable logical session ID after SQLite commit, not after provider creation. Native session creation may be uncertain after a transport failure and must not be retried automatically unless provider-native deduplication is proven.
- FR-012 P0: List and inspect sessions including provider/native ID, owner, parent/task, current turn, status, workspace, timestamps, capabilities, budget state, and recovery status.
- FR-013 P0: Read events/transcript from a monotonic cursor, with bounded pages and gap/error reporting; consumers can retry safely.
- FR-014 P0: Send only when session/turn/lease epochs are valid; queue follow-ups with a recorded policy, maximum age, bounded count and explicit stop/cancel invalidation. Never auto-replay uncertain native prompts.
- FR-015 P0: Provide separate `steer` command for in-flight turns only when adapter advertises it; otherwise return UNSUPPORTED, never silently translate to `send`.
- FR-016 P0: Request turn cancellation, record that ACP cancellation is a notification (not completion acknowledgment), and confirm interruption via observed terminal event/process state. Escalation and unknown outcomes are distinct. Cancel pending turns according to queue policy.
- FR-017 P0: Stop/unload a managed session while preserving its data and workspace; fence queued/in-flight commands, record cooperative/forced/uncertain termination separately, and never mark Stopped if the managed process may still be active.
- FR-018 P0: Resume only when native provider capability is proven and original workspace, provider identity, tool permissions, MCP server list and limits are revalidated. Report NOT_RESUMABLE or NEEDS_RECONCILIATION as appropriate; never equate a replayed transcript with native model state.
- FR-019 P0: Archive only after the daemon proves the worker is stopped and no unknown native side effects are pending; otherwise return NEEDS_RECONCILIATION. Archive remains read-only.
- FR-020 P0: MCP may request a non-destructive deletion plan but MUST NOT execute durable-history purge or worktree deletion. A separately invoked operator CLI may purge archived session metadata only after showing scope and consequences; it cannot prove a human identity against other same-user processes. Never delete provider-native history, branches, worktrees or unmerged data by default.
- FR-021 P0: Mutating commands require current lease token, monotonically increasing lease epoch, session version and active turn generation. Validate at both admission and immediately before native dispatch under per-session serialization. Epoch change fences queued commands; already-dispatched effects cannot be rolled back.
- FR-022 P0: Parent/orchestrator disconnection must not automatically cancel child sessions.
- FR-023 P0: Enforce child limits via runtime-issued opaque delegation references with verified parent/task linkage, not caller-supplied parent labels. Reject cycles and unreserved capacity; any provider may supervise itself only as separate managed sessions. Limits do not prevent trusted same-user bypass outside the daemon.

### Async messaging and event delivery

- FR-030 P0: Commit command intent, state transition, durable sequence allocation and audit record transactionally before external dispatch. External process effects cannot be atomically committed with SQLite; uncertainty is a first-class state.
- FR-031 P0: Distinguish locally queued, dispatch-intent-recorded, provider-acknowledged, observed-in-turn (only with explicit provider evidence), rejected, and delivery-unknown. Provider RPC acceptance never proves model incorporation.
- FR-032 P0: Preserve typed event envelopes (status, text delta, tool call, permission request, usage, warning, artifact, error) and optional provider-specific details.
- FR-033 P0: Preserve source event IDs/correlation IDs; event subscribers deduplicate by session sequence.
- FR-034 P0: Bound in-memory queues and event payloads; blob spools need atomic publish/checksums and startup orphan reconciliation. On disk-full or failed durable write, stop accepting new work, record evidence-incomplete when possible, and fail closed; never claim zero loss during a storage outage.
- FR-035 P0: Return typed errors: UNSUPPORTED, NOT_AUTHORIZED, CONFLICT, NOT_FOUND, PROVIDER_UNAVAILABLE, AUTH_REQUIRED, TIMEOUT, UNCERTAIN_DELIVERY, NEEDS_RECONCILIATION, INVALID_STATE, RESOURCE_LIMIT, STALE_LEASE, CURSOR_EXPIRED, DURABILITY_UNAVAILABLE, CLEANUP_UNSAFE and SECURITY_CAPABILITY_UNAVAILABLE. Errors must distinguish known refusal from uncertain external effects.

### Workspaces and results

- FR-040 P0: Mutating workers use uniquely managed Git worktrees at a pinned base commit, or fail; this is only Git change separation, never an OS security sandbox. Refuse unsafe existing paths, hostile Git hooks/config or unmanaged workspace ownership where detectable.
- FR-041 P0: Reviewer tasks use an independent snapshot of the exact source SHA, with immutable input manifest and no shared writable author checkout. Only call a reviewer OS-enforced read-only when OS sandbox tests prove it; otherwise label advisory and withhold any enforced-read-only guarantee. Submodules/LFS/ignored content require explicit snapshot inclusion or omission evidence.
- FR-042 P0: Do not copy uncommitted changes from the user's checkout by default; explain when input state differs from the snapshot.
- FR-043 P0: Collect branch, base SHA, head SHA, dirty status, changed files, diff references, and optional test evidence as artifacts. Do not merge or push automatically.
- FR-044 P0: v1 runtime NEVER recursively deletes workspaces or worktrees, and never automatically removes branches. Provide a cleanup plan with managed-identity proof, dirty/untracked/ignored checks and clear manual Git instructions. Stop/archive/session metadata purge never remove work products.
- FR-045 P0: Canonicalization alone is insufficient: validate managed root identity and symlink/junction/reparse-point traversal at point of use, with OS-anchored handles where available. If safe path ownership cannot be proved, refuse to spawn or remove. No recursive deletion in v1.

### Policies and observability

- FR-050 P0: Reserve concurrency, depth, turn and wall-clock quotas transactionally before external effects. Enforce OS resource limits only where tested; otherwise report unavailable. Same-OS-user clients can bypass policy by launching providers outside the daemon, so do not claim system-wide quota enforcement.
- FR-051 P0: Report provider tokens/cost only when available, including provenance. No fabricated estimates and no claims of hard monetary enforcement when provider accounting is unavailable.
- FR-052 P0: Native agent logins remain provider-owned; do not copy, read, or print provider authentication secrets.
- FR-053 P0: Structured audit entries include initiator connection, claimed actor label, delegation reference, target, command, allow/deny, lease epoch, operation state and outcome. A same-user actor label is not an authenticated identity. A failed required journal write blocks new dispatch.
- FR-054 P0: Local-only, OS-user-restricted IPC by default; no open TCP listener.
- FR-055 P0: Owner-only operations (merge, release, deploy, settings changes) are NEVER available through orchestrator MCP, provider adapters, or delegated approval callbacks. Local CLI identity alone is not proof of a human. Agent-owned Git credentials can still bypass rules outside the service; isolation and credential separation remain necessary.
- FR-056 P0: `doctor` command validates install, runtime, protocol handshakes, permissions, and platform safety features without submitting a paid prompt unless explicitly asked.

### Client surfaces

- FR-060 P0: Short-lived stdio MCP bridge connects to persistent daemon. All tools have versioned JSON schemas and stable error codes.
- FR-061 P0: CLI supports lifecycle, event tail and leases; potentially destructive metadata purge requires a separate operator command with explicit typed confirmation and no automatic workspace removal. This is an accident guard, not authentication of a human.
- FR-062 P0: MCP bridge crashes/disconnects do not terminate managed workers or lose persisted events.
- FR-063 P0: Publish separately tested host MCP and worker protocol matrices with supported OS/version/capabilities. An MCP-capable host may orchestrate any supported worker; an unsupported host cannot be described as compatible without evidence.

### Release-blocking hardening requirements (added after adversarial review)

- FR-064 P0: A session has separate conversation, process, turn and command states. Stop/start/archive/cancel races must have explicit guarded transitions; a process exit cannot certify turn success.
- FR-065 P0: Every session has an actor-serialized command queue and monotonic control `epoch`; an epoch bump fences all undispatched prior commands. Never promise rollback of an already-dispatched command.
- FR-066 P0: Provider calls of unknown outcome MUST NOT be automatically retried unless the native provider offers documented idempotency or a read-only reconciliation query proves non-delivery. Local request deduplication is not provider exactly-once execution.
- FR-067 P0: Permission requests from ACP/SDK providers, including filesystem, terminal and MCP-server requests, are denied by default. Only explicitly scoped supported operations may be forwarded. No worker may approve its own escalation or a sibling's privileged request.
- FR-068 P0: On resumptions, toolset/cwd/additional roots/permissions MUST be reauthorized; no restored session silently inherits broader access. Unknown capabilities block that action.
- FR-069 P0: Durable event history must expose `retainedFromSequence`, `nextSequence`, evidence completeness and content redaction/truncation markers. Expired cursors return CURSOR_EXPIRED with a recovery path; never silently skip missing history.
- FR-070 P0: Distinguish local client disconnect, daemon crash, OS reboot, provider process exit, and provider conversation loss. Only the local client-disconnect case guarantees continued execution while daemon health is maintained.
- FR-071 P0: The daemon must fail closed on IPC identity ambiguity, privileged/root or elevated daemon startup, unverifiable process identity, untrusted workspace roots, SQLite corruption, WAL on unsupported network filesystems, and unsupported security capabilities.
- FR-072 P0: Reviewer independence is an evidence claim, not a role string: record distinct session IDs, exact source SHA, provider/model identity when available, input snapshot manifest, and no author-managed writable workspace. This does not prove unbiased judgment.
- FR-073 P0: Retained audit metadata and deletable personal transcript content have distinct policies. Deletion must accurately describe backups, provider-native history and any undeletable evidence. Never promise immutable audit records against the same OS user.
- FR-074 P0: The host's ability to control others must be orthogonal to worker capabilities. Worker conformance and host MCP tests are separate release evidence; feature-conditional support must not be labeled PASS by inference.
- FR-075 P0: If active provider/process state cannot be established after crash, mark NeedsReconciliation, block new turns, preserve work, and expose a manual recovery path; never replay speculative prompts.
- FR-076 P0: Recovery of an unknown native effect must require positive evidence to mark VerifiedApplied or VerifiedNotApplied; an operator may mark AbandonedUnknown without claiming absence of side effects. An explicit new request is required to reattempt unknown work.

### Performance, resource-exhaustion and security release gates (audit revision 1.2)

The requirements below address newly identified workload and trust-boundary failures. They amend, not waive, FR-001 through FR-076. All stated buffer and page ceilings are **proposed defaults**, subject to measured release profiles.

- FR-077 P0: Enforce both item-count and byte-size ceilings on **every** IPC frame, active MCP request, queued command, event ingress, pending response, per-session payload spool and global payload store. Reject oversized frames *before* allocating the declared length; reject malformed, deeply nested or duplicate-field JSON with typed errors. Never decompress unbounded content.
- FR-078 P0: Preserve a physically durable `DispatchIntent` and security admission record before any native effect. Set and verify SQLite `journal_mode=WAL` and `synchronous=FULL` for the control database on a supported local filesystem. Acknowledge a durable intent only after successful COMMIT. Never substitute `NORMAL` to increase throughput without revising the security/durability contract.
- FR-079 P0: Use a bounded **dedicated synchronous SQLite writer** rather than awaiting `Microsoft.Data.Sqlite` async methods on shared async workers (they execute synchronously). Keep transactions short; use a small bounded read pool, parameterized SQL, keyset event pagination and short read transactions. Writer backlog exceeding a deadline makes new admissions fail closed.
- FR-080 P0: Preserve STOP, CANCEL and emergency-stop liveness when normal operation queues or provider RPCs are stuck. A priority/latching supervisor lane, independent of the normal per-session message queue, fences new dispatch immediately and can terminate verified managed processes without waiting for a provider response. On journal outage, record evidence incomplete rather than claim durable stop.
- FR-081 P0: Critical lifecycle/security events and command outcomes are never silently dropped; ordinary provider text and telemetry use bounded, explicitly classified retention. A full ingress queue or payload spool pauses new admissions; if worker output cannot be safely throttled and drained, stop that worker and mark evidence incomplete. Record dropped diagnostic counts and exact retained sequence boundaries.
- FR-082 P0: Concurrently drain and bound **both** provider stdout and stderr. Stream-parse bounded frames with protocol-level limits; avoid `ReadToEnd`, `Task.Result`, synchronous callback reentry or holding session/SQLite locks while awaiting native I/O. A stalled output consumer must not deadlock cancellation or hold unbounded memory.
- FR-083 P0: Execute only a validated absolute provider executable using `UseShellExecute=false` and structured `ArgumentList`. Resolve executable version and file identity at spawn time; never interpolate prompts/paths into a shell command or execute a caller-provided arbitrary executable as a provider. Executable provenance and allowed upgrade behavior must be recorded.
- FR-084 P0: Use explicitly constructed child environments, not ambient inheritance. Do not forward GitHub/admin/cloud tokens or broad MCP tool credentials by default. Provider-specific login material may be necessary; when the worker can access the full same-user home, report credential isolation as unenforced and reject tasks requiring stronger guarantees.
- FR-085 P0: Authenticate the **OS-user boundary**, not a model-provided identity, at IPC connection time. Bind sockets/pipes in owner-restricted namespaces; verify ownership/permissions, daemon instance identity and effective endpoint settings on startup/reconnect. No predictable world-accessible socket, remote Windows named-pipe access, or permissive fallback on ACL errors. This is not same-user peer authentication.
- FR-086 P0: Configure Git plumbing to avoid executing project-controlled hooks, filters, aliases or submodule/LFS helpers implicitly. Treat repository Git configuration and provider workspaces as untrusted; disabled or explicitly approved external helper behavior must be documented and tested. Do not claim a worktree is a containment boundary.
- FR-087 P0: Normalize **provider-supplied** output as untrusted data. Its text, JSON-looking content, filenames, Markdown and tool messages cannot directly become MCP commands, privileged approvals, tool grants, shell arguments or policy changes. Only typed, correlation-validated adapter events may advance runtime state. This does not prevent all model-level prompt injection.
- FR-088 P0: Default-disable remote telemetry and raw-prompt logging. Guard logs, traces, diagnostics, event exports and backups as sensitive artifacts; remove/control tokens and credential fields before serialization and export. Retention/purge describe what cannot be deleted from provider history or external backups. Use low-cardinality metrics (never session/prompt IDs as metric labels).
- FR-089 P0: Keep SQLite on a tested local filesystem; use WAL checkpoint health, bounded writer lock timeouts, storage-reserve alarms and disk quotas. Active readers must release snapshots promptly. Detect stalled checkpoints, growing WAL or write starvation, stop admitting new work before exhausted capacity and never assume `journal_size_limit` alone prevents WAL growth.
- FR-090 P0: Use indexed `(session_id,sequence)` seek pagination, bounded page size and non-offset queries; startup recovery scans indexed **active/unknown** sessions and operations, never replays the full event history. Retained event integrity metadata survives compaction and archival.
- FR-091 P0: Fairly schedule independent sessions (round-robin or tested weighted fairness), bound active session actors/process handles and enforce per-session/global quotas. A noisy agent cannot indefinitely starve another agent's progress, STOP or CANCEL. Preserve stable per-session event order.
- FR-092 P0: At spawn require recorded OS-enforcement capabilities for the requested task policy (read-only, resource limits, credential restrictions). If requested enforcement is unavailable, fail with SECURITY_CAPABILITY_UNAVAILABLE; only explicitly accepted best-effort trusted-worker mode may run without it.
- FR-093 P0: Use reviewed, locked dependencies and reproducible inputs. Check dependency vulnerabilities, provider executable identity, NuGet lockfiles, generated artifacts and release signatures; publish supported version/hashes in the compatibility matrix. Never auto-execute unreviewed provider installers/updaters.
- FR-094 P0: Supply reproducible, native-.NET performance evidence before release: cold start/idle RSS, P50/P95/P99 admission latency, journal commit latency, event ingest throughput, queue fairness, provider output saturation, checkpoint/WAL growth, CPU, open handles and shutdown/cancel latency. Define budgets after hardware baselines; reference Python benchmarks never substitute for .NET acceptance evidence.
- FR-095 P0: Define overload and fail-closed observability: RESOURCE_LIMIT for quotas, DURABILITY_UNAVAILABLE for unsafe persistence, FRAME_TOO_LARGE/INVALID_JSON for parsing, and EVIDENCE_INCOMPLETE for output gaps. No `SUCCEEDED` classification while required event evidence is missing or uncommitted.
- FR-096 P0: An ACP/provider protocol failure or hostile input may terminate only the responsible session/adapter; it must not crash the daemon or other sessions. Enforce concurrent connection/stream caps, per-client request limits, timeout policies and isolation of background-service failures.
- FR-097 P0: Separate a **control-plane trust boundary** (same OS user) from an **untrusted worker sandbox**. The default cannot promise protection against a malicious same-user coding agent. Any claims of enforced privilege limits require a tested OS sandbox plus restricted credentials; approval, publishing and merging remain out of scope.
- FR-098 P0: Separate `observe` and `control` session capabilities. **Every MCP session read/list/event/export and every mutation** must validate a daemon-issued session/task-scope grant. List defaults to caller scope; unauthorized reads return NOT_AUTHORIZED without revealing session existence. Explicit scoped handoff grants require current controller authority; never let caller-provided `sessionId`, `parentId`, role or owner string grant access. Operator CLI can inspect local records, but same-user agents can impersonate it outside a sandbox.
- FR-099 P0: CANCEL, STOP, RESUME and ARCHIVE must validate caller grant, current control lease/epoch and applicable session version/turn generation before action, with a separately documented emergency-stop CLI exception. STOP/CANCEL cannot wait behind normal message queues. Revoking scope invalidates queued commands before dispatch and prevents subsequent reads/exports using that grant.


### Proposed bounded resource profile (requires measurement)

| Resource | Initial default | Saturation behavior |
| --- | --- | --- |
| Internal IPC frame | 1 MiB; JSON max depth 32 | Reject before payload allocation; close abusive client |
| Prompt body | 256 KiB | Reject with RESOURCE_LIMIT |
| Normal session commands | 32 commands and 2 MiB per session | Reject/deflect new sends; STOP independent |
| Normalized event ingress | 256 events/4 MiB per session; 4096 events/32 MiB global | Backpressure then pause/stop noisy worker; explicit evidence gap |
| Large external payload | 8 MiB per individual spool item | Reject/stop or authorized chunking; no silent truncation |
| Payload spool | 64 MiB per session; 1 GiB global | Stop new work before quota is exceeded |
| Event listing | 100 events or 1 MiB maximum response, whichever first | Cursor pagination |
| Metadata DB busy wait | 1 second max per admission (initial proposal) | DURABILITY_UNAVAILABLE, never unbounded await |
| Background writer batch | At most 128 provider events or 10 ms between commits | Preserve order, commit before acknowledging durability |

These numbers are **not** tested production limits. The 20-agent simulated stress scenario must explicitly raise the configured worker limit above the default 4; it must not bypass admission or resource enforcement. Event text may be coalesced only with documented source-sequence mapping. Never batch a required `DispatchIntent` behind slow non-critical output if doing so delays safe admission. A crash can still lose uncommitted provider output: record evidence incomplete at recovery.

## 5. Contract-level semantics

### Session creation

Input: `provider`, `workspace`, `role`, `initialPrompt`, `taskId?`, `parentSessionId?`, `model?`, `permissionProfile`, `executionLimits`, `clientRequestId`.

Output: `sessionId`, `operationId`, `state=created|starting`, `capabilities`, `workspaceId`, `requestedAt`, `version`.

`clientRequestId` is scoped to a daemon-issued caller context and stored with a canonical request digest, unique in the local database. Repeating identical input returns the same local intent; mismatch returns CONFLICT. A lost native `session/new` response can still leave an unknown remote session: reconcile or quarantine, never create a second native session on blind retry. The daemon-issued context is not a cryptographic same-user identity boundary.

### Follow-up / steering

`session_send(sessionId, message, when=now_if_idle|queue_after_turn, clientRequestId, leaseEpoch, expectedVersion)` returns an operation ID. Queue entries have TTL, turn generation, lease epoch and a bounded count. They are fenced on stop, cancellation policy, lease transfer and archive. `session_steer(sessionId, activeTurnId, message, clientRequestId, leaseEpoch)` rejects unsupported, stale or preempted turns. Provider acknowledgment is not proof of model incorporation.

### Reading events

`session_events(sessionId, afterSequence, limit)` returns ordered entries, `retainedFromSequence`, `nextSequence`, and an evidence-completeness flag. If the cursor predates retained history, return CURSOR_EXPIRED and a documented snapshot/recovery path. Duplicates across reconnect are allowed; sequence meaning is stable. Provider text is untrusted data and never interpreted as control instructions. Storage failure may make evidence incomplete and blocks new work.

### Stop / archive / delete

`cancel` requests interruption of a turn (possibly uncertain); `stop` affects a managed process and fences the queue; `archive` locks a verified stopped conversation; `delete`/purge plans retain artifacts and do not remove workspace/branch. MCP cannot execute destructive purge. `workspace_cleanup` in v1 is plan-only, with user-executed Git cleanup outside the daemon.

### Permissions

Use daemon-issued delegation references and lease tokens to prevent accidental cross-session writes and reduce unauthorized control through the public API. The v1 threat model is trusted same-OS-user local clients; per-agent labels and leases are collision safeguards, **not cryptographic isolation between processes owned by the same OS user**. Agent-generated claims of owner approval carry no authority. Access to the local socket by an already-compromised same-user process remains a residual risk.

## 6. Required security behavior

- MCP calls cannot invoke publishing, merging, releasing, repo deletion, arbitrary shell execution, or OS privilege escalation as orchestrator service operations.
- Workspace profiles expose precise enforcement: `enforced` only with a real OS sandbox or equivalent; otherwise `best_effort` or `unavailable`. A worktree alone never counts as `enforced`.
- Runtime cannot prevent a native coding agent from independently using credentials already available to its own OS process. Strong isolation requires a platform sandbox and restricted credentials; v1 does not claim full hostile-code isolation.
- Default deny for provider client-side file, terminal and tool/permission callbacks; permission escalation must not be approved through sibling or self-referential agent messages. When no independently trusted approval channel exists, reject.
- Treat model-generated text, workspace files, and tool outputs as untrusted for the daemon. Paths, arguments and API state transitions must be validated independently.
- Protect SQLite, socket/pipe, transcripts, and audit data with per-user OS permissions. Transcripts may contain sensitive content; support redaction, retention policy, and deletion with clear limitations.
- Support an emergency stop command that immediately fences admissions, persistently blocks new sessions, and attempts graceful/forced shutdown of managed workers; if durable state cannot be written, fail closed and report diagnostic limits.

## 7. Exclusions

Not v1: web dashboard, remote agents, multi-user authorization, provider API model gateway, hosted orchestration, generic DAG/scheduler, full software factory, arbitrary UI automation, autonomous GitHub administration, autonomous merges/releases, automatic provider credential rotation, automatic tool approval, portable live-state migration between different providers, and unconditional attachment to unmanaged TUI sessions.

## 8. Quality requirements and proposed acceptance thresholds

All thresholds below are proposed engineering requirements, not measurements or performance promises.

- QR-001: No local session/operation ID collision across restarts; concurrent duplicate requests produce one durable local intent, and unknown native effects never trigger blind retry.
- QR-002: Exactly one daemon per OS user per configured instance, with recoverable stale-lock detection.
- QR-003: Event journal ordering remains monotonic under 20 concurrent simulated agents.
- QR-004: At least 10 simultaneous sessions in a mocked-process stress test without unbounded queues or lost required events.
- QR-005: Real-agent smoke tests for all four worker providers where supported by the platform. The four-agent all-to-all lead matrix must pass before claiming universal lead compatibility.
- QR-006: Crash and restart of MCP bridge, lead, worker and daemon each produce a documented state and evidence-completeness flag. Only bridge/lead disconnect guarantees worker continuity; daemon crash is OS-dependent.
- QR-007: Explicit NACK or confirmed failure when steer is unsupported. No fake success.
- QR-008: Cleanup-plan-only tests with tracked, untracked, ignored, uncommitted, symlink/junction, path swap, Git LFS and submodule cases. No daemon workspace recursive delete path.
- QR-009: No unredacted credentials in logs or error messages in a seeded-secret fixture test; never promise exhaustive secret detection.
- QR-010: Complete OS-native CI for Linux x64, Windows x64, macOS x64/arm64; Linux arm64 via a suitable native runner or documented release validation. Additional RIDs only after representative smoke tests.
- QR-011: All public commands have versioned schemas, failure codes, docs and automated contract tests.
- QR-012: No warnings on build; analyzers, formatting, unit, integration and protocol-conformance suites pass. Fuzz malformed JSON-RPC, paths and provider event payloads.
- QR-013: Adversarial lease/stop/cancel/dispatch ordering model tests prove no stale queued command crosses a lease or session generation boundary.
- QR-014: Simulated crash points before dispatch, after provider acceptance and before event persist never cause automatic duplicate native prompts.
- QR-015: Missing disk, full disk, corrupt SQLite, stalled consumers and oversized frames do not cause claimed successful durable writes or unbounded memory.
- QR-016: ACP permission callbacks are denied by default, without accidentally executing terminal/file operations under daemon credentials.

## 9. Versioning and compatibility policy

- SemVer for public CLI commands, MCP tool input/output contracts, and daemon IPC schema.
- Major protocol/SDK updates receive compatibility tests against target provider binaries.
- Pin NuGet dependencies and `global.json` SDK; renovate only through reviewed changes.
- Persist `schema_version` on the database and `wire_version` in every IPC handshake.
- Migrations forward-only with backup and downgrade refusal unless tested, preserving recoverability.
- Document separate support matrices for host (can invoke MCP tools) and worker (can be controlled), per OS and version.

## 10. Proposed default limits (configurable)

- 4 simultaneous worker sessions per local instance.
- 2 direct children per orchestrating session, maximum delegation depth 2.
- 1 active turn per worker session, unless adapter proves safe concurrent turns.
- 60-minute wall-clock limit per turn and 3-minute graceful stop deadline, then forced termination where supervisor supports it.
- 2 automatic retries only for operations proven idempotent and safe; otherwise mark uncertain and require reconciliation.
- No default automatic monetary spend limit claim; if usage data unavailable, enforce local turn/time/session caps instead.
- Persist mandatory lifecycle/audit history with a documented retention floor; content payloads have separate configurable retention and size ceilings. Deleted/expired ranges produce CURSOR_EXPIRED. Never claim complete transcripts after redaction or storage failure.

Defaults are proposals and should be tested, not presented as universal optimal values.

## 11. Release gating

v1 is releasable only if all P0 safety invariants and tests pass, including dispatch fencing, uncertain delivery, default-denied permission callbacks, plan-only workspace cleanup, and crash recovery classification. Provider-dependent operations must be labelled conditional and tested where advertised. All required OS/protocol gates must have evidence. The service is NOT a secure sandbox without platform-specific enforcement tests; no agent-supplied owner approval counts.

## 12. Compatibility with existing projects

- DKSTACK remains the authority for skills, safe delegation, independent review and owner-only actions; see https://github.com/daniel-kindl/dkstack/blob/main/workflow/subagents.md and https://github.com/daniel-kindl/dkstack/blob/main/workflow/safety.md.
- Orchestrator enforces only its own API permissions and process/workspace protections. It must not silently override DKSTACK policy.
- Factory may later use this runtime as an execution dependency; deterministic checks, workflow scheduling, classifying fixes/features, merge gates and releases belong to Factory, not this service. See https://github.com/daniel-kindl/factory/blob/main/vision.md.

## 13. Open implementation gates, not open product scope

1. Choose an ACP v1 .NET library only after protocol conformance and live-agent tests, otherwise implement a minimal protocol client with System.Text.Json.
2. Validate exact Claude Code structured CLI options and whether streaming stdin supports prompt delivery during an active turn on each supported CLI version.
3. Validate which Grok and Antigravity ACP capabilities are present after handshake on each OS.
4. Confirm permitted process isolation mechanisms per OS, distinguishing workspace separation from actual privilege restrictions.
5. Choose exact release packaging/installer strategy after native cross-platform smoke tests.

## 14. Source references (checked 2026-11-26)

- .NET support policy: https://dotnet.microsoft.com/en-us/platform/support/policy
- Official MCP C# SDK: https://github.com/modelcontextprotocol/csharp-sdk
- MCP Tasks implementation: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/tasks/tasks.md
- ACP protocol: https://github.com/agentclientprotocol/agent-client-protocol
- ACP v1 session setup: https://github.com/agentclientprotocol/agent-client-protocol/blob/main/docs/protocol/v1/session-setup.mdx
- Codex App Server: https://learn.chatgpt.com/docs/app-server
- Grok ACP: https://github.com/xai-org/grok-build/blob/main/crates/codegen/xai-grok-pager/docs/user-guide/15-agent-mode.md
- Antigravity ACP: https://antigravity.google/docs/cli/acp/
- .NET Process.Kill caveats: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill
- Microsoft.Data.Sqlite concurrency: https://learn.microsoft.com/dotnet/standard/data/sqlite/database-errors
- MCP C# SDK v2 Tasks/version negotiation: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/versioning.md
- ACP session capabilities: https://github.com/agentclientprotocol/agent-client-protocol/blob/main/docs/protocol/v1/session-setup.mdx
- SQLite WAL network filesystem warning: https://sqlite.org/wal.html
- Windows Job Object behavior: https://learn.microsoft.com/windows/win32/procthread/job-objects
