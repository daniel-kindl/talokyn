# Contributing

Talokyn follows [DKSTACK](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md), including [issues](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/workflow/issues.md), [review and merge](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/workflow/review-and-merge.md), [quality gates](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/coding/quality-gates.md), and [versioning](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/versioning/index.md).

1. Create or reuse a GitHub issue with acceptance criteria.
2. Make one branch and pull request per logical change.
3. Implement observable behavior test-first; keep compiler warnings at zero.
4. Run the full build, format, documentation and smoke checks before proposing review.
5. Obtain independent review of the exact head. The author never reviews or merges their own PR.

```sh
dotnet restore Talokyn.slnx
dotnet build Talokyn.slnx --no-restore -warnaserror
dotnet format Talokyn.slnx --verify-no-changes --no-restore
dotnet run --project tests/Talokyn.SmokeTests/Talokyn.SmokeTests.csproj --no-restore
node scripts/check-consumer.mjs --tag v1.6.0
node scripts/check-maps.mjs
node scripts/check-context.mjs
node scripts/check-dashes.mjs
node scripts/check-writing.mjs
node scripts/check-links.mjs
```

On pull requests, run `node scripts/check-adrs.mjs --base <base-ref>` and validate the PR title/body with the pinned scripts. The scripts originate unchanged from DKSTACK v1.6.0 (format checks) and v1.7.0 (consumer and PR helpers).

Until production code exists, no real-provider tests or security claims can be verified. Report each skipped check as **NOT RUN**. This project follows [SemVer](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/versioning/index.md); versions start at 0.1.0 and no release is authorized by this bootstrap.
