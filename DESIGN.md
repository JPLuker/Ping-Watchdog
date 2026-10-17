# DESIGN.md — UI Philosophy & System ("Aurora Glass")

> **For any AI or developer building UI in this project:** read this entire file before writing a single line of UI code. This document overrides your defaults. When this file and your instincts disagree, this file wins. When this file is silent, follow the *spirit* in Part 1 and the decision procedure in Part 21.

---

## Table of contents

0. How to use this document
1. The philosophy in one breath
2. Core principles
3. What the reference images taught us
4. The layer model (depth)
5. Color
6. Materials (glass)
7. Shape & radius
8. Spacing & layout
9. Typography
10. Copy & microcopy — **the "say less" rule**
11. Iconography
12. Imagery & illustration
13. Motion
14. Components
15. Data & dashboards
16. Platform adaptation (web, desktop, mobile, games)
17. Accessibility floors (non-negotiable)
18. Performance budget
19. Banned defaults & anti-patterns
20. Token reference (copy-paste CSS)
21. AI build procedure & self-review checklist
22. Extending this document

---

## 0. How to use this document

**Precedence, highest first:**
1. Accessibility floors (Part 17). Never broken, not even for looks.
2. Explicit instructions from the person in the current task.
3. The copy rules (Part 10).
4. Tokens and component specs (Parts 5–9, 14, 20).
5. Principles (Part 2).
6. Your own judgment, constrained by everything above.

**Rules for AI agents:**
- Use the tokens in Part 20. Never invent a hex code, radius, shadow, or spacing value inline. If a value you need doesn't exist, use the nearest token and leave a `/* TOKEN-GAP: wanted X */` comment.
- Before finishing, run the self-review checklist in Part 21 and fix every failure.
- Do not explain the design in the UI itself. The interface is not a pitch deck.

---

## 1. The philosophy in one breath

**Calm, floating, and quiet. Surfaces of soft glass drift over a field of ambient color. One thing glows at a time. Every element is rounded, every word earns its place, and nothing decorates without doing a job.**

Shorthand: **Aurora Glass.**
- *Aurora*: soft, diffuse color glows (blue → violet → teal) living in the background, like light, not paint.
- *Glass*: frosted, translucent, rounded surfaces that float above that light.

The feeling to aim for: *a premium tool that respects your time.* Apple's Liquid Glass era, Google's Stitch/Gemini calm, and a dashboard that tells you the number, not a story about the number.

---

## 2. Core principles

Each principle has: **what it means → why → how it shows up → how it fails.**

### P1. One focal point per screen
- **Means:** every screen has exactly one thing that is visually loudest (a primary action, a hero number, a prompt bar).
- **Why:** the new Gemini UI beat the old one by removing competition for attention.
- **Shows up as:** one accent-colored element per view; everything else is neutral.
- **Fails when:** three cards are all purple, two buttons are both primary, or a gradient headline competes with a gradient card.

### P2. Float, don't fence
- **Means:** navigation and controls hover above content as pills and capsules rather than living in full-width bars with hard dividers.
- **Why:** this is the defining move in every reference: the Games app tab pill, the Stitch toolbars, iOS 26 controls.
- **Shows up as:** floating pill tab bars, detached toolbars, cards with gaps between them instead of borders.
- **Fails when:** content is chopped into grids of bordered boxes, or there are hard 1px lines everywhere.

### P3. Light behind, not on
- **Means:** gradients are ambient (blurred glows at the edges of the canvas), never fills on buttons, cards, or large text blocks.
- **Exception:** a gradient may be used on **one short display string per screen** (e.g. a greeting) or one hero card.
- **Fails when:** buttons have gradients, every card has a gradient, or gradients are hard-edged and saturated.

### P4. Rounded everything, concentric always
- **Means:** nothing has sharp corners. Nested shapes follow the concentric rule (Part 7).
- **Fails when:** an inner element has the same radius as its container and looks "pinched," or there's a mix of sharp and round.

### P5. Say less (see Part 10)
- **Means:** labels, not sentences. Data, not narration. No taglines inside the app.
- **Fails when:** the UI describes itself, reassures the user, or markets features to someone who already installed it.

### P6. Decoration must do a job
- **Means:** an icon, illustration, or glow exists only if it aids recognition, hierarchy, or state.
- **Fails when:** a big decorative heartbeat illustration fills half a hero card that a user will see 500 times.

### P7. Content is the color
- **Means:** when the content has imagery (game art, photos, album covers), let it supply the color and keep chrome neutral, like the Games app over the samurai art.
- **Fails when:** chrome competes with content imagery.

### P8. Calm by default, loud only for state
- **Means:** the resting state is quiet. Color intensity rises only for things that need attention: errors, live status, the primary action.
- **Fails when:** status badges, success colors, and accents all shout at the same volume.

### P9. Same soul, native body
- **Means:** the visual language stays consistent across web, desktop, and mobile, but each platform keeps its native conventions (window controls, back gestures, system fonts where appropriate). See Part 16.

### P10. Legibility beats vibes
- **Means:** if glass, gradient, or a translucent effect ever makes text hard to read, the effect loses. Always.

### P11. Not every piece of data needs to be everywhere
- **Means:** each piece of information has one home, the place where someone actually uses it. It appears elsewhere only if that other screen can't do its job without it.
- **Why:** repeating data across the header, sidebar, cards, and footer creates noise, makes every screen feel equally busy, and destroys the one-focal-point rule.
- **Shows up as:**
  - **One home per fact.** Version number → About. Host count → the Hosts screen. Account details → the avatar menu.
  - **Show what the task needs right now.** A list row shows name + status; details live on the detail view, a hover, or an expandable row.
  - **Progressive disclosure.** Summary first, detail on demand (click, expand, hover, drill-in). Advanced options sit behind "More" or in Settings.
  - **Summaries only when they drive a decision.** A dashboard shows what changed or what needs attention ("3 hosts down"), not an inventory of everything that exists.
  - **The repeat test:** if the same value is visible in two places on one screen, delete one.
- **Fails when:** the sidebar, header, and a stat card all show the same count; every row shows every column "just in case"; a dashboard restates what each module's own screen already shows; metadata (IDs, timestamps, paths) is visible by default when nobody needs it at a glance.

---

## 3. What the reference images taught us

