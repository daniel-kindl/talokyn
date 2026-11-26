# Agent Orchestrator v1 - Product Specification

Status: red-team revision 1.1. Proposed, not implementation-approved
Date: 2026-11-26
Baseline: v1 original. See [RED_TEAM_REPORT.md](RED_TEAM_REPORT.md) for discovered design failures and fixes.
Name: Agent Orchestrator (working title)
Owner: repository owner. No repository has been selected for this new project

## 1. Mission

Provide a local, vendor-neutral session control plane for coding agents. Any supported MCP-capable coding agent may be a lead. A lead may start, observe, message, steer where supported, cancel, stop, resume, archive, and safely dispose of managed worker sessions. Agents can change roles between tasks. The service owns sessions independently of its MCP clients. The guarantees are about the daemon's managed commands and evidence, not the trustworthiness of the agent process. Native provider side effects are not exactly-once without provider idempotency.

This product is infrastructure, not a model wrapper, development workflow engine, code review service, or autonomous software factory. It must function without DKSTACK or Factory installed.

### Terminology

- **Provider**: installed coding agent such as Codex, Claude Code, Grok Build, or Antigravity CLI.
- **Host**: a coding agent (or human CLI) invoking the orchestrator.
- **Adapter**: provider-specific implementation of session control and event normalization.
- **Session**: a logical conversation with a durable runtime ID and optional provider-native ID.
- **Turn**: a single prompt/execution cycle in a session. A session can have many turns.
- **Task**: the desired work result and acceptance criteria. Can own multiple sessions.
- **Operation**: asynchronous runtime command with durable status and idempotency key.
- **Event**: immutable, ordered record of a state change or normalized worker output.
- **Workspace**: a repository checkout, Git worktree, or managed temporary directory.
- **Control lease**: temporary exclusive right to mutate a session's control state.
- **Orchestrator**: a host operating a task or session through the public API. Not necessarily the parent process.

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

P0: Linux x64 and ARM64, Windows x64, macOS x64 and ARM64. P1: Windows ARM64. A provider is usable on a platform only if its own executable supports that platform. No Bash/PowerShell/WSL, Docker, Node, or Python requirement for the orchestrator binary itself. Installed workers may have their own dependencies.

## 3. User stories

1. As a Claude Code user, I can create a Codex implementation session, read its events, and send a follow-up turn.
2. As a Codex user, I can create a Claude Code reviewer session on an exact commit. I can read its verdict and evidence.
3. As a Grok or Antigravity user with MCP tool support, I can invoke the same public tools. I can manage sessions within my authorization scope.
4. As the user, I can list sessions and see their owner/task/workspace/state. I can interrupt a hung session, resume eligible sessions, and retain unmerged code.
5. As an integrator, I can query capabilities. I can decline or explicitly queue a request rather than incorrectly assume live steering support.
6. As a process owner, I can restart the orchestrating agent or MCP bridge without losing the managed workers.
7. As a future Factory developer, I can call the documented API and consume ordered durable events. Factory does not import DKSTACK rules or provider-specific CLIs.

## 4. Functional requirements

IDs are stable acceptance references. P0 is mandatory for v1 release unless explicitly called out as conditional capability.

### Discovery and configuration

- FR-001 P0: Enumerate configured/discovered agent executables, versions, authentication readiness (when testable without credentials exposure), active adapters, supported platforms, and capability flags.
- FR-002 P0: Do not hardcode provider model identifiers. Discover where supported, otherwise use provider defaults or user configuration.
- FR-003 P0: Resolve absolute executable paths without shell interpolation. Pin provider protocol version/capability negotiation per session.
- FR-004 P0: Runtime and CLI both show explicit disabled/degraded capability reasons.

### Session control

