namespace Yggdrasil.Web.Views

open Yggdrasil.Web
open Yggdrasil.Content

open Giraffe.ViewEngine

module Fragrance =

    let private txt = encodedText

    let index (config: SiteConfig) (owned: Fragrance list) (wishlist: Fragrance list) =
        let meta = config.Pages.Fragrances
        let body =
            Components.container [
                div [ _class "space-y-10" ] [
                    Components.pageHeading [ txt "Fragrances" ]
                    p [ _class "animate"; Components.stagger 1 ] [ txt "What I own and how I'd rate it. Each entry links to Fragrantica." ]
                    ul [ _class "animate flex flex-col gap-4"; Components.stagger 2 ] [
                        for fragrance in owned -> li [] [ Components.fragranceCard fragrance ]
                    ]
                    if not (List.isEmpty wishlist) then
                        div [ _class "space-y-10" ] [
                            div [ _class "space-y-2" ] [
                                h2 [ _class "animate text-sm font-semibold uppercase tracking-wider text-black/40 dark:text-white/40"; Components.stagger 3 ] [ txt "On my radar" ]
                                p [ _class "animate"; Components.stagger 4 ] [ txt "Bottles I'm considering next." ]
                            ]
                            ul [ _class "animate flex flex-col gap-4"; Components.stagger 5 ] [
                                for fragrance in wishlist -> li [] [ Components.fragranceCard fragrance ]
                            ]
                        ]
                ]
            ]

        { Layouts.page config "/fragrances" meta.Title meta.Description [ body ] with
            ActivePath = Some "/fragrances" }
