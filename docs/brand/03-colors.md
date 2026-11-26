# Color system

The dark theme is the primary presentation. Most surfaces use neutral ink and slate. Teal is functional. Brass is a small identifying detail.

## Primary palette

| Name | Hex | Use |
| --- | --- | --- |
| Ink 950 | `#0D1418` | Canvas and avatar background |
| Ink 900 | `#162128` | Standard panels and cards |
| Ink 850 | `#1C2A31` | Elevated cards and active containers |
| Ink 750 | `#33434A` | Borders and dividers |
| Stone 50 | `#E9EDEC` | High-emphasis text |
| Stone 300 | `#B7C1C1` | Secondary text |
| Stone 450 | `#8C9E9F` | Muted text with verified contrast |
| Teal 500 | `#80ACA1` | Identity accent and chart highlight |
| Teal 400 | `#9CC1B8` | Filled primary action |
| Brass 500 | `#B8A689` | Small logo and editorial detail |

The complete palette appears in [tokens.json](../../design/brand/tokens.json).

## Accent allocation

Target roughly 85% neutral surfaces and typography. Reserve about 12% for teal and no more than 3% for brass.

These percentages are visual direction, not an automated threshold.

Do not apply the brass accent to critical status. Warning uses a separate semantic token.

## Semantic tokens

| Token | Dark role |
| --- | --- |
| `--tk-bg-canvas` | Page background |
| `--tk-bg-surface` | Cards and navigation |
| `--tk-bg-raised` | Elevated or selected surface |
| `--tk-fg-primary` | Main text |
| `--tk-fg-secondary` | Supporting text |
| `--tk-fg-muted` | Nonessential text |
| `--tk-border-default` | Standard separation |
| `--tk-action-primary-bg` | Primary button |
| `--tk-action-primary-fg` | Text on primary button |
| `--tk-focus-ring` | Keyboard focus |
| `--tk-status-*` | Operational states |
| `--tk-detail-brass` | Decorative identity detail |

Avoid coding literal hex values into components. Component rules should use semantic tokens.

## Measured examples

| Combination | Contrast |
| --- | ---: |
| Primary text / canvas | 15.74:1 |
| Secondary text / standard surface | 8.90:1 |
| Muted text / elevated surface | 5.27:1 |
| Action text / primary action | 9.50:1 |
| Light primary text / light canvas | 17.13:1 |
| Light action text / dark teal action | 6.29:1 |

These are calculated WCAG contrast ratios for the exact named hex pairs. They do not prove the contrast of every future component.

## States

- Success uses sage-green `#A3C7B0`.
- Warning uses soft ochre `#D6BC92`.
- Error uses muted rose `#DDA6A5`.
- Information uses desaturated blue `#B0C4D5`.

Use a text label or icon next to every status color. Do not communicate failures by a red dot alone.

For disabled controls, preserve readable descriptions and avoid unverified opacity shortcuts.

## Light theme

Light tokens are included for system integration and accessibility. They are not the primary brand expression.

The light theme uses warm off-white, neutral dark text and dark-teal actions.

Both themes must maintain equivalent information hierarchy.

## Accessibility

Normal text needs at least 4.5:1 contrast. Large text and meaningful UI graphics need at least 3:1.

Focus indicators must remain visible against the adjacent colors. Verify each new combination before using it.
