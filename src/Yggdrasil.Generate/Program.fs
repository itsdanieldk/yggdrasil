module Yggdrasil.Generate.Program

open Yggdrasil.Web
open Yggdrasil.Content

open System
open System.IO
open System.Collections.Concurrent
open System.Text.RegularExpressions

let rec private copyDir (src: string) (dst: string) (skipTop: Set<string>) (isTop: bool) =
    Directory.CreateDirectory dst |> ignore

    for file in Directory.GetFiles src do
        let name = Path.GetFileName file
        if not (isTop && skipTop.Contains name) then
            File.Copy(file, Path.Combine(dst, name), true)

    for dir in Directory.GetDirectories src do
        let name = Path.GetFileName dir
        if not (isTop && skipTop.Contains name) then
            copyDir dir (Path.Combine(dst, name)) skipTop false

let writeSite (config: SiteConfig) (content: SiteContent) (distDir: string) =
    let routes = Route.all content
    printfn "Rendering %d routes ..." routes.Length

    let failures = ConcurrentBag<string * exn>()

    routes
    |> List.toArray
    |> Array.Parallel.iter (fun route ->
        try
            let outPath = Path.Combine(distDir, Route.outputPath route)
            Directory.CreateDirectory(Path.GetDirectoryName outPath) |> ignore
            File.WriteAllText(outPath, Route.render config content route)
        with ex ->
            failures.Add(Route.outputPath route, ex))

    if failures.IsEmpty then
        Ok()
    else
        failures
        |> Seq.sortBy fst
        |> Seq.map (fun (path, ex) -> $"{path}: {ex.Message}")
        |> List.ofSeq
        |> Error

let private hostProvided =
    Set [ "/_vercel/insights/script.js" ]

let private referenceRegex =
    Regex "\\b(?:href|src)=\"([^\"]+)\"|\\bsrcset=\"([^\"]+)\""

let verifyReferences (distDir: string) =
    let resolves (reference: string) =
        let bare = reference.Split('#').[0].Split('?').[0]
        let decoded = Uri.UnescapeDataString bare
        let target = Path.Combine(distDir, decoded.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))

        // Only the shapes Vercel serves without cleanUrls: `/foo` resolving to `foo.html` would pass here
        // and 404 in production.
        if decoded.EndsWith "/" then
            File.Exists(Path.Combine(target, "index.html"))
        else
            File.Exists target || File.Exists(Path.Combine(target, "index.html"))

    let isExternal (reference: string) =
        reference = ""
        || reference.StartsWith "#"
        || reference.StartsWith "http://"
        || reference.StartsWith "https://"
        || reference.StartsWith "mailto:"
        || reference.StartsWith "data:"
        || hostProvided.Contains reference

    let problems = ResizeArray<string>()

    for page in Directory.EnumerateFiles(distDir, "*.html", SearchOption.AllDirectories) do
        for m in referenceRegex.Matches(File.ReadAllText page) do
            let references =
                if m.Groups.[1].Success then
                    [ m.Groups.[1].Value ]
                else
                    m.Groups.[2].Value.Split ','
                    |> Array.choose (fun candidate -> candidate.Trim().Split ' ' |> Array.tryHead)
                    |> List.ofArray

            for reference in references do
                if not (isExternal reference) && not (resolves reference) then
                    let where = Path.GetRelativePath(distDir, page)
                    problems.Add $"{where}: references \"{reference}\", which is not in the output"

    if problems.Count = 0 then
        Ok()
    else
        problems |> Seq.distinct |> List.ofSeq |> Error

let private reportErrors (label: string) (errors: string list) =
    eprintfn "%s failed with %d error(s):" label errors.Length
    errors |> List.iter (eprintfn "  - %s")
    1

[<EntryPoint>]
let main argv =
    match Config.resolveProjectRoot argv with
    | Error error -> reportErrors "Configuration" [ error ]
    | Ok projectRoot ->

    let sitePath = Path.Combine(projectRoot, "site.yaml")

    match Site.load sitePath with
    | Error errors -> reportErrors "Site config" errors
    | Ok siteConfig ->

    match Config.resolve argv siteConfig.BaseUrl with
    | Error errors -> reportErrors "Configuration" errors
    | Ok generator ->

    match Config.distDirectory generator with
    | Error error -> reportErrors "Configuration" [ error ]
    | Ok distDir ->

    let contentRoot = Path.Combine(projectRoot, "content")
    let staticRoot = Path.Combine(projectRoot, "static")
    let assetsDir = Path.Combine(projectRoot, "assets")
    let binDir = Path.Combine(projectRoot, ".bin")

    let paths =
        { ContentRoot = contentRoot
          GrammarRoot = Path.Combine(assetsDir, "grammars") }

    let config = { siteConfig with BaseUrl = generator.BaseUrl }

    printfn "Loading content from %s" contentRoot

    match SiteContent.loadWithHighlighter paths with
    | Error errors -> reportErrors "Content load" errors
    | Ok(content, highlighter) ->
        printfn
            "  %d notes, %d projects, %d fragrances"
            content.Notes.Length
            content.Projects.Length
            content.Fragrances.Length

        let fallbacks = Highlight.fallbacks highlighter

        fallbacks
        |> List.iter (fun e -> eprintfn "  highlight fallback: %s [%s] — %s" e.SourcePath e.Language e.Reason)

        if not (List.isEmpty fallbacks) then
            eprintfn "Build failed: %d code fence(s) could not be highlighted." fallbacks.Length
            eprintfn "  Tag the fence with a supported language, leave it untagged for a plain"
            eprintfn "  block, or add a grammar to assets/grammars/ and register it in Highlight.fs."
            eprintfn "  Supported: %s" (String.concat ", " Highlight.supportedLanguages)
            1
        else
            if Directory.Exists distDir then
                Directory.Delete(distDir, true)

            Directory.CreateDirectory distDir |> ignore

            let assets =
                try
                    printfn "Copying static assets (excluding assets/) ..."
                    copyDir staticRoot distDir (Set [ "assets"; "cache_manifest.json" ]) true

                    if generator.SkipAssets then
                        printfn "SKIP_ASSETS set — skipping CSS/JS and OG-card build."
                    else
                        printfn "Building CSS/JS (standalone tailwind + esbuild) ..."
                        Assets.build binDir assetsDir distDir

                        printfn "Generating OG share cards ..."
                        let fontsDir = Path.Combine(assetsDir, "fonts")
                        OgImage.generateAll config fontsDir distDir content.Notes content.Projects

                    Ok()
                with ex ->
                    Error [ Util.exceptionDetail ex ]

            match assets with
            | Error errors -> reportErrors "Assets" errors
            | Ok() ->

            match writeSite config content distDir with
            | Error errors -> reportErrors "Rendering" errors
            | Ok() ->

            let verified =
                if generator.SkipAssets then
                    Ok()
                else
                    printfn "Verifying every reference resolves ..."
                    verifyReferences distDir

            match verified with
            | Error errors -> reportErrors "Output verification" errors
            | Ok() ->
                printfn "Done. Output in %s" distDir
                0
