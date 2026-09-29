<div align="center">

# 🌲 Yggdrasil

<p><strong>Static site generator for <a href="https://itsdaniel.dk">itsdaniel.dk</a>, written in F#</strong></p>

<p>
  <a href="https://github.com/itsdanieldk/yggdrasil/actions/workflows/ci.yml"><img src="https://github.com/itsdanieldk/yggdrasil/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4.svg?logo=dotnet" alt=".NET 10">
  <a href="LICENSE.md"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"></a>
  <a href="https://itsdaniel.dk"><img src="https://img.shields.io/badge/site-itsdaniel.dk-000000.svg" alt="itsdaniel.dk"></a>
</p>

</div>

---

Turns the Markdown and YAML in [`content/`](content) and the files in [`static/`](static) into a portable
`dist/` — pages, feeds, share cards, CSS and JS — with nothing running at request time. Any static host
works; this one deploys to Vercel.

- **Fail-loud validation** — every content error is listed at once, before anything is written
- **No Node toolchain** — standalone Tailwind and esbuild binaries, checksum-verified and cached in `.bin/`
- **OG share cards** — 1200×630 PNGs drawn with SkiaSharp at build time
- **Reference-checked output** — the build fails if any emitted `href`, `src` or `srcset` doesn't resolve

## Usage

Needs the .NET 10 SDK, pinned in `global.json`.

```sh
dotnet run --project src/Yggdrasil.Generate    # build the site into dist/
bash scripts/check-dist.sh                     # the output checks CI runs
python3 -m http.server 8799 --directory dist   # preview it
```

A run validates all content, recreates `dist/`, copies `static/`, builds minified CSS and JS, draws the share
cards, renders every route in parallel and checks every reference. It finds the project root through
`global.json`, so it works from any directory.

The first run downloads **Tailwind 4.3.3** and **esbuild 0.28.1** into `.bin/`, rejecting any download that
doesn't match its checksum in [`Assets.fs`](src/Yggdrasil.Generate/Assets.fs) — so bumping a version means
updating its pins. Platforms are Tailwind's: macOS, Linux (glibc or musl) and Windows, on x64 or arm64.

| Variable      | Default                 | Purpose                                                                                                                             |
| ------------- | ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `SITE_URL`    | `site.yaml`'s `url`     | Absolute `http(s)` base URL for canonical links, Open Graph and feeds.                                                              |
| `SITE_ROOT`   | found via `global.json` | Project root; a single CLI argument takes precedence. Must contain `global.json` and `site.yaml`, since `dist/` beneath it is deleted. |
| `SKIP_ASSETS` | _(unset)_               | Any value skips CSS/JS, share cards and reference checks. The output is incomplete, so use it only to iterate on HTML.              |

## Tests

```sh
dotnet test                                                              # what CI runs
dotnet run --project tests/Yggdrasil.Tests -- --filter-test-list feeds   # Expecto console, with arguments
dotnet test --collect:"XPlat Code Coverage"                              # Cobertura under TestResults/
```

The console app runs only the modules listed in `Main.fs`, so register every new `[<Tests>]` module there.
`Content` and `Web` sit at 94–97% line coverage; `Generate` is near 60%, since `Assets` downloads binaries and
runs external processes.

## Content

| Collection | File                                  | Required                                 | Optional                                   |
| ---------- | ------------------------------------- | ---------------------------------------- | ------------------------------------------ |
| Notes      | `content/notes/<slug>/index.md`       | `title`, `description`, `date`           | `updatedDate`, `tags`, `draft`, `featured` |
| Projects   | `content/projects/<slug>/index.md`    | as notes                                 | as notes, plus `demoURL`, `repoURL`        |
| Pages      | `content/pages/{home,about}/index.md` | `title`, `description`                   | `heading`, `emoji`                         |
| Fragrances | `content/fragrances/<slug>.yaml`      | `name`, `house`, `url`, `rating`         | `note`, `concentration`, `wishlist`, `draft` |

- **The folder or file name is the slug, and so the URL**: lowercase letters, digits and single hyphens.
  Anything the loader would otherwise skip fails the build instead — a stray file, an `Index.md`, a `.yml`
  fragrance, a page other than home and about. Names starting with a dot are ignored.
- **Unknown keys fail the build**, so `drafts: true` can't silently publish; each collection accepts only
  its own keys. Dates are ISO (`2026-09-29`). `draft: true` leaves an entry out, `featured: true` lists it
  first, and a `wishlist: true` fragrance takes no `rating`.
- **Images**: put the PNG next to `index.md` and link it as `![alt](./name.png)`. The build reads its size,
  but the page points at `/images/<collection>/<slug>/name.webp`, which nothing generates — convert it into
  `static/images/` yourself, or reference verification fails. A fragrance's photos are
  `static/images/fragrances/<slug>/bottle.png` and `bottle@2x.png`.
