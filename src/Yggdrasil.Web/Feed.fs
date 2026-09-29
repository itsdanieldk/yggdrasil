namespace Yggdrasil.Web

open Yggdrasil.Content

open System.Globalization

open System

module Feed =

    let private isXmlLegal (c: char) =
        c = '\t' || c = '\n' || c = '\r' || c >= ' '

    let private escape (text: string) =
        text
        |> String.filter isXmlLegal
        |> fun t ->
            t
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;")

    let private rfc822 (date: DateOnly) =
        date.ToString("ddd, dd MMM yyyy '00:00:00 GMT'", CultureInfo.InvariantCulture)

    let rss (config: SiteConfig) (content: SiteContent) =
        let items =
            Content.feedEntries content
            |> List.map (fun entry ->
                let url = Site.absoluteUrl config (Components.entryPath entry)

                "<item>"
                + $"<title>{escape entry.Title}</title>"
                + $"<link>{escape url}</link>"
                + $"<guid isPermaLink=\"true\">{escape url}</guid>"
                + $"<description>{escape entry.Description}</description>"
                + $"<pubDate>{rfc822 entry.Date}</pubDate>"
                + "</item>")
            |> String.concat ""

        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<rss version=\"2.0\"><channel>"
        + $"<title>{escape config.Name}</title>"
        + $"<description>{escape content.Home.Description}</description>"
        + $"<link>{escape config.BaseUrl}</link>"
        + items
        + "</channel></rss>"

    let sitemapIndex (config: SiteConfig) =
        let loc = Site.absoluteUrl config "/sitemap-0.xml"

        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<sitemapindex xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
        + $"<sitemap><loc>{escape loc}</loc></sitemap>"
        + "</sitemapindex>"

    let lastmod (date: DateOnly) =
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

    let sitemap (config: SiteConfig) (entries: (string * string option) list) =
        let urls =
            entries
            |> List.distinctBy fst
            |> List.map (fun (path, lastmod) -> Site.absoluteUrl config path, lastmod)
            |> List.sortBy fst
            |> List.map (fun (loc, lastmod) ->
                let lm =
                    match lastmod with
                    | Some d -> $"<lastmod>{d}</lastmod>"
                    | None -> ""

                $"<url><loc>{escape loc}</loc>{lm}</url>")
            |> String.concat ""
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"
        + urls
        + "</urlset>"

    let robots (config: SiteConfig) =
        let sitemap = Site.absoluteUrl config "/sitemap-index.xml"
        $"User-agent: *\nAllow: /\n\nSitemap: {sitemap}"

    let webmanifest (config: SiteConfig) (description: string) =
        let icon size =
            Json.node
                [ "src", Json.s $"/favicon/android-chrome-{size}x{size}.png"
                  "sizes", Json.s $"{size}x{size}"
                  "type", Json.s "image/png"
                  "purpose", Json.s "any maskable" ]

        let categories =
            [ "education"; "news"; "technology"; "software"; "software engineering"
              "engineering"; "programming"; "computer science"; "computers" ]
            |> List.map Json.s

        Json.render
            [ "name", Json.s $"{config.Name} - {config.Author}"
              "short_name", Json.s config.Name
              "description", Json.s description
              "icons", Json.arr [ icon 192; icon 512 ]
              "theme_color", Json.s "#f0efed"
              "background_color", Json.s "#f0efed"
              "display", Json.s "standalone"
              "start_url", Json.s "/"
              "scope", Json.s "/"
              "orientation", Json.s "portrait-primary"
              "categories", Json.arr categories ]
