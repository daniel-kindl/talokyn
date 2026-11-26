# Agent Orchestrator v1 - Adversarial Design Review

Review date: 2026-11-26
Status: design-level red-team, revised documents generated; NO production runtime exists
Baseline: original SPEC.md, ARCHITECTURE.md, VERIFICATION.md in the previous design package
Output: revised SPEC.md, ARCHITECTURE.md, VERIFICATION.md and runnable reference contract tests

## Verdict

**The original v1 design was not implementation-ready.** Its main weakness was presenting local state, session control, workspace separation and human approvals as stronger guarantees than an external process orchestrator can provide. An implementation that followed the ambiguous parts could produce duplicate agent actions, stale steering, misleading completion states, unsafe cleanup, unearned read-only claims or unauthorized operations through provider tools.

The revised design is substantially safer **on paper**, not verified in production. All actual .NET integration tests, authenticated provider tests, platform-specific process tests, filesystem race tests and security penetration tests are still NOT RUN.

## Scope and method

1. Read all three original documents in full and compared their guarantees against each other.
2. Built failure traces around command admission, lease transfer, native RPC ambiguity, crash recovery, storage loss, permissions and filesystem races.
3. Checked ACP v1 session setup/cancel semantics, Codex App Server lifecycle, MCP C# SDK task negotiation, Windows Job Object behavior, SQLite WAL constraints and MCP trust assumptions against upstream documentation.
4. Wrote a minimal deliberately unsafe model for two recurring failures, then a fenced reference model. Ran Python standard-library unit tests against both. These tests are SPECIFICATION EXAMPLES, **not** .NET implementation tests.
5. Revised v1 contracts and verification gates to distinguish what is guaranteed, conditional, impossible to guarantee, and still needing real testing.

Severity: **P0** = release-blocking architectural correctness/security risk; **P1** = severe robustness/compatibility risk; **P2** = scope/documentation/operational risk.

## Critical findings (P0)

### F01 - Stale control lease causes unauthorized steering

**Attack:** controller A queues steer while its lease is valid. Lease expires; controller B acquires a new lease. Old A command is dispatched because the dispatcher only checks authorization at enqueue.

**Failure:** mutually inconsistent instructions can reach one agent; recorded lease owner and actual controller diverge.

**Fix:** monotonic `control_epoch` and `turn_generation`; validate current token, epoch, session version and turn generation both at admission and immediately before native dispatch under a per-session serialized mailbox. Queue items from older epochs become Fenced. An already dispatched call cannot be revoked, so its effect is reported honestly.

**Proof required:** ADV-001, ADV-002 and full short-interleaving exploration. Reference model includes the unsafe counterexample and corrected fencing.

### F02 - Lost acknowledgment leads to duplicate native actions

**Attack:** provider accepts `turn/start` or `session/new`; daemon loses response; client retries the same request; a second native turn/session is created.

**Failure:** duplicate edits, test runs, API calls or external side effects. Local idempotency keys alone cannot undo a provider request already executed.

**Fix:** persist intent before side effect; acknowledge local intent idempotently; represent native result as EffectUnknown after uncertain loss; never automatically replay a non-idempotent provider action. Query native state read-only where possible. Otherwise require operator reconciliation.

**Proof required:** ADV-003, ADV-004, ADV-022, fake provider counter exactly 1 after ambiguous response. The unsafe reference model demonstrably executes twice.

### F03 - Agent labels cannot prove who is human

**Attack:** an agent running under the owner's OS account uses the local CLI or MCP bridge and labels itself owner, reviewer or supervisor.

**Failure:** fake human approval or false independence. OS-user-restricted sockets are not per-agent authentication.

**Fix:** make trusted same-OS-user threat boundary explicit; use scopes/leases to prevent accidental collisions, not to claim cryptographic actor identity. No owner-only release, merge, deploy or settings-change operation in the service. Destructive metadata purge must be an explicit operator flow but is not strong human authentication. Genuine separation requires dedicated OS identities, credential isolation and externally enforced approvals. Refuse running the daemon as root/elevated administrator by default, or its IPC endpoint may become a privilege escalation surface.

**Proof required:** ADV-008, ADV-023, SEC-007. **Residual:** malicious same-user agents can bypass this runtime entirely; this cannot be fixed by a C# API contract alone.

