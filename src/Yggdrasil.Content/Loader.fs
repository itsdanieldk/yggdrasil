namespace Yggdrasil.Content

open System.IO
open System.Text.RegularExpressions

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module SiteContent =

    let requiredPages = [ "home"; "about" ]

    let private collections = [ "notes"; "projects"; "pages"; "fragrances" ]

    // Discovery is strict because whatever it passes over disappears from the site without an error: a
    // misnamed index file, a .yml fragrance, a folder under a mistyped collection name.

    // Dot-names (.DS_Store, editor state) are never content.
    let private visible (paths: string array) =
        paths
        |> Array.filter (fun path -> not (Path.GetFileName(path).StartsWith '.'))
        |> Array.sort
        |> List.ofArray

    let private checkSlug (path: string) (name: string) =
        if Util.isValidSlug name then
            Ok()
        else
            // iCloud names a conflict copy "name 2", which would otherwise publish as a second entry.
            let hint = if Regex.IsMatch(name, @" \d+$") then " (an iCloud conflict copy?)" else ""
            let rule = "names become URLs, so use lowercase letters, digits and single hyphens"
            Error [ $"{path}: \"{name}\" is not a valid slug{hint}; {rule}" ]

    // One Result per entry folder, plus an error for every stray file beside them.
    let private entryFiles (root: string) (collection: string) =
        let dir = Path.Combine(root, collection)

        let entry (folder: string) =
            // Matched by name rather than File.Exists: macOS finds "Index.md" case-insensitively and Linux
            // does not, so the entry would build locally and be missing from the deployed site.
            let hasIndex = Directory.GetFiles folder |> Array.exists (fun file -> Path.GetFileName file = "index.md")

            result {
                let! () = checkSlug folder (Path.GetFileName folder)
                and! () = if hasIndex then Ok() else Error [ $"{folder}: no index.md" ]
                return Path.Combine(folder, "index.md")
            }

        let stray (file: string) =
            Error [ $"{file}: unexpected file; each entry in {collection} is a folder holding index.md" ]

        if Directory.Exists dir then
            List.map entry (visible (Directory.GetDirectories dir))
            @ List.map stray (visible (Directory.GetFiles dir))
        else
            []

    let private fragranceFiles (root: string) =
        let dir = Path.Combine(root, "fragrances")

        let fragrance (file: string) =
            if Path.GetExtension file = ".yaml" then
                checkSlug file (Path.GetFileNameWithoutExtension file) |> Result.map (fun () -> file)
            else
                Error [ $"{file}: unexpected file; each fragrance is a .yaml file" ]

        let folder (path: string) =
            Error [ $"{path}: unexpected folder; each fragrance is a .yaml file" ]

        if Directory.Exists dir then
            List.map fragrance (visible (Directory.GetFiles dir))
            @ List.map folder (visible (Directory.GetDirectories dir))
        else
            []

    let private knownPage (file: string) =
        let folder = Path.GetDirectoryName file
        let id = Path.GetFileName folder

        if List.contains id requiredPages then
            Ok file
        else
            let supported = requiredPages |> List.map (fun page -> $"\"{page}\"") |> String.concat " and "
            Error [ $"{folder}: unknown page \"{id}\"; only {supported} are supported" ]

    let private layoutErrors (root: string) =
        if Directory.Exists root then
            [ for folder in visible (Directory.GetDirectories root) do
                  let name = Path.GetFileName folder

                  if not (List.contains name collections) then
                      $"{folder}: unknown folder \"{name}\"; content/ holds notes, projects, pages and fragrances"

              for file in visible (Directory.GetFiles root) do
                  $"{file}: unexpected file in content/" ]
        else
            []

    // Only the split and the YAML parse can stop a file early, because everything after them needs their
    // output; unknown keys, field errors and body errors are then reported together.
    let private readEntry (renderer: Markdown.Renderer) (allowed: Set<string>) decode (path: string) =
        let id = Path.GetFileName(Path.GetDirectoryName path)

        result {
            let! frontmatter, rawBody = Parser.split path (File.ReadAllText path)
            let! dto = Parser.deserialize path frontmatter
            let renderedBody = renderer.Render(path, rawBody)
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

    let build (renderer: Markdown.Renderer) (paths: ContentPaths) =
        let root = paths.ContentRoot
        let load loader files = files |> List.map (Result.bind loader) |> collectResults
        let pageFiles = entryFiles root "pages" |> List.map (Result.bind knownPage)
        let notes = entryFiles root "notes" |> load (loadNote renderer)
        let projects = entryFiles root "projects" |> load (loadProject renderer)
        let fragrances = fragranceFiles root |> load loadFragrance
        let pages = pageFiles |> load (loadPage renderer)

        let publishedNotes =
            notes |> Result.map (Util.published (fun (n: Note) -> n.Draft) (fun n -> n.Featured, n.Date))

        let publishedProjects =
            projects |> Result.map (Util.published (fun (p: Project) -> p.Draft) (fun p -> p.Featured, p.Date))

        // Checked against the files on disk rather than the parsed pages, so a missing page is reported
        // even when another file fails to load.
        let missingPages =
            let present =
                pageFiles
                |> List.choose Result.toOption
                |> List.map (fun file -> Path.GetFileName(Path.GetDirectoryName file))
                |> set

            [ for id in requiredPages do
                  if not (present.Contains id) then
                      $"content/pages/{id}/index.md: required page is missing" ]

        let tagErrors =
            match publishedNotes, publishedProjects with
            | Ok notes, Ok projects -> Content.tagCounts notes projects |> List.map fst |> Content.tagSlugErrors
            | _ -> []

        match publishedNotes, publishedProjects, fragrances, pages, layoutErrors root @ missingPages @ tagErrors with
        | Ok notes, Ok projects, Ok fragrances, Ok pages, [] ->
            // Both are present: missingPages is empty, and every page file parsed.
            let page id = pages |> List.find (fun (p: Page) -> p.Id = id)

            Ok
                { Notes = notes
                  Projects = projects
                  Fragrances = fragrances |> List.filter (fun f -> not f.Draft)
                  Home = page "home"
                  About = page "about" }
        | notes, projects, fragrances, pages, validationErrors ->
            Error(errorsOf notes @ errorsOf projects @ errorsOf fragrances @ errorsOf pages @ validationErrors)

    let load (paths: ContentPaths) =
        build (Markdown.Renderer(Highlight.create paths.GrammarRoot)) paths
