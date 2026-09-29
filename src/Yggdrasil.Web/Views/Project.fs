namespace Yggdrasil.Web.Views

open Yggdrasil.Web
open Yggdrasil.Content

open Giraffe.ViewEngine

module Project =

    let private txt = encodedText

    let index (config: SiteConfig) (projects: Project list) =
        let meta = config.Pages.Projects

        let body =
            Components.container [
                div [ _class "space-y-10" ] [
                    Components.pageHeading [ txt "Projects" ]
                    p [ _class "animate"; Components.stagger 1 ] [ txt "Open source work and side projects, mostly in the F# and .NET ecosystem." ]
                    ul [ _class "animate flex flex-col gap-4"; Components.stagger 2 ] [
                        for project in projects -> li [] [ Components.arrowCard (FeedEntry.Project project) ]
                    ]
                ]
            ]

        { Layouts.page config "/projects" meta.Title meta.Description [ body ] with
            ActivePath = Some "/projects" }

    let show (config: SiteConfig) (project: Project) (prev: Project option) (next: Project option) =
        let links =
            match project.DemoUrl, project.RepoUrl with
            | None, None -> []
            | _ ->
                [ nav [ _class "animate flex gap-1" ] [
                      match project.DemoUrl with
                      | Some d -> Components.siteLink { Components.link with External = true } d [ txt "demo" ]
                      | None -> ()
                      match project.DemoUrl, project.RepoUrl with
                      | Some _, Some _ -> span [] [ txt "/" ]
                      | _ -> ()
                      match project.RepoUrl with
                      | Some r -> Components.siteLink { Components.link with External = true } r [ txt "repository" ]
                      | None -> ()
                  ] ]

        let body = ArticleLayout.articlePage (FeedEntry.Project project) (Option.map FeedEntry.Project prev) (Option.map FeedEntry.Project next) "/projects" "Back to projects" links

        let article =
            { Published = project.Date
              Modified = defaultArg project.UpdatedDate project.Date
              Tags = project.Tags }

        { Layouts.page config $"/projects/{project.Id}" project.Title project.Description body with
            OgType = "article"
            OgImage = Some(Site.ogImagePath "projects" project.Id)
            JsonLd = Some(JsonLd.project config project)
            ActivePath = Some "/projects"
            Article = Some article }