### F04 - Provider callback can cross a privilege boundary

**Attack:** ACP worker asks its client for terminal execution, file writing, permission grant or MCP server configuration. Naive ACP client implements callbacks with daemon's own filesystem/process privileges or forwards an agent's approval.

**Failure:** confused deputy; worker gains powers not granted by its task.

**Fix:** explicit allowlist of ACP client methods, default deny for file/terminal/permission callbacks, capability and workspace checks, no arbitrary shell in daemon, no inheritance of full orchestrator MCP tools. External native agents can still access ambient OS credentials if unsandboxed.

**Proof required:** ADV-009, ADV-021 and SEC-004/005.

### F05 - Filesystem cleanup has a TOCTOU hole

**Attack:** check a canonical workspace path and Git status; another process swaps a directory junction/symlink/reparse point or adds untracked data before deletion.

**Failure:** cleanup may delete work not owned by the orchestrator; checking a path string once is not sufficient.

**Fix:** v1 daemon workspace cleanup is **plan-only**. Never recursively delete worktrees or branches. Manual Git cleanup remains separate; show managed marker, Git status and preservation warnings. Future automated deletion needs an independently reviewed OS-anchored algorithm and TOCTOU tests.

**Proof required:** ADV-011, SEC-001/003; no recursively deleting API in v1.

### F06 - A read-only reviewer is not actually read-only

**Attack:** reviewer runs in an ordinary writable Git worktree and can modify files, credentials or the local repository while returning a verdict about a supposed fixed snapshot.

**Failure:** contaminated review evidence, conflict of interest or accidental modification to the author workspace.

**Fix:** independent snapshot from exact commit SHA, distinct reviewer session and immutable source manifest. Report read-only as enforced ONLY if tested OS-level restrictions prohibit writes. Otherwise state advisory and decline to claim enforced isolation. Submodules/LFS/untracked content must be explicitly recorded.

**Proof required:** ADV-012, SEC-002/009. **Residual:** separate sessions do not prove an unbiased model.

### F07 - Daemon crash and process survival were conflated

**Attack:** kill daemon while a worker executes a tool. On Windows, a kill-on-close Job Object terminates its workers; on Unix, orphaned workers may remain, possibly detached.

**Failure:** 'session persists' can be misread as 'the executing process continues on every OS'. Attempting to kill by PID alone can hit a recycled unrelated process.

**Fix:** distinguish client/bridge disconnect, daemon death, OS reboot, worker death and provider conversation loss. Only client disconnect with healthy daemon guarantees live workers continue. Recover committed state; verify platform process identity; classify unobserved execution NeedsReconciliation and preserve artifacts.

**Proof required:** ADV-013/014; RES-001/002/003/004/006 on each OS.

### F08 - Storage failure can falsify audit durability

**Attack:** fill disk after provider emitted a tool event or while a large payload blob is being published. SQLite transactions cannot atomically commit external spool files and native provider execution.

**Failure:** event loss, dangling refs, false 'recorded successfully', unbounded buffering, or repeated execution when recovering.

**Fix:** transactionally record intent and reserved slots; stage large blobs with checksums/atomic publish and startup orphan reconciliation; prevent new dispatch if mandatory persistence fails; expose evidence-incomplete state. Never claim zero loss during daemon downtime or disk failure.

**Proof required:** ADV-016/017/022; RES-007/008/010.

### F09 - Session state model omitted many races

**Attack:** cancel vs successful completion, archive while process alive, stop while starting, resume with unresolved turn, or stop while queued follow-up exists.

**Failure:** session marked Stopped although worker alive; interrupted overwrites successful result; archived session still receives queued command.

**Fix:** separate conversation, process, turn and operation state machines. Derived session availability. Stop fences queue, cancel waits for evidence, archive refuses unknown work, resume requires reconciliation. Never infer turn success from process exit.

**Proof required:** ADV-005/006/007/019 and all state-machine property tests.

### F10 - Recursive delegation limits are spoofable

**Attack:** a managed agent submits a fake parentSessionId or opens a different MCP bridge to evade task depth or worker count caps.

**Failure:** runaway recursion, context loops and uncontrolled spend within the service.