| Reference | Take this | Leave this |
|---|---|---|
| Prodify dashboard (light, lavender) | Soft lavender accent, pill action chips next to the greeting, generous whitespace, colored progress bars as the only color in a list | Purple-gradient page background, tiny gray metadata text |
| Spectra dashboard (dark) | Dark surfaces stacked in 2–3 tones, a single periwinkle hero card, one orange "highlight bar" in a gray chart | Low-contrast gray table text |
| iOS 26 / Liquid Glass (light + dark) | Pill controls, frosted capsules, circular icon buttons, segmented controls as glass pills | White text on pale glass (contrast failure) |
| macOS Games app | Floating pill tab bar over full-bleed imagery, content-supplied color, neutral chrome | — |
| Google Stitch | Dark canvas with aurora glows bleeding from card edges, floating vertical toolbar, pill controls, a visible design-token sheet | — |
| Gemini old → new | Centered single prompt bar, chips beneath, sparse sidebar of icons, gradient used only on the greeting | The old cluttered sidebar of recents |
| Google Home landing page | Editorial layout for **marketing/website pages only**: big product image, short headline, lots of air | Do not use this flat style inside the app |

---

## 4. The layer model (depth)

Every screen is built from these layers, bottom to top. Never skip the order, and never put more than one glass layer directly on top of another.

| # | Layer | What lives here | Material |
|---|---|---|---|
| 0 | **Canvas** | The base background | Solid `--canvas` |
| 1 | **Ambient** | Aurora glows, or full-bleed content imagery | Blurred gradients / images, `pointer-events: none` |
| 2 | **Surface** | Main content panels, cards, lists, tables | Solid or near-solid `--surface` (readability first) |
| 3 | **Glass** | Floating navigation, toolbars, chips over imagery, sidebars over ambient | Frosted glass (Part 6) |
| 4 | **Overlay** | Menus, popovers, sheets, dialogs, toasts | Thick glass + shadow + optional dimming scrim |

**Rules**
- Dense content (tables, long text, forms) lives on **Surface**, not Glass.
- Glass is for things that **float**: navigation, toolbars, chips, overlays, small cards over imagery.
- Depth is shown with blur, translucency, and soft shadow, not borders. Borders are hairline, low-opacity highlights only.

---

## 5. Color

### 5.1 Palette roles

| Role | Purpose | Light | Dark |
|---|---|---|---|
| `canvas` | Page background | `#F4F5FA` | `#0B0D16` |
| `surface` | Cards, panels | `#FFFFFF` | `#151826` |
| `surface-2` | Nested / hover surfaces | `#EEF0F7` | `#1D2133` |
| `surface-3` | Pressed / selected neutral | `#E4E7F1` | `#262B40` |
| `text` | Primary text | `#14172B` | `#F2F3F8` |
| `text-2` | Secondary text | `#474D6B` | `#B8BCD0` |
| `text-3` | Tertiary / metadata (minimum allowed) | `#62688A` | `#9095AE` |
| `hairline` | Dividers, glass edge | `rgba(20,24,45,.08)` | `rgba(255,255,255,.08)` |
| `accent` | The ONE focal color | `#6B5CF0` | `#9C8EFF` |
| `accent-soft` | Accent backgrounds (selected pill, chip) | `#ECE9FF` | `rgba(156,142,255,.16)` |
| `on-accent` | Text on accent | `#FFFFFF` | `#0B0D16` |
| `teal` | Secondary data color, "healthy/online" | `#0F9F8F` | `#3DD9C4` |
| `signal` | Highlight a single data point (the "Thursday bar") | `#F2702E` | `#FF8E52` |
| `danger` | Errors, destructive, "down" | `#D93B3B` | `#FF6B6B` |
| `warning` | Degraded, pending | `#B7791F` | `#F5B547` |
| `success` | Confirmed, done | `#16935C` | `#4ADE95` |

> **Verify contrast with a checker before shipping.** These values are designed to pass WCAG AA in their intended pairings, but any new pairing must be tested (Part 17).

### 5.2 Usage ratio (per screen)
- **~75%** neutrals (canvas, surfaces, text)
- **~15%** ambient light (aurora glows, imagery)
- **~8%** accent: one primary action, the selected nav item, one focal element
- **~2%** signal/semantic colors, only where state demands it

### 5.3 The aurora recipe
Ambient glows are 2–4 large radial gradients, blurred, sitting at the **edges** of the canvas (never centered behind dense text).

- Colors: `#4F7BFF` (blue), `#8B5CF6` (violet), `#2DD4BF` (teal), optional `#F0ABFC` (pink) for warm light-mode moments.
- Opacity: **0.25–0.35 in light mode**, **0.35–0.55 in dark mode**.
- Size: 40–70% of viewport width each.
- They do not animate by default. Optional very slow drift (≥ 30s loop), disabled under reduced motion.

### 5.4 Gradient text
- Allowed on **one** short display string per screen (≤ 4 words), e.g. a greeting.
- Gradient: `accent → #4F7BFF` or `accent → teal`. Never more than 3 stops.
- Never on body text, buttons, labels, or numbers.

### 5.5 Light and dark
- Both modes are required from day one. Default to the OS setting; offer a manual toggle.
- Dark mode is **not** inverted light mode: surfaces get *lighter* as they rise (canvas darkest, overlays lightest). Accents get lighter and less saturated.
- Never pure black (`#000`) or pure white text on pure black; use the tokens.

---

## 6. Materials (glass)

### 6.1 Glass tiers

| Tier | Use | Light background | Dark background | Blur |
|---|---|---|---|---|
| `glass-thin` | Chips over imagery, small badges | `rgba(255,255,255,.45)` | `rgba(30,34,52,.45)` | 16px |
| `glass` | Nav pills, toolbars, sidebars | `rgba(255,255,255,.62)` | `rgba(26,30,46,.62)` | 24px |
| `glass-thick` | Menus, popovers, sheets, dialogs | `rgba(255,255,255,.80)` | `rgba(22,26,40,.82)` | 32px |

All tiers add:
- `backdrop-filter: blur(X) saturate(160%)`
- a **1px inner top highlight** (`inset 0 1px 0 rgba(255,255,255,.55)` light / `.10` dark) to fake the glass edge
- a **hairline border** using `--hairline`
- a soft drop shadow (Part 20 tokens)

### 6.2 The contrast floor for glass
Text on glass must meet contrast against the **worst-case** content that could scroll behind it. Practical rules:
- If imagery can pass behind it, use `glass` or thicker, never `glass-thin`, for any text.
- If contrast still fails, add a tint scrim, raise opacity, or move the content to a Surface.
- Never white text on light glass. Never rely on text-shadow to rescue contrast.

