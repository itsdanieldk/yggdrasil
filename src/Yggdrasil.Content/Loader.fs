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

    let private readEntry (renderer: Markdown.Renderer) (allowed: Set<string>) (path: string) =
        result {
            let id = Path.GetFileName(Path.GetDirectoryName path)
            let contents = File.ReadAllText path
            let! frontmatter, rawBody = Parser.split path contents
            let! () = Parser.rejectUnknownKeys path allowed frontmatter
            let! dto = Parser.deserialize path frontmatter
            let! renderedBody = renderer.Render(path, rawBody)
            return id, dto, rawBody, renderedBody
        }

    let private loadNote (renderer: Markdown.Renderer) (path: string) =
        readEntry renderer Parser.noteKeys path
        |> Result.bind (fun (id, dto, rawBody, renderedBody) ->
            Parser.decodeNote path id dto rawBody renderedBody)

    let private loadProject (renderer: Markdown.Renderer) (path: string) =
        readEntry renderer Parser.projectKeys path
        |> Result.bind (fun (id, dto, rawBody, renderedBody) ->
            Parser.decodeProject path id dto rawBody renderedBody)

    let private loadPage (renderer: Markdown.Renderer) (path: string) =
        readEntry renderer Parser.pageKeys path
        |> Result.bind (fun (id, dto, _, renderedBody) ->
            Parser.decodePage path id dto (Markdown.staggerParagraphs renderedBody))

    let private loadFragrance (path: string) =
        Fragrance.decode path (Path.GetFileNameWithoutExtension path) (File.ReadAllText path)

    let private errorsOf =
        function
        | Ok _ -> []
        | Error errors -> errors

    let requiredPages = [ "home"; "about" ]

    let build (renderer: Markdown.Renderer) (paths: ContentPaths) =
        let noteResults = entryFiles paths.ContentRoot "notes" |> List.map (loadNote renderer)
        let projectResults = entryFiles paths.ContentRoot "projects" |> List.map (loadProject renderer)
        let fragranceResults = fragranceFiles paths.ContentRoot |> List.map loadFragrance
        let pageResults = entryFiles paths.ContentRoot "pages" |> List.map (loadPage renderer)

        match
            collectResults noteResults,
            collectResults projectResults,
            collectResults fragranceResults,
            collectResults pageResults
        with
        | Ok notes, Ok projects, Ok fragrances, Ok pages ->
            let pageMap =
                pages
                |> List.map (fun (p: Page) -> p.Id, p)
                |> Map.ofList

            let publishedNotes =
                Util.published (fun (n: Note) -> n.Draft) (fun n -> n.Featured, n.Date) notes

            let publishedProjects =
                Util.published (fun (p: Project) -> p.Draft) (fun p -> p.Featured, p.Date) projects

            let liveFragrances =
                fragrances
                |> List.filter (fun f -> not f.Draft)

            let validationErrors =
                [ for id in requiredPages do
                      if not (Map.containsKey id pageMap) then
                          $"content/pages/{id}/index.md: required page is missing"
                  yield!
                      Content.tagCounts publishedNotes publishedProjects
                      |> List.map fst
                      |> Content.tagSlugErrors ]

            if not (List.isEmpty validationErrors) then
                Error validationErrors
            else

            Ok
                { Notes = publishedNotes
                  Projects = publishedProjects
                  Fragrances = liveFragrances
                  Pages = pageMap }
        | notes, projects, fragrances, pages ->
            Error(errorsOf notes @ errorsOf projects @ errorsOf fragrances @ errorsOf pages)

    let loadWithHighlighter (paths: ContentPaths) =
        let highlighter = Highlight.create paths.GrammarRoot
        let renderer = Markdown.Renderer highlighter
        build renderer paths
        |> Result.map (fun content -> content, highlighter)

    let load (paths: ContentPaths) =
        loadWithHighlighter paths
        |> Result.map fst
