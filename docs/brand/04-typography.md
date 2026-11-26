# Typography

Use two font families. Favor legibility and broad platform support over visual novelty.

## Families

| Role | Family | Weight |
| --- | --- | --- |
| Logo and headlines | Inter | 600 |
| UI labels | Inter | 500 |
| Body text | Inter | 400 |
| Numeric tables | IBM Plex Mono | 400 or 500 |
| CLI commands and code | IBM Plex Mono | 400 |
| Fallback | System UI / SF Mono / Consolas | Native |

Inter and IBM Plex Mono are open-licensed typefaces. Read their upstream license terms before redistribution.

- [Inter](https://github.com/rsms/inter)
- [IBM Plex](https://github.com/IBM/plex)

No font binaries are checked into the repository.

## Type scale

| Token | Size | Suggested usage |
| --- | --- | --- |
| `xs` | 12px | Captions and supporting metadata |
| `sm` | 14px | Secondary labels |
| `base` | 16px | Body and controls |
| `md` | 18px | Large body |
| `lg` | 20px | Section headings |
| `xl` | 24px | Page subheading |
| `2xl` | 32px | Page heading |
| `3xl` | 40px | Hero heading |
| `4xl` | 48px | Wide marketing hero |

The token values use rem units. Do not lock body text in pixels when user scaling matters.

## Line height

- Tight headings use 1.2.
- Body text uses 1.5.
- Long reading content may use 1.65.

Use 65 to 75 characters as a comfortable maximum line length for documentation. Narrow copy can use shorter lines.

## Letter spacing

Use normal tracking for paragraphs. Small uppercase eyebrow labels may use 0.11em tracking.

The wordmark uses about 0.085em tracking. Do not imitate it in normal application controls.

Avoid all caps for long descriptions and error messages.

## Numbers, logs and code

Use tabular numbers for uptime and durations. Use the mono family for CLI output, commands and code.

Do not render entire tables in mono unless alignment provides a clear benefit.

Prefer 14px or larger mono text in interfaces. Support user text scaling.

## Weight limits

Use only regular, medium, semibold and bold. Do not create hierarchy with extreme thinness or fake outlines.

Inter SemiBold in the brand lockup is intentional. Never substitute a futuristic display font.
