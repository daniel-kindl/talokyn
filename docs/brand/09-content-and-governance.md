# Content, ownership and governance

## Name

Write **Talokyn** in prose. Use `talokyn` for commands, package names and configuration keys.

Use **TALOKYN** only inside the wordmark and short branded eyebrow labels.

## Product descriptor

Preferred descriptor: **Agent orchestration infrastructure**.

Short statement: **A control plane for coding agents.**

Long statement: Talokyn is designed to coordinate independent coding-agent sessions across tools and providers.

Do not claim that this design-only repository already operates autonomous workflows.

## Voice

Use clear technical language. Prefer one useful claim over a list of promotional adjectives.

Use active voice and concrete nouns. Explain limitations and unsupported operations directly.

Avoid "revolutionary", "sentient", "magical", "unstoppable" and similar language.

## Content patterns

| Context | Preferred |
| --- | --- |
| Intro | A control plane for coding agents. |
| Session detail | The provider did not acknowledge the operation. |
| Approval | Review this action before proceeding. |
| Missing capability | This provider does not support live steering. |
| Error | The agent connection closed before the request completed. |

All examples are proposed UX copy, not actual runtime behavior.

## Governance

Brand tokens live in [tokens.json](../../design/brand/tokens.json). Generate CSS rather than editing its values by hand.

SVG assets live under [assets/brand](../../assets/brand/README.md). Derivative formats must name the canonical source.

An approved logo geometry change updates every affected lockup, icon and guide in the same PR.

Create an issue for new identity components. Follow DKSTACK's branching, tests, review and merge process.

The owner approves the design direction. The author must not self-approve or self-merge.

## License and third-party materials

Talokyn artwork and SVG geometry are authored for this repository. Existing repository licensing applies to the committed files.

Inter and IBM Plex Mono are independently licensed. The repository links to their upstream sources and does not include font files.

Do not add external icon packs without documenting licensing and provenance.

## Versioning

Track design changes in the project changelog. A breaking change to public token names needs migration notes.

Keep old token aliases only when compatibility has a clear need.

Mark unapproved layouts as concepts. Remove mock metrics before public product screenshots.
