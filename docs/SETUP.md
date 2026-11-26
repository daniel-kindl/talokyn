# Maintainer setup

This repository is a dkstack consumer. It follows [bootstrap](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/skills/bootstrap/SKILL.md).

## GitHub settings

The owner approved these settings on 2026-11-26. They are applied:

- Ruleset `main` on the default branch: a pull request for each change and squash merge only. The checks `validate (ubuntu-latest)`, `validate (windows-latest)`, `validate (macos-latest)` and `docs` must pass. Linear history, no force-push, no deletion and no bypass.
- Required approvals: 0, and no code owner review. The owner and the agents use one account. See the exception in [AGENTS.md](../AGENTS.md#exceptions).
- Ruleset `release tags`: tags `v*` cannot be updated or deleted.
- Merge methods: squash only.
- Secret scanning with push protection, Dependabot security updates and private vulnerability reporting are on.
- Labels: `type:bug`, `type:feature`, `type:chore`, `type:refactor`, `type:docs`, `type:research` and `priority:p0` to `priority:p3`. The labels `bug`, `enhancement` and `documentation` became `type:bug`, `type:feature` and `type:docs`.

## Open items

- If the Actions quota stops a CI run, run the checks locally and report CI as NOT RUN.