- **Code fences need a supported language**: `fsharp`/`fs`/`f#`, `scala` or `bash`/`shell`/`sh`/`shellscript`.
  Any other label, a typo included, fails the build; leave a fence untagged for a plain block. To add a
  language, see [`assets/grammars/README.md`](assets/grammars/README.md).

## Layout

```
content/                  Markdown and YAML, plus source PNGs
static/                   copied verbatim into dist/
assets/                   Tailwind and esbuild inputs, TextMate grammars (see its README), share-card fonts
scripts/check-dist.sh     output checks, shared with CI
src/Yggdrasil.Content/    domain model and content pipeline
src/Yggdrasil.Web/        views, layouts, feeds and routes
src/Yggdrasil.Generate/   the entry point you run
tests/Yggdrasil.Tests/    Expecto suite
```

`dist/` and `.bin/` are build output and gitignored.

## Making it yours

- **Identity is data:** `site.yaml` (name, author, tagline, URL, avatar, socials, nav, and each index page's
  title and description), the home and about pages, and the avatar and favicons in `static/`. The home
  page's description also feeds RSS, the web manifest and the structured data.
- **The theme is code:** layout, components, colors and routes, plus a few site-specific details — the intro
  lines and the wishlist heading in `src/Yggdrasil.Web/Views/`, the manifest's categories (`Feed.fs`), the
  page language (`Layouts.fs`, `JsonLd.fs`) and the avatar's 800×800 size (`JsonLd.fs`).
- `content/fragrances/` is a personal collection you'll want to delete.

## Deploying

Vercel's build image has no .NET, so CI builds the site and uploads it **prebuilt**. The `deploy` job in
[`ci.yml`](.github/workflows/ci.yml) runs only after `test` and `generate` pass, and `vercel.json` turns off
Vercel's own Git deployments, so CI is the only path to production. The job:

1. runs `vercel build`, which generates the site again, and `scripts/check-dist.sh` on its output;
2. uploads it with `--skip-domain`, so the new deployment isn't live yet;
3. smoke-tests that deployment — pages, an article, the feed, the stylesheet, the 404 page and the security
   headers;
4. only then runs `vercel promote`. A deployment that fails never reaches the domain.

To roll back, promote the previous deployment in the Vercel dashboard (instant), then revert the commit.

One-time setup:

1. Repository secrets `VERCEL_TOKEN`, `VERCEL_ORG_ID` and `VERCEL_PROJECT_ID`, the last two from
   `.vercel/project.json` after `vercel link`.
2. Repository secret `VERCEL_AUTOMATION_BYPASS_SECRET`, from **Settings → Deployment Protection → Protection
   Bypass for Automation**. Deployment Protection redirects the staged URL to an SSO login; the smoke test
   sends this secret to get past it.
3. `SITE_URL` in the Vercel environment, if it differs from `site.yaml`'s `url`.

> **Don't hand-write `.vercel/output/config.json`.** `vercel build` translates `trailingSlash` and `headers`
> from `vercel.json` into Build Output API routes; by hand, the CSP and HSTS headers silently go missing.

> **Don't turn `cleanUrls` back on.** It republishes every `…/index.html` at `…/index`, and Vercel's Instant
> Static serving never maps `/` or `/about` back to those paths, so every page 404s. The directory-per-page
> output doesn't need it.

> **Vercel Web Analytics can't work here.** Vercel adds its `/_vercel/insights/*` routes only to deployments
> it builds itself, and these are prebuilt. Reference verification rejects that path, since nothing in
> `dist/` serves it.

## Gotchas

- **Highlighting uses one theme**, Catppuccin Frappé, in both light and dark mode. Its colors are inline, so
  code blocks need no dark-mode CSS.
- **Tailwind only sees class names written out in full** in `src/Yggdrasil.Web`, which `assets/css/app.css`
  points it at with `@source`.
- **Share cards draw from the TTF files in `assets/fonts/`**, not the WOFF2 the site serves: SkiaSharp's
  Linux build has no Brotli, so it can't decode WOFF2. The faces are Latin subsets, so a title or tag with
  another character (`→`, `λ`) fails the build rather than drawing an empty box.
- The footer year is fixed at generate time; rebuild to refresh it.

## Licensing

- **The generator is MIT** — `src/`, `tests/`, `assets/`, build config and docs. Take it and build your own
  site. See [`LICENSE.md`](LICENSE.md).
- **The content is not** — notes, project write-ups, fragrance reviews and the avatar are reserved
  ([`LICENSE-CONTENT.md`](LICENSE-CONTENT.md) has the exact paths). Replace `content/` with your own and
  you're clear; `site.yaml` is yours to edit.
- **Fragrance photographs** under `static/images/fragrances/` are third-party product shots, not mine to
  sublicense — a fork should delete them.

Redistributed third-party files carry attribution beside themselves:
[`assets/grammars/README.md`](assets/grammars/README.md) for the grammars and theme, and `static/fonts/OFL.txt`
for the fonts (shipped at `/fonts/OFL.txt`, as the SIL OFL requires). Everything else is a build-time NuGet
dependency, never redistributed.
