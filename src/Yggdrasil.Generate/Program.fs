module Yggdrasil.Generate.Program

open Yggdrasil.Web
open Yggdrasil.Content

open System
open System.IO
open System.Collections.Concurrent
open System.Text.RegularExpressions

let rec private copyDir (src: string) (dst: string) =
    Directory.CreateDirectory dst |> ignore

    for file in Directory.GetFiles src do
        File.Copy(file, Path.Combine(dst, Path.GetFileName file), true)

    for dir in Directory.GetDirectories src do
        copyDir dir (Path.Combine(dst, Path.GetFileName dir))

// Emptied rather than overwritten, so a page whose source was deleted can't survive from an earlier build.
let private recreateDir (dir: string) =
    if Directory.Exists dir then
        Directory.Delete(dir, true)

    Directory.CreateDirectory dir |> ignore

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

let private referenceRegex =
    Regex "\\b(?:href|src)=\"([^\"]+)\"|\\bsrcset=\"([^\"]+)\""

let verifyReferences (distDir: string) =
    let resolves (reference: string) =
        let bare = reference.Split('#').[0].Split('?').[0]
        let decoded = Uri.UnescapeDataString bare
        let target = Path.Combine(distDir, decoded.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))

        // Only the shapes Vercel serves without cleanUrls: `/foo` resolving to `foo.html` would pass here
        // and 404 in production.
        if decoded.EndsWith '/' then
            File.Exists(Path.Combine(target, "index.html"))
        else
            File.Exists target || File.Exists(Path.Combine(target, "index.html"))

    let isExternal (reference: string) =
        reference = ""
        || reference.StartsWith '#'
        || [ "http://"; "https://"; "mailto:"; "data:" ]
           |> List.exists (fun scheme -> reference.StartsWith(scheme, StringComparison.Ordinal))

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

// Tags a stage's errors with the name main reports them under.
let private stage (label: string) =
    Result.mapError (fun (errors: string list) -> label, errors)

let private run argv =
    result {
        let! projectRoot = Config.resolveProjectRoot argv |> Result.mapError List.singleton |> stage "Configuration"
        let! siteConfig = Site.load (Path.Combine(projectRoot, "site.yaml")) |> stage "Site config"
        let! generator = Config.resolve siteConfig.BaseUrl |> Result.mapError List.singleton |> stage "Configuration"

        let contentRoot = Path.Combine(projectRoot, "content")
        let staticRoot = Path.Combine(projectRoot, "static")
        let assetsDir = Path.Combine(projectRoot, "assets")
        let binDir = Path.Combine(projectRoot, ".bin")
        let distDir = Path.Combine(projectRoot, "dist")

        let paths =
            { ContentRoot = contentRoot
              GrammarRoot = Path.Combine(assetsDir, "grammars") }

        let config = { siteConfig with BaseUrl = generator.BaseUrl }

        printfn "Loading content from %s" contentRoot
        let! content = SiteContent.load paths |> stage "Content load"

        printfn
            "  %d notes, %d projects, %d fragrances"
            content.Notes.Length
            content.Projects.Length
            content.Fragrances.Length

        recreateDir distDir

        let assets =
            try
                printfn "Copying static assets ..."
                copyDir staticRoot distDir

                if generator.SkipAssets then
                    printfn "SKIP_ASSETS set — skipping CSS/JS and OG-card build."
                    Ok()
                else
                    printfn "Building CSS/JS (standalone tailwind + esbuild) ..."
                    Assets.build binDir assetsDir distDir

                    printfn "Generating OG share cards ..."
                    let fontsDir = Path.Combine(assetsDir, "fonts")
                    OgImage.generateAll config fontsDir distDir content.Notes content.Projects
            with ex ->
                Error [ Util.exceptionDetail ex ]

        do! assets |> stage "Assets"
        do! writeSite config content distDir |> stage "Rendering"

        let verified =
            if generator.SkipAssets then
                Ok()
            else
                printfn "Verifying every reference resolves ..."
                verifyReferences distDir

        do! verified |> stage "Output verification"
        return distDir
    }

[<EntryPoint>]
let main argv =
    match run argv with
    | Ok distDir ->
        printfn "Done. Output in %s" distDir
        0
    | Error(label, errors) ->
        eprintfn "%s failed with %d error(s):" label errors.Length
        errors |> List.iter (eprintfn "  - %s")
        1
