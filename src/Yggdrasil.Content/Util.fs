namespace Yggdrasil.Content

open System
open System.Text.RegularExpressions

module Util =

    let private wordsPerMinute = 200.0

    let slugifyTag (tag: string) =
        let lowered = tag.ToLowerInvariant().Replace("#", "sharp")
        let hyphenated = Regex.Replace(lowered, "[^a-z0-9]+", "-")
        hyphenated.Trim '-'

    let readingTime (content: string) =
        let text =
            content
            |> fun s -> Regex.Replace(s, @"```.*?```", "", RegexOptions.Singleline)
            |> fun s -> Regex.Replace(s, @"!\[[^\]]*\]\([^)]*\)", "")
            |> fun s -> Regex.Replace(s, @"\[([^\]]*)\]\([^)]*\)", "$1")
            |> fun s -> Regex.Replace(s, @"<[^>]+>", "")
            |> fun s -> Regex.Replace(s, @"&[#a-z0-9]+;", " ", RegexOptions.IgnoreCase)
            |> fun s -> Regex.Replace(s, @"[-*_~`#>|\\]", "")
            |> fun s -> Regex.Replace(s, @"\n{2,}", " ")

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
            url.StartsWith "/" && not (url.Length > 1 && (url.[1] = '/' || url.[1] = '\\'))

        siteRelative
        || [ "http://"; "https://"; "mailto:" ]
           |> List.exists (fun scheme -> url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))

    let rec exceptionDetail (ex: exn) =
        match ex.InnerException with
        | null -> ex.Message
        | inner -> ex.Message + " → " + exceptionDetail inner

    let published (isDraft: 'a -> bool) (getDate: 'a -> 'k) (entries: 'a list) =
        entries
        |> List.filter (isDraft >> not)
        |> List.sortByDescending getDate