**Fix:** daemon-issued scoped delegation references and atomic reservation counters tied to real parent/task lineage; increment accounting before native spawn. Same-user processes can launch providers directly outside the daemon, so quotas are not OS-wide security controls.

**Proof required:** ADV-020, ACC-012 and authorization tests.

### F11 - Owner-only protection is limited to orchestrator APIs

**Attack:** worker gets the user's GitHub admin token or shell access and directly runs `git`, `gh` or external deployment commands.

**Failure:** DKSTACK review/merge/release policy bypass without calling a forbidden orchestrator method.

**Fix:** state this unfixable limit under same-user unsandboxed execution. Avoid providing powerful credentials to workers; use OS separation/sandbox or GitHub App identity for true enforcement. No claim that 'API does not expose merge' proves that agents cannot merge.

**Proof required:** ADV-023 and explicit threat-model review. **Residual:** high until credential separation is deployed.

## High-priority findings (P1)

| ID | Broken assumption | Mitigation | Required test |
| --- | --- | --- | --- |
| F12 | ACP cancel means the active turn has stopped | Cancellation is a notification; await terminal observation or classify outcome unknown | ADV-006/019 |
| F13 | ACP resume/load are universal | Negotiate and record exact capabilities per binary/OS/session | ADV-010/019 |
| F14 | Resume inherits safe cwd/tool settings | Revalidate cwd, MCP servers, extra directories and permission profile | ADV-010 |
| F15 | Same sequence cursor always yields complete transcript | Return retainedFromSequence, CURSOR_EXPIRED and evidence-complete flag | ADV-018 |
| F16 | A bounded queue can absorb infinite output without loss | Disk quotas, spooling, backpressure and fail-closed policy on storage error | ADV-016 |
| F17 | Git source SHA describes all code under review | Manifest submodules/LFS/ignored files and generated dependencies | ADV-012 |
| F18 | MCP Tasks are portable across protocol versions | Tasks v2 only after negotiated 2026-07-28+; otherwise operation IDs | Protocol fixture |
| F19 | A platform's 'process tree' is always one killable group | Platform-specific Job Object/process-group/escape tests | ADV-013/014 |
| F20 | Hosted as worker implies hosted as MCP lead | Separate host and worker matrices, real tests, no inferred PASS | ADV-024 |
| F21 | Security can wait for hardening milestone | Move fencing, journaling, callbacks and stop-queue rules into M1 gate | M1 release gate |
| F22 | Low token usage equals low or enforced spend | Usage fields and price data may be absent; cap sessions/time, label cost unknown | Budget contract |
| F23 | Retention and delete can be both immutable and complete | Separate audit metadata from transcript content; acknowledge deletion limits | ADV-018 |
| F24 | Daemon can attach to arbitrary preexisting TUI sessions | Only managed sessions guaranteed; external attach provider/OS-specific | Compatibility matrix |

## Worked break/fix traces

### Trace A - lease TOCTOU

```text
Broken:
  A obtains epoch=7
  A queues steer for turn T
  lease expires; B obtains epoch=8
  dispatcher submits queued A steer to provider  <- violation

Repaired:
  A queues steer with epoch=7
  lease expires; B obtains epoch=8
  dispatcher rechecks epoch under session serialization
  queued A steer => Fenced; provider receives no call
```

The repaired behavior DOES NOT revoke a request that crossed provider dispatch before epoch change.

### Trace B - uncertain provider acceptance

```text
Broken:
  SQLite: accepted request X
  native provider: prompt X accepted
  transport ack lost
  daemon retries X -> provider sees two turns

Repaired:
  SQLite: accepted X, DispatchIntent
  provider possibly accepted X
  ack lost -> EffectUnknown
  reconcile read-only, or require operator decision
  no automatic replay of X
```

### Trace C - cancel/stop while messages queue

```text
Broken:
  turn T is running
  follow-up F is queued
  STOP sent; process exits
  queued F starts a new native turn on resume

Repaired:
  STOP increments session control epoch
  F -> Fenced
  native process exit / unknown state recorded separately
  resumed conversation does not auto-dispatch F
```

### Trace D - incorrect owner approval

