namespace Yggdrasil.Content

open System
open System.Text.RegularExpressions

module Util =

    let private wordsPerMinute = 200.0

    // Order matters: fenced code goes first so nothing inside it is counted, and images go before
    // links because the link pattern would otherwise leave an image's alt text behind as prose.
    let private markupStrips =
        [ @"```.*?```", "", RegexOptions.Singleline
          @"!\[[^\]]*\]\([^)]*\)", "", RegexOptions.None
          @"\[([^\]]*)\]\([^)]*\)", "$1", RegexOptions.None
          @"<[^>]+>", "", RegexOptions.None
          @"&[#a-z0-9]+;", " ", RegexOptions.IgnoreCase
          @"[-*_~`#>|\\]", "", RegexOptions.None ]

    let slugifyTag (tag: string) =
        let lowered = tag.ToLowerInvariant().Replace("#", "sharp")
        let hyphenated = Regex.Replace(lowered, "[^a-z0-9]+", "-")
        hyphenated.Trim '-'

    let private slugPattern = Regex @"^[a-z0-9]+(?:-[a-z0-9]+)*\z"

    // Content folder and file names are used verbatim as URL path segments, so they must already be slugs.
    let isValidSlug (name: string) = slugPattern.IsMatch name

    let readingTime (content: string) =
        let strip (text: string) (pattern: string, replacement: string, options: RegexOptions) =
            Regex.Replace(text, pattern, replacement, options)

        let text = markupStrips |> List.fold strip content

        let wordCount =
            Regex.Split(text.Trim(), @"\s+")
            |> Array.filter (fun w -> w <> "")
            |> Array.length

        let minutes = int (ceil (float wordCount / wordsPerMinute))
        if minutes = 0 then
            "< 1 min read"
        else
            $"{minutes} min read"

    let isSafeUrl (url: string) =
        let url = url.Trim()

        let siteRelative =
            url.StartsWith '/' && not (url.Length > 1 && (url.[1] = '/' || url.[1] = '\\'))

        siteRelative
        || [ "http://"; "https://"; "mailto:" ]
           |> List.exists (fun scheme -> url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))

    let rec exceptionDetail (ex: exn) =
        match ex.InnerException with
        | null -> ex.Message
        | inner -> ex.Message + " → " + exceptionDetail inner

    // A blank value counts as missing: `title: ""` would otherwise build a page with an empty heading.
    let required (path: string) (field: string) (value: string) =
        if String.IsNullOrWhiteSpace value then
            Error [ $"{path}: {field}: required field is missing" ]
        else
            Ok(value.Trim())

    // A blank value is absent. Anything else is kept verbatim: the home heading's trailing space is what
    // separates it from the emoji.
    let optional (value: string) =
        if String.IsNullOrWhiteSpace value then None else Some value

    let published (isDraft: 'a -> bool) (getDate: 'a -> 'k) (entries: 'a list) =
        entries
        |> List.filter (isDraft >> not)
        |> List.sortByDescending getDate
