# Talokyn design tokens

[Visual system](../../docs/brand/README.md) · [Asset index](../../assets/brand/README.md)

## Source of truth

`tokens.json` contains color primitives, semantic themes, type, space, radii, strokes, motion and shadow values.

`tokens.css` is generated from this JSON. Do not edit CSS tokens directly.

```sh
node tools/brand/generate-brand-tokens.mjs
node tools/brand/generate-brand-tokens.mjs --check
node tools/brand/check-brand.mjs
```

These commands run from the repository root. They require Node.js 24 or later.

## CSS integration

```css
@import "../design/brand/tokens.css";

.app {
  color: var(--tk-fg-primary);
  background: var(--tk-bg-canvas);
  font-family: var(--tk-font-family-sans);
}

.button-primary {
  color: var(--tk-action-primary-fg);
  background: var(--tk-action-primary-bg);
  border-radius: var(--tk-radius-sm);
  padding: var(--tk-space-3) var(--tk-space-4);
}
```

Dark is the default. To select the light theme, set `data-tk-theme="light"` on the root element.

Theme values resolve to primitive tokens. Components should use semantic aliases rather than raw color values.

## Cross-platform mapping

For Avalonia or other native UI clients, map the JSON roles to platform resources.

Do not parse CSS in native components. Generate native resource files when a native UI is introduced.

Maintain matching semantic roles across platforms. Do not assume identical shadows or native focus treatment.

## Breakpoints

Breakpoint values are layout policies, not immutable brand tokens. Select them based on the actual product content.

A responsive system must function at a 320px CSS viewport before treating the desktop layout as complete.

## Validation boundary

The included tests check selected contrast pairs and basic SVG safety. They do not validate all future UI states.

Any new semantic pair must add a contrast regression check.
