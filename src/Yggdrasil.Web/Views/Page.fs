namespace Yggdrasil.Web.Views

open Yggdrasil.Web
open Yggdrasil.Content

open Giraffe.ViewEngine

module Page =

    let private txt = encodedText

    let private heading (page: Page) (extraClass: string) =
        h1 [ _class $"animate font-semibold tracking-tight text-black dark:text-white {extraClass}" ] [
            txt page.Heading
            match page.Emoji with
            | Some emoji ->
                txt " "
                span [ _class "text-5xl" ] [ txt emoji ]
            | None -> ()
        ]

    let home (config: SiteConfig) (content: SiteContent) =
        let page = content.Home
        let notes = content.Notes |> List.truncate config.NotesOnHomepage
        let projects = content.Projects |> List.truncate config.ProjectsOnHomepage
        let body =
            Components.container [
                heading page "text-3xl"
                div [ _class "space-y-16" ] [
                    section [] [
                        article [ _class "space-y-4" ] [ rawText page.Body ]
                        Components.socialLinks config 4
                    ]
                    Components.homeSection 5 "Latest notes" "See all notes" "/notes" (notes |> List.map FeedEntry.Note)
                    Components.homeSection 6 "Recent projects" "See all projects" "/projects" (projects |> List.map FeedEntry.Project)
                ]
            ]

        { Layouts.page config "/" page.Title page.Description [ body ] with
            JsonLd = Some(JsonLd.website config page.Description) }

    let about (config: SiteConfig) (content: SiteContent) =
        let page = content.About

        let srcset ext =
            [ 400; 600; 800 ]
            |> List.map (fun w -> $"{config.Avatar.Path}-{w}.{ext} {w}w")
            |> String.concat ", "

        let body =
            Components.container [
                div [ _class "space-y-8" ] [
                    Components.pageHeading [ txt page.Heading ]
                    article [ _class "space-y-4" ] [ rawText page.Body ]
                    tag "picture" [ _class "animate block mx-auto w-[232px] h-[232px]"; Components.stagger 5 ] [
                        tag "source" [ _type "image/webp"; attr "srcset" (srcset "webp"); _sizes "232px" ] []
                        img [ _src $"{config.Avatar.Path}.png"; attr "srcset" (srcset "png"); _sizes "232px"; _alt config.Avatar.Alt; attr "loading" "lazy"; attr "decoding" "async"; _width "232"; _height "232"; _class "w-[232px] h-[232px] object-cover rounded-full" ]
                    ]
                    Components.socialLinks config 6
                ]
            ]

        { Layouts.page config "/about" page.Title page.Description [ body ] with
            ActivePath = Some "/about"
            JsonLd = Some(JsonLd.person config) }
