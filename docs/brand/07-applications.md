# Applications and assets

The identity must work in GitHub, a terminal, documentation and optional desktop tooling.

## GitHub README

Use the [1280x360 README banner](../../assets/brand/banners/readme-dark.svg) as the first branded image.

It uses a self-contained dark background. The README remains legible in either GitHub theme.

Show a short product statement beneath the banner. Then present current status, documentation and installation.

Avoid making the entire README an image. Search engines and assistive technology need real text.

## Social preview

Use the [1200x630 social card](../../assets/brand/banners/social-dark.svg). Keep the center-left logo and title unobstructed.

Do not add release version numbers until a release exists.

## Documentation

Use the [1440x280 documentation header](../../assets/brand/banners/docs-header-dark.svg). Prefer a smaller header within the actual docs layout.

The application should preserve a high-contrast reading column and visible navigation.

## CLI

Use plain text for routine commands. A colorized brand greeting is optional and must not affect machine-readable output.

Examples:

```text
talokyn --version
talokyn agent list
talokyn session list
talokyn session inspect <session-id>
```

These commands illustrate a proposed future interface. They are not implemented by the bootstrap.

Use ANSI color only when the terminal supports it. Respect `NO_COLOR` and redirection.

The brandmark is not required in terminal output.

## Application icon

Use [icon-dark.svg](../../assets/brand/logo/icon-dark.svg) for square launchers. Preserve its integral background tile.

Use the compact glyph at small sizes. Verify each raster export before release.

## Favicon and tray

The core mark is detailed. Prefer an explicitly validated compact glyph for 16px contexts.

At 16px, run visual inspection against dark and light browser chrome. Use the [compact favicon](../../assets/brand/logo/favicon.svg) with monochrome fallback.

## Brand asset inventory

Read the [asset index](../../assets/brand/README.md) for exact file names, dimensions and intended use.

## Export

Keep SVG as the canonical asset. Export PNG only for destinations that do not accept SVG.

Use 1x, 2x and 3x variants when the destination requests raster. Check dimensions and alpha handling after each export.

Do not rasterize a complete README as one image. Do not use an opaque logo bitmap as a source asset.

## Illustrative examples

Examples and moodboards are exploratory. They must never imply that Talokyn has shipping desktop screens, dashboards or live users.
