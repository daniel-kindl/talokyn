# Contributing

Talokyn follows [dkstack](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md). Read [AGENTS.md](AGENTS.md) for the project facts and the exceptions. The dkstack pages own the rules for [issues](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/workflow/issues.md), [review and merge](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/workflow/review-and-merge.md), [quality gates](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/coding/quality-gates.md), [commits and branches](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/version-control/index.md) and [versioning](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/versioning/index.md).

## Checks

Run all checks before you open a pull request. CI runs the same checks.

```sh
dotnet restore Talokyn.slnx
dotnet build Talokyn.slnx --no-restore -warnaserror
dotnet format Talokyn.slnx --verify-no-changes --no-restore
dotnet run --project tests/Talokyn.SmokeTests/Talokyn.SmokeTests.csproj --no-restore
node scripts/check-consumer.mjs --tag v1.7.0 --hashes scripts/dkstack-hashes.v1.7.0.json
node scripts/check-links.mjs
node scripts/check-dashes.mjs
node scripts/check-writing.mjs
node scripts/check-maps.mjs
node scripts/check-context.mjs
node scripts/check-adrs.mjs --base origin/main
node scripts/check-title.mjs -- "<pull request title>"
```

The scripts in `scripts/` come unchanged from the dkstack tag `v1.7.0`. To also check the dkstack URLs and their anchors, clone the dkstack branch `v1` outside this repository. Then run `node scripts/check-links.mjs . --dkstack-root <clone>`.

The project uses semantic versioning. The version starts at 0.1.0. No version is released yet.
