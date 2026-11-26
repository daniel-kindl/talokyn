# Accessibility and quality

## Baseline

Design to WCAG 2.2 AA. All meaningful controls require visible focus and programmatic names.

A branding package cannot independently prove full app accessibility. Verification must continue in each consuming application.

## Color

- Normal text must reach 4.5:1.
- Large text and essential non-text graphics must reach 3:1.
- Status icons need text, shape or another non-color cue.
- Focus must stand out from adjacent surfaces.
- Error notices need both a message and a recovery path.

[Color measurements](03-colors.md#measured-examples) record selected token pairs.

## Typography and scaling

Text must remain usable at 200% zoom. Page structure should not require horizontal scrolling at narrow viewport widths.

Maintain an understandable reading order. Provide accessible table labels for orchestration states.

## Asset checks

- SVG assets need a valid viewBox and accessible title.
- SVGs must contain no script, external image or external font URL.
- Logo marks must remain legible in monochrome.
- All assets require predictable aspect ratios.
- Decorative patterns must not carry critical information.

## Visual verification plan

Render and inspect the brand in both supported themes. At minimum, inspect 320px and wide compositions.

Review the icon at 16, 24, 32, 64 and 256px. Review banners at native size and half-size.

Check reduced motion, forced colors and screen readers when application UI exists.

## Automated checks

The [brand check](../../scripts/check-brand.mjs) validates token synchronization, contrast pairs and asset safety properties.

Run it using Node.js:

```sh
node scripts/check-brand.mjs
node scripts/generate-brand-tokens.mjs --check
```

The check is a safeguard, not a replacement for rendered inspection. More nuanced visual defects require a human review.

## Release gate

Do not approve the brand solely because token tests pass. The mark needs owner feedback and separate visual inspection.

Record any exception and its rationale. Do not weaken DKSTACK quality gates to approve aesthetic defects.
