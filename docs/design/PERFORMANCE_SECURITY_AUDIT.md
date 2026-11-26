# Agent Orchestrator v1.2 - Performance and Security Audit

Date: 2026-11-26  
Status: design and executable reference-model audit. **There is no production .NET daemon.**  
Baseline: v1.1 red-team hardened SPEC, ARCHITECTURE, VERIFICATION.  
Deliverable: amended design requirements FR-077..FR-099, implementation constraints, 32 new release scenarios, reproducible reference benchmarks and tests.

## Verdict and evidence levels

**Design verdict: NOT READY TO SHIP.** It is now more prescriptive, but needs a real .NET implementation, OS integration, provider tests, native performance measurements and security checks. The inherited red-team limitations still apply.

Evidence legend:

- **Measured (Python/SQLite/Git)**: tests genuinely executed in a Linux ephemeral container. They prove only those tools/fixtures, **not** .NET runtime performance or security.
- **Confirmed upstream**: provider/runtime documentation explains behavior; not independently reproduced in .NET here.
- **Design risk**: plausible exploitable failure if an implementation violates the revised requirement; not an observed exploit in nonexistent production code.
- **Residual**: not solvable merely by changing the runtime contract.

### Finding summary and fixes

| ID | Sev | Finding | Evidence | Fix in revised design | Runtime status |
| --- | --- | --- | --- | --- | --- |
| P01 | P0 | Microsoft.Data.Sqlite `*Async` calls run synchronously: blocking shared async workers | Confirmed upstream | Dedicated synchronous bounded DB writer and short-lived reads; FR-079 | NOT IMPLEMENTED |
| P02 | P0 | SQLite WAL may grow indefinitely when readers pin snapshots | Measured busy checkpoint + upstream | Short read transactions, checkpoint health, disk quotas, fail-closed admission; FR-089 | NOT IMPLEMENTED |
| P03 | P0 | Choosing WAL `synchronous=NORMAL` for speed breaks power-loss durability of acknowledged intent | Measured local throughput, confirmed upstream | Force/verify FULL for command-intent and audit durability; batch high-volume output separately; FR-078 | NOT IMPLEMENTED |
| P04 | P0 | Queue saturation can starve STOP/CANCEL | Reference test + design risk | Separate priority/latching supervisor path, independent of ordinary RPC queue; FR-080 | NOT IMPLEMENTED |
| P05 | P0 | Count-only/unbounded event buffering enables memory/disk exhaustion | Unsafe reference reproduced | Per-session and global **byte and count** budgets, overload refusal, evidence gaps; FR-077/081 | NOT IMPLEMENTED |
| P06 | P1 | Reading stdout then stderr synchronously can deadlock provider pipes | Reference concurrent-drain test + .NET docs | Independently drain both streams with byte limits and termination path; FR-082 | NOT IMPLEMENTED |
| P07 | P1 | Front-removal array/list queue has quadratic draining cost | Measured | Use bounded `Channel<T>`/`Queue<T>` and fair per-session servicing; FR-091 | NOT IMPLEMENTED |
| P08 | P1 | OFFSET transcript pagination slows as history grows | Measured SQLite queries | `(session_id, sequence)` index and keyset page queries; FR-090 | NOT IMPLEMENTED |
| P09 | P1 | Replaying entire history on daemon startup scales with all lifetime events | Design risk | Indexed nonterminal recovery only; FR-090 | NOT IMPLEMENTED |
| P10 | P1 | One noisy event source can starve independent sessions | Design risk + bounded queue reference | Global byte budget, per-session quotas, fairness scheduler, metrics; FR-091 | NOT IMPLEMENTED |
| P11 | P1 | Many tiny text deltas, slow MCP readers and unbounded telemetry cardinality amplify memory | Design risk | Short cursor reads, bounded spool, optional controlled coalescing, low-cardinality metrics; FR-081/088 | NOT IMPLEMENTED |
| S01 | P0 | Git worktree creation runs hooks from repository config | **Real scratch Git proof** | Disable hooks/external helpers, isolate Git config; FR-086 | NOT IMPLEMENTED |
| S02 | P0 | Unbounded/duplicate-field JSON-RPC allows memory attacks and ambiguous authority fields | Unsafe Python JSON counterexample | Strict size/depth/UTF-8/duplicate-field parsing before dispatch; FR-077 | NOT IMPLEMENTED |
| S03 | P0 | Child process may inherit admin/GitHub/cloud credentials | Reference explicit-env fixture + design risk | Allowlisted environment, explicit provider-auth exceptions, no false same-user isolation; FR-084/097 | NOT IMPLEMENTED |
| S04 | P0 | Provider binary/argv via PATH or shell interpolation can execute unintended programs | Design risk | Trusted absolute executable + file identity, structured args, no shell; FR-083 | NOT IMPLEMENTED |
| S05 | P0 | IPC endpoint permission or name confusion can leak control to other users | Upstream + design risk | Owner-only socket/pipe, instance check, remote pipe denial, fail closed; FR-085 | NOT IMPLEMENTED |
| S06 | P0 | Model text can masquerade as administrative JSON-RPC, tool call or approval | Reference data-only test + design risk | Typed correlated provider events; text never directly becomes commands; FR-087 | NOT IMPLEMENTED |
| S07 | P0 | ACP client file/terminal/permission callbacks can act as confused deputy | Prior red-team finding | Default deny, no daemon-powered arbitrary tool; audit every grant; FR-067/097 | NOT IMPLEMENTED |
| S08 | P0 | Read-only/worktree separation not equivalent to sandbox or credential isolation | Residual + design risk | Enforced OS capability gate or explicit trusted-best-effort downgrade; FR-092/097 | NOT IMPLEMENTED |
| S09 | P1 | Sensitive raw transcripts and telemetry exports can leak secrets | Design risk | Structured logging allowlist, retention, opt-in export, ACLs, no remote export by default; FR-088 | NOT IMPLEMENTED |
| S10 | P1 | Git filters, LFS, submodules, includeIf can run further helpers under daemon account | Design risk | Scoped sanitized Git environment, explicit helper deny and tests; FR-086 | NOT IMPLEMENTED |
| S11 | P1 | Provider protocol malformed event can crash entire service | Design risk | Per-adapter quarantine and resource limits; FR-096 | NOT IMPLEMENTED |
| S12 | P1 | Dependency/provider executable replacement changes execution authority | Design risk | Lockfiles, version + binary identity, signatures and re-probing; FR-093 | NOT IMPLEMENTED |
| S13 | P1 | SQLite/logs/diagnostics writable by same OS user can be tampered with | Residual | Fail closed on detected corruption; no claim of append-only against same-user attacker; FR-088/097 | NOT IMPLEMENTED |
| S14 | P0 | Unscoped transcript/list/event APIs can disclose unrelated worker prompts | API contract audit | Every read/export/list requires observe grant, no enumeration outside scope; FR-098 | NOT IMPLEMENTED |
| S15 | P0 | STOP/CANCEL/RESUME/ARCHIVE signatures omitted explicit control authorization | API contract audit | Control grant plus current lease/epoch/version, separate emergency-stop path; FR-099 | NOT IMPLEMENTED |

