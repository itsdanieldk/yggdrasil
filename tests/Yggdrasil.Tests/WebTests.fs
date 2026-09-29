module Yggdrasil.Tests.WebTests

open Yggdrasil.Web
open Yggdrasil.Content
open Yggdrasil.Generate
open Yggdrasil.Tests.Support

open System.IO
open System.Text.Json
open System.Text.RegularExpressions

open Expecto

let private note () = Fixtures.note "a-note"

let private project () = Fixtures.project "a-project"

let private noteShow = NoteShow(note (), None, None)

let private render (route: Route) =
    Route.render Fixtures.config Fixtures.content route

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
                let p = project ()

                // Act
                let html = renderNode (Components.arrowCard (FeedEntry.Project p))

                // Assert
                Expect.stringContains html $"href=\"/projects/{p.Id}\"" "link"
                Expect.isFalse (html.Contains p.ReadingTime) "no reading time on projects"
            }

            test "fragrance_card renders the name, meta line and responsive image" {
                // Arrange
                let f = Fixtures.fragrance "a-bottle"

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
                let html = renderNode (Components.socialLinks { Fixtures.config with Socials = socials } 0)

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
                let html = renderNode (Components.siteHeader { Fixtures.config with Nav = nav } None)

                // Assert
                Expect.stringContains html "href=\"/writing\"" "first item"
                Expect.stringContains html "href=\"/work\"" "second item"
                Expect.isLessThan (html.IndexOf "/writing") (html.IndexOf "/work") "config order preserved"
                Expect.equal (html.Split(">/<").Length - 1) 1 "one separator between two items"
                Expect.isFalse (html.Contains "/fragrances") "no hardcoded sections remain"
            }

            test "the active nav item is highlighted" {
                // Act
                let html = renderNode (Components.siteHeader Fixtures.config (Some "about"))

                // Assert
                Expect.isTrue
                    (Regex.IsMatch(html, "<a href=\"/about\" class=\"[^\"]*!text-accent"))
                    "the about link carries the accent class"

                Expect.equal (Regex.Matches(html, "!text-accent").Count) 1 "no other item is highlighted"
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
                let json = JsonLd.note Fixtures.config n

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
                let json = JsonLd.project Fixtures.config { project () with Title = hostile }

                // Assert
                Expect.isFalse (json.Contains "</script>") "no literal closing tag"
                Expect.isFalse (json.Contains "<img") "no literal tag"
            }

            test "a hostile site name stays safe in the web manifest" {
                // Act
                let manifest = Feed.webmanifest { Fixtures.config with Name = hostile; Author = "A\\B\"C" }

                // Assert
                use doc = JsonDocument.Parse manifest
                Expect.stringContains (doc.RootElement.GetProperty("short_name").GetString()) "Pwn" "decodes back"
                Expect.isFalse (manifest.Contains "</script>") "no literal closing tag"
            }

            test "RSS stays well-formed XML with control characters in a title" {
                // Arrange
                let n = { note () with Title = "Bad\u0000ness\b & <tags>"; Description = "d\u000B" }

                let feed =
                    { Fixtures.content with
                        Notes = [ n ]
                        Projects = [] }

                // Act
                let rss = Feed.rss Fixtures.config feed

                // Assert
                let parsed = System.Xml.Linq.XDocument.Parse rss
                Expect.isFalse (rss.Contains "\u0000") "no raw NUL"
                Expect.isFalse (rss.Contains "&#8;") "controls dropped, not encoded"
                Expect.stringContains (parsed.ToString()) "Badness" "surrounding text survives"
            }

            test "the sitemap escapes URLs rather than interpolating them raw" {
                // Act
                let xml = Feed.sitemap Fixtures.config [ "/notes/a&b", None; "/plain", Some "2024-01-02" ]

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
                let body = Feed.rss Fixtures.config Fixtures.content
                let titles = matches "<item><title>(.*?)</title>" body
                let entries = Content.feedEntries Fixtures.content

                // Assert
                Expect.stringContains body "<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss version=\"2.0\">" "prologue"
                Expect.stringContains body "<title>example</title>" "channel title"
                Expect.equal titles.Length entries.Length "one item per feed entry"
                Expect.equal (List.head titles) (List.head entries).Title "newest first"
            }

            test "rss channel link keeps its trailing slash; item links do not" {
                // Act
                let body = Feed.rss Fixtures.config Fixtures.content

                // Assert
                Expect.stringContains body "<link>https://example.com/</link>" "channel link (trailing slash)"
                Expect.stringContains body "<link>https://example.com/notes/a-note</link>" "item link (no slash)"
            }

            test "rss dates are RFC 822" {
                // Act
                let body = Feed.rss Fixtures.config Fixtures.content

                // Assert
                Expect.stringContains body "<pubDate>Tue, 02 Jan 2024 00:00:00 GMT</pubDate>" "pubDate"
            }

            test "sitemap index points at the sitemap" {
                // Act
                let xml = Feed.sitemapIndex Fixtures.config

                // Assert
                Expect.stringContains xml "<loc>https://example.com/sitemap-0.xml</loc>" "loc"
            }

            test "sitemap lists static, content and tag urls, sorted" {
                // Act
                let xml = Feed.sitemap Fixtures.config (Route.sitemapEntries Fixtures.content)
                let locs = matches "<loc>(.*?)</loc>" xml

                // Assert
                Expect.contains locs "https://example.com/" "home"
                Expect.contains locs "https://example.com/notes/a-note" "note"
                Expect.contains locs "https://example.com/tags/fsharp" "tag"
                Expect.equal locs (List.sort locs) "sorted"
            }

            test "robots.txt allows everything and links the sitemap" {
                // Arrange
                let expected = "User-agent: *\nAllow: /\n\nSitemap: https://example.com/sitemap-index.xml"

                // Act
                let robots = Feed.robots Fixtures.config

                // Assert
                Expect.equal robots expected "robots"
            }

            test "sitemap carries lastmod for content pages but not static ones" {
                // Act
                let xml = Feed.sitemap Fixtures.config (Route.sitemapEntries Fixtures.content)

                // Assert
                Expect.stringContains xml "<loc>https://example.com/notes/a-note</loc><lastmod>" "note has lastmod"
                Expect.stringContains xml "<url><loc>https://example.com/</loc></url>" "home has no lastmod"
            }

            test "rss escapes special characters in titles and descriptions" {
                // Arrange
                let escNote =
                    { Fixtures.note "esc" with
                        Title = "A & B <c>"
                        Description = "d \"q\" '" }

                // Act
                let rss = Feed.rss Fixtures.config { Fixtures.content with Notes = [ escNote ]; Projects = [] }

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
                      noteShow
                      ProjectShow(project (), None, None)
                      TagShow("F#", [], []) ]

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
                    Expect.stringContains out (System.Net.WebUtility.HtmlEncode tag) (Util.slugifyTag tag)
            }

            test "output is directory-style so both slash forms resolve" {
                // Act
                let note = Route.outputPath noteShow
                let home = Route.outputPath Home
                let rss = Route.outputPath Rss

                // Assert
                Expect.equal note "notes/a-note/index.html" "note"
                Expect.equal home "index.html" "home"
                Expect.equal rss "rss.xml" "rss"
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
                let html = render Home

                // Assert
                Expect.stringContains html "<title>example — Ada Lovelace &#183; Analyst</title>" "home title"
            }

            test "other pages are suffixed with the site name" {
                // Act
                let html = render NotesIndex

                // Assert
                Expect.stringContains html "<title>Notes | example</title>" "notes title"
            }

            test "canonical urls have no trailing slash" {
                // Act
                let html = render NotesIndex

                // Assert
                Expect.stringContains html "<link rel=\"canonical\" href=\"https://example.com/notes\">" "canonical"
            }

            test "articles are declared as og:type article" {
                // Act
                let html = render noteShow

                // Assert
                Expect.stringContains html "<meta property=\"og:type\" content=\"article\">" "og:type"
            }

            test "every page carries og:site_name and an image alt" {
                // Act
                let html = render Home

                // Assert
                Expect.stringContains html "<meta property=\"og:site_name\" content=\"example\">" "og:site_name"

                Expect.stringContains
                    html
                    "<meta property=\"og:image:alt\" content=\"example — Ada Lovelace &#183; Analyst\">"
                    "og:image:alt"
            }

            test "pages reference a 1200x630 share card — default for index, per-post for articles" {
                // Act
                let home = render Home
                let article = render noteShow

                // Assert
                Expect.stringContains
                    home
                    "<meta property=\"og:image\" content=\"https://example.com/og/default.png\">"
                    "home default card"

                Expect.stringContains home "<meta property=\"og:image:width\" content=\"1200\">" "width"
                Expect.stringContains home "<meta property=\"og:image:height\" content=\"630\">" "height"

                Expect.stringContains
                    article
                    "<meta property=\"og:image\" content=\"https://example.com/og/notes/a-note.png\">"
                    "per-note card"
            }

            test "the non-standard meta name=title is not emitted" {
                // Act
                let html = render Home

                // Assert
                Expect.isFalse (html.Contains "<meta name=\"title\"") "no meta name=title"
            }

            test "article pages emit article:* tags, one per note tag" {
                // Arrange
                let n = note ()
                let published = DateParser.toIsoDatetime n.Date

                // Act
                let html = render noteShow

                // Assert
                Expect.stringContains
                    html
                    $"<meta property=\"article:published_time\" content=\"{published}\">"
                    "published_time"

                Expect.stringContains html "<meta property=\"article:author\" content=\"Ada Lovelace\">" "author"
                Expect.equal (matches "property=\"article:tag\" content=\"([^\"]*)\"" html) n.Tags "one meta per tag"
            }

            test "every page carries a polite status region for the copy buttons' announcements" {
                // Act
                let html = render Home

                // Assert
                Expect.stringContains html "<div id=\"status\" role=\"status\" class=\"sr-only\"></div>" "status region"
            }

            test "an article without neighbours renders no navigation and no empty comment nodes" {
                // Act
                let html = render noteShow

                // Assert
                Expect.isFalse (html.Contains "Previous and next posts") "no navigation"
                Expect.isFalse (html.Contains "<!--") "no comment nodes"
            }

            test "non-article pages emit no article:* tags" {
                // Act
                let html = render Home

                // Assert
                Expect.isFalse (html.Contains "property=\"article:") "home has no article tags"
            }
        ]

        testList "not found" [
            test "the 404 page keeps the site chrome" {
                // Act
                let body = Layouts.render (Views.NotFound.page Fixtures.config)

                // Assert
                Expect.stringContains body "Go to home" "cta"
                Expect.stringContains body "href=\"/notes\"" "notes link"
                Expect.isFalse (body.Contains "rel=\"canonical\"") "noindex pages omit canonical"
            }

            test "the 404 page is marked noindex" {
                // Act
                let body = Layouts.render (Views.NotFound.page Fixtures.config)

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
                      noteShow, "BlogPosting"
                      ProjectShow(project (), None, None), "CreativeWork" ]

                // Act
                let parsed =
                    [ for route, expectedType in cases ->
                        let el = ldNode expectedType (ldScript (render route))
                        route, expectedType, el.GetProperty("@type").GetString() ]

                // Assert
                for route, expectedType, actualType in parsed do
                    Expect.equal actualType expectedType $"{route}"
            }

            test "a note's BlogPosting carries an ImageObject, language, and keywords" {
                // Act
                let json = ldScript (render noteShow)
                let bp = ldNode "BlogPosting" json
                let image = bp.GetProperty "image"

                // Assert
                Expect.equal (image.GetProperty("@type").GetString()) "ImageObject" "image is an ImageObject"

                Expect.stringContains
                    (image.GetProperty("url").GetString())
                    "/og/notes/a-note.png"
                    "image url is the OG card"

                Expect.equal (image.GetProperty("width").GetInt32()) 1200 "image width"
                Expect.equal (bp.GetProperty("inLanguage").GetString()) "en-US" "inLanguage"
                Expect.isTrue (bp.GetProperty("keywords").GetString().Length > 0) "keywords present"
            }

            test "an article's publisher is an Organization with a logo" {
                // Act
                let json = ldScript (render noteShow)
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
                let json = ldScript (render noteShow)
                let crumbs = ldNode "BreadcrumbList" json

                let trail =
                    crumbs.GetProperty("itemListElement").EnumerateArray()
                    |> Seq.map (fun e -> e.GetProperty("position").GetInt32(), e.GetProperty("name").GetString())
                    |> List.ofSeq

                // Assert
                Expect.equal
                    trail
                    [ 1, "Home"; 2, "Notes"; 3, (note ()).Title ]
                    "breadcrumb trail"
            }

            test "website and person JSON-LD carry their headline fields" {
                // Act
                use wdoc = JsonDocument.Parse(JsonLd.website Fixtures.config)
                use pdoc = JsonDocument.Parse(JsonLd.person Fixtures.config)
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

            test "directory-index and extensioned shapes both resolve" {
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

            test "an extensionless reference does not resolve to a flat .html file" {
                // Arrange
                withDist [ "index.html", """<a href="/about">a</a>"""; "about.html", "x" ] (fun root ->
                    // Act
                    let problems = Program.verifyReferences root |> expectProblems "flat html"

                    // Assert
                    Expect.stringContains problems "/about" "without cleanUrls Vercel 404s this, so the build must too")
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
                    Expect.isOk (Program.writeSite config content tmp) "every route renders"

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
                let files =
                    Fixtures.requiredPageFiles
                    @ [ "notes/live/index.md", "---\ntitle: Live\ndescription: D\ndate: 2024-01-02\n---\nprose\n"
                        "notes/hidden/index.md",
                        "---\ntitle: Hidden\ndescription: D\ndate: 2024-01-03\ndraft: true\n---\nprose\n" ]

                // Act
                let result = Fixtures.withContentRoot files SiteContent.load

                // Assert
                match result with
                | Error errs -> failtestf "load failed: %s" (String.concat "; " errs)
                | Ok loaded ->
                    let rss = Feed.rss Fixtures.config loaded
                    let sitemap = Feed.sitemap Fixtures.config (Route.sitemapEntries loaded)
                    Expect.isFalse
                        (loaded.Notes |> List.exists (fun n -> n.Id = "hidden"))
                        "draft absent from content.Notes"
                    Expect.isFalse (hasNoteRoute loaded "hidden") "no route for the draft"
                    Expect.isFalse (rss.Contains "hidden") "draft absent from RSS"
                    Expect.isFalse (sitemap.Contains "hidden") "draft absent from the sitemap"
                    Expect.isTrue (hasNoteRoute loaded "live") "the published note still has a route"
            }
        ]
    ]
