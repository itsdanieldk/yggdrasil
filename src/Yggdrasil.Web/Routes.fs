namespace Yggdrasil.Web

open Yggdrasil.Content

type Route =
    | Home
    | About
    | NotesIndex
    | NoteShow of note: Note * prev: Note option * next: Note option
    | ProjectsIndex
    | ProjectShow of project: Project * prev: Project option * next: Project option
    | FragrancesIndex
    | TagsIndex
    | TagShow of tag: string * notes: Note list * projects: Project list
    | NotFound
    | Rss
    | SitemapIndex
    | Sitemap
    | Robots
    | Webmanifest

module Route =

    let urlPath (route: Route) =
        match route with
        | Home -> "/"
        | About -> "/about"
        | NotesIndex -> "/notes"
        | NoteShow(n, _, _) -> $"/notes/{n.Id}"
        | ProjectsIndex -> "/projects"
        | ProjectShow(p, _, _) -> $"/projects/{p.Id}"
        | FragrancesIndex -> "/fragrances"
        | TagsIndex -> "/tags"
        | TagShow(tag, _, _) -> $"/tags/{Util.slugifyTag tag}"
        | Rss -> "/rss.xml"
        | SitemapIndex -> "/sitemap-index.xml"
        | Sitemap -> "/sitemap-0.xml"
        | NotFound -> "/404"
        | Robots -> "/robots.txt"
        | Webmanifest -> "/site.webmanifest"

    let outputPath (route: Route) =
        match route with
        | Home -> "index.html"
        | NotFound -> "404.html"
        | Rss
        | SitemapIndex
        | Sitemap
        | Robots
        | Webmanifest -> (urlPath route).TrimStart '/'
        | About
        | NotesIndex
        | NoteShow _
        | ProjectsIndex
        | ProjectShow _
        | FragrancesIndex
        | TagsIndex
        | TagShow _ -> (urlPath route).TrimStart '/' + "/index.html"

    let isIndexable (route: Route) =
        match route with
        | Home
        | About
        | NotesIndex
        | NoteShow _
        | ProjectsIndex
        | ProjectShow _
        | FragrancesIndex
        | TagsIndex
        | TagShow _ -> true
        | NotFound
        | Rss
        | SitemapIndex
        | Sitemap
        | Robots
        | Webmanifest -> false

    let all (content: SiteContent) =
        [ Home
          About
          NotesIndex
          ProjectsIndex
          FragrancesIndex
          TagsIndex
          yield! content.Notes
            |> Content.withNeighbours
            |> List.map NoteShow
          yield! content.Projects
            |> Content.withNeighbours
            |> List.map ProjectShow
          yield!
              Content.allTags content
              |> List.map (fun t -> TagShow(t, Content.notesWithTag t content, Content.projectsWithTag t content))
          NotFound
          Rss
          SitemapIndex
          Sitemap
          Robots
          Webmanifest ]

    let sitemapEntries (content: SiteContent) =
        all content
        |> List.filter isIndexable
        |> List.map (fun route ->
            let modified =
                match route with
                | NoteShow(n, _, _) -> Some(Feed.lastmod (defaultArg n.UpdatedDate n.Date))
                | ProjectShow(p, _, _) -> Some(Feed.lastmod (defaultArg p.UpdatedDate p.Date))
                | _ -> None
            urlPath route, modified)

    let render (config: SiteConfig) (content: SiteContent) (route: Route) =
        match route with
        | Home -> Layouts.render (Views.Page.home config content)
        | About -> Layouts.render (Views.Page.about config content)
        | NotesIndex -> Layouts.render (Views.Note.index config (Content.notesByYear content.Notes))
        | NoteShow(note, prev, next) -> Layouts.render (Views.Note.show config note prev next)
        | ProjectsIndex -> Layouts.render (Views.Project.index config content.Projects)
        | ProjectShow(project, prev, next) -> Layouts.render (Views.Project.show config project prev next)
        | FragrancesIndex ->
            Layouts.render (
                Views.Fragrance.index
                    config
                    (Content.ownedFragrances content.Fragrances)
                    (Content.wishlistFragrances content.Fragrances)
            )
        | TagsIndex -> Layouts.render (Views.Tag.index config (Content.tagCounts content.Notes content.Projects))
        | TagShow(tag, notes, projects) -> Layouts.render (Views.Tag.show config tag notes projects)
        | Rss -> Feed.rss config content
        | SitemapIndex -> Feed.sitemapIndex config
        | Sitemap -> Feed.sitemap config (sitemapEntries content)
        | NotFound -> Layouts.render (Views.NotFound.page config)
        | Robots -> Feed.robots config
        | Webmanifest -> Feed.webmanifest config content.Home.Description