Totals: **15 P0, 11 P1** (risk ratings for the design, not assigned CVEs). Unimplemented safeguards are not security fixes in a deployed application.

## Actual measured reference results

Run: `python benchmarks/benchmark_reference.py`. Environment and precise timings in `BENCHMARK_RESULTS.json`. Temporary local Linux x86-64 filesystem, Python 3.13.5 / SQLite 3.46.1. Ratios are environment-dependent.

| Workload | Unsafe/slower baseline | Corrected approach | Observed change |
| --- | ---: | ---: | ---: |
| Front-removal queue, 80,000 items | Python `list.pop(0)` 326.731 ms | `collections.deque.popleft` 1.914 ms | **170.7x faster** |
| 80 late-history transcript queries | OFFSET 116.024 ms | Keyset 2.393 ms | **48.5x faster** |
| 300 independently committed transactions | SQLite FULL 128.80 ms | NORMAL 2.26 ms | NORMAL faster here, **not acceptable** for durable intent |
| WAL checkpoint with active reader | `TRUNCATE` returned busy=1, WAL 45,352 bytes | Close reader and retry: busy=0, WAL 0 bytes | Reproduced reader-induced checkpoint stall |
| Git `worktree add` and `post-checkout` hook | Scratch hook executed | `-c core.hooksPath=<empty dir>`: no execution | Concrete attack/fix demonstration |

