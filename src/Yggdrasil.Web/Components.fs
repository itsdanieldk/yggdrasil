namespace Yggdrasil.Web

open Yggdrasil.Content

open Giraffe.ViewEngine

open System
open System.Globalization

module Components =

    let private txt = encodedText

    let private cls (parts: string list) =
        _class (parts |> List.filter (String.IsNullOrEmpty >> not) |> String.concat " ")

    let private optAttr (cond: bool) (a: XmlAttribute) =
        if cond then
            [ a ]
        else
            []

    let stagger (i: int) =
        _style $"--i:{i}"

    let entryPath (e: FeedEntry) =
        match e with
        | FeedEntry.Note n -> $"/notes/{n.Id}"
        | FeedEntry.Project p -> $"/projects/{p.Id}"

    let tagPath (tag: string) =
        $"/tags/{Util.slugifyTag tag}"

    let container (children: XmlNode list) =
        div [ _class "mx-auto max-w-screen-sm px-6" ] children

    let pageHeading (children: XmlNode list) =
        h1 [ _class "animate text-3xl font-semibold tracking-tight text-black dark:text-white" ] children

    type LinkOpts =
        { External: bool
          Underline: bool
          Class: string
          Extra: XmlAttribute list }

    let link =
        { External = false
          Underline = true
          Class = ""
          Extra = [] }

    let siteLink (o: LinkOpts) (href: string) (children: XmlNode list) =
        a
            ([ _href href ]
             @ optAttr o.External (_target "_blank")
             @ optAttr o.External (_rel "noopener noreferrer")
             @ [ cls
                     [ "inline-block decoration-black/15 dark:decoration-white/30 hover:decoration-accent text-current hover:text-accent transition-colors duration-300 ease-in-out"
                       if o.Underline then "underline underline-offset-4" else ""
                       o.Class ] ]
             @ o.Extra)
            children

    let arrowIcon (className: string) =
        rawText (
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" "
            + $"class=\"stroke-2 fill-none stroke-current group-hover:stroke-accent {className}\">"
            + "<line x1=\"5\" y1=\"12\" x2=\"19\" y2=\"12\" class=\"translate-x-2 group-hover:translate-x-0 scale-x-0 group-hover:scale-x-100 transition-transform duration-300 ease-in-out\"></line>"
            + "<polyline points=\"12 5 5 12 12 19\" class=\"translate-x-1 group-hover:translate-x-0 transition-transform duration-300 ease-in-out\"></polyline>"
            + "</svg>"
        )

    let tagPill (tag: string) (href: string) =
        a [ _href href; _class "hover-card inline-block text-xs px-2 py-0.5 rounded" ] [ txt tag ]

    let formattedDate (date: DateOnly) =
        tag "time" [ attr "datetime" (DateParser.toIsoDatetime date) ] [ txt (date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)) ]

    let private cardMeta (e: FeedEntry) =
        match e with
        | FeedEntry.Note n -> n.ReadingTime :: n.Tags
        | FeedEntry.Project p -> p.Tags

    let arrowCard (entry: FeedEntry) =
        let meta = cardMeta entry

        a [ _href (entryPath entry); _class "hover-card relative group flex py-4 px-5 pr-12 rounded-lg" ] [
            div [ _class "flex flex-col flex-1" ] [
                div [ _class "font-semibold text-lg" ] [ txt entry.Title ]
                div [ _class "text-base" ] [ txt entry.Description ]
                if not (List.isEmpty meta) then
                    div [ _class "mt-1 text-sm text-black/40 dark:text-white/40" ] [ txt (String.concat " · " meta) ]
            ]
            arrowIcon "absolute top-1/2 right-3 -translate-y-1/2 size-6 rotate-180"
        ]

    let textButton (href: string) (external: bool) (className: string) (children: XmlNode list) =
        a
            ([ _href href ]
             @ optAttr external (_target "_blank")
             @ optAttr external (_rel "noopener noreferrer")
             @ [ cls [ "hover-card inline-flex items-center justify-center px-6 py-3 rounded-lg"; className ] ])
            children

    let backToPrev (href: string) (children: XmlNode list) =
        a [ _id "back-to-prev"; _href href; _class "hover-card relative group w-fit flex pl-7 pr-3 py-1.5 flex-nowrap rounded" ] [
            arrowIcon "absolute top-1/2 left-2 -translate-y-1/2 size-4"
            div [ _class "text-sm" ] children
        ]

    let backToTop =
        button [ _id "back-to-top"; attr "aria-label" "Back to top"; _class "hover-card relative group w-fit flex pl-10 pr-4 py-2 flex-nowrap rounded" ] [
            arrowIcon "absolute top-1/2 left-3 -translate-y-1/2 size-5 rotate-90"
            div [ _class "text-sm" ] [ txt "Back to top" ]
        ]

    let socialLinks (config: SiteConfig) (index: int) =
        ul [ _class "animate flex flex-wrap gap-2"; stagger index ] [
            for social in config.Socials do
                li [ _class "flex gap-x-2 text-nowrap" ] [
                    siteLink
                        { link with External = true; Extra = [ attr "aria-label" $"{config.Name} on {social.Name}" ] }
                        social.Href
                        [ txt social.Name ]
                    span [ attr "aria-hidden" "true" ] [ txt "/" ]
                ]
            li [ _class "line-clamp-1" ] [
                siteLink { link with Extra = [ attr "aria-label" $"Email {config.Name}" ] } $"mailto:{config.Email}" [ txt config.Email ]
            ]
        ]

    let private navCard (rotated: bool) (labelBefore: bool) (labelText: string) (entry: FeedEntry) =
        let arrow = arrowIcon (if rotated then "size-3.5 rotate-180" else "size-3.5")

        a [ _href (entryPath entry); _class "hover-card group flex flex-col py-3 px-4 rounded-lg" ] [
            span [ _class "text-xs text-black/40 dark:text-white/40 mb-1 inline-flex items-center gap-1" ] [
                if labelBefore then
                    yield txt (labelText + " ")
                    yield arrow
                else
                    yield arrow
                    yield txt (" " + labelText)
            ]
            span [ _class "text-sm font-semibold line-clamp-1" ] [ txt entry.Title ]
        ]

    let postNavigation (prev: FeedEntry option) (next: FeedEntry option) =
        [ if prev.IsSome || next.IsSome then
              nav [ _class "animate flex gap-4 mt-16"; attr "aria-label" "Previous and next posts" ] [
                  div [ _class "flex-1" ] [
                      match prev with
                      | Some p -> navCard false false "Previous" p
                      | None -> ()
                  ]
                  div [ _class "flex-1" ] [
                      match next with
                      | Some n -> navCard true true "Next" n
                      | None -> ()
                  ]
              ] ]

    let copyLinkButton =
        button [ _id "copy-link-button"; attr "aria-label" "Copy link to clipboard"; _class "group size-8 flex items-center justify-center rounded-full text-black/40 dark:text-white/40 hover:text-black dark:hover:text-white transition-colors duration-300 ease-in-out" ] [
            rawText "<svg id=\"copy-link-icon\" xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\"><path d=\"M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71\"></path><path d=\"M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71\"></path></svg>"
            rawText "<svg id=\"copy-link-check\" class=\"hidden\" xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\"><polyline points=\"20 6 9 17 4 12\"></polyline></svg>"
        ]

    let fragranceCard (f: Fragrance) =
        let meta = Fragrance.meta f

        a [ _href f.Url; _target "_blank"; _rel "noopener noreferrer"; _class "hover-card relative group flex items-center gap-4 py-4 px-5 pr-12 rounded-lg" ] [
            img [ _src f.Image; attr "srcset" $"{f.Image} 1x, {f.Image2x} 2x"; _alt ""; _width "64"; _height "64"; attr "loading" "lazy"; attr "decoding" "async"; _class "size-16 shrink-0 object-cover rounded" ]
            div [ _class "flex flex-col flex-1" ] [
                div [ _class "font-semibold text-lg" ] [ txt f.Name ]
                match f.Note with
                | Some note -> div [ _class "text-base" ] [ txt note ]
                | None -> ()
                div [ _class "mt-1 text-sm text-black/40 dark:text-white/40" ] [ txt (String.concat " · " meta) ]
            ]
            arrowIcon "absolute top-1/2 right-3 -translate-y-1/2 size-6 rotate-[135deg]"
        ]

    // Matched on href, not label, so relabelling an item in site.yaml can't silently lose its highlight.
    let private navActive (activePath: string option) (href: string) =
        if activePath = Some href then "!text-accent" else ""

    let private navSep =
        span [ _class "text-black/25 dark:text-white/25" ] [ txt "/" ]

    let siteHeader (config: SiteConfig) (activePath: string option) =
        header [] [
            container [
                div [ _class "flex flex-wrap gap-y-2 justify-between" ] [
                    siteLink { link with Underline = false } "/" [
                        div [ _class "text-2xl font-title" ] [ txt config.Name ]
                    ]
                    nav [ _class "flex flex-wrap gap-2 text-lg font-medium" ] [
                        for i, item in List.indexed config.Nav do
                            if i > 0 then
                                navSep

                            siteLink { link with Class = navActive activePath item.Href } item.Href [ txt item.Label ]
                    ]
                ]
            ]
        ]

    let private themeButton (id: string) (label: string) (iconSvg: string) =
        button [ _id id; attr "aria-label" label; attr "aria-pressed" "false"; _class "group size-8 flex items-center justify-center rounded-full" ] [ rawText iconSvg ]

    let private sunSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"18\" height=\"18\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" class=\"group-hover:stroke-black dark:group-hover:stroke-white transition-colors duration-300 ease-in-out\"><circle cx=\"12\" cy=\"12\" r=\"5\"></circle><line x1=\"12\" y1=\"1\" x2=\"12\" y2=\"3\"></line><line x1=\"12\" y1=\"21\" x2=\"12\" y2=\"23\"></line><line x1=\"4.22\" y1=\"4.22\" x2=\"5.64\" y2=\"5.64\"></line><line x1=\"18.36\" y1=\"18.36\" x2=\"19.78\" y2=\"19.78\"></line><line x1=\"1\" y1=\"12\" x2=\"3\" y2=\"12\"></line><line x1=\"21\" y1=\"12\" x2=\"23\" y2=\"12\"></line><line x1=\"4.22\" y1=\"19.78\" x2=\"5.64\" y2=\"18.36\"></line><line x1=\"18.36\" y1=\"5.64\" x2=\"19.78\" y2=\"4.22\"></line></svg>"

    let private moonSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"18\" height=\"18\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" class=\"group-hover:stroke-black dark:group-hover:stroke-white transition-colors duration-300 ease-in-out\"><path d=\"M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z\"></path></svg>"

    let private systemSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"18\" height=\"18\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" class=\"group-hover:stroke-black dark:group-hover:stroke-white transition-colors duration-300 ease-in-out\"><rect x=\"2\" y=\"3\" width=\"20\" height=\"14\" rx=\"2\" ry=\"2\"></rect><line x1=\"8\" y1=\"21\" x2=\"16\" y2=\"21\"></line><line x1=\"12\" y1=\"17\" x2=\"12\" y2=\"21\"></line></svg>"

    let siteFooter (config: SiteConfig) =
        let year = DateTime.UtcNow.Year
        footer [ _class "animate" ] [
            container [
                div [ _class "relative" ] [ div [ _class "absolute right-0 -top-20" ] [ backToTop ] ]
                div [ _class "flex justify-between items-center" ] [
                    div [] [ rawText $"&copy; {year} | "; txt config.Name ]
                    div [ _class "flex flex-wrap gap-1 items-center" ] [
                        themeButton "light-theme-button" "Light theme" sunSvg
                        themeButton "dark-theme-button" "Dark theme" moonSvg
                        themeButton "system-theme-button" "System theme" systemSvg
                    ]
                ]
            ]
        ]

    let readingProgress =
        div [ _id "reading-progress" ] []

    let homeSection (index: int) (title: string) (linkLabel: string) (linkHref: string) (entries: FeedEntry list) =
        section [ _class "animate space-y-6"; stagger index ] [
            div [ _class "flex flex-wrap gap-y-2 items-center justify-between" ] [
                h2 [ _class "text-sm font-semibold uppercase tracking-wider text-black/40 dark:text-white/40" ] [ txt title ]
                siteLink link linkHref [ txt linkLabel ]
            ]
            ul [ _class "flex flex-col gap-4" ] [
                for entry in entries do
                    li [] [ arrowCard entry ]
            ]
        ]
