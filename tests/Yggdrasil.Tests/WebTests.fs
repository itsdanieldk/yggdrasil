module Yggdrasil.Tests.WebTests

open Yggdrasil.Web
open Yggdrasil.Content
open Yggdrasil.Generate
open Yggdrasil.Tests.Support

open System.IO
open System.Text.Json
open System.Text.RegularExpressions

open Expecto

let private note () =
    (Content.getNote "understanding-functional-effect-systems" content).Value

let private fio () =
    (Content.getProject "fio" content).Value

let private matches (pattern: string) (input: string) =
    Regex.Matches(input, pattern, RegexOptions.Singleline)
    |> Seq.map (fun m -> m.Groups.[1].Value)
    |> List.ofSeq

let private ldScript (html: string) =
    match matches "<script[^>]*application/ld\\+json[^>]*>(.*?)</script>" html with
    | json :: _ -> json
    | [] -> failwith "no JSON-LD block"

let private ldNode (expectedType: string) (json: string) =
    use doc = JsonDocument.Parse json
    let root = doc.RootElement

    let el =
        match root.TryGetProperty "@graph" with
        | true, graph ->
            graph.EnumerateArray()
            |> Seq.find (fun n -> n.GetProperty("@type").GetString() = expectedType)
        | _ -> root

    el.Clone()

