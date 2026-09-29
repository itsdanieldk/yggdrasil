namespace Yggdrasil.Web

open Yggdrasil.Content

open System.Text.Json.Nodes

module Json =

    let s (v: string) =
        JsonValue.Create v :> JsonNode

    let i (v: int) =
        JsonValue.Create v :> JsonNode

    let node (pairs: (string * JsonNode) list) =
        let o = JsonObject()
        for k, v in pairs do
            o.[k] <- v
        o :> JsonNode

    let arr (items: JsonNode list) =
        JsonArray(List.toArray items) :> JsonNode

    let render (pairs: (string * JsonNode) list) =
        (node pairs).ToJsonString()

module JsonLd =

    let private s = Json.s
    let private i = Json.i
    let private node = Json.node
    let private arr = Json.arr

    // A function, not a value: a JsonNode can belong to only one parent, so reusing one instance
    // across documents would throw.
    let private context () = "@context", s "https://schema.org"

    let private toJson (pairs: (string * JsonNode) list) =
        Json.render (pairs @ [ context () ])

    let private toJsonGraph (nodes: JsonNode list) =
        Json.render [ context (); "@graph", arr nodes ]

    let private authorNode (config: SiteConfig) =
        node [ "@type", s "Person"; "name", s config.Author; "url", s config.BaseUrl ]

    let private sameAs (config: SiteConfig) =
        arr (config.Socials |> List.map (fun so -> s so.Href))

    let private imageObject (config: SiteConfig) (path: string) (w: int) (h: int) =
        node
            [ "@type", s "ImageObject"
              "url", s (Site.absoluteUrl config path)
              "width", i w
              "height", i h ]

    let website (config: SiteConfig)  =
        toJson
            [ "@type", s "WebSite"
              "name", s config.Name
              "url", s config.BaseUrl
              "description", s (config.Page "home").Description
              "author",
              node
                  [ "@type", s "Person"
                    "name", s config.Author
                    "url", s (Site.absoluteUrl config "/about")
                    "sameAs", sameAs config ]
              "inLanguage", s "en-US" ]

    let person (config: SiteConfig) =
        let biography =
            [ match config.Person.WorksFor with
              | Some org -> "worksFor", node [ "@type", s "Organization"; "name", s org ]
              | None -> ()

              match config.Person.AlumniOf with
              | Some school -> "alumniOf", node [ "@type", s "CollegeOrUniversity"; "name", s school ]
              | None -> ()

              match config.Person.KnowsAbout with
              | [] -> ()
              | topics -> "knowsAbout", arr (topics |> List.map s) ]

        toJson (
            [ "@type", s "Person"
              "name", s config.Author
              "url", s (Site.absoluteUrl config "/about")
              "image", imageObject config $"{config.Avatar.Path}.png" 800 800
              "jobTitle", s config.Tagline ]
            @ biography
            @ [ "sameAs", sameAs config ]
        )

    let private cardImage (config: SiteConfig) (collection: string) (id: string) =
        imageObject config (Site.ogImagePath collection id) 1200 630

    let private publisher (config: SiteConfig) =
        node
            [ "@type", s "Organization"
              "name", s config.Name
              "url", s config.BaseUrl
              "logo", imageObject config "/favicon/android-chrome-512x512.png" 512 512 ]

    let private breadcrumb (config: SiteConfig) (crumbs: (string * string) list) =
        node
            [ "@type", s "BreadcrumbList"
              "itemListElement",
              arr
                  [ for pos, (name, path) in List.indexed crumbs ->
                        node
                            [ "@type", s "ListItem"
                              "position", i (pos + 1)
                              "name", s name
                              "item", s (Site.absoluteUrl config path) ] ] ]

    let private keywords (tags: string list) =
        if List.isEmpty tags then [] else [ "keywords", s (String.concat ", " tags) ]

    let note (config: SiteConfig) (entry: Note) =
        let blogPosting =
            node (
                [ "@type", s "BlogPosting"
                  "headline", s entry.Title
                  "description", s entry.Description
                  "image", cardImage config "notes" entry.Id
                  "datePublished", s (DateParser.toIsoDatetime entry.Date)
                  "dateModified", s (DateParser.toIsoDatetime (defaultArg entry.UpdatedDate entry.Date))
                  "author", authorNode config
                  "publisher", publisher config
                  "inLanguage", s "en-US"
                  "mainEntityOfPage",
                  node [ "@type", s "WebPage"; "@id", s (Site.absoluteUrl config $"/notes/{entry.Id}") ] ]
                @ keywords entry.Tags
            )

        let crumbs =
            breadcrumb config [ "Home", "/"; "Notes", "/notes"; entry.Title, $"/notes/{entry.Id}" ]

        toJsonGraph [ blogPosting; crumbs ]

    let project (config: SiteConfig) (entry: Project) =
        let urlPair key = Option.map (fun u -> key, s u) >> Option.toList

        let creativeWork =
            node (
                [ "@type", s "CreativeWork"
                  "name", s entry.Title
                  "description", s entry.Description
                  "image", cardImage config "projects" entry.Id
                  "datePublished", s (DateParser.toIsoDatetime entry.Date)
                  "dateModified", s (DateParser.toIsoDatetime (defaultArg entry.UpdatedDate entry.Date))
                  "author", authorNode config
                  "publisher", publisher config
                  "inLanguage", s "en-US" ]
                @ keywords entry.Tags
                @ urlPair "url" entry.DemoUrl
                @ urlPair "codeRepository" entry.RepoUrl
            )

        let crumbs =
            breadcrumb config [ "Home", "/"; "Projects", "/projects"; entry.Title, $"/projects/{entry.Id}" ]

        toJsonGraph [ creativeWork; crumbs ]
