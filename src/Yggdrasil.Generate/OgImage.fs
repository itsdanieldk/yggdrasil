module Yggdrasil.Generate.OgImage

open Yggdrasil.Content

open System
open System.IO

open SkiaSharp

[<Literal>]
let private width = 1200

[<Literal>]
let private height = 630

let private margin = 90f

let private ground = SKColor.Parse "#f0efed"
let private ink = SKColor.Parse "#1c1917"
let private muted = SKColor.Parse "#78716c"
let private accent = SKColor.Parse "#7c5cdb"

let private titleSizes = [ 78f; 70f; 62f; 54f; 48f; 42f ]

let private titleTop = 220f
let private titleMaxHeight = 300f

type private CardFonts =
    { Wordmark: SKTypeface
      Sans: SKTypeface
      SansBold: SKTypeface }

    interface IDisposable with
        member this.Dispose() =
            this.Wordmark.Dispose()
            this.Sans.Dispose()
            this.SansBold.Dispose()

let private loadTypeface (path: string) =
    match SKTypeface.FromFile path with
    | null ->
        failwith (
            $"could not load font %s{path} — the file is missing, or this platform's Skia build "
            + "cannot parse it. OG cards use TTF, not the WOFF2 the site serves, because the "
            + "NoDependencies Linux Skia build ships without Brotli and so cannot decode WOFF2."
        )
    | typeface -> typeface

let private loadFonts (fontsDir: string) =
    let load name = loadTypeface (Path.Combine(fontsDir, name))
    { Wordmark = load "metamorphous-latin-400-normal.ttf"
      Sans = load "fira-sans-latin-400-normal.ttf"
      SansBold = load "fira-sans-latin-700-normal.ttf" }

let private baselineFor (font: SKFont) (top: float32) =
    top - font.Metrics.Ascent

let private lineHeightOf (font: SKFont) =
    font.Size * 1.12f

let private newFont (typeface: SKTypeface) (size: float32) =
    let font = new SKFont(typeface, size)
    font.Edging <- SKFontEdging.SubpixelAntialias
    font.Subpixel <- true
    font

let private wrapLines (font: SKFont) (maxWidth: float32) (text: string) =
    let words =
        text.Split ' '
        |> Array.filter (fun word -> word <> "")

    if Array.isEmpty words then
        []
    else
        let finished, current =
            words
            |> Array.fold
                (fun (finished, current) word ->
                    if current = "" then
                        finished, word
                    else
                        let candidate = current + " " + word

                        if font.MeasureText candidate <= maxWidth then
                            finished, candidate
                        else
                            current :: finished, word)
                ([], "")

        List.rev (current :: finished)

let private layoutTitle (typeface: SKTypeface) (title: string) =
    let maxWidth = float32 width - margin * 2f

    let heightAt (size: float32) =
        use font = newFont typeface size
        let lines = wrapLines font maxWidth title
        float32 (List.length lines) * lineHeightOf font

    let size =
        titleSizes
        |> List.tryFind (fun size -> heightAt size <= titleMaxHeight)
        |> Option.defaultValue (List.last titleSizes)

    let font = newFont typeface size
    font, wrapLines font maxWidth title

let private drawCard (config: SiteConfig) (fonts: CardFonts) (title: string) (tags: string list) (outPath: string) =
    use surface = SKSurface.Create(SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
    let canvas = surface.Canvas
    canvas.Clear ground

    use paint = new SKPaint(IsAntialias = true)

    paint.Color <- accent
    canvas.DrawRect(0f, 0f, 16f, float32 height, paint)

    use wordmark = newFont fonts.Wordmark 48f
    canvas.DrawText(config.Name, margin, baselineFor wordmark 96f, SKTextAlign.Left, wordmark, paint)

    let titleFont, lines = layoutTitle fonts.SansBold title
    use titleFont = titleFont
    paint.Color <- ink
    let firstBaseline = baselineFor titleFont titleTop
    let lineHeight = lineHeightOf titleFont

    lines
    |> List.iteri (fun i line ->
        canvas.DrawText(line, margin, firstBaseline + float32 i * lineHeight, SKTextAlign.Left, titleFont, paint))

    if not (List.isEmpty tags) then
        use tagFont = newFont fonts.Sans 30f
        paint.Color <- muted
        let tagsText = tags |> String.concat "   ·   "
        canvas.DrawText(tagsText, margin, baselineFor tagFont (float32 height - 110f), SKTextAlign.Left, tagFont, paint)

    Directory.CreateDirectory(Path.GetDirectoryName outPath)
    |> ignore

    use image = surface.Snapshot()
    use data = image.Encode(SKEncodedImageFormat.Png, 100)
    use file = File.Create outPath
    data.SaveTo file

let generateAll (config: SiteConfig) (fontsDir: string) (distDir: string) (notes: Note list) (projects: Project list) =
    use fonts = loadFonts fontsDir
    let outPath (sitePath: string) = Path.Combine(distDir, sitePath.TrimStart '/')

    drawCard config fonts $"{config.Author} — {config.Tagline}" config.OgDefaultTags (outPath Site.defaultOgImagePath)

    for n in notes do
        drawCard config fonts n.Title (List.truncate 3 n.Tags) (outPath (Site.ogImagePath "notes" n.Id))

    for p in projects do
        drawCard config fonts p.Title (List.truncate 3 p.Tags) (outPath (Site.ogImagePath "projects" p.Id))
