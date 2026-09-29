namespace Yggdrasil.Content

open System.IO

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SiteContent =

    let private entryFiles (root: string) (sub: string) =
        let dir = Path.Combine(root, sub)
        if Directory.Exists dir then
            Directory.GetDirectories dir
            |> Array.map (fun d -> Path.Combine(d, "index.md"))
            |> Array.filter File.Exists
            |> Array.sort
            |> List.ofArray
        else
            []

    let private fragranceFiles (root: string) =
        let dir = Path.Combine(root, "fragrances")
        if Directory.Exists dir then
            Directory.GetFiles(dir, "*.yaml")
            |> Array.sort
            |> List.ofArray
        else
            []

    // Only the split and the YAML parse can stop a file early, because everything after them needs their
    // output; unknown keys, field errors and body errors are then reported together.
    let private readEntry (renderer: Markdown.Renderer) (allowed: Set<string>) decode (path: string) =
        let id = Path.GetFileName(Path.GetDirectoryName path)

        result {
            let! frontmatter, rawBody = Parser.split path (File.ReadAllText path)
            let! dto = Parser.deserialize path frontmatter
            let renderedBody = renderer.Render(path, rawBody) |> Result.mapError List.singleton
            let! () = Yaml.rejectUnknownKeys path allowed frontmatter
            and! entry = decode path id dto rawBody renderedBody
            return entry
        }

    let private loadNote renderer path =
        readEntry renderer Parser.noteKeys Parser.decodeNote path

    let private loadProject renderer path =
        readEntry renderer Parser.projectKeys Parser.decodeProject path

    let private loadPage renderer path =
        let decode path id dto _ renderedBody =
            Parser.decodePage path id dto (renderedBody |> Result.map Markdown.staggerParagraphs)

        readEntry renderer Parser.pageKeys decode path

    let private loadFragrance (path: string) =
        Fragrance.decode path (Path.GetFileNameWithoutExtension path) (File.ReadAllText path)

    let private errorsOf =
        function
        | Ok _ -> []
        | Error(errors: string list list) -> List.concat errors

    let requiredPages = [ "home"; "about" ]

    let build (renderer: Markdown.Renderer) (paths: ContentPaths) =
        let pageFiles = entryFiles paths.ContentRoot "pages"
        let notes = entryFiles paths.ContentRoot "notes" |> List.map (loadNote renderer) |> collectResults
        let projects = entryFiles paths.ContentRoot "projects" |> List.map (loadProject renderer) |> collectResults
        let fragrances = fragranceFiles paths.ContentRoot |> List.map loadFragrance |> collectResults
        let pages = pageFiles |> List.map (loadPage renderer) |> collectResults

        let publishedNotes =
            notes |> Result.map (Util.published (fun (n: Note) -> n.Draft) (fun n -> n.Featured, n.Date))

        let publishedProjects =
            projects |> Result.map (Util.published (fun (p: Project) -> p.Draft) (fun p -> p.Featured, p.Date))

        // Checked against the files on disk rather than the parsed pages, so a missing page is reported
        // even when another file fails to load.
        let missingPages =
            let present = pageFiles |> List.map (fun file -> Path.GetFileName(Path.GetDirectoryName file)) |> set

            [ for id in requiredPages do
                  if not (present.Contains id) then
                      $"content/pages/{id}/index.md: required page is missing" ]

        let tagErrors =
            match publishedNotes, publishedProjects with
            | Ok notes, Ok projects -> Content.tagCounts notes projects |> List.map fst |> Content.tagSlugErrors
            | _ -> []

        match publishedNotes, publishedProjects, fragrances, pages, missingPages @ tagErrors with
        | Ok notes, Ok projects, Ok fragrances, Ok pages, [] ->
            Ok
                { Notes = notes
                  Projects = projects
                  Fragrances = fragrances |> List.filter (fun f -> not f.Draft)
                  Pages = pages |> List.map (fun (p: Page) -> p.Id, p) |> Map.ofList }
        | notes, projects, fragrances, pages, validationErrors ->
            Error(errorsOf notes @ errorsOf projects @ errorsOf fragrances @ errorsOf pages @ validationErrors)

    let loadWithHighlighter (paths: ContentPaths) =
        let highlighter = Highlight.create paths.GrammarRoot
        let renderer = Markdown.Renderer highlighter
        build renderer paths
        |> Result.map (fun content -> content, highlighter)

    let load (paths: ContentPaths) =
        loadWithHighlighter paths
        |> Result.map fst
