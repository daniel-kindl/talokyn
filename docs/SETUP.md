# Maintainer setup

Follow [DKSTACK bootstrap](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/skills/bootstrap/SKILL.md). This repository is an independent DKSTACK consumer.

## GitHub repository settings (owner action required)

- Protect `main`: require PR, required `validate` status, linear history, no force pushes, no deletion or administrative bypass.
- Allow squash merge only; preserve independent review of exact head.
- Enable secret scanning with push protection, private vulnerability reporting, dependency alerts.
- If agents and owner use the same GitHub identity, document the limitation and ensure the owner performs the independent human review for workflow changes. Do not conflate account login with agent identity.
- Establish DKSTACK type labels (`type:bug`, `type:feature`, `type:chore`, `type:refactor`, `type:docs`, `type:research`) and priority labels (`priority:p0` through `priority:p3`) with owner permission.
- Ensure the Actions quota permits running checks; until then validate locally and report CI as NOT RUN.

Repository protection and label settings **have not been changed** by this bootstrap.
