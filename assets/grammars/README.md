# Vendored grammars and themes

Third-party TextMate files, checked in rather than fetched so the build needs no network beyond the
Tailwind/esbuild download. Read at build time only; never served to the browser.

Only the languages this site actually uses are vendored. A fence tagged with anything else — `json`,
`yaml`, `xml`, `csharp` — **fails the build** rather than degrading quietly.

**To add a language:** drop the grammar here and register it in
[`Highlight.fs`](../../src/Yggdrasil.Content/Highlight.fs) in **both** `scopeByLang` (fence label →
scope) and `grammarFiles` (scope → filename). Themes need no change — they resolve colours by scope.
Keep the two in step: `Highlight.create` loads every registered grammar eagerly, so a scope listed
without its file throws `FileNotFoundException` at startup instead of failing gracefully.

**Grammars must be self-contained.** `GrammarRegistryOptions.GetInjections` returns `null`, so a
grammar that `include`s an unregistered scope will not resolve it. For that reason, **do not add HTML
or Markdown grammars** — they embed other languages.

## Grammars

| File | Scope | Fence labels | Source | Licence |
| ---- | ----- | ------------ | ------ | ------- |
| `fsharp.tmLanguage.json` | `source.fsharp` | `fsharp`, `fs`, `f#` | [ionide/ionide-fsgrammar](https://github.com/ionide/ionide-fsgrammar), as vendored by [microsoft/vscode](https://github.com/microsoft/vscode) † | MIT |
| `scala.tmLanguage.json` | `source.scala` | `scala` | [scala/vscode-scala-syntax](https://github.com/scala/vscode-scala-syntax) † | MIT |
| `shell.tmLanguage.json` | `source.shell` | `bash`, `shell`, `sh`, `shellscript` | [jeff-hykin/better-shell-syntax](https://github.com/jeff-hykin/better-shell-syntax) @ `35020b0` | MIT |

† These files embed no provenance, so they were identified by fingerprinting their `repository` rule
names — strong circumstantial identification, not a recorded download. Diff against the named upstream
if you need certainty.

`fsharp.tmLanguage.json` `include`s `text.html.markdown` inside its doc-comment rules. That scope is
not registered, so the include is inert and `///` comments get no nested Markdown highlighting.

## Themes

| File | Source | Licence |
| ---- | ------ | ------- |
| `catppuccin-frappe.json` | [shikijs/textmate-grammars-themes](https://github.com/shikijs/textmate-grammars-themes/blob/main/packages/tm-themes/themes/catppuccin-frappe.json) | MIT |

Frappé is the only theme, used in both site themes, so a code block looks the same in either. It is the
lightest of the dark Catppuccin flavours (`#303446`), which keeps it from reading as a harsh black
rectangle on the light canvas.

## Licence

These grammars and the theme are the only third-party *source* checked into this repository, so this
is where their attribution lives. (The other redistributed third-party asset is the fonts, whose SIL
OFL 1.1 notices are in `static/fonts/OFL.txt`.)

Every file here is MIT licensed. Copyright (c) Microsoft Corporation; the Ionide contributors; the
Scala contributors; Jeff Hykin; and the Catppuccin contributors. Per-file attribution is in the tables
above.

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction,
> including without limitation the rights to use, copy, modify, merge, publish, distribute,
> sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
> NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
> OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