- FR-010 P0: Start a managed session using a durable, canonicalized request record, explicit workspace, provider, role, permission profile, limits, task, lineage, and client request key. Atomically reserve session/limits before any native spawn. Reject conflicting retries.
- FR-011 P0: Return a durable logical session ID after SQLite commit, not after provider creation. Native session creation may be uncertain after a transport failure and must not be retried automatically unless provider-native deduplication is proven.
- FR-012 P0: List and inspect sessions including provider/native ID, owner, parent/task, current turn, status, workspace, timestamps, capabilities, budget state, and recovery status.
- FR-013 P0: Read events/transcript from a monotonic cursor, with bounded pages and gap/error reporting. Consumers can retry safely.
- FR-014 P0: Send only when session/turn/lease epochs are valid. Queue follow-ups with a recorded policy, maximum age, bounded count and explicit stop/cancel invalidation. Never auto-replay uncertain native prompts.
- FR-015 P0: Provide separate `steer` command for in-flight turns only when adapter advertises it. Otherwise return UNSUPPORTED, never silently translate to `send`.
- FR-016 P0: Request turn cancellation, record that ACP cancellation is a notification (not completion acknowledgment), and confirm interruption via observed terminal event/process state. Escalation and unknown outcomes are distinct. Cancel pending turns according to queue policy.
- FR-017 P0: Stop/unload a managed session while preserving its data and workspace. Fence queued/in-flight commands, record cooperative/forced/uncertain termination separately, and never mark Stopped if the managed process may still be active.
- FR-018 P0: Resume only when native provider capability is proven and original workspace, provider identity, tool permissions, MCP server list and limits are revalidated. Report NOT_RESUMABLE or NEEDS_RECONCILIATION as appropriate. Never equate a replayed transcript with native model state.
- FR-019 P0: Archive only after the daemon proves the worker is stopped and no unknown native side effects are pending. Otherwise return NEEDS_RECONCILIATION. Archive remains read-only.
- FR-020 P0: MCP may request a non-destructive deletion plan but MUST NOT execute durable-history purge or worktree deletion. A separately invoked operator CLI may purge archived session metadata only after showing scope and consequences. It cannot prove a human identity against other same-user processes. Never delete provider-native history, branches, worktrees or unmerged data by default.
- FR-021 P0: Mutating commands require current lease token, monotonically increasing lease epoch, session version and active turn generation. Validate at both admission and immediately before native dispatch under per-session serialization. Epoch change fences queued commands. Already-dispatched effects cannot be rolled back.
- FR-022 P0: Parent/orchestrator disconnection must not automatically cancel child sessions.
- FR-023 P0: Enforce child limits via runtime-issued opaque delegation references with verified parent/task linkage, not caller-supplied parent labels. Reject cycles and unreserved capacity. Any provider may supervise itself only as separate managed sessions. Limits do not prevent trusted same-user bypass outside the daemon.

### Async messaging and event delivery

- FR-030 P0: Commit command intent, state transition, durable sequence allocation and audit record transactionally before external dispatch. External process effects cannot be atomically committed with SQLite. Uncertainty is a first-class state.
- FR-031 P0: Distinguish locally queued, dispatch-intent-recorded, provider-acknowledged, observed-in-turn (only with explicit provider evidence), rejected, and delivery-unknown. Provider RPC acceptance never proves model incorporation.
- FR-032 P0: Preserve typed event envelopes (status, text delta, tool call, permission request, usage, warning, artifact, error) and optional provider-specific details.
- FR-033 P0: Preserve source event IDs/correlation IDs. Event subscribers deduplicate by session sequence.
- FR-034 P0: Bound in-memory queues and event payloads. Blob spools need atomic publish/checksums and startup orphan reconciliation. On disk-full or failed durable write, stop accepting new work, record evidence-incomplete when possible, and fail closed. Never claim zero loss during a storage outage.
- FR-035 P0: Return typed errors: UNSUPPORTED, NOT_AUTHORIZED, CONFLICT, NOT_FOUND, PROVIDER_UNAVAILABLE, AUTH_REQUIRED, TIMEOUT, UNCERTAIN_DELIVERY, NEEDS_RECONCILIATION, INVALID_STATE, RESOURCE_LIMIT, STALE_LEASE, CURSOR_EXPIRED, DURABILITY_UNAVAILABLE, CLEANUP_UNSAFE and SECURITY_CAPABILITY_UNAVAILABLE. Errors must distinguish known refusal from uncertain external effects.

