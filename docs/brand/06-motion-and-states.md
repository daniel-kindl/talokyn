# Motion, states and components

The brand supports quiet, predictable interaction. Motion communicates changes in state. It does not decorate idle surfaces.

## Motion tokens

| Token | Duration | Role |
| --- | ---: | --- |
| Fast | 120ms | Hover and pressed feedback |
| Normal | 200ms | Focus, selection and small transitions |
| Slow | 280ms | Dialog and panel transitions |
| None | 0ms | Reduced-motion override |

Use the standard cubic easing token for movement. Use the exit token for dismissals.

Respect `prefers-reduced-motion`. Do not run perpetual animated logo or background effects.

## Controls

| State | Visual treatment |
| --- | --- |
| Default | Neutral surface and clear label |
| Hover | Slight surface lift or border change |
| Pressed | Stronger surface change without scale bounce |
| Keyboard focus | 3px teal focus indicator |
| Selected | Raised neutral surface with text or icon |
| Disabled | Stable layout, explicit disabled semantics |
| Loading | Local skeleton or concise inline status |
| Error | Error icon, text and repair action |
| Success | Confirmation text and optional status symbol |

Never put destructive and safe actions in identical visual hierarchy.

## Skeleton loading

Prefer a skeleton matching the final content's shape. Use animation only when motion is allowed.

For short bounded operations, a stable status line is acceptable. Avoid replacing entire pages with a spinner.

## Tables and event feeds

Use compact rows, subtle dividers and tabular numeric values. Distinguish severity with labels and icons.

Make copy, pause and follow actions easy to find. Do not let row hover trigger a destructive command.

## Controls for agent orchestration

An approval is a deliberate user action. A session state is factual and auditable.

Use labels such as `running`, `paused`, `unknown`, and `failed` when the runtime supports them.

Never render a connection as online without verified evidence. In concept mockups, use the label **illustrative only**.

## Elevation

Flat layered surfaces should carry most hierarchy. Use one modest shadow token for floating panels.

A stronger shadow is reserved for dialogs. Avoid blurred halo glows around the guardian mark.