### 6.3 Honest limits
- On the web, `backdrop-filter` produces frosted blur. It does **not** produce Apple's true refraction/lensing. Don't fake refraction with distorting SVG filters in production UI; it's expensive and fragile.
- Native platforms should use native materials where available: iOS/macOS system materials, Windows 11 Mica/Acrylic.

### 6.4 Fallbacks (required)
- `@supports not (backdrop-filter: blur(1px))` → use opaque `--surface` with the same radius and shadow.
- `@media (prefers-reduced-transparency: reduce)` → opaque surfaces.
- Low-power / large-list contexts → opaque surfaces (Part 18).

---

## 7. Shape & radius

### 7.1 Scale

| Token | Value | Use |
|---|---|---|
| `--r-xs` | 6px | Tiny elements: checkboxes, small tags inside dense tables |
| `--r-sm` | 10px | Inputs inside cards, small buttons in dense UI |
| `--r-md` | 14px | List rows, menu items, inner cards |
| `--r-lg` | 20px | Standard cards |
| `--r-xl` | 28px | Large/hero cards, sheets, dialogs |
| `--r-2xl` | 36px | Window-scale containers, mobile sheets |
| `--r-pill` | 9999px | Buttons, chips, tabs, search bars, toggles, status pills |

### 7.2 Pill-first
Interactive controls default to **pill** shape: buttons, chips, segmented controls, tab bars, search and prompt inputs, status badges. Square-ish controls are the exception (dense tables, code editors).

### 7.3 The concentric rule
When a rounded element sits inside a rounded container:
**inner radius = outer radius − padding between them** (minimum `--r-xs`).

Example: card radius 20px, 8px padding → inner element radius 12px. This prevents the "pinched corner" look that AI-generated UIs almost always get wrong.

### 7.4 Circles
Icon-only buttons are perfect circles (equal width and height, `--r-pill`). Avatars are circles.

---

## 8. Spacing & layout

### 8.1 Base unit: 4px

| Token | px | Typical use |
|---|---|---|
| `--s-1` | 4 | Icon-to-label gap in tight places |
| `--s-2` | 8 | Gap between chips, inner pill padding (vertical) |
| `--s-3` | 12 | Gap inside list rows |
| `--s-4` | 16 | Default padding, mobile side gutter |
| `--s-5` | 20 | Card padding (compact) |
| `--s-6` | 24 | Card padding (default), gap between cards |
| `--s-8` | 32 | Section gap |
| `--s-10` | 40 | Large section gap |
| `--s-12` | 48 | Page top padding (desktop) |
| `--s-16` | 64 | Hero spacing |
| `--s-24` | 96 | Marketing-page section spacing |

**Never** use values off this scale (no 13px, 18px, 22px).

### 8.2 Density modes
- **Comfortable** (default): spacing as above.
- **Compact** (data-heavy screens, admin tools): drop one step (24 → 16, 16 → 12). Radii drop one step too.
- A screen uses one density. Don't mix.

### 8.3 Layout patterns
- **Focus layout** (assistants, prompts, start screens): one centered column, max width 720px, focal element at ~40% of viewport height, chips below.
- **Workspace layout** (tools, dashboards): sidebar (240–280px, collapsible to 72px icon rail) + main content. Content max width 1280px; let tables go wider.
- **Media layout** (galleries, game libraries): full-bleed hero, floating glass nav on top, horizontal shelves below.

### 8.4 Grid
- 12-column, 24px gutters on desktop; 4-column, 16px gutters on mobile.
- Cards in a row share the same height. Align to a baseline; don't let card heights wobble.

### 8.5 Breathing room
When in doubt, add space, not lines. Separate groups with whitespace first, surface change second, hairline divider last.

---

## 9. Typography

### 9.1 Families
- **Web & cross-platform:** `Geist` (fallbacks: `"SF Pro Text", "Segoe UI Variable", system-ui, sans-serif`). Free, neutral, close to the SF Pro feel.
- **Native desktop/mobile:** use the **system font** (SF Pro on Apple, Segoe UI Variable on Windows, Roboto/Google Sans on Android). Native apps should feel native.
- **Monospace:** `Geist Mono` (fallback `ui-monospace, "Cascadia Code", Menlo, monospace`) for IDs, IPs, hostnames, code, logs.
- Max **two** families per product (sans + mono).

### 9.2 Scale

| Token | Size / line-height | Weight | Use |
|---|---|---|---|
| `display` | 48 / 52 | 600 | One per screen, hero/greeting only |
| `title-1` | 32 / 38 | 600 | Page title |
| `title-2` | 24 / 30 | 600 | Section title |
| `title-3` | 20 / 26 | 600 | Card title |
| `body-lg` | 17 / 26 | 400 | Prominent body, prompt input |
| `body` | 15 / 22 | 400 | Default body (desktop & web) |
| `label` | 14 / 20 | 500 | Buttons, chips, nav items |
| `caption` | 13 / 18 | 400 | Metadata, timestamps |
| `micro` | 12 / 16 | 500 | Absolute minimum; badges only |

Mobile body is 16–17px. **Nothing below 12px, ever.**

### 9.3 Rules
- Weights in use: 400, 500, 600. Avoid 300 (too thin on glass) and 800+.
- Titles: tight tracking (−0.01 to −0.02em). Body: default tracking.
- **No ALL-CAPS eyebrow labels** ("START WITH MONITORING", "ACTIVE MODULES"). Use sentence case at `caption` or `label` size. All-caps is reserved for 2–4 letter badges (e.g. "NEW", "LIVE").
- Numbers in tables, stats, and timers use `font-variant-numeric: tabular-nums`.
- Truncate with ellipsis on one line for list items; give the full value in a tooltip or on hover/press.
- Sentence case for everything: buttons, titles, menu items.

---

## 10. Copy & microcopy — the "say less" rule

> **This is the most commonly violated rule in AI-built UI. Treat it as a hard constraint.**

The person has already installed, opened, or navigated to this screen. They do not need to be sold to, reassured, or told what the screen is for. **Inside an app, text exists to label, to report state, or to help someone act. Nothing else.**

### 10.1 The test for every string
Before writing any text, ask: **"If I delete this, does the user lose the ability to understand, decide, or act?"**
- **No → delete it.**
- **Yes → shorten it to the fewest words that keep that ability.**

### 10.2 Banned inside the app