### Workspaces and results

- FR-040 P0: Mutating workers use uniquely managed Git worktrees at a pinned base commit, or fail. This is only Git change separation, never an OS security sandbox. Refuse unsafe existing paths, hostile Git hooks/config or unmanaged workspace ownership where detectable.
- FR-041 P0: Reviewer tasks use an independent snapshot of the exact source SHA, with immutable input manifest and no shared writable author checkout. Only call a reviewer OS-enforced read-only when OS sandbox tests prove it. Otherwise label advisory and withhold any enforced-read-only guarantee. Submodules/LFS/ignored content require explicit snapshot inclusion or omission evidence.
- FR-042 P0: Do not copy uncommitted changes from the user's checkout by default. Explain when input state differs from the snapshot.
- FR-043 P0: Collect branch, base SHA, head SHA, dirty status, changed files, diff references, and optional test evidence as artifacts. Do not merge or push automatically.
- FR-044 P0: v1 runtime NEVER recursively deletes workspaces or worktrees, and never automatically removes branches. Provide a cleanup plan with managed-identity proof, dirty/untracked/ignored checks and clear manual Git instructions. Stop/archive/session metadata purge never remove work products.
- FR-045 P0: Canonicalization alone is insufficient: validate managed root identity and symlink/junction/reparse-point traversal at point of use, with OS-anchored handles where available. If safe path ownership cannot be proved, refuse to spawn or remove. No recursive deletion in v1.

### Policies and observability

- FR-050 P0: Reserve concurrency, depth, turn and wall-clock quotas transactionally before external effects. Enforce OS resource limits only where tested. Otherwise report unavailable. Same-OS-user clients can bypass policy by launching providers outside the daemon, so do not claim system-wide quota enforcement.
- FR-051 P0: Report provider tokens/cost only when available, including provenance. No fabricated estimates and no claims of hard monetary enforcement when provider accounting is unavailable.
- FR-052 P0: Native agent logins remain provider-owned. Do not copy, read, or print provider authentication secrets.
- FR-053 P0: Structured audit entries include initiator connection, claimed actor label, delegation reference, target, command, allow/deny, lease epoch, operation state and outcome. A same-user actor label is not an authenticated identity. A failed required journal write blocks new dispatch.
- FR-054 P0: Local-only, OS-user-restricted IPC by default. No open TCP listener.
- FR-055 P0: Owner-only operations (merge, release, deploy, settings changes) are NEVER available through orchestrator MCP, provider adapters, or delegated approval callbacks. Local CLI identity alone is not proof of a human. Agent-owned Git credentials can still bypass rules outside the service. Isolation and credential separation remain necessary.
- FR-056 P0: `doctor` command validates install, runtime, protocol handshakes, permissions, and platform safety features without submitting a paid prompt unless explicitly asked.

### Client surfaces

- FR-060 P0: Short-lived stdio MCP bridge connects to persistent daemon. All tools have versioned JSON schemas and stable error codes.
- FR-061 P0: CLI supports lifecycle, event tail and leases. Potentially destructive metadata purge requires a separate operator command with explicit typed confirmation and no automatic workspace removal. This is an accident guard, not authentication of a human.
- FR-062 P0: MCP bridge crashes/disconnects do not terminate managed workers or lose persisted events.
- FR-063 P0: Publish separately tested host MCP and worker protocol matrices with supported OS/version/capabilities. An MCP-capable host may orchestrate any supported worker. An unsupported host cannot be described as compatible without evidence.

### Release-blocking hardening requirements (added after adversarial review)

