module Yggdrasil.Tests.Support

open Yggdrasil.Content

open System.IO

open Giraffe.ViewEngine

let projectRoot =
    let rec find (dir: DirectoryInfo) =
        if isNull dir then
            failwith "could not locate the project root (global.json) above the test assembly"
        elif File.Exists(Path.Combine(dir.FullName, "global.json")) then
            dir.FullName
        else
            find dir.Parent

    find (DirectoryInfo System.AppContext.BaseDirectory)

let contentPaths =
    { ContentRoot = Path.Combine(projectRoot, "content")
      GrammarRoot = Path.Combine(projectRoot, "assets", "grammars") }

let config =
    match Site.load (Path.Combine(projectRoot, "site.yaml")) with
    | Ok c -> c
    | Error errs -> failwithf "site config failed:\n%s" (String.concat "\n" errs)

let content, highlighter =
    match SiteContent.loadWithHighlighter contentPaths with
    | Ok result -> result
    | Error errs -> failwithf "content load failed:\n%s" (String.concat "\n" errs)

let renderNode (node: XmlNode) =
    RenderView.AsString.htmlNode node

open Yggdrasil.Web

let routeIn (source: SiteContent) (predicate: Route -> bool) =
    Route.all source |> List.find predicate

let noteRoute (id: string) =
    routeIn content (function
        | NoteShow(n, _, _) -> n.Id = id
        | _ -> false)

let projectRoute (id: string) =
    routeIn content (function
        | ProjectShow(p, _, _) -> p.Id = id
        | _ -> false)

let tagRoute (tag: string) =
    routeIn content (function
        | TagShow(t, _, _) -> t = tag
        | _ -> false)

let hasNoteRoute (source: SiteContent) (id: string) =
    Route.all source
    |> List.exists (function
        | NoteShow(n, _, _) -> n.Id = id
        | _ -> false)
