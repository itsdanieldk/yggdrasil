module Yggdrasil.Tests.ContentTests

open Yggdrasil.Content
open Yggdrasil.Tests.Support

open System
open System.IO
open System.Text.RegularExpressions

open Expecto

let private okOr =
    function
    | Ok value -> value
    | Error error -> failtestf "expected Ok, got Error: %s" (string error)

let private errorText (result: Result<'a, string list>) =
    match result with
    | Ok value -> failtestf "expected Error, got Ok %A" value
    | Error errors -> String.concat "\n" errors

let private loadErrors files =
    errorText (Fixtures.withContentRoot files SiteContent.load)

[<Tests>]
let tests =
    testList "Content" [

        testList "DateParser" [
            test "optional passes None through" {
                // Act
                let result = DateParser.tryParseOptional "a/index.md" "updatedDate" None

                // Assert
                Expect.equal result (Ok None) "nil -> Ok None"
            }

            test "parses an ISO 8601 string" {
                // Act
                let result = DateParser.tryParse "a/index.md" "date" (Some "2023-12-31")

                // Assert
                Expect.equal result (Ok(DateOnly(2023, 12, 31))) "iso"
            }

            test "errors with the path on a malformed date string" {
                // Act
                let result = DateParser.tryParse "a/index.md" "date" (Some "2023-13-40")

                // Assert
                match result with
                | Error e -> Expect.stringContains e "a/index.md" "carries the path"
                | Ok _ -> failtest "expected Error for 2023-13-40"
            }

            test "rejects non-ISO shapes bare TryParse would accept" {
                // Act
                let result = DateParser.tryParse "a/index.md" "date" (Some "01/02/2024")

                // Assert
                Expect.isError result "MM/dd/yyyy rejected"
            }

            test "renders midnight UTC the way JS Date.toISOString does" {
                // Act
                let iso = DateParser.toIsoDatetime (DateOnly(2024, 1, 2))

                // Assert
                Expect.equal iso "2024-01-02T00:00:00.000Z" "iso datetime"
            }

            test "a required date that is missing is an error" {
                // Act
                let result = DateParser.tryParse "a/index.md" "date" None

                // Assert
                match result with
                | Error e -> Expect.stringContains e "required date is missing" "msg"
                | Ok _ -> failtest "expected Error for a missing required date"
            }

            test "an optional empty string passes through as None" {
                // Act
                let result = DateParser.tryParseOptional "a/index.md" "updatedDate" (Some "")

                // Assert
                Expect.equal result (Ok None) "empty -> None"
            }

            test "an optional but malformed date is an error" {
                // Act
                let result = DateParser.tryParseOptional "a/index.md" "updatedDate" (Some "not-a-date")

                // Assert
                Expect.isError result "invalid -> Error"
            }
        ]

        testList "Parser" [
            test "splits frontmatter and decodes typed attrs plus body" {
                // Arrange
                let contents = "---\ntitle: Hello\ntags:\n  - one\n  - two\n---\n# Body\n\nSome prose.\n"

                // Act
                let fm, body = Parser.split "content/notes/hello/index.md" contents |> okOr
                let dto = Parser.deserialize "x" fm |> okOr

                // Assert
                Expect.stringContains body "# Body" "body kept"
                Expect.equal dto.Title "Hello" "title"
                Expect.equal (List.ofArray dto.Tags) [ "one"; "two" ] "tags"
            }

            test "trims the leading newline off the body" {
                // Act
                let _, body = Parser.split "a/index.md" "---\ntitle: X\n---\n\nfirst line" |> okOr

                // Assert
                Expect.equal body "first line" "leading newline trimmed"
            }

            test "errors with the path when the frontmatter fence is missing" {
                // Act
                let result = Parser.split "bad.md" "no frontmatter here"

                // Assert
                Expect.stringContains (errorText result) "bad.md" "carries the path"
            }

            test "an unknown frontmatter key is an error rather than being ignored" {
                // Act
                let result =
                    Yaml.rejectUnknownKeys "content/notes/x/index.md" Parser.noteKeys "title: T\ndrafts: true\n"

                // Assert
                let errors = errorText result
                Expect.stringContains errors "content/notes/x/index.md" "carries the path"
                Expect.stringContains errors "\"drafts\"" "names the offending key"
            }

            test "every unknown key in the file is named at once" {
                // Act
                let result =
                    Yaml.rejectUnknownKeys "n.md" Parser.noteKeys "title: T\ntag: [a]\nupdated: 2024-01-01\n"

                // Assert
                let errors = errorText result
                Expect.stringContains errors "\"tag\"" "names the first"
                Expect.stringContains errors "\"updated\"" "names the second"
            }

            test "the keys each collection actually uses are accepted" {
                // Arrange
                let pageFm = "title: T\ndescription: D\nheading: H\nemoji: X\n"

                let noteFm =
                    "title: T\ndescription: D\ndate: 2024-01-01\nupdatedDate: 2024-02-01\n"
                    + "tags: [a]\ndraft: true\nfeatured: true\n"

                let projectFm = noteFm + "demoURL: /demo\nrepoURL: https://y.dk\n"

                // Act & Assert
                Expect.isOk (Yaml.rejectUnknownKeys "p.md" Parser.pageKeys pageFm) "page keys"
                Expect.isOk (Yaml.rejectUnknownKeys "n.md" Parser.noteKeys noteFm) "note keys"
                Expect.isOk (Yaml.rejectUnknownKeys "x.md" Parser.projectKeys projectFm) "project keys"
            }

            test "a key belonging to another collection is not accepted" {
                // Act & Assert
                Expect.isError (Yaml.rejectUnknownKeys "n.md" Parser.noteKeys "repoURL: x\n") "notes take no repoURL"
                Expect.isError (Yaml.rejectUnknownKeys "p.md" Parser.pageKeys "date: 2024-01-01\n") "pages take no date"
            }

            test "deserialize rejects malformed YAML with the path and a clear message" {
                // Act
                let result = Parser.deserialize "bad.md" "tags: [a, b"

                // Assert
                let errors = errorText result
                Expect.stringContains errors "bad.md" "carries the path"
                Expect.stringContains errors "invalid YAML frontmatter" "explains the failure"
            }
        ]

        testList "decode" [
            let dtoOf yaml = Parser.deserialize "x" yaml |> okOr
            let noteOf yaml =
                Parser.decodeNote "content/notes/x/index.md" "x" (dtoOf yaml) "raw body" (Ok "<p>rendered</p>")
            let baseFm = "title: Hello\ndescription: A note\ndate: 2024-01-02\n"

            test "decodes a note, defaulting the optional fields" {
                // Act
                let n = noteOf baseFm |> okOr

                // Assert
                Expect.equal n.Id "x" "id"
                Expect.equal n.Title "Hello" "title"
                Expect.equal n.Description "A note" "description"
                Expect.equal n.Date (DateOnly(2024, 1, 2)) "date"
                Expect.equal n.UpdatedDate None "updatedDate defaults to None"
                Expect.equal n.Tags [] "tags default to empty"
                Expect.isFalse n.Draft "draft defaults to false"
                Expect.isFalse n.Featured "featured defaults to false"
                Expect.equal n.Body "<p>rendered</p>" "rendered body kept"
            }

            test "featured: true is decoded on both notes and projects" {
                // Arrange
                let fm = baseFm + "featured: true\n"

                // Act
                let note = noteOf fm |> okOr

                let project =
                    Parser.decodeProject "content/projects/x/index.md" "x" (dtoOf fm) "raw body" (Ok "<p>rendered</p>")
                    |> okOr

                // Assert
                Expect.isTrue note.Featured "note featured"
                Expect.isTrue project.Featured "project featured"
            }

            test "a missing title is an error naming the field" {
                // Act
                let result = noteOf "description: D\ndate: 2024-01-02\n"

                // Assert
                Expect.stringContains (errorText result) "title" "names title"
            }

            test "a missing description is an error naming the field" {
                // Act
                let result = noteOf "title: T\ndate: 2024-01-02\n"

                // Assert
                Expect.stringContains (errorText result) "description" "names description"
            }

            test "a missing date is an error" {
                // Act
                let result = noteOf "title: T\ndescription: D\n"

                // Assert
                Expect.stringContains (errorText result) "date: required date is missing" "names the date field"
            }

            test "reads draft, tags, and an optional updatedDate" {
                // Arrange
                let fm = baseFm + "updatedDate: 2024-03-04\ndraft: true\ntags:\n  - a\n  - b\n"

                // Act
                let n = noteOf fm |> okOr

                // Assert
                Expect.isTrue n.Draft "draft"
                Expect.equal n.UpdatedDate (Some(DateOnly(2024, 3, 4))) "updatedDate"
                Expect.equal n.Tags [ "a"; "b" ] "tags"
            }

            test "a project reads demo/repo URLs from the aliased keys" {
                // Arrange
                let dto = dtoOf (baseFm + "demoURL: https://demo.example\nrepoURL: https://repo.example\n")

                // Act
                let p = Parser.decodeProject "p" "x" dto "raw" (Ok "rendered") |> okOr

                // Assert
                Expect.equal p.DemoUrl (Some "https://demo.example") "demoUrl"
                Expect.equal p.RepoUrl (Some "https://repo.example") "repoUrl"
            }

            test "a project without URLs decodes them as None" {
                // Arrange
                let dto = dtoOf baseFm

                // Act
                let p = Parser.decodeProject "p" "x" dto "raw" (Ok "rendered") |> okOr

                // Assert
                Expect.equal p.DemoUrl None "no demo"
                Expect.equal p.RepoUrl None "no repo"
            }
        ]

        testList "Fragrance" [
            let baseYaml extra =
                "name: Aventus\nhouse: Creed\nurl: https://example.com\n" + extra

            testList "decode" [
                test "builds an owned fragrance and computes image paths from the id" {
                    // Act
                    let f = Fragrance.decode "content/fragrances/aventus.yaml" "aventus" (baseYaml "rating: 8.5") |> okOr

                    // Assert
                    Expect.equal f.Id "aventus" "id"
                    Expect.equal f.Rating (Some 8.5) "rating"
                    Expect.isFalse f.Wishlist "not wishlist"
                    Expect.equal f.Image "/images/fragrances/aventus/bottle.png" "image"
                    Expect.equal f.Image2x "/images/fragrances/aventus/bottle@2x.png" "image2x"
                }

                test "builds a wishlist fragrance without a rating" {
                    // Act
                    let f = Fragrance.decode "costa-azzurra.yaml" "costa-azzurra" (baseYaml "wishlist: true") |> okOr

                    // Assert
                    Expect.isTrue f.Wishlist "wishlist"
                    Expect.equal f.Rating None "no rating"
                }

                test "reads the draft flag" {
                    // Act
                    let f = Fragrance.decode "x.yaml" "x" (baseYaml "rating: 5\ndraft: true") |> okOr

                    // Assert
                    Expect.isTrue f.Draft "draft"
                }

                test "errors when a wishlist entry carries a rating" {
                    // Act
                    let result = Fragrance.decode "x.yaml" "x" (baseYaml "wishlist: true\nrating: 5")

                    // Assert
                    Expect.stringContains (errorText result) "wishlist entries must not have a rating" "msg"
                }

                test "errors when a non-wishlist entry has no rating" {
                    // Act
                    let result = Fragrance.decode "x.yaml" "x" (baseYaml "")

                    // Assert
                    Expect.stringContains (errorText result) "rating is required" "msg"
                }

                test "errors when a required key is missing" {
                    // Act
                    let result = Fragrance.decode "x.yaml" "x" "name: N\nurl: u\nrating: 5"

                    // Assert
                    Expect.stringContains (errorText result) "house" "names the missing key"
                }

                test "errors when name or url is missing (not just house)" {
                    // Act
                    let withoutName = Fragrance.decode "x.yaml" "x" "house: Creed\nurl: u\nrating: 5"
                    let withoutUrl = Fragrance.decode "x.yaml" "x" "name: N\nhouse: Creed\nrating: 5"

                    // Assert
                    Expect.stringContains (errorText withoutName) "name" "names the missing name"
                    Expect.stringContains (errorText withoutUrl) "url" "names the missing url"
                }

                test "an unknown key is an error rather than being ignored" {
                    // Act
                    let result = Fragrance.decode "x.yaml" "x" (baseYaml "rating: 8\nwishlisted: true")

                    // Assert
                    Expect.stringContains (errorText result) "\"wishlisted\"" "names the offending key"
                }

                test "rejects malformed YAML with a clear message" {
                    // Act
                    let result = Fragrance.decode "bad.yaml" "bad" "name: [1, 2"

                    // Assert
                    Expect.stringContains (errorText result) "invalid YAML" "explains the failure"
                }
            ]

            testList "meta" [
                test "drops None parts and prints whole-number ratings without a decimal" {
                    // Arrange
                    let f = Fragrance.decode "x.yaml" "x" (baseYaml "rating: 8.0\nconcentration: EDP") |> okOr

                    // Act
                    let meta = Fragrance.meta f

                    // Assert
                    Expect.equal meta [ "Creed"; "EDP"; "8/10" ] "meta"
                }

                test "keeps fractional ratings and omits an absent concentration" {
                    // Arrange
                    let f = Fragrance.decode "x.yaml" "x" (baseYaml "rating: 8.5") |> okOr

                    // Act
                    let meta = Fragrance.meta f

                    // Assert
                    Expect.equal meta [ "Creed"; "8.5/10" ] "meta"
                }
            ]
        ]

        testList "slugifyTag" [
            test "expands # before stripping non-alphanumerics" {
                // Act
                let fsharp = Util.slugifyTag "F#"
                let csharp = Util.slugifyTag "C#"

                // Assert
                Expect.equal fsharp "fsharp" "F#"
                Expect.equal csharp "csharp" "C#"
            }

            test "lowercases and hyphenates" {
                // Act
                let spaced = Util.slugifyTag "effect system"
                let cased = Util.slugifyTag "Functional Programming"

                // Assert
                Expect.equal spaced "effect-system" "spaces"
                Expect.equal cased "functional-programming" "case"
            }

            test "trims leading and trailing separators" {
                // Act
                let spaced = Util.slugifyTag "  spaced  "
                let punctuated = Util.slugifyTag "!bang!"

                // Assert
                Expect.equal spaced "spaced" "spaces"
                Expect.equal punctuated "bang" "punct"
            }
        ]

        testList "readingTime" [
            test "excludes fenced code blocks" {
                // Arrange
                let prose = String.replicate 200 "word "
                let code = "```fsharp\n" + String.replicate 200 "let x = 1\n" + "```"

                // Act
                let withCode = Util.readingTime (prose + "\n" + code)
                let proseOnly = Util.readingTime prose

                // Assert
                Expect.equal withCode proseOnly "code excluded"
            }

            test "counts link text but not the target" {
                // Act
                let linked = Util.readingTime "[one two three](https://example.com/a/very/long/url)"
                let plain = Util.readingTime "one two three"

                // Assert
                Expect.equal linked plain "link text only"
            }

            test "rounds up and pluralises" {
                // Act
                let exact = Util.readingTime (String.replicate 200 "word ")
                let over = Util.readingTime (String.replicate 201 "word ")

                // Assert
                Expect.equal exact "1 min read" "200 words"
                Expect.equal over "2 min read" "201 words"
            }

            test "handles empty content" {
                // Act
                let readingTime = Util.readingTime ""

                // Assert
                Expect.equal readingTime "< 1 min read" "empty"
            }
        ]

        testList "collections" [
            test "notes and projects lead with featured entries, then run newest first, and exclude drafts" {
                // Act
                let collections =
                    [ content.Notes |> List.map (fun n -> n.Draft, (n.Featured, n.Date))
                      content.Projects |> List.map (fun p -> p.Draft, (p.Featured, p.Date)) ]

                // Assert
                for entries in collections do
                    Expect.isFalse (List.exists fst entries) "no drafts"
                    let keys = entries |> List.map snd
                    Expect.equal keys (List.sortByDescending id keys) "featured first, then date desc"
            }

            test "tag counts are ordered by count desc then name" {
                // Act
                let counts = Content.tagCounts content.Notes content.Projects

                // Assert
                Expect.equal counts (counts |> List.sortBy (fun (t, n) -> -n, t)) "ordered"
            }

            test "owned fragrances are rated and sorted by rating desc then name" {
                // Act
                let owned = Content.ownedFragrances content.Fragrances

                // Assert
                Expect.isFalse (List.exists (fun f -> f.Rating = None) owned) "all rated"
                Expect.equal owned (owned |> List.sortBy (fun f -> -(defaultArg f.Rating 0.0), f.Name)) "sorted"
            }

            test "wishlist fragrances carry no rating" {
                // Act
                let wishlist = Content.wishlistFragrances content.Fragrances

                // Assert
                Expect.isTrue (List.forall (fun f -> f.Rating = None) wishlist) "no rating"
            }

            test "a rating outside 0-10 is rejected, since it renders verbatim as \"n/10\"" {
                // Arrange & Act & Assert
                let yaml rating =
                    $"name: X\nhouse: Y\nurl: https://example.com\nrating: {rating}\n"

                for bad in [ "42"; "-1"; "10.5" ] do
                    let errors = errorText (Fragrance.decode "f.yaml" "x" (yaml bad))
                    Expect.stringContains errors "outside 0-10" $"names the range for {bad}"

                for good in [ "0"; "10"; "8.5" ] do
                    match Fragrance.decode "f.yaml" "x" (yaml good) with
                    | Ok f -> Expect.equal f.Rating (Some(float good)) $"{good} is accepted"
                    | Error e -> failtestf "rating %s should be accepted, got: %A" good e
            }

            test "formatRating drops the decimal only when the rating is whole" {
                // Arrange
                let cases = [ 8.0, "8"; 8.5, "8.5"; 10.0, "10"; 0.0, "0" ]

                // Act & Assert
                for rating, expected in cases do
                    Expect.equal (Fragrance.formatRating rating) expected $"{rating} renders as {expected}"
            }

            test "withNeighbours walks the real notes oldest-as-prev" {
                // Arrange
                let notes = content.Notes

                // Act
                let paired = Content.withNeighbours notes
                let _, firstPrev, firstNext = List.head paired

                // Assert
                Expect.isNone firstNext "newest has no newer neighbour"
                Expect.equal firstPrev (List.tryItem 1 notes) "prev is the next-older note"
            }

            test "feed entries interleave notes and projects newest first" {
                // Act
                let dates = Content.feedEntries content |> List.map (fun e -> e.Date)

                // Assert
                Expect.equal dates (List.sortDescending dates) "newest first"
            }

            test "notes are grouped by year, newest year first" {
                // Act
                let years = Content.notesByYear content.Notes |> List.map fst

                // Assert
                Expect.equal years (List.sortDescending years) "years descending"
            }

            test "every derived view tracks a filtered corpus instead of going stale" {
                // Arrange
                let newest = List.head content.Notes

                let narrowed =
                    { content with
                        Notes = [ newest ]
                        Projects = []
                        Fragrances = content.Fragrances |> List.filter (fun f -> f.Wishlist) }

                // Act
                let byYear = Content.notesByYear narrowed.Notes
                let counts = Content.tagCounts narrowed.Notes narrowed.Projects
                let owned = Content.ownedFragrances narrowed.Fragrances
                let wishlist = Content.wishlistFragrances narrowed.Fragrances

                // Assert
                Expect.equal (List.map fst byYear) [ newest.Date.Year ] "one year, the surviving note's"
                Expect.equal (counts |> List.map fst |> List.sort) (List.sort newest.Tags) "counts follow the note"
                Expect.isEmpty owned "no owned bottles survive the filter"
                Expect.hasLength wishlist (List.length narrowed.Fragrances) "all survivors are wishlist"
                Expect.equal (Content.allTags narrowed |> List.sort) (List.sort newest.Tags) "allTags agrees"
            }
        ]

        testList "published" [
            let published items = Util.published (fun (_, d, _) -> d) (fun (_, _, day) -> day) items

            test "removes drafts and sorts newest first" {
                // Arrange
                let items = [ ("a", false, 3); ("b", true, 9); ("c", false, 5); ("d", false, 1) ]

                // Act
                let ids = published items |> List.map (fun (id, _, _) -> id)

                // Assert
                Expect.equal ids [ "c"; "a"; "d" ] "drafts out, dates descending"
            }

            test "is stable among equal dates (keeps input order)" {
                // Arrange
                let items = [ ("a", false, 5); ("b", false, 5); ("c", false, 3) ]

                // Act
                let ids = published items |> List.map (fun (id, _, _) -> id)

                // Assert
                Expect.equal ids [ "a"; "b"; "c" ] "input order kept at equal dates"
            }

            test "a (featured, date) key floats featured entries above newer unfeatured ones" {
                // Arrange
                let items = [ ("a", false, (true, 1)); ("b", false, (false, 5)); ("c", false, (false, 9)) ]

                // Act
                let ids =
                    Util.published (fun (_, d, _) -> d) (fun (_, _, key) -> key) items
                    |> List.map (fun (id, _, _) -> id)

                // Assert
                Expect.equal ids [ "a"; "c"; "b" ] "featured first, then the rest newest first"
            }
        ]

        testList "withNeighbours" [
            let items = [ "a"; "b"; "c"; "d" ]

            test "a middle entry has both an older prev and a newer next" {
                // Act
                let paired = Content.withNeighbours items

                // Assert
                Expect.equal (List.item 1 paired) ("b", Some "c", Some "a") "b -> (prev c, next a)"
            }

            test "the newest entry has no newer neighbour" {
                // Act & Assert
                Expect.equal (List.head (Content.withNeighbours items)) ("a", Some "b", None) "a -> (b, None)"
            }

            test "the oldest entry has no older neighbour" {
                // Act & Assert
                Expect.equal (List.last (Content.withNeighbours items)) ("d", None, Some "c") "d -> (None, c)"
            }

            test "pairing stays positional when a featured entry breaks date order" {
                // Arrange
                let display = [ ("pinned", 2024); ("newest", 2026); ("middle", 2025) ]

                // Act
                let paired = Content.withNeighbours display

                // Assert
                let find id = paired |> List.find (fun ((i, _), _, _) -> i = id)
                let ids (item, prev, next) = fst item, Option.map fst prev, Option.map fst next

                Expect.equal (ids (find "pinned")) ("pinned", Some "newest", None) "top of index: prev is below it"
                Expect.equal (ids (find "newest")) ("newest", Some "middle", Some "pinned") "middle of index"
                Expect.equal (ids (find "middle")) ("middle", None, Some "newest") "bottom of index: no prev"
            }

            test "a single entry has neither neighbour" {
                // Act & Assert
                Expect.equal (Content.withNeighbours [ "only" ]) [ ("only", None, None) ] "no siblings"
            }

            test "an empty list yields nothing rather than throwing" {
                // Act & Assert
                Expect.isEmpty (Content.withNeighbours ([]: string list)) "empty in, empty out"
            }

            test "every entry is present exactly once, in order" {
                // Act
                let paired = Content.withNeighbours items

                // Assert
                Expect.equal (paired |> List.map (fun (x, _, _) -> x)) items "order and membership preserved"
            }
        ]

        testList "collectResults" [
            test "all Ok collapses to Ok, preserving order" {
                // Act
                let result = collectResults [ Ok 1; Ok 2; Ok 3 ]

                // Assert
                Expect.equal result (Ok [ 1; 2; 3 ]) "oks"
            }

            test "any Error aggregates every error, in order" {
                // Act
                let result = collectResults [ Ok 1; Error "e1"; Ok 2; Error "e2" ]

                // Assert
                Expect.equal result (Error [ "e1"; "e2" ]) "aggregated"
            }
        ]

        testList "Site" [
            test "absoluteUrl joins base and path without doubling the slash" {
                // Act
                let path = Site.absoluteUrl config "/notes/foo"
                let root = Site.absoluteUrl config "/"

                // Assert
                Expect.equal path (config.BaseUrl + "notes/foo") "path"
                Expect.equal root config.BaseUrl "root keeps a single slash"
            }

            test "absoluteUrl tolerates a base URL missing its trailing slash" {
                // Act
                let url = Site.absoluteUrl { config with BaseUrl = "https://x.dk" } "/notes/foo"

                // Assert
                Expect.equal url "https://x.dk/notes/foo" "no missing or doubled slash"
            }

            test "Page returns the configured metadata for a key" {
                // Act
                let meta = config.Page "notes"

                // Assert
                Expect.isNotEmpty meta.Title "notes title is configured"
                Expect.isNotEmpty meta.Description "notes description is configured"
            }

            test "Page fails loudly on a key that isn't configured" {
                // Act / Assert
                Expect.throws (fun () -> config.Page "nope" |> ignore) "an unconfigured key is an error, not a silent blank"
            }
        ]

        testList "loader" [
            test "a featured project leads the list even though it is not the newest" {
                // Arrange
                let project id date featured =
                    $"projects/{id}/index.md",
                    $"---\ntitle: {id}\ndescription: D\ndate: {date}\nfeatured: {featured}\n---\nbody\n"

                let files =
                    Fixtures.requiredPageFiles
                    @ [ project "older-featured" "2024-01-01" "true"
                        project "newest" "2026-01-01" "false"
                        project "middle" "2025-01-01" "false" ]

                // Act
                let loaded = Fixtures.withContentRoot files SiteContent.load |> okOr

                // Assert
                Expect.equal
                    (loaded.Projects |> List.map (fun p -> p.Id))
                    [ "older-featured"; "newest"; "middle" ]
                    "the featured project leads; the rest run newest first"
            }

            test "aggregates a decode error from every bad file at once" {
                // Arrange
                let files =
                    Fixtures.requiredPageFiles
                    @ [ "notes/bad-a/index.md", "---\ndescription: no title here\ndate: 2024-01-01\n---\nbody"
                        "notes/bad-b/index.md", "---\ntitle: T\ndescription: D\ndate: not-a-date\n---\nbody" ]

                // Act
                let result = Fixtures.withContentRoot files SiteContent.load

                // Assert
                match result with
                | Ok _ -> failtest "expected the bad files to fail the load"
                | Error errs ->
                    Expect.equal (List.length errs) 2 "one error per bad file"
                    Expect.isTrue (errs |> List.exists (fun e -> e.Contains "title")) "reports the missing title"
                    Expect.isTrue (errs |> List.exists (fun e -> e.Contains "invalid ISO date")) "reports the bad date"
            }

            test "a misspelled frontmatter key fails the load instead of quietly doing nothing" {
                // Arrange
                let files =
                    Fixtures.requiredPageFiles
                    @ [ "notes/typo/index.md",
                        "---\ntitle: T\ndescription: D\ndate: 2024-01-01\ndrafts: true\n---\nbody" ]

                // Act
                let result = Fixtures.withContentRoot files SiteContent.load

                // Assert
                match result with
                | Ok _ -> failtest "expected the typo to fail the load"
                | Error errs -> Expect.isTrue (errs |> List.exists (fun e -> e.Contains "\"drafts\"")) "names the key"
            }

            test "a note referencing a missing image fails the load (aggregated, not a crash)" {
                // Arrange
                let files =
                    Fixtures.requiredPageFiles
                    @ [ "notes/with-image/index.md",
                        "---\ntitle: T\ndescription: D\ndate: 2024-01-01\n---\n![x](./missing.png)\n" ]

                // Act
                let result = Fixtures.withContentRoot files SiteContent.load

                // Assert
                match result with
                | Ok _ -> failtest "expected the missing image to fail the load"
                | Error errs ->
                    Expect.isTrue (errs |> List.exists (fun e -> e.Contains "missing.png")) "names the image"
            }

            test "a relative image without ./ fails the load with a clear message" {
                // Arrange
                let files =
                    Fixtures.requiredPageFiles
                    @ [ "notes/bad-image/index.md",
                        "---\ntitle: T\ndescription: D\ndate: 2024-01-01\n---\n![x](x.png)\n" ]

                // Act
                let result = Fixtures.withContentRoot files SiteContent.load

                // Assert
                match result with
                | Ok _ -> failtest "expected the bad image reference to fail"
                | Error errs ->
                    Expect.isTrue
                        (errs |> List.exists (fun e -> e.Contains "must start with"))
                        "explains the convention"
            }
        ]

        testList "every error at once" [
            let note (frontmatter: string) (body: string) =
                Fixtures.requiredPageFiles @ [ "notes/n/index.md", $"---\n{frontmatter}---\n{body}" ]

            test "an empty frontmatter block is reported, not a crash" {
                // Act
                let errors = loadErrors (note "" "body\n")

                // Assert
                Expect.stringContains errors "index.md: YAML frontmatter is empty" "names the file and the problem"
            }

            test "a comment-only frontmatter block is reported, not a crash" {
                // Act
                let errors = loadErrors (note "# nothing yet\n" "body\n")

                // Assert
                Expect.stringContains errors "index.md: YAML frontmatter is empty" "names the file and the problem"
            }

            test "an empty fragrance file is reported, not a crash" {
                // Act
                let errors = loadErrors (Fixtures.requiredPageFiles @ [ "fragrances/empty.yaml", "" ])

                // Assert
                Expect.stringContains errors "empty.yaml: YAML file is empty" "names the file and the problem"
            }

            test "blank required fields are reported as missing" {
                // Act
                let errors = loadErrors (note "title: \"\"\ndescription: \" \"\ndate: \" \"\n" "body\n")

                // Assert
                Expect.stringContains errors "title: required field is missing" "a blank title"
                Expect.stringContains errors "description: required field is missing" "a whitespace description"
                Expect.stringContains errors "date: required date is missing" "a whitespace date"
            }

            test "a blank heading falls back to the title and a blank emoji is dropped" {
                // Arrange
                let files =
                    [ "pages/home/index.md",
                      "---\ntitle: Home\ndescription: D\nheading: \"\"\nemoji: \" \"\n---\nbody\n"
                      "pages/about/index.md", "---\ntitle: About\ndescription: D\n---\nbody\n" ]

                // Act
                let home = (Fixtures.withContentRoot files SiteContent.load |> okOr).Pages.["home"]

                // Assert
                Expect.equal home.Heading "Home" "a blank heading falls back to the title"
                Expect.equal home.Emoji None "a blank emoji is absent"
            }

            test "a malformed value names the value, not just the wrapper message" {
                // Act
                let yaml = "name: N\nhouse: H\nurl: https://example.com\nrating: 8,5\n"
                let errors = loadErrors (Fixtures.requiredPageFiles @ [ "fragrances/comma.yaml", yaml ])

                // Assert
                Expect.stringContains errors "8,5" "quotes the value that failed to parse"
            }

            test "every missing field in a note is reported, not just the first" {
                // Act
                let errors = loadErrors (note "description: D\n" "body\n")

                // Assert
                Expect.stringContains errors "title: required field is missing" "the title"
                Expect.stringContains errors "date: required date is missing" "the date"
            }

            test "an unknown key and a missing field in one note are both reported" {
                // Act
                let errors = loadErrors (note "titel: T\ndescription: D\ndate: 2024-01-01\n" "body\n")

                // Assert
                Expect.stringContains errors "\"titel\"" "the unknown key"
                Expect.stringContains errors "title: required field is missing" "the missing title"
            }

            test "an image error and a missing field in one note are both reported" {
                // Act
                let errors = loadErrors (note "description: D\ndate: 2024-01-01\n" "![x](./missing.png)\n")

                // Assert
                Expect.stringContains errors "missing.png" "the image"
                Expect.stringContains errors "title: required field is missing" "the missing title"
            }

            test "a code-fence error and a missing field in one note are both reported" {
                // Act
                let errors = loadErrors (note "description: D\ndate: 2024-01-01\n" "```cobol\nX\n```\n")

                // Assert
                Expect.stringContains errors "code fence language \"cobol\"" "the fence"
                Expect.stringContains errors "title: required field is missing" "the missing title"
            }

            test "every missing field in a fragrance is reported, not just the first" {
                // Act
                let errors = loadErrors (Fixtures.requiredPageFiles @ [ "fragrances/sparse.yaml", "rating: 8\n" ])

                // Assert
                Expect.stringContains errors "name: required field is missing" "the name"
                Expect.stringContains errors "house: required field is missing" "the house"
                Expect.stringContains errors "url: required field is missing" "the url"
            }

            test "a missing required page is reported alongside other load errors" {
                // Act
                let errors = loadErrors [ "notes/n/index.md", "---\ndescription: D\ndate: 2024-01-01\n---\nbody\n" ]

                // Assert
                Expect.stringContains errors "title: required field is missing" "the broken note"
                Expect.stringContains errors "content/pages/home/index.md: required page is missing" "the missing page"
            }
        ]

        testList "content layout" [
            let validNote = "---\ntitle: T\ndescription: D\ndate: 2024-01-01\n---\nbody\n"
            let validFragrance = "name: N\nhouse: H\nurl: https://example.com\nrating: 8\n"
            let withPages files = Fixtures.requiredPageFiles @ files

            test "a folder name that is not a valid slug is rejected, hinting at iCloud copies" {
                // Act
                let errors = loadErrors (withPages [ "notes/foo 2/index.md", validNote ])

                // Assert
                Expect.stringContains errors "\"foo 2\" is not a valid slug" "names the folder"
                Expect.stringContains errors "iCloud" "hints at the likely cause"
            }

            test "a fragrance file name that is not a valid slug is rejected" {
                // Act
                let errors = loadErrors (withPages [ "fragrances/Aventus.yaml", validFragrance ])

                // Assert
                Expect.stringContains errors "\"Aventus\" is not a valid slug" "names the file"
            }

            test "an entry folder without an exact-case index.md is rejected" {
                // Act
                let errors = loadErrors (withPages [ "notes/misnamed/Index.md", validNote ])

                // Assert
                Expect.stringContains errors "misnamed: no index.md" "names the folder"
            }

            test "a fragrance file that is not .yaml is rejected" {
                // Act
                let errors = loadErrors (withPages [ "fragrances/aventus.yml", validFragrance ])

                // Assert
                Expect.stringContains errors "aventus.yml" "names the file"
            }

            test "a page other than home and about is rejected" {
                // Act
                let page = "---\ntitle: Uses\ndescription: D\n---\nbody\n"
                let errors = loadErrors (withPages [ "pages/uses/index.md", page ])

                // Assert
                Expect.stringContains errors "unknown page \"uses\"" "names the page"
            }

            test "a stray file beside the entry folders is rejected" {
                // Act
                let errors = loadErrors (withPages [ "notes/draft.md", validNote ])

                // Assert
                Expect.stringContains errors "draft.md" "names the file"
            }

            test "an unknown folder under content/ is rejected" {
                // Act
                let errors = loadErrors (withPages [ "note/typo/index.md", validNote ])

                // Assert
                Expect.stringContains errors "\"note\"" "names the folder"
            }

            test "hidden files and folders are ignored" {
                // Arrange
                let files =
                    withPages
                        [ ".DS_Store", "x"
                          "notes/.DS_Store", "x"
                          "fragrances/.DS_Store", "x"
                          "notes/.drafts/index.md", "not content" ]

                // Act & Assert
                Expect.isOk (Fixtures.withContentRoot files SiteContent.load) "dotfiles are not content"
            }
        ]

        testList "isSafeUrl" [
            test "accepts the schemes the site actually renders" {
                // Act & Assert
                for url in [ "https://x.dk"; "http://x.dk"; "mailto:a@b.dk"; "/notes/foo"; "  https://x.dk  " ] do
                    Expect.isTrue (Util.isSafeUrl url) url
            }

            test "a protocol-relative URL does not pass as site-relative" {
                // Act & Assert
                for url in [ "//evil.dk"; "//evil.dk/notes"; "  //evil.dk  "; "/\\evil.dk" ] do
                    Expect.isFalse (Util.isSafeUrl url) url
            }

            test "rejects anything that would become a live non-navigational link" {
                // Act & Assert
                for url in [ "javascript:alert(1)"; "JavaScript:alert(1)"; "data:text/html,<script>"; "vbscript:x"; "notes/foo" ] do
                    Expect.isFalse (Util.isSafeUrl url) url
            }
        ]

        testList "staggerParagraphs" [
            test "top-level paragraphs are staggered in document order" {
                // Act
                let html = Markdown.staggerParagraphs "<p>one</p>\n<p>two</p>\n<p>three</p>"

                // Assert
                Expect.stringContains html "<p class=\"animate\" style=\"--i:1\">one" "first"
                Expect.stringContains html "<p class=\"animate\" style=\"--i:2\">two" "second"
                Expect.stringContains html "<p class=\"animate\" style=\"--i:3\">three" "third"
            }

            test "only line-start paragraphs are staggered: list items are not, blockquote lines are" {
                // Arrange
                let input = "<p>intro</p>\n<ul>\n<li><p>item</p></li>\n</ul>\n<blockquote>\n<p>quoted</p>\n</blockquote>"

                // Act
                let html = Markdown.staggerParagraphs input

                // Assert
                Expect.stringContains html "<p class=\"animate\" style=\"--i:1\">intro" "the top-level one is staggered"
                Expect.stringContains html "<li><p>item</p></li>" "list paragraph untouched"
                Expect.stringContains
                    html
                    "<p class=\"animate\" style=\"--i:2\">quoted"
                    "a blockquote's line is staggered"
                Expect.equal (html.Split("class=\"animate\"").Length - 1) 2 "only line-start paragraphs are stamped"
            }
        ]

        testList "tags" [
            test "tags sharing a slug are reported, naming every offender" {
                // Arrange
                let tags = [ "F#"; "Fsharp"; "F sharp"; "f-sharp"; "effect system" ]

                // Act
                let conflicts = Content.tagSlugErrors tags

                // Assert
                Expect.hasLength conflicts 2 "two independent clashes"
                let all = String.concat "\n" conflicts

                for tag in [ "F#"; "Fsharp"; "F sharp"; "f-sharp" ] do
                    Expect.stringContains all tag $"names {tag}"

                Expect.stringContains all "\"fsharp\"" "names the fsharp slug"
                Expect.stringContains all "\"f-sharp\"" "names the f-sharp slug"
                Expect.isFalse (all.Contains "effect system") "the unambiguous tag is not reported"
            }

            test "distinct slugs raise nothing" {
                // Act & Assert
                Expect.isEmpty (Content.tagSlugErrors [ "F#"; "concurrency"; ".NET" ]) "no clash"
            }

            test "a tag with no URL-safe characters is rejected, not silently given an empty slug" {
                // Arrange
                for tag in [ "Ø"; "日本語"; "—"; "+++" ] do
                    // Act
                    let errors = Content.tagSlugErrors [ tag ]

                    // Assert
                    Expect.hasLength errors 1 $"{tag} is reported"
                    Expect.stringContains (List.head errors) tag $"names {tag}"
                    Expect.stringContains (List.head errors) "no URL-safe characters" "explains why"
            }

            test "a tag keeps its page when at least one ASCII alphanumeric survives" {
                // Arrange
                let tag = "C++"

                // Act
                let errors = Content.tagSlugErrors [ tag ]

                // Assert
                Expect.isEmpty errors "C++ -> c is fine"
                Expect.equal (Util.slugifyTag tag) "c" "slug is c"
            }

            test "an empty slug is reported once even when several tags share it" {
                // Act
                let errors = Content.tagSlugErrors [ "Ø"; "日本語" ]

                // Assert
                Expect.hasLength errors 1 "grouped into a single error"
                Expect.stringContains (List.head errors) "Ø" "names the first"
                Expect.stringContains (List.head errors) "日本語" "names the second"
            }

            test "the real content has no slug clashes" {
                // Act & Assert
                Expect.isEmpty (Content.tagSlugErrors (Content.allTags content)) "committed tags are unambiguous"
            }
        ]

        testList "pages" [
            test "a missing required page is a load error, not a crash mid-render" {
                // Act
                let result = Fixtures.withContentRoot [] SiteContent.load

                // Assert
                match result with
                | Ok _ -> failtest "expected Error for a content root with no pages"
                | Error errors ->
                    let joined = String.concat "\n" errors

                    for id in SiteContent.requiredPages do
                        Expect.stringContains joined $"content/pages/{id}/index.md" $"names the missing {id} page"
            }

            test "the real content root supplies every required page" {
                // Assert
                for id in SiteContent.requiredPages do
                    Expect.isTrue (Map.containsKey id content.Pages) $"{id} page is present"
            }
        ]

        testList "highlight" [
            test "an unknown language falls back to a plain block and records the fallback" {
                // Arrange
                let h = Highlight.create contentPaths.GrammarRoot

                // Act
                let html = Highlight.highlight h "content/notes/x/index.md" "cobol" "SOME CODE\n"

                // Assert
                Expect.stringContains html "class=\"tm\"" "wrapper kept"
                Expect.stringContains html "SOME CODE" "code preserved"
                Expect.isTrue (Highlight.fallbacks h |> List.exists (fun e -> e.Language = "cobol")) "fallback recorded"
            }

            test "a known language highlights and records no fallback" {
                // Arrange
                let h = Highlight.create contentPaths.GrammarRoot

                // Act
                let html = Highlight.highlight h "x" "fsharp" "let x = 1\n"

                // Assert
                Expect.stringContains html "class=\"tm\"" "wrapper"
                Expect.isEmpty (Highlight.fallbacks h) "no fallbacks for a shipped grammar"
            }

            test "the other shipped grammars (scala, shell) load and highlight" {
                // Arrange
                let h = Highlight.create contentPaths.GrammarRoot

                // Act
                let scala = Highlight.highlight h "x" "scala" "val x = 1\n"
                let shell = Highlight.highlight h "x" "bash" "echo hi\n"

                // Assert
                Expect.stringContains scala "class=\"tm\"" "scala wrapper"
                Expect.stringContains shell "class=\"tm\"" "shell wrapper"
                Expect.isEmpty (Highlight.fallbacks h) "no fallbacks for shipped grammars"
            }

            test "language aliases resolve to the same grammar" {
                // Arrange
                let h = Highlight.create contentPaths.GrammarRoot

                // Act
                let fsharp = Highlight.highlight h "x" "fsharp" "let x = 1\n"
                let fs = Highlight.highlight h "x" "fs" "let x = 1\n"
                let hash = Highlight.highlight h "x" "f#" "let x = 1\n"
                let bash = Highlight.highlight h "x" "bash" "echo hi\n"
                let sh = Highlight.highlight h "x" "sh" "echo hi\n"
                let shellscript = Highlight.highlight h "x" "shellscript" "echo hi\n"

                // Assert
                Expect.stringContains fsharp "<span" "fsharp produced coloured tokens"
                Expect.stringContains bash "<span" "shell produced coloured tokens"
                Expect.equal fs fsharp "fs resolves to the fsharp grammar"
                Expect.equal hash fsharp "f# resolves to the fsharp grammar"
                Expect.equal sh bash "sh resolves to the shell grammar"
                Expect.equal shellscript bash "shellscript resolves to the shell grammar"
                Expect.isEmpty (Highlight.fallbacks h) "no fallbacks for aliases"
            }

            test "a removed language is no longer supported and falls back" {
                // Arrange
                let h = Highlight.create contentPaths.GrammarRoot

                // Act
                let html = Highlight.highlight h "x" "csharp" "var x = 1;\n"

                // Assert
                Expect.isFalse (List.contains "csharp" Highlight.supportedLanguages) "csharp is not advertised"
                Expect.stringContains html "class=\"tm\"" "wrapper kept"
                Expect.isTrue
                    (Highlight.fallbacks h |> List.exists (fun e -> e.Language = "csharp"))
                    "an unshipped language records a fallback, which fails the build"
            }

            test "a scope only another language's rule would claim keeps the default foreground" {
                // Arrange
                let h = Highlight.create contentPaths.GrammarRoot

                // Act
                let html = Highlight.highlight h "x" "fsharp" "let add x y = x + y\n"

                // Assert
                Expect.stringContains html "</span> add" "binding name is default-coloured, so no span"
                Expect.stringContains html "font-style:italic\"> x y </span>" "parameters keep variable.parameter styling"
            }

            test "an empty language fence yields a plain block and records no fallback" {
                // Arrange
                let h = Highlight.create contentPaths.GrammarRoot

                // Act
                let html = Highlight.highlight h "x" "" "plain text\n"

                // Assert
                Expect.stringContains html "class=\"tm\"" "wrapper kept"
                Expect.stringContains html "plain text" "code preserved"
                Expect.isEmpty (Highlight.fallbacks h) "an empty fence records no fallback"
            }
        ]

        testList "markdown" [
            // Keywords, a string and a comment, so several scopes get coloured.
            let sample =
                "## Effects as Values\n\nProse.\n\n"
                + "```fsharp\n// greet someone\nlet greet name = $\"Hello, {name}\"\n```\n"

            let renderer = Markdown.Renderer highlighter
            let render () = renderer.Render("content/notes/sample/index.md", sample) |> okOr

            test "headings get ids so client-side anchors can attach" {
                // Act
                let body = render ()

                // Assert
                Expect.stringContains body "<h2 id=\"effects-as-values\">" "heading id"
            }

            test "headings carry a server-rendered anchor link" {
                // Act
                let body = render ()

                // Assert
                Expect.stringContains
                    body
                    "<h2 id=\"effects-as-values\"><a class=\"heading-anchor\" href=\"#effects-as-values\""
                    "ssr anchor"
            }

            test "code blocks carry both light and dark themes, both Frappé" {
                // Act
                let body = render ()

                // Assert
                Expect.stringContains body "background-color:#303446" "frappé inline background"
                Expect.stringContains body "--tm-dark-bg:#303446" "dark background variable"
                Expect.stringContains body "class=\"tm\"" "wrapper class"
                Expect.stringContains body "<code style=\"color:#c6d0f5;--tm-dark:#c6d0f5\">" "default fg, both themes, on <code>"
            }

            test "every coloured token carries a dark counterpart" {
                // Arrange
                let body = render ()

                // Act
                let styles =
                    Regex.Matches(body, "<span style=\"([^\"]*)\"")
                    |> Seq.map (fun m -> m.Groups.[1].Value)
                    |> List.ofSeq

                let missing =
                    styles |> List.filter (fun s -> s.Contains "color:" && not (s.Contains "--tm-dark"))

                // Assert
                Expect.isNonEmpty styles "the fixture has styled spans to check"
                Expect.isEmpty missing "no coloured span omits its --tm-dark counterpart"
            }
        ]

        testList "image rewriting" [

            let renderInTemp (collection: string) (slug: string) (png: (string * string) option) (body: string) =
                let root = Path.Combine(Path.GetTempPath(), $"yggdrasil-img-{Guid.NewGuid():N}")
                let dir = Path.Combine(root, collection, slug)
                Directory.CreateDirectory dir |> ignore

                try
                    match png with
                    | Some(name, source) -> File.Copy(source, Path.Combine(dir, name))
                    | None -> ()

                    let renderer = Markdown.Renderer highlighter
                    renderer.Render(Path.Combine(dir, "index.md"), body)
                finally
                    Directory.Delete(root, true)

            let smallPng = Path.Combine(projectRoot, "static", "favicon", "favicon-16x16.png")

            test "a ./ source becomes a static webp path carrying the PNG's real dimensions" {
                // Act
                let result = renderInTemp "notes" "my-note" (Some("shot.png", smallPng)) "![A shot](./shot.png)"

                // Assert
                match result with
                | Error e -> failtestf "expected Ok, got: %s" e
                | Ok html ->
                    Expect.stringContains html "src=\"/images/notes/my-note/shot.webp\"" "webp path"
                    Expect.stringContains html "width=\"16\"" "width read from the PNG header"
                    Expect.stringContains html "height=\"16\"" "height read from the PNG header"
                    Expect.stringContains html "alt=\"A shot\"" "alt preserved"
                    Expect.stringContains html "loading=\"lazy\"" "lazy loading added"
                    Expect.isFalse (html.Contains "./shot.png") "the relative source is gone"
            }

            test "an image with a title is still rewritten, keeping the title" {
                // Act
                let result =
                    renderInTemp "notes" "my-note" (Some("shot.png", smallPng)) "![A shot](./shot.png \"A caption\")"

                // Assert
                match result with
                | Error e -> failtestf "expected Ok, got: %s" e
                | Ok html ->
                    Expect.stringContains html "src=\"/images/notes/my-note/shot.webp\"" "still rewritten"
                    Expect.stringContains html "width=\"16\"" "still measured"
                    Expect.stringContains html "title=\"A caption\"" "the title is carried over"
                    Expect.isFalse (html.Contains "./shot.png") "the relative source is gone"
            }

            test "a raw <img> with its own width does not end up with two" {
                // Arrange
                let body = "<img src=\"./shot.png\" alt=\"A\" width=\"99\">"

                // Act
                let result = renderInTemp "notes" "my-note" (Some("shot.png", smallPng)) body

                // Assert
                match result with
                | Error e -> failtestf "expected Ok, got: %s" e
                | Ok html ->
                    let widths = Regex.Matches(html, "\\bwidth=").Count
                    Expect.equal widths 1 "exactly one width attribute"
                    Expect.stringContains html "width=\"16\"" "the PNG's width, which matches its height"
            }

            test "absolute sources pass through untouched" {
                // Arrange
                let sources = [ "/already-static.png"; "https://example.com/x.png"; "data:image/gif;base64,R0lGOD" ]

                // Act & Assert
                for src in sources do
                    match renderInTemp "notes" "n" None $"![alt]({src})" with
                    | Error e -> failtestf "expected Ok for %s, got: %s" src e
                    | Ok html -> Expect.stringContains html src $"{src} is preserved"
            }

            test "a file that is not a PNG is rejected rather than given bogus dimensions" {
                // Arrange
                let notPng = Path.Combine(projectRoot, "static", "fonts", "metamorphous-latin-400-normal.woff2")

                // Act
                let result = renderInTemp "notes" "n" (Some("shot.png", notPng)) "![alt](./shot.png)"

                // Assert
                match result with
                | Ok _ -> failtest "a non-PNG should not be accepted"
                | Error e -> Expect.stringContains e "signature" "identifies the real problem"
            }

            test "the collection and slug come from the file's own location" {
                // Act
                let result = renderInTemp "projects" "deep-slug" (Some("a.png", smallPng)) "![alt](./a.png)"

                // Assert
                match result with
                | Error e -> failtestf "expected Ok, got: %s" e
                | Ok html -> Expect.stringContains html "/images/projects/deep-slug/a.webp" "path mirrors the source tree"
            }

            test "an image in a subfolder is rejected, since ./a/x.png and ./b/x.png would share one .webp" {
                // Act
                let result = renderInTemp "notes" "n" None "![x](./shots/x.png)"

                // Assert
                match result with
                | Ok _ -> failtest "a nested image should be rejected"
                | Error e -> Expect.stringContains e "subfolder" "explains the rule"
            }
        ]
    ]