- FR-064 P0: A session has separate conversation, process, turn and command states. Stop/start/archive/cancel races must have explicit guarded transitions. A process exit cannot certify turn success.
- FR-065 P0: Every session has an actor-serialized command queue and monotonic control `epoch`. An epoch bump fences all undispatched prior commands. Never promise rollback of an already-dispatched command.
- FR-066 P0: Provider calls of unknown outcome MUST NOT be automatically retried. The exception: the native provider offers documented idempotency, or a read-only reconciliation query proves non-delivery. Local request deduplication is not provider exactly-once execution.
- FR-067 P0: Permission requests from ACP/SDK providers, including filesystem, terminal and MCP-server requests, are denied by default. Only explicitly scoped supported operations may be forwarded. No worker may approve its own escalation or a sibling's privileged request.
- FR-068 P0: On resumptions, toolset/cwd/additional roots/permissions MUST be reauthorized. No restored session silently inherits broader access. Unknown capabilities block that action.
- FR-069 P0: Durable event history must expose `retainedFromSequence`, `nextSequence`, evidence completeness and content redaction/truncation markers. Expired cursors return CURSOR_EXPIRED with a recovery path. Never silently skip missing history.
- FR-070 P0: Distinguish local client disconnect, daemon crash, OS reboot, provider process exit, and provider conversation loss. Only the local client-disconnect case guarantees continued execution while daemon health is maintained.
- FR-071 P0: The daemon must fail closed on IPC identity ambiguity, privileged/root or elevated daemon startup, unverifiable process identity and untrusted workspace roots. It must also fail closed on SQLite corruption, WAL on unsupported network filesystems, and unsupported security capabilities.
- FR-072 P0: Reviewer independence is an evidence claim, not a role string. Record distinct session IDs, exact source SHA and provider/model identity when available. Also record the input snapshot manifest, and that no author-managed writable workspace exists. This does not prove unbiased judgment.
- FR-073 P0: Retained audit metadata and deletable personal transcript content have distinct policies. Deletion must accurately describe backups, provider-native history and any undeletable evidence. Never promise immutable audit records against the same OS user.
- FR-074 P0: The host's ability to control others must be orthogonal to worker capabilities. Worker conformance and host MCP tests are separate release evidence. Feature-conditional support must not be labeled PASS by inference.
- FR-075 P0: If active provider/process state cannot be established after crash, mark NeedsReconciliation, block new turns, preserve work, and expose a manual recovery path. Never replay speculative prompts.
- FR-076 P0: Recovery of an unknown native effect must require positive evidence to mark VerifiedApplied or VerifiedNotApplied. An operator may mark AbandonedUnknown without claiming absence of side effects. An explicit new request is required to reattempt unknown work.

## 5. Contract-level semantics

### Session creation

Input: `provider`, `workspace`, `role`, `initialPrompt`, `taskId?`, `parentSessionId?`, `model?`, `permissionProfile`, `executionLimits`, `clientRequestId`.

Output: `sessionId`, `operationId`, `state=created|starting`, `capabilities`, `workspaceId`, `requestedAt`, `version`.

`clientRequestId` is scoped to a daemon-issued caller context and stored with a canonical request digest, unique in the local database. Repeating identical input returns the same local intent. Mismatch returns CONFLICT. A lost native `session/new` response can still leave an unknown remote session: reconcile or quarantine, never create a second native session on blind retry. The daemon-issued context is not a cryptographic same-user identity boundary.

### Follow-up / steering

`session_send(sessionId, message, when=now_if_idle|queue_after_turn, clientRequestId, leaseEpoch, expectedVersion)` returns an operation ID. Queue entries have TTL, turn generation, lease epoch and a bounded count. They are fenced on stop, cancellation policy, lease transfer and archive. `session_steer(sessionId, activeTurnId, message, clientRequestId, leaseEpoch)` rejects unsupported, stale or preempted turns. Provider acknowledgment is not proof of model incorporation.