| Banned pattern | Example of what AI writes | What to do instead |
|---|---|---|
| Taglines / slogans | "Your IT toolkit, all together." | Page title: "Modules" (or nothing, if the nav already says it) |
| Self-description | "A focused screen for each tool." | Delete |
| Reassurance filler | "Local tools · your data stays here" | Delete. If privacy matters, put it in Settings or About |
| Instruction for the obvious | "Choose a module. Keep your work running in one place." | Delete. The cards are the instruction |
| Feature marketing | "Hosts, sites, outage alerts and wallboards. All of X's features, together." | Delete, or one short line in a first-run empty state only |
| Brand slogans in chrome | "One Toolkit. Tools built around your work." | Delete. Version and build info go in About |
| Eyebrow labels | "START WITH MONITORING" above a heading | Delete; the heading is enough |
| Explanatory subtitles under stats | "25 — Across your saved sites" | "25 hosts" or label "Hosts" + value "25" |
| Status-as-sentence | "Monitoring is ready" | A green status dot + "Monitoring" or "Online" |
| Cute empty states | "Nothing here yet! Let's get started 🚀" | "No hosts" + one button: "Add host" |
| Exclamation marks, emoji in UI chrome | "Saved!" | "Saved" |
| Redundant action words | "Click here to open monitoring" | "Open monitoring" |

### 10.3 Where longer text IS allowed
- **Marketing/website pages** (outside the app): headlines and short supporting lines are fine there, in the Google Home editorial style.
- **First-run onboarding:** one screen, one sentence per step, dismissible, never shown again.
- **Errors:** say what happened + what to do, in ≤ 2 short sentences. "Couldn't reach 10.0.4.12. Check the address or network."
- **Destructive confirmations:** state the consequence plainly. "Delete 3 hosts? This can't be undone."
- **Settings descriptions:** one line, only when the label alone is ambiguous.
- **Tooltips/help:** on demand, not on screen by default.

### 10.4 Voice
- Plain, short, calm, specific. Verb first on buttons: "Add host", "Run scan", "Export".
- Numbers over adjectives: "3 down" beats "Some hosts are experiencing issues".
- No first-person app voice ("I", "we") inside the tool. No "your" unless it disambiguates.

### 10.5 Downloads, installs, and progress specifically
A download or install screen needs exactly: **name, progress, size/speed or time remaining, and a cancel/pause control.** No feature recap, no "Sit back while we…", no rotating tips unless the person asked for them.

---

## 11. Iconography

- **Set:** one outline icon family across the product (e.g. Lucide, Phosphor Regular, or SF Symbols on Apple-native). Never mix families.
- **Stroke:** 1.75px at 20–24px icon sizes. Rounded caps and joins (matches the rounded shape language).
- **Sizes:** 16 (inline), 20 (default UI), 24 (nav/toolbar), 28–32 (circular icon buttons).
- **Filled vs outline:** outline at rest; filled for the selected nav item only.
- **Color:** icons take `text-2` by default, `accent` only when selected or when they are the focal action.
- **Every icon-only button has an accessible label and a tooltip on desktop.**
- No decorative icons next to every heading. An icon must aid recognition (nav, actions, file types, status).

---

## 12. Imagery & illustration

- Real content imagery (game art, photos, covers, thumbnails) is welcome and should be large; let it supply color (P7).
- **Illustrations inside tools are discouraged.** Allowed only in: empty states (small, ≤ 120px, monochrome or accent-tinted line art), onboarding, and marketing pages.
- No decorative hero illustrations on screens the user visits repeatedly.
- No stock-photo people, no 3D blob mascots, no isometric "tech" scenes.
- Thumbnails/covers use `--r-md` or `--r-lg` and the concentric rule inside cards.

---

## 13. Motion

### 13.1 Character
Motion is **soft, quick, and physical**: things settle like they have a little mass. Nothing bounces cartoonishly, nothing drifts slowly when the user is waiting.

### 13.2 Tokens

| Token | Duration | Use |
|---|---|---|
| `--t-instant` | 80ms | Press feedback, color changes |
| `--t-fast` | 160ms | Hover, toggles, chip selection |
| `--t-base` | 240ms | Menus, popovers, tab indicator slide |
| `--t-slow` | 360ms | Sheets, dialogs, page transitions |

- **Standard easing:** `cubic-bezier(.2, .8, .2, 1)` (fast out, gentle settle).
- **Exit easing:** `cubic-bezier(.4, 0, 1, 1)`, and exits run ~20% faster than entrances.
- **Springs (native/JS):** stiffness ~300, damping ~30. Subtle overshoot only on the selected-tab pill.

### 13.3 What moves
- The selected-state pill **slides** between tab/segment items (signature move).
- Overlays **scale from 0.96 → 1 + fade**, originating from their trigger.
- Pressed buttons **scale to 0.97**.
- Lists insert/remove with height + fade.
- **Never animate:** layout on every data refresh, numbers counting up on every load, background aurora at speed, anything on a loop that the user didn't start.

### 13.4 Reduced motion
`prefers-reduced-motion: reduce` → replace movement with simple opacity fades ≤ 120ms; disable aurora drift and parallax entirely.

---

## 14. Components

For each: anatomy → rules → states. All states required: **rest, hover, pressed, focus-visible, disabled, loading** (where applicable), and **selected** for selectables.

### 14.1 Floating tab bar (signature component)
- Glass pill (`glass`, `--r-pill`), floating with 12–16px margin from the edge it attaches to.
- 3–5 items. Each item: icon + label (label may hide on narrow screens; icon-only requires tooltips/labels).
- Selected item: an inner pill of `accent-soft` (light) / `surface-3` (dark) that **slides** between items; selected label/icon uses `accent`.
- Inner pill radius follows the concentric rule.
- Optional trailing circular search button detached from the pill, as in the Games app and iOS 26.

### 14.2 Sidebar
- Width 240–280px; collapses to a 72px icon rail.
- On ambient/imagery backgrounds it is `glass`; on plain canvas it is `surface` or transparent.
- Items: `label` text, 20px icon, `--r-md` rows, 36–40px tall.
- Selected item: `accent-soft` fill + `accent` icon. Only one selected.
- **No promotional cards** (alpha banners, "invite people", taglines) in the sidebar. Version info lives in Settings/About.
- Section headers: sentence case, `caption`, `text-3`. No all-caps.

### 14.3 Buttons

| Variant | Look | Use |
|---|---|---|
| Primary | Solid `accent`, `on-accent` text, pill | ONE per view: the focal action |
| Secondary | `surface-2` fill (or `glass` over imagery), `text` color, pill | Other actions |
| Ghost | No fill, `text-2`, pill on hover (`surface-2`) | Tertiary, toolbars |
| Destructive | Solid `danger` only inside confirmation dialogs; elsewhere a ghost button with `danger` text | Delete, remove |
| Icon | Circle, 36–44px, ghost or glass | Toolbars, media controls |

