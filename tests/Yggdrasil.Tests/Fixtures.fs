module Yggdrasil.Tests.Fixtures

open Yggdrasil.Content

open System
open System.IO

// Format tests assert exact output, so they run against this fixed identity and content rather than
// site.yaml and content/: editing a note or the tagline must not break an unrelated test. Only the
// smoke tests in each module use the real site.

let private meta (title: string) (description: string) : PageMeta =
    { Title = title; Description = description }

let config: SiteConfig =
    { BaseUrl = "https://example.com/"
      Name = "example"
      Author = "Ada Lovelace"
      Tagline = "Analyst"
      Email = "ada@example.com"
      Avatar = { Path = "/avatar"; Alt = "A portrait" }
      Person =
        { WorksFor = Some "Analytical Engines"
          AlumniOf = None
          KnowsAbout = [ "F#"; "Mathematics" ] }
      Nav =
        [ { Label = "notes"; Href = "/notes" }
          { Label = "about"; Href = "/about" } ]
      Socials = [ { Name = "github"; Href = "https://github.com/example" } ]
      NotesOnHomepage = 3
      ProjectsOnHomepage = 3
      OgDefaultTags = [ "fixtures" ]
      Pages =
        Map
            [ "home", meta "Home" "The home page."
              "notes", meta "Notes" "The notes index."
              "projects", meta "Projects" "The projects index."
              "fragrances", meta "Fragrances" "The fragrance index."
              "tags", meta "Tags" "The tags index."
              "about", meta "About" "The about page." ] }

let note (id: string) : Note =
    { Id = id
      Title = $"A note called {id}"
      Description = "A note used as a test fixture."
      Date = DateOnly(2024, 1, 2)
      UpdatedDate = None
      Body = "<p>Body text.</p>"
      ReadingTime = "1 min read"
      Tags = [ "F#"; "testing" ]
      Draft = false
      Featured = false }

let project (id: string) : Project =
    { Id = id
      Title = $"A project called {id}"
      Description = "A project used as a test fixture."
      Date = DateOnly(2023, 6, 1)
      UpdatedDate = None
      Body = "<p>Body text.</p>"
      ReadingTime = "1 min read"
      Tags = [ "F#" ]
      Draft = false
      Featured = false
      DemoUrl = None
      RepoUrl = Some $"https://github.com/example/{id}" }

let fragrance (id: string) : Fragrance =
    { Id = id
      Name = "Fixture"
      House = "House of Tests"
      Url = $"https://example.com/fragrances/{id}"
      Rating = Some 8.5
      Note = None
      Concentration = Some "EDP"
      Image = $"/images/fragrances/{id}/bottle.png"
      Image2x = $"/images/fragrances/{id}/bottle@2x.png"
      Wishlist = false
      Draft = false }

let page (id: string) : Page =
    { Id = id
      Title = $"The {id} page"
      Description = $"The {id} page, as a test fixture."
      Heading = $"The {id} page"
      Emoji = None
      Body = "<p>Page body.</p>" }

let content: SiteContent =
    { Notes = [ note "a-note" ]
      Projects = [ project "a-project" ]
      Fragrances = [ fragrance "a-bottle" ]
      Pages = Map [ for id in SiteContent.requiredPages -> id, page id ] }

// The required pages as (relative path, contents), for a content root that should otherwise load.
let requiredPageFiles =
    [ for id in SiteContent.requiredPages -> $"pages/{id}/index.md", $"---\ntitle: {id}\ndescription: D\n---\nbody\n" ]

// Writes the files under a fresh temporary content root, runs f against it, then deletes the root.
let withContentRoot (files: (string * string) list) (f: ContentPaths -> 'a) =
    let root = Path.Combine(Path.GetTempPath(), $"yggdrasil-content-{Guid.NewGuid():N}")
    Directory.CreateDirectory root |> ignore

    try
        for relative, text in files do
            let path = Path.Combine(root, relative)
            Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
            File.WriteAllText(path, text)

        f { ContentRoot = root; GrammarRoot = Support.contentPaths.GrammarRoot }
    finally
        Directory.Delete(root, true)