### Reading events

`session_events(sessionId, afterSequence, limit)` returns ordered entries, `retainedFromSequence`, `nextSequence`, and an evidence-completeness flag. If the cursor predates retained history, return CURSOR_EXPIRED and a documented snapshot/recovery path. Duplicates across reconnect are allowed. Sequence meaning is stable. Provider text is untrusted data and never interpreted as control instructions. Storage failure may make evidence incomplete and blocks new work.

### Stop / archive / delete

`cancel` requests interruption of a turn (possibly uncertain). `stop` affects a managed process and fences the queue. `archive` locks a verified stopped conversation. `delete`/purge plans retain artifacts and do not remove workspace/branch. MCP cannot execute destructive purge. `workspace_cleanup` in v1 is plan-only, with user-executed Git cleanup outside the daemon.

### Permissions

Use daemon-issued delegation references and lease tokens to prevent accidental cross-session writes and reduce unauthorized control through the public API. The v1 threat model is trusted same-OS-user local clients. Per-agent labels and leases are collision safeguards, **not cryptographic isolation between processes owned by the same OS user**. Agent-generated claims of owner approval carry no authority. Access to the local socket by an already-compromised same-user process remains a residual risk.

## 6. Required security behavior

- MCP calls cannot invoke publishing, merging, releasing, repo deletion, arbitrary shell execution, or OS privilege escalation as orchestrator service operations.
- Workspace profiles expose precise enforcement: `enforced` only with a real OS sandbox or equivalent. Otherwise `best_effort` or `unavailable`. A worktree alone never counts as `enforced`.
- Runtime cannot prevent a native coding agent from independently using credentials already available to its own OS process. Strong isolation requires a platform sandbox and restricted credentials. V1 does not claim full hostile-code isolation.
- Default deny for provider client-side file, terminal and tool/permission callbacks. Permission escalation must not be approved through sibling or self-referential agent messages. When no independently trusted approval channel exists, reject.
- Treat model-generated text, workspace files, and tool outputs as untrusted for the daemon. Paths, arguments and API state transitions must be validated independently.
- Protect SQLite, socket/pipe, transcripts, and audit data with per-user OS permissions. Transcripts may contain sensitive content. Support redaction, retention policy, and deletion with clear limitations.
- Support an emergency stop command that immediately fences admissions, persistently blocks new sessions, and attempts graceful/forced shutdown of managed workers. If durable state cannot be written, fail closed and report diagnostic limits.

## 7. Exclusions

Not v1: web dashboard, remote agents, multi-user authorization, provider API model gateway, hosted orchestration, generic DAG/scheduler and full software factory. Also not v1: arbitrary UI automation, autonomous GitHub administration, autonomous merges/releases, automatic provider credential rotation and automatic tool approval. Also not v1: portable live-state migration between different providers, and unconditional attachment to unmanaged TUI sessions.

## 8. Quality requirements and proposed acceptance thresholds

All thresholds below are proposed engineering requirements, not measurements or performance promises.