- Height: 36px (compact), 40px (default), 48–52px (mobile/primary hero).
- Horizontal padding: 16–20px. Icon + label gap 8px.
- No gradients, no glow shadows, no outlined primary buttons.
- Loading: spinner replaces the leading icon; label stays; width doesn't jump.

### 14.4 Chips (suggestion / filter)
- Pill, `label` text, 32–36px tall, `surface` + hairline (light) / `surface-2` (dark), or `glass-thin` over imagery.
- Optional leading 16px icon.
- Selected filter chip: `accent-soft` fill, `accent` text, leading check icon.
- Suggestion chips sit in one row under the focal element, wrap or scroll horizontally. Max ~5 visible.

### 14.5 Segmented control
- Glass or `surface-2` pill track; selected segment is a raised inner pill (`surface` light / `surface-3` dark) that slides.
- 2–5 segments, equal widths.

### 14.6 Inputs
- **Text field:** `--r-sm` in dense forms, pill for single-line search. 40px tall. `surface` fill, hairline border; focus = 2px `accent` ring with 2px offset.
- **Prompt / command bar** (focus layout): large rounded rectangle (`--r-xl`), `body-lg` text, min 56px tall, actions inside on the right (circular). Gentle shadow. This is the focal element of its screen.
- Labels above fields (`label`, `text-2`). Placeholders are examples, never instructions, never the only label.
- Errors: `danger` ring + one short line below.

### 14.7 Cards
- `surface`, `--r-lg` (or `--r-xl` for hero), padding `--s-6`, soft shadow `--shadow-1`.
- Card header: title (`title-3`) left, actions (ghost icon buttons / "…") right.
- **No eyebrow labels, no subtitles that restate the title, no decorative illustrations.**
- Hero card (max one per screen) may use `accent` tint or an aurora glow *inside* it. Its text must still pass contrast.

### 14.8 Lists & rows
- Rows 44–56px, `--r-md` hover background (`surface-2`), no dividers by default. Use hairline dividers only in long dense lists.
- Leading: icon/avatar/status dot. Trailing: value, chevron, or actions (actions appear on hover on desktop; always visible on touch).

### 14.9 Status pills & badges
- Pill, `micro` or `caption`, 20–24px tall.
- Format: **colored dot + one word** ("Online", "Down", "Degraded", "Paid", "Unpaid").
- Fill uses the semantic color at ~14% opacity; dot and text use the full semantic color (verify contrast).
- Never a sentence in a badge.

### 14.10 Menus & popovers
- `glass-thick`, `--r-lg`, padding 6px, items `--r-md` (concentric), 36px tall.
- Group with hairline separators + optional `caption` group header.
- Checkmark sits in a leading column so labels stay aligned.
- Destructive items last, in `danger` text.

### 14.11 Sheets & dialogs
- Dialogs: `glass-thick` or `surface`, `--r-xl`, max-width 440px, dimming scrim `rgba(8,10,20,.40)`.
- Title (`title-3`) + ≤ 2 lines of body + buttons right-aligned (desktop) or stacked full-width (mobile). Primary on the right/top.
- Mobile sheets: bottom-attached, `--r-2xl` top corners, grabber handle, swipe to dismiss.
- Close button: circular icon button, top-right.

### 14.12 Toasts & notifications
- Floating glass pill or rounded card, bottom-center (web/desktop) or top (mobile).
- One line: icon + message + optional single action ("Undo"). Auto-dismiss 4–6s; errors persist until dismissed.

### 14.13 Empty states
- Small optional line icon (≤ 48px) + one short line ("No hosts") + one primary action ("Add host"). That's all.

### 14.14 Loading
- Skeletons in the exact shape of the content (`surface-2`, subtle shimmer ≤ 1.5s loop, disabled under reduced motion).
- Spinners only inside buttons or for < 1s waits. No full-screen spinners with taglines.

### 14.15 Progress (downloads, installs, scans)
- Linear bar, pill-shaped track (`surface-2`) and fill (`accent`), 6–8px tall.
- Shows: item name, percent or bytes, time remaining, cancel/pause. Nothing else (Part 10.5).

### 14.16 Toggles, checkboxes, radios
- Toggle: pill track 44×26, circular thumb, `accent` when on, thumb slides with `--t-fast`.
- Checkbox: `--r-xs`, 18px; radio: circle 18px. Both `accent` when checked.

### 14.17 Avatars
- Circles: 24 / 32 / 40 / 56px. Initials fallback on `accent-soft` with `accent` text. Stacks overlap by 25% with a 2px `surface` ring.

### 14.18 Media controls
- Circular glass buttons; the play/pause button is the largest (focal), skip buttons smaller.

---

## 15. Data & dashboards

- **Lead with the number.** A stat card is: label (`caption`, `text-2`) + value (`title-1`, tabular) + optional delta ("+14%" in `success`/`danger`). No explanatory sentence.
- **Default to fewer columns and fields.** Show the 3–5 columns people scan by; put the rest in a column picker, an expandable row, or the detail view (P11).
- **Don't show stats that don't help a decision.** "Active modules: 1" is not a stat; it's a fact the sidebar already shows. Prefer stats that change and matter (hosts down, alerts today, uptime).
- **Charts:**
  - Neutral bars/lines in `text-3` at low opacity; highlight **one** series or data point in `accent` or `signal` (the Spectra "Thursday bar" pattern).
  - Rounded bar ends (pill), 2px line strokes, no 3D, no drop shadows, no chart junk.
  - Gridlines: hairline, horizontal only, dashed optional.
  - Always label units. Tooltips on hover show exact values.
  - Max 4 series per chart; colors in order: `accent`, `teal`, `signal`, `#4F7BFF`.
- **Tables:**
  - Live on `surface`, never glass.
  - Header row: `caption`, `text-2`, sentence case, sticky.
  - Body: `body` size minimum (not tiny gray text), tabular numbers, right-aligned numbers.
  - Row height 44px (comfortable) / 36px (compact). Hover = `surface-2`.
  - Status column uses status pills (14.9).
- **Live/real-time data:** update values in place without re-animating layout. A small pulsing dot (respecting reduced motion) indicates "live."

---

## 16. Platform adaptation

The **soul** (color roles, radii, pill-first, say-less copy, one focal point) is identical everywhere. The **body** adapts:

