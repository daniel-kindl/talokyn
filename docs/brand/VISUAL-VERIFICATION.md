# Visual verification record

Status: pending full application-level verification.

## Vector assets

The source SVGs were built on a fixed grid with flat fills. Their dimensions and basic safety properties are checked by `scripts/check-brand.mjs`.

The brand is proposed, not approved. Final UI adoption requires rendered checks in the target product.

## Minimum manual review

| Surface | Required evidence |
| --- | --- |
| Brandmark | 16, 24, 32, 64 and 256px raster renders |
| Wordmark | Clearspace and fallback font render |
| README banner | Native size and scaled size on GitHub |
| Social image | 1200x630 render with safe content margins |
| Documentation header | Full width and narrow crop |
| App icon | Dark and light launcher surfaces |
| Product UI | Keyboard, screen reader, zoom and focus states |

## Results

- Contrast: selected dark and light token pairs documented in [color guide](03-colors.md).
- SVG XML: basic structural validation is included in the automated brand check.
- Full application UI verification: NOT RUN, because no application UI exists yet.
- Screen reader and keyboard walkthrough: NOT RUN, because assets have no interactive behavior.
- Forced colors: NOT RUN, pending a consuming UI.
- Owner review: PENDING.

Keep these gaps visible in the PR. Do not mark visual verification PASS until evidence exists.