```text
Broken:
  worker sends "owner approved release" to MCP callback
  runtime accepts text as authorization

Repaired:
  no release operation exists in orchestrator
  provider callback cannot grant owner-only authority
  true external release requires independent control outside runtime
```

## Changes to the documents

- **SPEC.md:** 13 added FRs (`FR-064`..`FR-076`), corrected FR-010..FR-063, delivery semantics, release gate, quality thresholds, trust boundary.
- **ARCHITECTURE.md:** orthogonal lifecycle model; fenced per-session dispatcher; delivery unknown; credential/permission callbacks; crash recovery; SQLite/blob protocol; plan-only workspace deletion; ACP/MCP negotiation specifics.
- **VERIFICATION.md:** 24 adversarial scenarios and revised existing acceptance tests; move safety essentials to M1; separate real host/worker tests; explicitly forbid inferred compatibility passes.
- **tests/test_reference_model.py:** deliberately unsafe scheduler and corrected reference scheduler; automated counterexamples and invariant tests. Test suite success means the DESIGN MODEL behaves as expected, not that .NET or provider executables are safe.

## Verification performed / not performed

| Check | Status |
| --- | --- |
| Original three-document inspection | Completed |
| Upstream protocol/OS documentation review | Completed |
| Modeled stale-lease failure and corrected fence | Demonstrated by reference tests |
| Modeled double-delivery after lost acknowledgment | Demonstrated by reference tests |
| Corrected reference-model invariant tests | Executed, see TEST_RESULTS.txt |
| Static consistency checks on revised docs | Executed, see TEST_RESULTS.txt |
| .NET compilation and runtime integration | NOT RUN - implementation not created |
| Real provider CLI integration | NOT RUN - no authenticated sessions under test |
| Native Windows/macOS/Linux process-race tests | NOT RUN - OS-specific supervisor not implemented |
| Workspace destruction/prompt injection penetration tests | NOT RUN - runtime not implemented |

## Residual risks and honest limits

1. **Same-user trust remains fundamental.** OS-user authentication does not distinguish individual agents. A worker with arbitrary access can use the user's credentials outside the daemon. This needs OS identity separation and credential isolation, not a lease tweak.
2. **Exactly-once native provider execution is impossible to promise generally.** Unknown outcomes sometimes require human resolution. This is a safer outcome than blind retry.
3. **Uninterrupted running turns across daemon crashes are impossible to promise generally.** Some OS process policies kill workers; others leave orphans. Durable conversation restore is provider-specific.
4. **A writable process is not made read-only by a worktree.** Enforced security depends on an actual tested sandbox; worktree only separates Git modifications.
5. **Four-vendor support and full OS parity remain unproven.** Ship only tested provider/host combinations, documenting unsupported/not-run cases, or delay the release.
6. **No performance or memory benchmarks exist.** The runtime is I/O-bound, but capacity and resource budgets require measurement.
7. **Plan-only deletion trades convenience for correctness.** Full automated workspace cleanup should be a separately designed future feature after filesystem race tests.

## Implementation stop/go criteria

Do not start the full adapter feature matrix until a small .NET proof demonstrates: (a) one durable daemon with a fake agent, (b) fenced dispatch on control change, (c) uncertain native effect handling, (d) no automatic workspace deletion, and (e) default-denied native client callbacks. Then validate Codex and Claude; only afterward add ACP providers.

Do not claim stable v1 until all P0 platform/provider cells have actual evidence. A capability known not to be supported is acceptable only when its corresponding behavior is clearly conditional and the public API returns UNSUPPORTED rather than a fabricated success.

## Independent source checks

- ACP v1 session setup: https://github.com/agentclientprotocol/agent-client-protocol/blob/main/docs/protocol/v1/session-setup.mdx
- ACP v1 overview/cancellation: https://github.com/agentclientprotocol/agent-client-protocol/blob/main/docs/protocol/v1/overview.mdx
- ACP test compatibility kit: https://github.com/agentclientprotocol/acp-tck
- Codex App Server: https://learn.chatgpt.com/docs/app-server
- MCP C# SDK versioning: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/versioning.md
- MCP C# SDK Tasks: https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/tasks/tasks.md
- MCP trust model: https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/SECURITY.md
- Windows Job Objects: https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects
- SQLite WAL caveats: https://sqlite.org/wal.html