### 16.1 Web (browser)
- Full Aurora Glass with `backdrop-filter` + fallbacks.
- Responsive breakpoints: 480 / 768 / 1024 / 1280 / 1536.
- Below 768px: sidebar becomes a floating bottom tab bar or a drawer.
- Respect `prefers-color-scheme`, `prefers-reduced-motion`, `prefers-reduced-transparency`, `prefers-contrast`.

### 16.2 Desktop apps (Electron, Tauri, native Windows/macOS)
- **Keep native window chrome conventions.** Windows: caption buttons top-right, consider Mica/Acrylic for the window backdrop. macOS: traffic lights top-left, vibrancy materials, unified toolbar.
- Use the **system font** in native shells; Geist is acceptable in Electron/Tauri if consistent across the product.
- Denser defaults than web (compact density is acceptable), full keyboard support, right-click context menus, visible focus rings, standard shortcuts (Ctrl/Cmd+K command palette encouraged).
- Respect OS light/dark and accent settings where sensible; our accent can override only inside the content area.
- Don't fake a title bar with a full-width colored strip. A glass toolbar may merge with the title bar area if the OS supports it.

### 16.3 Mobile (iOS, Android, PWA)
- Touch targets ≥ 44×44pt (iOS) / 48×48dp (Android).
- Floating bottom tab bar (glass pill) with 3–5 items; primary content scrolls beneath it.
- Bottom sheets over dialogs where possible. Swipe-back on iOS; system back on Android.
- Body text 16–17px. Respect Dynamic Type / font scaling; layouts must not break at 200%.
- iOS: prefer native materials and SF Symbols. Android: Material 3 components are acceptable if restyled with our radii, palette, and copy rules.

### 16.4 Games & immersive apps
- Menus and HUD follow the same principles: floating glass panels, pill buttons, one focal action, say-less copy.
- HUD text needs an extra contrast scrim since game backgrounds are unpredictable.
- Controller/keyboard focus states must be big and obvious (2–3px `accent` ring + slight scale).
- Engine-native (Godot, Unity): replicate glass with a semi-transparent panel + blur shader if cheap; otherwise use an opaque `surface` panel at 90% opacity.

### 16.5 Marketing / website pages
- This is the only place for headlines, taglines, feature descriptions, and editorial layouts (the Google Home reference).
- Same palette, radii, and type. More whitespace (`--s-24` between sections). Big imagery.

---

## 17. Accessibility floors (non-negotiable)

- **Contrast (WCAG 2.2 AA):** body text ≥ 4.5:1; large text (≥ 24px, or ≥ 19px bold) and UI components/icons ≥ 3:1. Test text on glass against worst-case backgrounds.
- **Minimum text size:** 12px absolute, 13px for anything people must read, 15px+ for body.
- **Focus:** every interactive element has a visible `:focus-visible` ring: 2px `accent` + 2px offset. Never `outline: none` without a replacement.
- **Targets:** ≥ 24×24px on desktop (44×44 preferred for primary), ≥ 44/48 on touch.
- **Color is never the only signal:** status uses dot + word, errors use icon + text, charts use labels.
- **Keyboard:** full operability, logical tab order, Esc closes overlays, arrow keys move within menus, tabs, and segmented controls.
- **Screen readers:** semantic HTML first, accessible names on icon buttons, live regions for toasts and live status changes.
- **Motion & transparency:** honor reduced-motion and reduced-transparency preferences (Parts 6.4, 13.4).
- **Zoom:** layouts survive 200% zoom / large system text without overlap or horizontal scroll.

---

## 18. Performance budget

- Backdrop blur is expensive. **Max ~3 glass layers visible at once.** Never put glass on every list row or table cell.
- Large blur radii on big areas (full-screen sheets) should switch to an opaque surface on low-end hardware or when the device reports low power.
- Aurora glows: static images/gradients preferred; if animated, transform/opacity only, GPU-friendly, ≥ 30s loops, paused when the tab is hidden.
- Animate only `transform` and `opacity`.
- Target 60fps scrolling; virtualize lists over ~200 rows.
- Fonts: subset, `font-display: swap`, ≤ 2 families.

---

## 19. Banned defaults & anti-patterns

AI tools reach for these by habit. **Do not use them:**

**Visual**
- Purple/blue gradient **fills** on buttons, whole cards, or page backgrounds (ambient glows are fine; fills are not)
- Glowing/neon drop shadows on buttons
- Outlined-everything layouts: every card with a visible 1px border and no shadow
- Hard full-width horizontal dividers between every section
- Sharp corners mixed with rounded corners
- Same radius on nested elements (violates the concentric rule)
- Grids of identical stat cards whose numbers don't matter
- Decorative illustrations in repeat-use screens
- Emoji as icons in app chrome
- More than one primary button per view
- Tiny gray text (< 13px, `text-3` on glass)
- Text over busy imagery without a scrim or glass
- Default library looks shipped unstyled (raw Bootstrap, untouched shadcn defaults, Material defaults)
- Inter as the default font (use the families in Part 9)

**Copy** (see Part 10)
- Taglines, slogans, mission statements inside the app
- ALL-CAPS eyebrow labels above headings
- Subtitles that restate the title
- Reassurance and privacy blurbs in headers
- Promotional cards in sidebars
- Instructions for the obvious ("Choose a module to get started")
- Exclamation marks and emoji in UI text
- Explanations of stats beneath stats

**Behavioral**
- Count-up number animations on every load
- Auto-playing carousels in tools
- Modals for non-critical info
- Toasts for things the UI already shows

---

## 20. Token reference (copy-paste CSS)

