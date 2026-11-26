# Layout, geometry and iconography

## Grid

Use an underlying 4px spacing scale. Let content determine the layout before adding decoration.

| Context | Recommendation |
| --- | --- |
| Compact control | 8 to 12px internal spacing |
| Card | 16 to 24px padding |
| Section | 32 to 48px vertical spacing |
| Main page | 24px narrow and 48px wide padding |
| Content width | 72rem maximum for product UI |
| Reading column | 42rem maximum for documentation |
| Standard grid | 12 columns for wide interfaces |
| Mobile | Single column by default |

Use responsive behavior rather than stretching fixed cards into narrow layouts.

## Alignment

Use strong left alignment for text and data. Center branding only in simple introduction or loading contexts.

Cards should share an edge. Avoid staggered grids when they obscure scanning.

## Corners and strokes

- Small controls: 6px radius.
- Standard cards: 10px radius.
- Dialogs or large containers: 14px radius.
- Borders: 1px solid by default.
- Emphasis should primarily use a stronger border or tone.
- Avoid floating thick shadows and full pill cards.

The primary brandmark itself uses straight edges and no outline stroke.

## Icon system

Eight base SVG icons are supplied in [the icon folder](../../assets/brand/icons/agent.svg). Their visual language is consistent.

| Icon | Purpose |
| --- | --- |
| agent | Agent selection and provider inventory |
| session | Persistent session state |
| handoff | Delegation, message or control transfer |
| policy | Permission and approval states |
| workspace | Isolated workspaces |
| events | Events, logs and audit |
| process | Process supervision |
| isolation | Boundaries and containment |

Icons use a 24x24 grid, 1.75-unit strokes, rounded joins and no filled decorations by default.

At 16px, verify stroke consistency and optical alignment. Icons must never replace text for ambiguous actions.

## Motif

The [flow-line motif](../../assets/brand/patterns/flow-lines.svg) represents converging agent work.

Use it on wide banners, presentation covers and documentation section headers. Keep it subdued.

Do not place dense flow patterns behind body text. Do not animate the motif continuously.

## Imagery

Prefer authentic code, architecture diagrams and real interface captures. Use a documented concept label on pre-product mockups.

Do not use generic robot portraits, fake terminal activity or ungrounded metrics as marketing evidence.

## Responsive constraints

At 320px, show only essential controls. A wide brand lockup may become a mark plus a short title.

Long session identifiers should truncate with accessible full-text disclosure. Data tables should support safe horizontal scrolling.
