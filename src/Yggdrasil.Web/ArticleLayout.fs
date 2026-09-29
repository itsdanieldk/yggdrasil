namespace Yggdrasil.Web

open Yggdrasil.Content

open Giraffe.ViewEngine

module ArticleLayout =

    let private txt = encodedText

    let articlePage
        (entry: FeedEntry)
        (prev: FeedEntry option)
        (next: FeedEntry option)
        (backHref: string)
        (backLabel: string)
        (children: XmlNode list) =
        let tags = match entry with FeedEntry.Note n -> n.Tags | FeedEntry.Project p -> p.Tags

        [ Components.readingProgress
          Components.container [
              div [ _class "animate" ] [ Components.backToPrev backHref [ txt backLabel ] ]
              div [ _class "space-y-1 my-10" ] [
                  div [ _class "animate flex items-center gap-1.5"; Components.stagger 1 ] [
                      div [ _class "font-base text-sm" ] [ Components.formattedDate entry.Date ]
                      rawText "&bull;"
                      div [ _class "font-base text-sm" ] [ txt entry.ReadingTime ]
                      match entry.UpdatedDate with
                      | Some ud when ud <> entry.Date ->
                          div [ _class "contents" ] [
                              rawText "&bull;"
                              div [ _class "font-base text-sm" ] [ txt "Updated: "; Components.formattedDate ud ]
                          ]
                      | _ -> ()
                  ]
                  div [ _class "animate flex items-center gap-2"; Components.stagger 2 ] [
                      h1 [ _class "text-3xl font-semibold tracking-tight text-black dark:text-white" ] [ txt entry.Title ]
                      Components.copyLinkButton
                  ]
                  if not (List.isEmpty tags) then
                      div [ _class "animate flex flex-wrap gap-1.5"; Components.stagger 3 ] [
                          for tag in tags -> Components.tagPill tag (Components.tagPath tag)
                      ]
                  yield! children
              ]
              article [ _class "animate"; Components.stagger 4 ] [ rawText entry.Body ]
              yield! Components.postNavigation prev next
          ] ]