```css
/* ============ Aurora Glass tokens ============ */
:root {
  /* --- Color: light --- */
  --canvas: #F4F5FA;
  --surface: #FFFFFF;
  --surface-2: #EEF0F7;
  --surface-3: #E4E7F1;
  --text: #14172B;
  --text-2: #474D6B;
  --text-3: #62688A;
  --hairline: rgba(20, 24, 45, 0.08);

  --accent: #6B5CF0;
  --accent-soft: #ECE9FF;
  --on-accent: #FFFFFF;
  --teal: #0F9F8F;
  --signal: #F2702E;
  --danger: #D93B3B;
  --warning: #B7791F;
  --success: #16935C;

  --aurora-blue: #4F7BFF;
  --aurora-violet: #8B5CF6;
  --aurora-teal: #2DD4BF;
  --aurora-pink: #F0ABFC;
  --aurora-opacity: 0.30;

  /* --- Glass: light --- */
  --glass-thin-bg: rgba(255, 255, 255, 0.45);
  --glass-bg: rgba(255, 255, 255, 0.62);
  --glass-thick-bg: rgba(255, 255, 255, 0.80);
  --glass-highlight: inset 0 1px 0 rgba(255, 255, 255, 0.55);

  /* --- Shadows: light --- */
  --shadow-1: 0 1px 2px rgba(15, 20, 45, 0.04), 0 4px 16px rgba(15, 20, 45, 0.06);
  --shadow-2: 0 2px 6px rgba(15, 20, 45, 0.06), 0 12px 32px rgba(15, 20, 45, 0.10);
  --shadow-3: 0 8px 24px rgba(15, 20, 45, 0.10), 0 24px 64px rgba(15, 20, 45, 0.16);

  /* --- Radius --- */
  --r-xs: 6px;
  --r-sm: 10px;
  --r-md: 14px;
  --r-lg: 20px;
  --r-xl: 28px;
  --r-2xl: 36px;
  --r-pill: 9999px;

  /* --- Spacing (4px base) --- */
  --s-1: 4px;  --s-2: 8px;  --s-3: 12px; --s-4: 16px;
  --s-5: 20px; --s-6: 24px; --s-8: 32px; --s-10: 40px;
  --s-12: 48px; --s-16: 64px; --s-24: 96px;

  /* --- Type --- */
  --font-sans: "Geist", "SF Pro Text", "Segoe UI Variable", system-ui, sans-serif;
  --font-mono: "Geist Mono", ui-monospace, "Cascadia Code", Menlo, monospace;

  --fs-display: 48px; --lh-display: 52px;
  --fs-title-1: 32px; --lh-title-1: 38px;
  --fs-title-2: 24px; --lh-title-2: 30px;
  --fs-title-3: 20px; --lh-title-3: 26px;
  --fs-body-lg: 17px; --lh-body-lg: 26px;
  --fs-body: 15px;    --lh-body: 22px;
  --fs-label: 14px;   --lh-label: 20px;
  --fs-caption: 13px; --lh-caption: 18px;
  --fs-micro: 12px;   --lh-micro: 16px;

  /* --- Motion --- */
  --t-instant: 80ms;
  --t-fast: 160ms;
  --t-base: 240ms;
  --t-slow: 360ms;
  --ease: cubic-bezier(0.2, 0.8, 0.2, 1);
  --ease-exit: cubic-bezier(0.4, 0, 1, 1);

  /* --- Focus --- */
  --focus-ring: 0 0 0 2px var(--canvas), 0 0 0 4px var(--accent);

  color-scheme: light dark;
}

/* --- Color: dark (OS preference) --- */
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --canvas: #0B0D16;
    --surface: #151826;
    --surface-2: #1D2133;
    --surface-3: #262B40;
    --text: #F2F3F8;
    --text-2: #B8BCD0;
    --text-3: #9095AE;
    --hairline: rgba(255, 255, 255, 0.08);
    --accent: #9C8EFF;
    --accent-soft: rgba(156, 142, 255, 0.16);
    --on-accent: #0B0D16;
    --teal: #3DD9C4;
    --signal: #FF8E52;
    --danger: #FF6B6B;
    --warning: #F5B547;
    --success: #4ADE95;
    --aurora-opacity: 0.45;
    --glass-thin-bg: rgba(30, 34, 52, 0.45);
    --glass-bg: rgba(26, 30, 46, 0.62);
    --glass-thick-bg: rgba(22, 26, 40, 0.82);
    --glass-highlight: inset 0 1px 0 rgba(255, 255, 255, 0.10);
    --shadow-1: 0 1px 2px rgba(0, 0, 0, 0.30), 0 4px 16px rgba(0, 0, 0, 0.25);
    --shadow-2: 0 2px 6px rgba(0, 0, 0, 0.35), 0 12px 32px rgba(0, 0, 0, 0.35);
    --shadow-3: 0 8px 24px rgba(0, 0, 0, 0.40), 0 24px 64px rgba(0, 0, 0, 0.50);
  }
}

/* --- Color: dark (manual toggle) — same values as above --- */
:root[data-theme="dark"] {
  --canvas: #0B0D16;
  --surface: #151826;
  --surface-2: #1D2133;
  --surface-3: #262B40;
  --text: #F2F3F8;
  --text-2: #B8BCD0;
  --text-3: #9095AE;
  --hairline: rgba(255, 255, 255, 0.08);
  --accent: #9C8EFF;
  --accent-soft: rgba(156, 142, 255, 0.16);
  --on-accent: #0B0D16;
  --teal: #3DD9C4;
  --signal: #FF8E52;
  --danger: #FF6B6B;
  --warning: #F5B547;
  --success: #4ADE95;
  --aurora-opacity: 0.45;
  --glass-thin-bg: rgba(30, 34, 52, 0.45);
  --glass-bg: rgba(26, 30, 46, 0.62);
  --glass-thick-bg: rgba(22, 26, 40, 0.82);
  --glass-highlight: inset 0 1px 0 rgba(255, 255, 255, 0.10);
  --shadow-1: 0 1px 2px rgba(0, 0, 0, 0.30), 0 4px 16px rgba(0, 0, 0, 0.25);
  --shadow-2: 0 2px 6px rgba(0, 0, 0, 0.35), 0 12px 32px rgba(0, 0, 0, 0.35);
  --shadow-3: 0 8px 24px rgba(0, 0, 0, 0.40), 0 24px 64px rgba(0, 0, 0, 0.50);
}

/* ============ Base ============ */
body {
  background: var(--canvas);
  color: var(--text);
  font-family: var(--font-sans);
  font-size: var(--fs-body);
  line-height: var(--lh-body);
  -webkit-font-smoothing: antialiased;
}

:focus-visible {
  outline: none;
  box-shadow: var(--focus-ring);
}

/* ============ Ambient aurora ============ */
.aurora {
  position: fixed;
  inset: 0;
  z-index: -1;
  pointer-events: none;
  opacity: var(--aurora-opacity);
  background:
    radial-gradient(45% 55% at 0% 0%,   var(--aurora-blue)   0%, transparent 70%),
    radial-gradient(40% 50% at 100% 10%, var(--aurora-violet) 0%, transparent 70%),
    radial-gradient(50% 45% at 85% 100%, var(--aurora-teal)  0%, transparent 70%);
  filter: blur(40px);
}

/* ============ Materials ============ */
.glass-thin,
.glass,
.glass-thick {
  border: 1px solid var(--hairline);
  box-shadow: var(--glass-highlight), var(--shadow-2);
}
.glass-thin  { background: var(--glass-thin-bg);  backdrop-filter: blur(16px) saturate(160%); -webkit-backdrop-filter: blur(16px) saturate(160%); }
.glass       { background: var(--glass-bg);       backdrop-filter: blur(24px) saturate(160%); -webkit-backdrop-filter: blur(24px) saturate(160%); }
.glass-thick { background: var(--glass-thick-bg); backdrop-filter: blur(32px) saturate(160%); -webkit-backdrop-filter: blur(32px) saturate(160%); box-shadow: var(--glass-highlight), var(--shadow-3); }

@supports not ((backdrop-filter: blur(1px)) or (-webkit-backdrop-filter: blur(1px))) {
  .glass-thin, .glass, .glass-thick { background: var(--surface); }
}
@media (prefers-reduced-transparency: reduce) {
  .glass-thin, .glass, .glass-thick {
    background: var(--surface);
    backdrop-filter: none;
    -webkit-backdrop-filter: none;
  }
  .aurora { display: none; }
}

/* ============ Motion preferences ============ */
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 1ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 1ms !important;
    scroll-behavior: auto !important;
  }
}
```

