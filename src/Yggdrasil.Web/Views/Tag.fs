namespace Yggdrasil.Web.Views

open Yggdrasil.Web
open Yggdrasil.Content

open Giraffe.ViewEngine

module Tag =

    let private txt = encodedText

    let index (config: SiteConfig) (tags: (string * int) list) =
        let meta = config.Pages.Tags
        let body =
            Components.container [
                div [ _class "space-y-10" ] [
                    Components.pageHeading [ txt "Tags" ]
                    if not (List.isEmpty tags) then
                        div [ _class "animate flex flex-wrap gap-2"; Components.stagger 1 ] [
                            for tag, count in tags do
                                a [ _href (Components.tagPath tag); _class "hover-card inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg" ] [
                                    span [] [ txt tag ]
                                    span [ _class "text-xs text-black/40 dark:text-white/40" ] [ txt (string count) ]
                                ]
                        ]
                    else
                        p [ _class "animate"; Components.stagger 1 ] [ txt "No tags yet." ]
                ]
            ]

        { Layouts.page config "/tags" meta.Title meta.Description [ body ] with
            ActivePath = Some "/tags" }

    let show (config: SiteConfig) (tag: string) (notes: Note list) (projects: Project list) =
        let slug = Util.slugifyTag tag

        let sectionFor (index: int) (label: string) (entries: FeedEntry list) =
            if List.isEmpty entries then
                []
            else
                [ section [ _class "animate space-y-4"; Components.stagger index ] [
                      div [ _class "text-sm font-semibold uppercase tracking-wider text-black/40 dark:text-white/40" ] [ txt label ]
                      ul [ _class "flex flex-col gap-4" ] [ for entry in entries -> li [] [ Components.arrowCard entry ] ]
                  ] ]

        let body =
            Components.container [
                div [ _class "space-y-10" ] [
                    div [ _class "animate" ] [ Components.backToPrev "/tags" [ txt "Back to tags" ] ]
                    Components.pageHeading [ rawText "Tagged &ldquo;"; txt tag; rawText "&rdquo;" ]
                    yield! sectionFor 1 "Notes" (notes |> List.map FeedEntry.Note)
                    yield! sectionFor 2 "Projects" (projects |> List.map FeedEntry.Project)
                ]
            ]

        { Layouts.page config $"/tags/{slug}" tag $"Content tagged \"{tag}\"" [ body ] with
            ActivePath = Some "/tags" }
