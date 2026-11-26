# Agent Orchestrator v1 - Product Specification

Status: proposed design, not implementation-approved
Date: 2026-11-26
Name: Agent Orchestrator (working title)
Owner: repository owner; no repository has been selected for this new project

## 1. Mission

Provide a local, vendor-neutral session control plane for coding agents. Any supported MCP-capable coding agent may be a lead and may start, observe, message, steer where supported, cancel, stop, resume, archive, and safely dispose of managed worker sessions. Agents can change roles between tasks. The service owns sessions independently of its MCP clients.

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
| Security | Local trusted-user boundary, runtime authorization and explicit approval boundaries |
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

- FR-010 P0: Start a managed session in an explicit workspace with provider, role, prompt, permission profile, limits, task ID, optional parent session, and unique idempotency key.
- FR-011 P0: Return a durable session ID immediately; initialization and turn execution are asynchronous operations.
- FR-012 P0: List and inspect sessions including provider/native ID, owner, parent/task, current turn, status, workspace, timestamps, capabilities, budget state, and recovery status.
- FR-013 P0: Read events/transcript from a monotonic cursor, with bounded pages and gap/error reporting; consumers can retry safely.
- FR-014 P0: Send a follow-up prompt to an idle session, or explicitly queue it for an in-progress turn; return operation ID and delivery semantics.
- FR-015 P0: Provide separate `steer` command for in-flight turns only when adapter advertises it; otherwise return UNSUPPORTED, never silently translate to `send`.
- FR-016 P0: Cancel the active turn without deleting the session, including graceful request, escalation timeout, and recorded result.
- FR-017 P0: Stop/unload a managed session while preserving its data and workspace; differentiate cooperative shutdown from forced termination.
- FR-018 P0: Resume from native provider state if available; otherwise report NOT_RESUMABLE and preserve event history and artifacts. Never claim replayed text equals restored model state.
- FR-019 P0: Archive session metadata and history; session becomes read-only and no longer consumes a live worker process.
- FR-020 P0: Explicitly delete eligible archived/stopped session records after showing the deletion plan. Do not delete provider-native history, branches, worktrees, or unmerged data by default.
- FR-021 P0: Transfer control via exclusive, expiring control lease; enforce an optimistic concurrency version for mutating operations.
- FR-022 P0: Parent/orchestrator disconnection must not automatically cancel child sessions.
- FR-023 P0: Explicit parent/child delegation limits with cycle detection, depth limit, and budget inheritance. Any provider can supervise itself only in distinct sessions within limits.

### Async messaging and event delivery

- FR-030 P0: Journal session/turn/operation lifecycle events durably in defined order.
- FR-031 P0: Distinguish message states: queued, submitted, provider-accepted, provider-confirmed-applied (only when evidenced), rejected, and unknown.
- FR-032 P0: Preserve typed event envelopes (status, text delta, tool call, permission request, usage, warning, artifact, error) and optional provider-specific details.
- FR-033 P0: Preserve source event IDs/correlation IDs; event subscribers deduplicate by session sequence.
- FR-034 P0: Bound memory and event payloads. Large output is spooled to managed files with checksums, not silently discarded.
- FR-035 P0: Return typed, nonambiguous errors: UNSUPPORTED, NOT_AUTHORIZED, CONFLICT, NOT_FOUND, PROVIDER_UNAVAILABLE, AUTH_REQUIRED, TIMEOUT, UNCERTAIN_DELIVERY, RECOVERY_REQUIRED, INVALID_STATE, RESOURCE_LIMIT.

### Workspaces and results

- FR-040 P0: Mutating worker sessions run in uniquely named managed Git worktrees derived from a recorded base commit, or refuse when isolation cannot be established.
- FR-041 P0: Read-only/reviewer tasks use a fixed source commit. Report isolation enforcement level: enforced, best-effort, or unavailable.
- FR-042 P0: Do not copy uncommitted changes from the user's checkout by default; explain when input state differs from the snapshot.
- FR-043 P0: Collect branch, base SHA, head SHA, dirty status, changed files, diff references, and optional test evidence as artifacts. Do not merge or push automatically.
- FR-044 P0: Never remove unknown/untracked/uncommitted files or branches with unmerged work during cleanup. Require explicit, confirmed decisions for destructive cleanup.
- FR-045 P0: Symlink and path traversal checks for managed workspace roots, including path canonicalization before spawn/delete.

### Policies and observability

- FR-050 P0: Per-user and per-task concurrency, max depth, turns, wall-clock timeout, and retry budgets with safe defaults; hard-enforce local resource limits.
- FR-051 P0: Report provider tokens/cost only when available, including provenance. No fabricated estimates and no claims of hard monetary enforcement when provider accounting is unavailable.
- FR-052 P0: Native agent logins remain provider-owned; do not copy, read, or print provider authentication secrets.
- FR-053 P0: Structured audit entries for initiator, target, command, allow/deny, session ID, workspace, timestamps and operation result.
- FR-054 P0: Local-only, OS-user-restricted IPC by default; no open TCP listener.
- FR-055 P0: Human-only actions must not become authorized because an agent message says the owner approved them.
- FR-056 P0: `doctor` command validates install, runtime, protocol handshakes, permissions, and platform safety features without submitting a paid prompt unless explicitly asked.

### Client surfaces