[<Tests>]
let tests =
    testList "Web" [
        testList "components" [
            test "arrow_card renders a note's title, description, reading time and link" {
                // Arrange
                let n = note ()

                // Act
                let html = renderNode (Components.arrowCard (FeedEntry.Note n))

                // Assert
                Expect.stringContains html n.Title "title"
                Expect.stringContains html n.Description "description"
                Expect.stringContains html n.ReadingTime "reading time (notes only)"
                Expect.stringContains html $"href=\"/notes/{n.Id}\"" "link"
            }

            test "arrow_card for a project shows its link but no reading time" {
                // Arrange
                let p = fio ()

                // Act
                let html = renderNode (Components.arrowCard (FeedEntry.Project p))

                // Assert
                Expect.stringContains html $"href=\"/projects/{p.Id}\"" "link"
                Expect.isFalse (html.Contains p.ReadingTime) "no reading time on projects"
            }

            test "fragrance_card renders the name, meta line and responsive image" {
                // Arrange
                let f = List.head (Content.ownedFragrances content.Fragrances)

                // Act
                let html = renderNode (Components.fragranceCard f)
                let decoded = System.Net.WebUtility.HtmlDecode html

                // Assert
                Expect.stringContains html f.Name "name"
                Expect.stringContains decoded (String.concat " · " (Fragrance.meta f)) "meta line"
                Expect.stringContains html $"srcset=\"{f.Image} 1x, {f.Image2x} 2x\"" "srcset"
                Expect.stringContains html $"href=\"{f.Url}\"" "link"
            }

            test "social_links renders the supplied socials plus the email link" {
                // Arrange
                let socials = [ { Name = "github"; Href = "https://example.com/gh" } ]

                // Act
                let html = renderNode (Components.socialLinks { config with Socials = socials } 0)

                // Assert
                Expect.stringContains html "github" "name"
                Expect.stringContains html "href=\"https://example.com/gh\"" "href"
                Expect.stringContains html "mailto:" "email link"
            }

            test "the header renders the nav from config, in order, separated" {
                // Arrange
                let nav =
                    [ { Label = "writing"; Href = "/writing" }
                      { Label = "work"; Href = "/work" } ]

                // Act
                let html = renderNode (Components.siteHeader { config with Nav = nav } None)

                // Assert
                Expect.stringContains html "href=\"/writing\"" "first item"
                Expect.stringContains html "href=\"/work\"" "second item"
                Expect.isLessThan (html.IndexOf "/writing") (html.IndexOf "/work") "config order preserved"
                Expect.equal (html.Split(">/<").Length - 1) 1 "one separator between two items"
                Expect.isFalse (html.Contains "/fragrances") "no hardcoded sections remain"
            }

            test "the active nav item is highlighted" {
                // Act
                let html = renderNode (Components.siteHeader config (Some "about"))

                // Assert
                Expect.stringContains html "!text-accent" "active item carries the accent class"
            }

            test "tag_pill renders the tag text inside its link" {
                // Act
                let html = renderNode (Components.tagPill "F#" "/tags/fsharp")

                // Assert
                Expect.stringContains html "F#" "text"
                Expect.stringContains html "href=\"/tags/fsharp\"" "link"
            }

            test "formatted_date shows a human date and an ISO datetime attribute" {
                // Act
                let html = renderNode (Components.formattedDate (System.DateOnly(2025, 11, 2)))

                // Assert
                Expect.stringContains html "Nov 2, 2025" "human-readable"
                Expect.stringContains html "datetime=\"2025-11-02T00:00:00.000Z\"" "iso datetime attr"
            }
        ]

        testList "injection" [
            let hostile = "Pwn </script><script>alert(1)</script> & <img src=x onerror=alert(1)>"

            test "a hostile note title cannot escape the JSON-LD script block" {
                // Arrange
                let n = { note () with Title = hostile }

                // Act
                let json = JsonLd.note config n

                // Assert
                Expect.isFalse (json.Contains "</script>") "no literal closing tag survives"
                Expect.isFalse (json.Contains "<img") "no literal tag survives"

                use doc = JsonDocument.Parse json
                let headline =
                    doc.RootElement.GetProperty("@graph").EnumerateArray()
                    |> Seq.find (fun e -> e.GetProperty("@type").GetString() = "BlogPosting")
                    |> fun e -> e.GetProperty("headline").GetString()

                Expect.equal headline hostile "the title round-trips intact once decoded"
            }

            test "a hostile project title cannot escape either" {
                // Act
                let json = JsonLd.project config { fio () with Title = hostile }

                // Assert
                Expect.isFalse (json.Contains "</script>") "no literal closing tag"
                Expect.isFalse (json.Contains "<img") "no literal tag"
            }

            test "a hostile site name stays safe in the web manifest" {
                // Act
                let manifest = Feed.webmanifest { config with Name = hostile; Author = "A\\B\"C" }

                // Assert
                use doc = JsonDocument.Parse manifest
                Expect.stringContains (doc.RootElement.GetProperty("short_name").GetString()) "Pwn" "decodes back"
                Expect.isFalse (manifest.Contains "</script>") "no literal closing tag"
            }

            test "RSS stays well-formed XML with control characters in a title" {
                // Arrange
                let n = { note () with Title = "Bad\u0000ness\b & <tags>"; Description = "d\u000B" }

                let feed =
                    { content with
                        Notes = [ n ]
                        Projects = [] }

                // Act
                let rss = Feed.rss config feed

                // Assert
                let parsed = System.Xml.Linq.XDocument.Parse rss
                Expect.isFalse (rss.Contains "\u0000") "no raw NUL"
                Expect.isFalse (rss.Contains "&#8;") "controls dropped, not encoded"
                Expect.stringContains (parsed.ToString()) "Badness" "surrounding text survives"
            }

            test "the sitemap escapes URLs rather than interpolating them raw" {
                // Act
                let xml = Feed.sitemap config [ "/notes/a&b", None; "/plain", Some "2024-01-02" ]

                // Assert
                System.Xml.Linq.XDocument.Parse xml |> ignore
                Expect.stringContains xml "a&amp;b" "ampersand escaped"
                Expect.isFalse (xml.Contains "a&b<") "no raw ampersand"
            }

            test "every rendered page is structurally sound" {
                // Act & Assert
                for route in Route.all content do
                    let html = Route.render config content route
                    let where = Route.urlPath route

                    if html.StartsWith "<!DOCTYPE" then
                        Expect.stringEnds html "</html>" $"{where} closes <html>"

                        for tag in [ "<html "; "<head>"; "<body>" ] do
                            Expect.equal (html.Split(tag).Length - 1) 1 $"{where} has exactly one {tag}"
            }

            test "every rendered page has balanced script tags" {
                // Act & Assert
                for route in Route.all content do
                    let html = Route.render config content route

                    if html.StartsWith "<!DOCTYPE" then
                        let opens = html.Split("<script").Length - 1
                        let closes = html.Split("</script>").Length - 1
                        Expect.equal opens closes $"balanced <script> in {Route.urlPath route}"
            }
        ]

        testList "feeds" [
            test "rss lists every note and project, newest first" {
                // Act
                let body = Feed.rss config content
                let titles = matches "<item><title>(.*?)</title>" body

                // Assert
                Expect.stringContains body "<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss version=\"2.0\">" "prologue"
                Expect.stringContains body "<title>itsdaniel</title>" "channel title"
                Expect.equal titles.Length (Content.feedEntries content).Length "one item per feed entry"
                Expect.equal (List.head titles) (List.head (Content.feedEntries content)).Title "newest first"
            }

            test "rss channel link keeps its trailing slash; item links do not" {
                // Act
                let body = Feed.rss config content

                // Assert
                Expect.stringContains body "<link>https://itsdaniel.dk/</link>" "channel link (trailing slash)"
                Expect.stringContains body "<link>https://itsdaniel.dk/notes/understanding-functional-effect-systems</link>" "item link (no slash)"
            }

            test "rss dates are RFC 822" {
                // Act
                let body = Feed.rss config content

                // Assert
                Expect.stringContains body "<pubDate>Wed, 22 Apr 2026 00:00:00 GMT</pubDate>" "pubDate"
            }

            test "sitemap index points at the sitemap" {
                // Act
                let xml = Feed.sitemapIndex config

                // Assert
                Expect.stringContains xml "<loc>https://itsdaniel.dk/sitemap-0.xml</loc>" "loc"
            }

            test "sitemap lists static, content and tag urls, sorted" {
                // Act
                let locs = matches "<loc>(.*?)</loc>" (Feed.sitemap config (Route.sitemapEntries content))

                // Assert
                Expect.contains locs "https://itsdaniel.dk/" "home"
                Expect.contains locs "https://itsdaniel.dk/notes/understanding-functional-effect-systems" "note"
                Expect.contains locs "https://itsdaniel.dk/tags/fsharp" "tag"
                Expect.equal locs (List.sort locs) "sorted"
            }

            test "robots.txt allows everything and links the sitemap" {
                // Arrange
                let expected = "User-agent: *\nAllow: /\n\nSitemap: https://itsdaniel.dk/sitemap-index.xml"

                // Act
                let robots = Feed.robots config

                // Assert
                Expect.equal robots expected "robots"
            }

            test "sitemap carries lastmod for content pages but not static ones" {
                // Act
                let xml = Feed.sitemap config (Route.sitemapEntries content)

                // Assert
                Expect.stringContains xml "<loc>https://itsdaniel.dk/notes/understanding-functional-effect-systems</loc><lastmod>" "note has lastmod"
                Expect.stringContains xml "<url><loc>https://itsdaniel.dk/</loc></url>" "home has no lastmod"
            }

            test "rss escapes special characters in titles and descriptions" {
                // Arrange
                let escNote: Note =
                    { Id = "esc"
                      Title = "A & B <c>"
                      Description = "d \"q\" '"
                      Date = System.DateOnly(2020, 1, 1)
                      UpdatedDate = None
                      Body = ""
                      ReadingTime = "1 min read"
                      Tags = []
                      Draft = false
                      Featured = false }

                // Act
                let rss = Feed.rss config { content with Notes = [ escNote ]; Projects = [] }

                // Assert
                Expect.stringContains rss "A &amp; B &lt;c&gt;" "title escaped"
                Expect.stringContains rss "&quot;" "double quote escaped"
                Expect.stringContains rss "&#39;" "apostrophe escaped"
            }
        ]

        testList "isIndexable" [
            test "every content page is indexable" {
                // Arrange
                let indexable =
                    [ Home
                      About
                      NotesIndex
                      ProjectsIndex
                      FragrancesIndex
                      TagsIndex
                      noteRoute "understanding-functional-effect-systems"
                      projectRoute "fio"
                      tagRoute (List.head (Content.allTags content)) ]

                // Act & Assert
                for route in indexable do
                    Expect.isTrue (Route.isIndexable route) $"{route} should be indexable"
            }

            test "the 404 page and every machine-readable route are excluded" {
                // Arrange
                let excluded = [ NotFound; Rss; SitemapIndex; Sitemap; Robots; Webmanifest ]

                // Act & Assert
                for route in excluded do
                    Expect.isFalse (Route.isIndexable route) $"{route} should not be indexable"
            }

            test "the sitemap contains exactly the indexable routes, and no others" {
                // Act
                let all = Route.all content
                let entries = Route.sitemapEntries content |> List.map fst |> Set.ofList
                let expected = all |> List.filter Route.isIndexable |> List.map Route.urlPath |> Set.ofList

                // Assert
                Expect.equal entries expected "sitemap membership follows isIndexable"
                Expect.isFalse (entries.Contains "/404") "the 404 page is never in the sitemap"
                Expect.isFalse (entries.Contains "/rss.xml") "the feed is not a page"
            }

            test "no indexable route slips through with an empty URL path" {
                // Arrange
                let indexable = Route.all content |> List.filter Route.isIndexable

                // Act
                let paths = indexable |> List.map (fun route -> route, Route.urlPath route)

                // Assert
                for route, path in paths do
                    Expect.isFalse (path.EndsWith "//") $"{route} has a doubled slash"
                    Expect.notEqual path "/tags/" $"{route} collides with the tags index"
            }
        ]

        testList "routes" [
            test "every route renders to non-empty output" {
                // Act
                let outputs = [ for route in Route.all content -> route, Route.render config content route ]

                // Assert
                for route, out in outputs do
                    Expect.isTrue (out.Length > 0) $"empty output for {route}"
            }

            test "every note, project and tag page contains its title" {
                // Act & Assert (one render per note, project and tag page)
                for n in content.Notes do
                    Expect.stringContains (Route.render config content (noteRoute n.Id)) n.Title n.Id
                for p in content.Projects do
                    Expect.stringContains (Route.render config content (projectRoute p.Id)) p.Title p.Id
                for tag in Content.allTags content do
                    let out = Route.render config content (tagRoute tag)
                    Expect.stringContains out "<!DOCTYPE html>" (Util.slugifyTag tag)
            }

            test "output is directory-style so both slash forms resolve" {
                // Act
                let note = Route.outputPath (noteRoute "understanding-functional-effect-systems")
                let home = Route.outputPath Home
                let rss = Route.outputPath Rss

                // Assert
                Expect.equal note "notes/understanding-functional-effect-systems/index.html" "note"
                Expect.equal home "index.html" "home"
                Expect.equal rss "rss.xml" "rss"
            }

            test "content types match the route kind" {
                // Act
                let rss = Route.contentType Rss
                let sitemap = Route.contentType Sitemap
                let robots = Route.contentType Robots
                let home = Route.contentType Home

                // Assert
                Expect.equal rss "application/rss+xml" "rss"
                Expect.equal sitemap "application/xml" "sitemap"
                Expect.equal robots "text/plain" "robots"
                Expect.equal home "text/html" "html"
            }

            test "Route.all enumerates a show page for every note and project" {
                // Act
                let routes = Route.all content

                // Assert
                for n in content.Notes do
                    Expect.isTrue (hasNoteRoute content n.Id) n.Id
                for p in content.Projects do
                    Expect.isTrue (routes |> List.exists (function ProjectShow(x, _, _) -> x.Id = p.Id | _ -> false)) p.Id
            }
        ]

        testList "head metadata" [
            test "the home page title is descriptive and keyword-bearing" {
                // Act
                let html = Route.render config content Home

                // Assert
                Expect.stringContains html "<title>itsdaniel — Daniel Larsen &#183; Software Engineer</title>" "home title"
            }

            test "other pages are suffixed with the site name" {
                // Act
                let html = Route.render config content NotesIndex

                // Assert
                Expect.stringContains html "<title>Notes | itsdaniel</title>" "notes title"
            }

            test "canonical urls have no trailing slash" {
                // Act
                let html = Route.render config content NotesIndex

                // Assert
                Expect.stringContains html "<link rel=\"canonical\" href=\"https://itsdaniel.dk/notes\">" "canonical"
            }

            test "articles are declared as og:type article" {
                // Act
                let html = Route.render config content (noteRoute "understanding-functional-effect-systems")

                // Assert
                Expect.stringContains html "<meta property=\"og:type\" content=\"article\">" "og:type"
            }

            test "every page carries og:site_name and an image alt" {
                // Act
                let html = Route.render config content Home

                // Assert
                Expect.stringContains html "<meta property=\"og:site_name\" content=\"itsdaniel\">" "og:site_name"

                Expect.stringContains
                    html
                    "<meta property=\"og:image:alt\" content=\"itsdaniel — Daniel Larsen &#183; Software Engineer\">"
                    "og:image:alt"
            }

            test "pages reference a 1200x630 share card — default for index, per-post for articles" {
                // Act
                let home = Route.render config content Home
                let note = Route.render config content (noteRoute "understanding-functional-effect-systems")

                // Assert
                Expect.stringContains home "<meta property=\"og:image\" content=\"https://itsdaniel.dk/og/default.png\">" "home default card"
                Expect.stringContains home "<meta property=\"og:image:width\" content=\"1200\">" "width"
                Expect.stringContains home "<meta property=\"og:image:height\" content=\"630\">" "height"

                Expect.stringContains
                    note
                    "<meta property=\"og:image\" content=\"https://itsdaniel.dk/og/notes/understanding-functional-effect-systems.png\">"
                    "per-note card"
            }

            test "the non-standard meta name=title is not emitted" {
                // Act
                let html = Route.render config content Home

                // Assert
                Expect.isFalse (html.Contains "<meta name=\"title\"") "no meta name=title"
            }

            test "article pages emit article:* tags, one per note tag" {
                // Arrange
                let note = (Content.getNote "understanding-functional-effect-systems" content).Value

                // Act
                let html = Route.render config content (noteRoute note.Id)

                // Assert
                Expect.stringContains html $"<meta property=\"article:published_time\" content=\"{DateParser.toIsoDatetime note.Date}\">" "published_time"
                Expect.stringContains html "<meta property=\"article:author\" content=\"Daniel Larsen\">" "author"
                Expect.equal (matches "property=\"article:tag\" content=\"([^\"]*)\"" html) note.Tags "one meta per tag, in order"
            }

            test "non-article pages emit no article:* tags" {
                // Act
                let html = Route.render config content Home

                // Assert
                Expect.isFalse (html.Contains "property=\"article:") "home has no article tags"
            }
        ]

        testList "not found" [
            test "the 404 page keeps the site chrome" {
                // Act
                let body = Layouts.render (Views.NotFound.page config)

                // Assert
                Expect.stringContains body "Go to home" "cta"
                Expect.stringContains body "href=\"/notes\"" "notes link"
                Expect.isFalse (body.Contains "rel=\"canonical\"") "noindex pages omit canonical"
            }

            test "the 404 page is marked noindex" {
                // Act
                let body = Layouts.render (Views.NotFound.page config)

                // Assert
                Expect.stringContains body "<meta name=\"robots\" content=\"noindex, follow\">" "noindex"
            }
        ]

        testList "structured data" [
            test "JSON-LD is valid JSON with the expected type" {
                // Arrange
                let cases =
                    [ Home, "WebSite"
                      About, "Person"
                      noteRoute "understanding-functional-effect-systems", "BlogPosting"
                      projectRoute "fio", "CreativeWork" ]

                // Act
                let parsed =
                    [ for route, expectedType in cases ->
                        let el = ldNode expectedType (ldScript (Route.render config content route))
                        route, expectedType, el.GetProperty("@type").GetString() ]

                // Assert
                for route, expectedType, actualType in parsed do
                    Expect.equal actualType expectedType $"{route}"
            }

            test "a note's BlogPosting carries an ImageObject, language, and keywords" {
                // Act
                let json = ldScript (Route.render config content (noteRoute "understanding-functional-effect-systems"))
                let bp = ldNode "BlogPosting" json
                let image = bp.GetProperty "image"

                // Assert
                Expect.equal (image.GetProperty("@type").GetString()) "ImageObject" "image is an ImageObject"

                Expect.stringContains
                    (image.GetProperty("url").GetString())
                    "/og/notes/understanding-functional-effect-systems.png"
                    "image url is the OG card"

                Expect.equal (image.GetProperty("width").GetInt32()) 1200 "image width"
                Expect.equal (bp.GetProperty("inLanguage").GetString()) "en-US" "inLanguage"
                Expect.isTrue (bp.GetProperty("keywords").GetString().Length > 0) "keywords present"
            }

            test "an article's publisher is an Organization with a logo" {
                // Act
                let json = ldScript (Route.render config content (noteRoute "understanding-functional-effect-systems"))
                let publisher = (ldNode "BlogPosting" json).GetProperty "publisher"

                // Assert
                Expect.equal (publisher.GetProperty("@type").GetString()) "Organization" "publisher is an Organization"

                Expect.stringContains
                    (publisher.GetProperty("logo").GetProperty("url").GetString())
                    "/favicon/android-chrome-512x512.png"
                    "logo url"
            }

            test "article pages carry a BreadcrumbList home -> section -> title" {
                // Act
                let json = ldScript (Route.render config content (noteRoute "understanding-functional-effect-systems"))
                let crumbs = ldNode "BreadcrumbList" json

                let trail =
                    crumbs.GetProperty("itemListElement").EnumerateArray()
                    |> Seq.map (fun e -> e.GetProperty("position").GetInt32(), e.GetProperty("name").GetString())
                    |> List.ofSeq

                // Assert
                Expect.equal
                    trail
                    [ 1, "Home"; 2, "Notes"; 3, "Understanding Functional Effect Systems" ]
                    "breadcrumb trail"
            }

            test "website and person JSON-LD carry their headline fields" {
                // Act
                use wdoc = JsonDocument.Parse(JsonLd.website config)
                use pdoc = JsonDocument.Parse(JsonLd.person config)
                let inLanguage = wdoc.RootElement.GetProperty("inLanguage").GetString()

                let knows =
                    pdoc.RootElement.GetProperty("knowsAbout").EnumerateArray()
                    |> Seq.map (fun e -> e.GetString())
                    |> List.ofSeq

                // Assert
                Expect.equal inLanguage "en-US" "website inLanguage"
                Expect.contains knows "F#" "person knowsAbout includes F#"
            }
        ]

        testList "generate" [
            test "writeSite writes every route plus the 404 page into the output dir" {
                // Arrange
                let tmp = Path.Combine(Path.GetTempPath(), "yggdrasil-writesite-" + System.Guid.NewGuid().ToString("N"))
                let noteId = (List.head content.Notes).Id
                let projectId = (List.head content.Projects).Id

                try
                    // Act
                    let result = Program.writeSite config content tmp

                    // Assert
                    match result with
                    | Ok() -> ()
                    | Error errors -> failtestf "writeSite reported: %s" (String.concat "; " errors)

                    Expect.isTrue (File.Exists(Path.Combine(tmp, "index.html"))) "home"
                    Expect.isTrue (File.Exists(Path.Combine(tmp, "notes", noteId, "index.html"))) "note page"
                    Expect.isTrue (File.Exists(Path.Combine(tmp, "projects", projectId, "index.html"))) "project page"
                    Expect.isTrue (File.Exists(Path.Combine(tmp, "rss.xml"))) "rss"
                    Expect.isTrue (File.Exists(Path.Combine(tmp, "404.html"))) "404"
                finally
                    if Directory.Exists tmp then Directory.Delete(tmp, true)
            }

            test "a failure in the asset phase is reported, not thrown out of main" {
                // Arrange
                let tmp = Path.Combine(Path.GetTempPath(), "yggdrasil-assets-" + System.Guid.NewGuid().ToString("N"))
                Directory.CreateDirectory tmp |> ignore

                try
                    File.Copy(Path.Combine(projectRoot, "global.json"), Path.Combine(tmp, "global.json"))
                    File.Copy(Path.Combine(projectRoot, "site.yaml"), Path.Combine(tmp, "site.yaml"))

                    let grammars = Path.Combine(tmp, "assets", "grammars")
                    Directory.CreateDirectory grammars |> ignore

                    for file in Directory.GetFiles contentPaths.GrammarRoot do
                        File.Copy(file, Path.Combine(grammars, Path.GetFileName file))

                    for id in SiteContent.requiredPages do
                        let dir = Path.Combine(tmp, "content", "pages", id)
                        Directory.CreateDirectory dir |> ignore
                        File.WriteAllText(Path.Combine(dir, "index.md"), $"---\ntitle: {id}\ndescription: D\n---\nbody\n")

                    let loaded =
                        SiteContent.load { ContentRoot = Path.Combine(tmp, "content"); GrammarRoot = grammars }

                    Expect.isTrue (Result.isOk loaded) "the content itself loads"

                    // Act
                    let exitCode = Program.main [| tmp |]

                    // Assert
                    Expect.equal exitCode 1 "the missing static/ directory fails the build"
                    Expect.isTrue (Directory.Exists(Path.Combine(tmp, "dist"))) "it got as far as the asset phase"
                finally
                    if Directory.Exists tmp then Directory.Delete(tmp, true)
            }
        ]

        testList "reference verification" [

            let withDist (files: (string * string) list) (f: string -> unit) =
                let root = Path.Combine(Path.GetTempPath(), $"yggdrasil-verify-{System.Guid.NewGuid():N}")

                try
                    for relative, contents in files do
                        let path = Path.Combine(root, relative)
                        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
                        File.WriteAllText(path, contents)

                    f root
                finally
                    if Directory.Exists root then Directory.Delete(root, true)

            let expectProblems label result =
                match result with
                | Ok() -> failtestf "%s: expected unresolved references, got Ok" label
                | Error(problems: string list) -> String.concat "\n" problems

            test "output whose every reference resolves passes" {
                // Arrange
                withDist
                    [ "index.html", """<img src="/a.png"><a href="/notes/one">n</a><a href="/">home</a>"""
                      "a.png", "x"
                      "notes/one/index.html", "<p>one</p>" ]
                    (fun root ->
                        // Act
                        let result = Program.verifyReferences root

                        // Assert
                        match result with
                        | Ok() -> ()
                        | Error problems -> failtestf "expected Ok, got: %s" (String.concat "; " problems))
            }

            test "a missing image is reported, naming both the page and the reference" {
                // Arrange
                withDist [ "fragrances/index.html", """<img src="/images/fragrances/x/bottle.png">""" ] (fun root ->
                    // Act
                    let problems = Program.verifyReferences root |> expectProblems "missing image"

                    // Assert
                    Expect.stringContains problems "fragrances" "names the page"
                    Expect.stringContains problems "/images/fragrances/x/bottle.png" "names the reference")
            }

            test "a missing srcset candidate is caught, not just the first URL" {
                // Arrange
                withDist
                    [ "index.html", """<img srcset="/b.png 1x, /b@2x.png 2x">"""
                      "b.png", "x" ]
                    (fun root ->
                        // Act
                        let problems = Program.verifyReferences root |> expectProblems "srcset"

                        // Assert
                        Expect.stringContains problems "/b@2x.png" "names the 2x variant"
                        Expect.isFalse (problems.Contains "/b.png 1x") "the resolving candidate is not reported")
            }

            test "cleanUrls shapes both resolve" {
                // Arrange
                withDist
                    [ "index.html", """<a href="/notes/one">a</a><a href="/robots.txt">b</a>"""
                      "notes/one/index.html", "x"
                      "robots.txt", "x" ]
                    (fun root ->
                        // Act
                        let result = Program.verifyReferences root

                        // Assert
                        Expect.isTrue (Result.isOk result) "extensionless and extensioned both resolve")
            }

            test "external, host-provided and in-page references are skipped" {
                // Arrange
                let refs =
                    """<a href="https://example.com">x</a><a href="mailto:a@b.c">y</a><a href="#top">z</a>"""
                    + """<img src="data:image/gif;base64,R0lGOD"><script src="/_vercel/insights/script.js"></script>"""

                withDist [ "index.html", refs ] (fun root ->
                    // Act
                    let result = Program.verifyReferences root

                    // Assert
                    Expect.isTrue (Result.isOk result) "nothing off-site is checked")
            }

            test "the committed site's real output resolves end to end" {
                // Arrange
                let tmp = Path.Combine(Path.GetTempPath(), $"yggdrasil-verify-real-{System.Guid.NewGuid():N}")

                try
                    Program.writeSite config content tmp |> ignore

                    // Act
                    let result = Program.verifyReferences tmp

                    // Assert
                    match result with
                    | Ok() -> ()
                    | Error problems ->
                        let unexpected =
                            problems
                            |> List.filter (fun p ->
                                not (p.Contains "/assets/" || p.Contains "/og/" || p.Contains "/favicon"
                                     || p.Contains "/avatar" || p.Contains "/images/" || p.Contains "/fonts/"))

                        Expect.isEmpty unexpected "every internal page link resolves"
                finally
                    if Directory.Exists tmp then Directory.Delete(tmp, true)
            }
        ]

        testList "drafts" [
            test "a draft note is excluded from routes, RSS, and the sitemap" {
                // Arrange
                let tmp = Path.Combine(Path.GetTempPath(), "yggdrasil-draft-" + System.Guid.NewGuid().ToString("N"))
                let noteDir name = Path.Combine(tmp, "notes", name)
                Directory.CreateDirectory(noteDir "live") |> ignore
                Directory.CreateDirectory(noteDir "hidden") |> ignore
                File.WriteAllText(Path.Combine(noteDir "live", "index.md"), "---\ntitle: Live\ndescription: D\ndate: 2024-01-02\n---\nprose\n")
                File.WriteAllText(Path.Combine(noteDir "hidden", "index.md"), "---\ntitle: Hidden\ndescription: D\ndate: 2024-01-03\ndraft: true\n---\nprose\n")

                for id in SiteContent.requiredPages do
                    let dir = Path.Combine(tmp, "pages", id)
                    Directory.CreateDirectory dir |> ignore
                    File.WriteAllText(Path.Combine(dir, "index.md"), $"---\ntitle: {id}\ndescription: D\n---\nbody\n")

                try
                    // Act
                    let result = SiteContent.load { ContentRoot = tmp; GrammarRoot = contentPaths.GrammarRoot }

                    // Assert
                    match result with
                    | Error errs -> failtestf "load failed: %s" (String.concat "; " errs)
                    | Ok loaded ->
                        Expect.isFalse (loaded.Notes |> List.exists (fun n -> n.Id = "hidden")) "draft absent from content.Notes"
                        Expect.isFalse (hasNoteRoute loaded "hidden") "no route for the draft"
                        Expect.isFalse ((Feed.rss config loaded).Contains "hidden") "draft absent from RSS"
                        Expect.isFalse ((Feed.sitemap config (Route.sitemapEntries loaded)).Contains "hidden") "draft absent from the sitemap"
                        Expect.isTrue (hasNoteRoute loaded "live") "the published note still has a route"
                finally
                    Directory.Delete(tmp, true)
            }
        ]
    ]
