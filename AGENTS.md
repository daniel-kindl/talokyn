# AGENTS.md

Read and follow the DKSTACK guidance.

> Read and follow https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md

Start with the [triage skill](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/skills/triage/SKILL.md). It selects the rules and skills for the task.

DKSTACK owns shared rules. Talokyn supplies project-specific facts and decisions, not copies of DKSTACK instructions.

## Exceptions

None approved. Record any future exception with the DKSTACK rule, the project rule, the reason and owner approval. Safety rules have no exceptions.

## Project facts

- Name: Talokyn; repository: `daniel-kindl/talokyn`.
- Stage: initial bootstrap; no operational agent orchestrator yet.
- Stack: C# 15, .NET 11; cross-platform Linux, Windows and macOS.
- Current specification: [docs/design/SPEC.md](docs/design/SPEC.md); architecture [docs/design/ARCHITECTURE.md](docs/design/ARCHITECTURE.md); verification [docs/design/VERIFICATION.md](docs/design/VERIFICATION.md).
- Development: `dotnet restore Talokyn.slnx`, `dotnet build Talokyn.slnx --no-restore -warnaserror`, `dotnet format Talokyn.slnx --verify-no-changes --no-restore`, `dotnet run --project tests/Talokyn.SmokeTests/Talokyn.SmokeTests.csproj --no-restore`.
- Evidence: reference-model tests and benchmarks in the audit package are **not** proof of a working .NET implementation.
- Keep any privileged process control and real-provider adapters behind explicit acceptance gates. Do not claim security guarantees that OS isolation cannot enforce.
- Repository workflow: one issue, one branch, one PR; independent review before merge; no agent self-merges.
- Reference DKSTACK via its released `v1` branch, not `main`. Copied check format checks are pinned to `v1.6.0`; consumer and PR helpers originate from `v1.7.0` and must remain unchanged.

