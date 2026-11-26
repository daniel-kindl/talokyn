# AGENTS.md

Read and follow the guidelines in dkstack.

> Read and follow https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md

Start with the [triage skill](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/skills/triage/SKILL.md). It maps tasks to the relevant sections and skills.

dkstack owns all other rules. Record project exceptions and project facts below.

## Exceptions

A project can differ from a dkstack rule only through an exception here. See
[Precedence](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md#precedence).
The owner approved each exception below in an owner-controlled chat session on 2026-11-26.

- dkstack rule: [setup-quality-gates](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/skills/setup-quality-gates/SKILL.md) step 4, copy the hash list `dkstack-hashes.json` from the same tag as the scripts.
- Project rule: the scripts and `scripts/dkstack-hashes.json` are copied unchanged from the tag `v1.7.0`. The consumer check reads `scripts/dkstack-hashes.v1.7.0.json`. That file holds the output of `node scripts/check-consumer.mjs --print-hashes` on the scripts of the tag `v1.7.0`.
- Reason: the hash list of `v1.7.0` has no entry for `v1.7.0`. Without the project list, the check cannot pass on the newest release. Remove the project list when a dkstack release ships an entry for its own tag.

- dkstack rule: [setup-repo-protection](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/skills/setup-repo-protection/SKILL.md), the `CODEOWNERS` entry that requires the owner's review for the CI workflow files.
- Project rule: no required approval. The owner reads the diff of each CI workflow change, writes the review in the pull request and merges it.
- Reason: the owner and the agents use one account. The code host does not let it approve its own pull request.

## Project facts

- Name: Talokyn. Repository: `daniel-kindl/talokyn`.
- Stage: initial bootstrap. No operational agent orchestrator yet.
- Stack: C# 15, .NET 11. Cross-platform Linux, Windows and macOS.
- Current specification: [docs/design/SPEC.md](docs/design/SPEC.md). Architecture [docs/design/ARCHITECTURE.md](docs/design/ARCHITECTURE.md). Verification [docs/design/VERIFICATION.md](docs/design/VERIFICATION.md).
- Development: `dotnet restore Talokyn.slnx`, `dotnet build Talokyn.slnx --no-restore -warnaserror`, `dotnet format Talokyn.slnx --verify-no-changes --no-restore`, `dotnet run --project tests/Talokyn.SmokeTests/Talokyn.SmokeTests.csproj --no-restore`, `dotnet run --project tests/Talokyn.ContractTests/Talokyn.ContractTests.csproj --no-restore`.
- Checks: [CONTRIBUTING.md](CONTRIBUTING.md) lists the documentation checks. The scripts in `scripts/` come from the dkstack tag `v1.7.0`. Do not change them. To update them, copy them again from a newer tag.
- Commits and branches: [version-control](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/version-control/index.md). Versions: [versioning](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/versioning/index.md). The version is in `Directory.Build.props`.
- Evidence: reference-model tests and benchmarks in the audit package are **not** proof of a working .NET implementation.
- Keep any privileged process control and real-provider adapters behind explicit acceptance gates. Do not claim security guarantees that OS isolation cannot enforce.
