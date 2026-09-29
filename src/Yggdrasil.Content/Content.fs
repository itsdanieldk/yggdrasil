namespace Yggdrasil.Content

type SiteContent =
    { Notes: Note list
      Projects: Project list
      Fragrances: Fragrance list
      Pages: Map<string, Page> }

type ContentPaths =
    { ContentRoot: string
      GrammarRoot: string }

module Content =

    let ownedFragrances (fragrances: Fragrance list) =
        fragrances
        |> List.filter (fun f -> not f.Wishlist)
        |> List.sortBy (fun f -> -(defaultArg f.Rating 0.0), f.Name)

    let wishlistFragrances (fragrances: Fragrance list) =
        fragrances
        |> List.filter (fun f -> f.Wishlist)
        |> List.sortBy (fun f -> f.House, f.Name)

    let tagCounts (notes: Note list) (projects: Project list) =
        let noteTags = notes |> List.collect (fun n -> n.Tags)
        let projectTags = projects |> List.collect (fun p -> p.Tags)

        noteTags @ projectTags
        |> List.countBy id
        |> List.sortBy (fun (tag, count) -> -count, tag)

    let notesByYear (notes: Note list) =
        notes
        |> List.groupBy (fun n -> n.Date.Year)
        |> List.sortByDescending fst

    let allTags (content: SiteContent) =
        tagCounts content.Notes content.Projects |> List.map fst

    let tagSlugErrors (tags: string list) =
        tags
        |> List.groupBy Util.slugifyTag
        |> List.collect (fun (slug, group) ->
            let names = group |> List.map (fun t -> $"\"{t}\"") |> String.concat ", "
            [ if slug = "" then
                  $"tag {names} has no URL-safe characters, so it has no page of its own — rename it"
              elif List.length group > 1 then
                  $"tags {names} all slugify to \"{slug}\" — rename one so each tag has its own page" ])

    let getPage (id: string) (content: SiteContent) =
        match Map.tryFind id content.Pages with
        | Some page -> page
        | None -> failwith $"content/pages/{id}/index.md is missing"

    let notesWithTag (tag: string) (content: SiteContent) =
        content.Notes
        |> List.filter (fun e -> List.contains tag e.Tags)

    let projectsWithTag (tag: string) (content: SiteContent) =
        content.Projects
        |> List.filter (fun e -> List.contains tag e.Tags)

    let withNeighbours (items: 'a list) =
        let arr = List.toArray items

        arr
        |> Array.mapi (fun i item ->
            let prev = if i + 1 < arr.Length then Some arr.[i + 1] else None
            let next = if i > 0 then Some arr.[i - 1] else None
            item, prev, next)
        |> List.ofArray

    let feedEntries content =
        (content.Notes |> List.map FeedEntry.Note)
        @
        (content.Projects |> List.map FeedEntry.Project)
        |> List.sortByDescending (fun e -> e.Date)