- QR-001: No local session/operation ID collision across restarts. Concurrent duplicate requests produce one durable local intent, and unknown native effects never trigger blind retry.
- QR-002: Exactly one daemon per OS user per configured instance, with recoverable stale-lock detection.
- QR-003: Event journal ordering remains monotonic under 20 concurrent simulated agents.
- QR-004: At least 10 simultaneous sessions in a mocked-process stress test without unbounded queues or lost required events.
- QR-005: Real-agent smoke tests for all four worker providers where supported by the platform. The four-agent all-to-all lead matrix must pass before claiming universal lead compatibility.
- QR-006: Crash and restart of MCP bridge, lead, worker and daemon each produce a documented state and evidence-completeness flag. Only bridge/lead disconnect guarantees worker continuity. Daemon crash is OS-dependent.
- QR-007: Explicit NACK or confirmed failure when steer is unsupported. No fake success.
- QR-008: Cleanup-plan-only tests with tracked, untracked, ignored, uncommitted, symlink/junction, path swap, Git LFS and submodule cases. No daemon workspace recursive delete path.
- QR-009: No unredacted credentials in logs or error messages in a seeded-secret fixture test. Never promise exhaustive secret detection.
- QR-010: Complete OS-native CI for Linux x64, Windows x64, macOS x64/arm64. Linux arm64 via a suitable native runner or documented release validation. Additional RIDs only after representative smoke tests.
- QR-011: All public commands have versioned schemas, failure codes, docs and automated contract tests.
- QR-012: No warnings on build. Analyzers, formatting, unit, integration and protocol-conformance suites pass. Fuzz malformed JSON-RPC, paths and provider event payloads.
- QR-013: Adversarial lease/stop/cancel/dispatch ordering model tests prove no stale queued command crosses a lease or session generation boundary.
- QR-014: Simulated crash points before dispatch, after provider acceptance and before event persist never cause automatic duplicate native prompts.
- QR-015: Missing disk, full disk, corrupt SQLite, stalled consumers and oversized frames do not cause claimed successful durable writes or unbounded memory.
- QR-016: ACP permission callbacks are denied by default, without accidentally executing terminal/file operations under daemon credentials.

## 9. Versioning and compatibility policy

- SemVer for public CLI commands, MCP tool input/output contracts, and daemon IPC schema.
- Major protocol/SDK updates receive compatibility tests against target provider binaries.
- Pin NuGet dependencies and `global.json` SDK. Renovate only through reviewed changes.
- Persist `schema_version` on the database and `wire_version` in every IPC handshake.
- Migrations forward-only with backup and downgrade refusal unless tested, preserving recoverability.
- Document separate support matrices for host (can invoke MCP tools) and worker (can be controlled), per OS and version.

## 10. Proposed default limits (configurable)

- 4 simultaneous worker sessions per local instance.
- 2 direct children per orchestrating session, maximum delegation depth 2.
- 1 active turn per worker session, unless adapter proves safe concurrent turns.
- 60-minute wall-clock limit per turn and 3-minute graceful stop deadline, then forced termination where supervisor supports it.
- 2 automatic retries only for operations proven idempotent and safe. Otherwise mark uncertain and require reconciliation.
- No default automatic monetary spend limit claim. If usage data unavailable, enforce local turn/time/session caps instead.
- Persist mandatory lifecycle/audit history with a documented retention floor. Content payloads have separate configurable retention and size ceilings. Deleted/expired ranges produce CURSOR_EXPIRED. Never claim complete transcripts after redaction or storage failure.

Defaults are proposals and should be tested, not presented as universal optimal values.

## 11. Release gating

v1 is releasable only if all P0 safety invariants and tests pass. These include dispatch fencing, uncertain delivery, default-denied permission callbacks, plan-only workspace cleanup, and crash recovery classification. Provider-dependent operations must be labelled conditional and tested where advertised. All required OS/protocol gates must have evidence. The service is NOT a secure sandbox without platform-specific enforcement tests. No agent-supplied owner approval counts.

## 12. Compatibility with existing projects

- DKSTACK remains the authority for skills, safe delegation, independent review and owner-only actions. See https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/workflow/subagents.md and https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/workflow/safety.md.
- Orchestrator enforces only its own API permissions and process/workspace protections. It must not silently override DKSTACK policy.
- Factory may later use this runtime as an execution dependency. Deterministic checks, workflow scheduling, classifying fixes/features, merge gates and releases belong to Factory, not this service. See https://github.com/daniel-kindl/factory/blob/main/vision.md.

## 13. Open implementation gates, not open product scope

1. Choose an ACP v1 .NET library only after protocol conformance and live-agent tests. Otherwise implement a minimal protocol client with System.Text.Json.
2. Validate exact Claude Code structured CLI options. On each supported CLI version, validate whether streaming stdin supports prompt delivery during an active turn.
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