### 20.1 Do / Don't examples

**Primary button**
```css
/* DO */
.btn-primary {
  height: 40px;
  padding: 0 var(--s-5);
  border-radius: var(--r-pill);
  background: var(--accent);
  color: var(--on-accent);
  font: 500 var(--fs-label)/var(--lh-label) var(--font-sans);
  border: none;
  transition: transform var(--t-instant) var(--ease), filter var(--t-fast) var(--ease);
}
.btn-primary:hover  { filter: brightness(1.06); }
.btn-primary:active { transform: scale(0.97); }

/* DON'T */
.btn-primary {
  background: linear-gradient(135deg, #667eea, #764ba2); /* gradient fill */
  box-shadow: 0 0 24px rgba(118, 75, 162, 0.6);           /* neon glow */
  border-radius: 8px;                                      /* not pill */
  padding: 13px 22px;                                      /* off-scale */
}
```

**Concentric radius**
```css
/* DO: card 20px, padding 8px → inner 12px */
.card        { border-radius: var(--r-lg); padding: var(--s-2); }
.card > .row { border-radius: calc(var(--r-lg) - var(--s-2)); }

/* DON'T: same radius inside and out → pinched corners */
.card        { border-radius: 20px; padding: 8px; }
.card > .row { border-radius: 20px; }
```

**Stat card copy**
```html
<!-- DO -->
<div class="stat">
  <span class="stat-label">Hosts down</span>
  <span class="stat-value">3</span>
</div>

<!-- DON'T -->
<div class="stat">
  <span class="eyebrow">CONFIGURED HOSTS</span>
  <span class="stat-value">25</span>
  <span class="subtitle">Across your saved sites</span>
</div>
```

**Page header copy**
```html
<!-- DO -->
<h1>Modules</h1>

<!-- DON'T -->
<p class="eyebrow">TOOLKIT</p>
<h1>Your IT toolkit, all together.</h1>
<p>Choose a module. Keep your work running in one place.</p>
```

---

## 21. AI build procedure & self-review checklist

### 21.1 Procedure
1. **Identify the screen type:** focus, workspace, media, or marketing (Part 8.3 / 16.5).
2. **Name the one focal point.** Write it in a code comment at the top of the component: `/* FOCAL: prompt bar */`.
3. **Pick density:** comfortable or compact.
4. **Assign layers:** what is canvas, ambient, surface, glass, overlay (Part 4).
5. **Write the copy first, then cut it** using the test in Part 10.1. Aim to delete at least a third of what you first wrote.
6. **Build with tokens only** (Part 20).
7. **Build all states:** rest, hover, pressed, focus-visible, disabled, loading, selected, empty, error.
8. **Build both light and dark.**
9. **Run the checklist below.** Fix every "no" before reporting done.

### 21.2 Self-review checklist

**Focus & hierarchy**
- [ ] Exactly one primary/focal element per view?
- [ ] Accent color used on ≤ ~8% of the screen?
- [ ] Gradient text used at most once, on ≤ 4 words?

**Information placement**
- [ ] Every value appears in only one place on this screen?
- [ ] Each piece of data shown here is needed for this screen's task (otherwise it moves to its home screen or behind disclosure)?
- [ ] Details, metadata, and advanced options are on demand rather than shown by default?

**Copy**
- [ ] Zero taglines, slogans, or self-descriptions?
- [ ] Zero ALL-CAPS eyebrow labels?
- [ ] Zero subtitles that restate a title or explain a stat?
- [ ] Every string passes "if deleted, can the user still understand, decide, act?"
- [ ] Buttons are verb-first, sentence case, no exclamation marks?

**Shape & space**
- [ ] All interactive controls pill-shaped (unless dense-table exception)?
- [ ] Concentric radius rule followed everywhere?
- [ ] Every spacing value is on the 4px token scale?
- [ ] Groups separated by space before lines?

**Materials**
- [ ] Glass used only for floating elements, never under dense content?
- [ ] ≤ 3 glass layers visible at once?
- [ ] Glass fallbacks present (no-support + reduced-transparency)?

**Accessibility**
- [ ] Text contrast ≥ 4.5:1 (body) and ≥ 3:1 (large/UI), including on glass?
- [ ] No text < 12px; readable text ≥ 13px?
- [ ] Visible focus ring on every interactive element?
- [ ] Status never conveyed by color alone?
- [ ] Reduced-motion respected?
- [ ] Keyboard fully operable?

**Platform**
- [ ] Native window/navigation conventions kept for this platform?
- [ ] Touch targets meet platform minimums on mobile?
- [ ] Works in both light and dark?

**Anti-patterns**
- [ ] Nothing from Part 19 present?

---

## 22. Extending this document

- New component? Add it to Part 14 with anatomy, rules, and states, using existing tokens.
- New token needed? Add it to Part 20 **and** its table in the relevant Part. Search the codebase for `TOKEN-GAP` comments periodically and resolve them here.
- New reference image? Add a row to Part 3: what to take, what to leave.
- Changing a core value (accent, radius scale, font)? Change it here first, then in code. This file is the source of truth.

**Changelog**
- v1.1 — Added P11 "Not every piece of data needs to be everywhere" (one home per fact, progressive disclosure), table column default, checklist section.
- v1.0 — Initial Aurora Glass system: layer model, glass tiers, pill-first shape language, concentric radii, say-less copy rules, platform adaptation, tokens.