- FR-060 P0: Short-lived stdio MCP bridge connects to persistent daemon. All tools have versioned JSON schemas and stable error codes.
- FR-061 P0: CLI supports all lifecycle commands and can display event tail, inspect leases, and confirm human-only destructive actions.
- FR-062 P0: MCP bridge crashes/disconnects do not terminate managed workers or lose persisted events.
- FR-063 P0: Host registration documents MCP configuration for all four target agent clients where supported; incompatible clients can use CLI.

## 5. Contract-level semantics

### Session creation

Input: `provider`, `workspace`, `role`, `initialPrompt`, `taskId?`, `parentSessionId?`, `model?`, `permissionProfile`, `executionLimits`, `clientRequestId`.

Output: `sessionId`, `operationId`, `state=created|starting`, `capabilities`, `workspaceId`, `requestedAt`, `version`.

`clientRequestId` must be unique within an actor's scope. Repeating it with identical content returns the same result; repeating it with different content is CONFLICT.

### Follow-up / steering

`session_send(sessionId, message, when=now_if_idle|queue_after_turn, clientRequestId)` returns an operation ID. Queued operations are not claimed as read by the model. `session_steer(sessionId, activeTurnId, message, clientRequestId)` rejects unsupported or stale turns; a successful provider response means accepted by provider, not necessarily already incorporated into reasoning.

### Reading events

`session_events(sessionId, afterSequence, limit)` returns ordered entries and `nextSequence`. A client may receive an entry more than once across reconnects but never observes incompatible meaning for the same `sessionId` and `sequence`. Raw provider output is treated as untrusted data, not as orchestrator instructions.

### Stop / archive / delete

`cancel` affects a turn; `stop` affects a running process; `archive` locks a completed/stopped conversation while retaining history; `delete` removes selected local records. Workspace/branch removal is a separate `workspace_cleanup` plan and explicit human-confirmed operation, not an effect of session deletion.

### Permissions

Use capability-based authorization to decide which actor may inspect or control a session. The v1 threat model is trusted same-OS-user local clients; per-agent identity labels and control leases are collision safeguards, **not cryptographic isolation between processes owned by the same OS user**. Never represent them as such.

## 6. Required security behavior

- MCP calls cannot invoke publishing, merging, releasing, repo deletion, arbitrary shell execution, or OS privilege escalation as orchestrator service operations.
- Workspace profiles expose precise enforcement: `enforced` only with a real OS sandbox or equivalent; otherwise `best_effort` or `unavailable`. A worktree alone never counts as `enforced`.
- Runtime cannot prevent a native coding agent from independently using credentials already available to its own OS process. Strong isolation requires a platform sandbox and restricted credentials; v1 does not claim full hostile-code isolation.
- No default automatic tool approval. Provider permission prompts must be surfaced or denied; forbidden owner-only actions cannot be approved by another agent.
- Treat model-generated text, workspace files, and tool outputs as untrusted for the daemon. Paths, arguments and API state transitions must be validated independently.
- Protect SQLite, socket/pipe, transcripts, and audit data with per-user OS permissions. Transcripts may contain sensitive content; support redaction, retention policy, and deletion with clear limitations.
- Support an emergency stop command that blocks new sessions and attempts graceful/forced shutdown of managed workers while preserving artifacts and durable status.

## 7. Exclusions

Not v1: web dashboard, remote agents, multi-user authorization, provider API model gateway, hosted orchestration, generic DAG/scheduler, full software factory, arbitrary UI automation, autonomous GitHub administration, autonomous merges/releases, automatic provider credential rotation, automatic tool approval, portable live-state migration between different providers, and unconditional attachment to unmanaged TUI sessions.

## 8. Quality requirements and proposed acceptance thresholds

All thresholds below are proposed engineering requirements, not measurements or performance promises.

- QR-001: No session/operation ID collision across restarts; idempotency concurrency tests pass.
- QR-002: Exactly one daemon per OS user per configured instance, with recoverable stale-lock detection.
- QR-003: Event journal ordering remains monotonic under 20 concurrent simulated agents.
- QR-004: At least 10 simultaneous sessions in a mocked-process stress test without unbounded queues or lost required events.
- QR-005: Real-agent smoke tests for all four worker providers where supported by the platform. The four-agent all-to-all lead matrix must pass before claiming universal lead compatibility.
- QR-006: Crash and restart of MCP bridge, lead process, worker, and daemon each produce a documented recoverable/terminal state, not silent loss.
- QR-007: Explicit NACK or confirmed failure when steer is unsupported. No fake success.
- QR-008: Non-destructive cleanup tests with tracked, untracked, ignored and uncommitted files.
- QR-009: No unredacted credentials in logs or error messages in a seeded-secret fixture test; never promise exhaustive secret detection.
- QR-010: Complete OS-native CI for Linux x64, Windows x64, macOS x64/arm64; Linux arm64 via a suitable native runner or documented release validation. Additional RIDs only after representative smoke tests.
- QR-011: All public commands have versioned schemas, failure codes, docs and automated contract tests.
- QR-012: No warnings on build; analyzers, formatting, unit, integration and protocol-conformance suites pass. Fuzz malformed JSON-RPC, paths and provider event payloads.

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
- Persist event history until archived/deleted under the user's retention configuration; a sensible default may be decided after pilot sizing.

Defaults are proposals and should be tested, not presented as universal optimal values.

## 11. Release gating

v1 is releasable only if all P0 requirements are implemented or explicitly scoped as capability-conditional; core tests pass on targeted OSes; real agent interactions pass; hostile-input and crash tests pass; artifacts are preserved on cleanup; and the capability matrix truthfully describes unsupported operations. No v1 release should be described as a secure sandbox unless a platform-specific isolation test demonstrates it.

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
