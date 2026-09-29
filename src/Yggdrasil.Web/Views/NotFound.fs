namespace Yggdrasil.Web.Views

open Yggdrasil.Web
open Yggdrasil.Content

open Giraffe.ViewEngine

module NotFound =

    let private txt = encodedText

    let page (config: SiteConfig) =
        let body =
            Components.container [
                div [ _class "space-y-10 text-center" ] [
                    div [ _class "animate space-y-8" ] [
                        div [ _class "text-9xl font-title font-bold text-black dark:text-white" ] [ txt "404" ]
                        h1 [ _class "text-3xl font-title font-semibold text-black dark:text-white" ] [ txt "Page Not Found" ]
                        p [ _class "text-lg" ] [ txt "Sorry, the page you're looking for doesn't exist or has been moved." ]
                    ]
                    div [ _class "animate flex flex-col sm:flex-row gap-4 justify-center"; Components.stagger 1 ] [
                        Components.textButton "/" false "" [ txt "Go to home" ]
                        Components.textButton "/notes" false "" [ txt "Browse notes" ]
                    ]
                ]
            ]

        { Layouts.page config "/404" "Page Not Found" "The page you're looking for doesn't exist." [ body ] with
            NoIndex = true }