*Critical interpretation:* These are not C#/.NET benchmarks, cannot establish Native AOT RSS/cold-start, and do not prove a malicious provider is sandboxed. SQLite NORMAL's apparent speed is not a recommendation: WAL NORMAL may lose acknowledged transactions on power failure. FULL remains required for security-critical admission/outbox state. Provider text buffering and noncritical event batching must preserve truthful evidence reporting.

## Corrected operational model

1. Validate endpoint, request framing, declared size, JSON depth/duplicates, capabilities and workspace identity before expensive work.
2. Admit work only through an item- and byte-bounded queue; reserve limits and persist critical intent under SQLite WAL FULL before responding or dispatching.
3. Dispatch via a fair session scheduler; forbid unbounded per-session channels, caller-controlled shell args and ambient secret inheritance.
4. Independently read stdout and stderr, enforce frame/stream bytes, stream mandatory events to a bounded DB writer, and handle backpressure with explicit stop/pause and gap status.
5. Process stop/cancel through a separate supervisor lane even when normal event queues or provider calls are stuck.
6. Poll events with bounded indexed cursor queries, without holding SQLite readers open across network/IPC waits; monitor WAL size/checkpoint/busy.
7. Treat all native effects as potentially uncertain after transport loss; preserve previous red-team fencing/reconciliation rules.
8. Persist only permitted audit data, restrict raw transcripts, require opt-in external diagnostics/telemetry, and never infer human approval or security isolation from a caller label.

## Security boundary that remains impossible to guarantee in v1

- **Malicious agents running as the same OS user** can access the socket, databases, other user-owned files and credentials if those paths are readable; per-agent labels, MCP, leases or self-declared roles are not trustworthy authentication.
- **Provider-native agents may run tools outside ACP callbacks.** Denying ACP client callbacks alone is insufficient to sandbox the CLI.
- **Provider-native auth is necessary to make paid models run.** If credentials reside in a shared home folder, an unsandboxed worker may have broader access than requested. Narrow env inheritance reduces obvious leaks but does not solve home-directory access.
- **No sandbox portability by assertion.** Read-only enforcement and resource limits need distinct Linux/Windows/macOS tests. Unsupported OS constraints return `SECURITY_CAPABILITY_UNAVAILABLE`, not an imaginary secure worker.
- **Power loss vs daemon crash** are different. SQLite FULL offers stronger durability but cannot guarantee safety on failing/misconfigured storage, nor can it atomically commit a provider side effect.
- **Prompt injection into the supervising model** cannot be eliminated merely by wrapping tool output as structured data. The daemon can prevent direct execution of forged messages, not all bad decisions by an LLM.

## What was actually executed

- Existing 17 tests from prior red-team package; new byte-bound queue/JSON/SQLite/process/IPC reference contracts, including intentionally unsafe counterexamples, plus an actual scratch Git hook demonstration: `python -m unittest discover -s tests -v`.
- SQLite query, WAL FULL/NORMAL commit, checkpoint-stall, FIFO queue reference microbenchmarks using Python and a temporary local DB.
- Document consistency checks for links, IDs and required audit sections.
- **NOT RUN:** .NET builds (SDK not available in this environment), actual .NET unit or integration suites, ACP and provider CLIs, full OS sandboxes, Windows/macOS executions, fuzzing of a production parser, 30-minute/8-hour load tests, independent penetration testing.

## Next blocker / stop-go criteria

The design can be used for implementation planning, but **release remains blocked** until .NET runtime and native-provider conformance tests prove the listed safety and performance requirements on every advertised OS/RID. The first engineering experiment should build a minimal .NET IPC daemon with a bounded synchronous SQLite writer and mocked provider that emits adversarial stdout/stderr concurrently. It should prove STOP under saturation, security denial, and no duplicate external dispatch before adding the four real agents.

## Sources

- Microsoft.Data.Sqlite async limitations: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async
- Microsoft.Data.Sqlite error handling: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors
- SQLite WAL and checkpoint starvation: https://sqlite.org/wal.html
- SQLite durability: https://www.sqlite.org/pragma.html#pragma_synchronous
- .NET Channels backpressure: https://learn.microsoft.com/en-us/dotnet/core/extensions/channels
- .NET PipeOptions.CurrentUserOnly: https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions
- .NET 11 Unix named-pipe permission tightening: https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/namedpipeserverstream-unix-permissions
- ProcessStartInfo and untrusted input: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo
