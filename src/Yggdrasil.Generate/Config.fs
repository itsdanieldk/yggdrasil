module Yggdrasil.Generate.Config

open System
open System.IO

type GeneratorConfig =
    { BaseUrl: string
      SkipAssets: bool }

let private env name =
    match Environment.GetEnvironmentVariable name with
    | null
    | "" -> None
    | value -> Some value

let validateBaseUrl (source: string) (raw: string) =
    let trimmed = raw.Trim()

    // A host is required as well as the scheme: "https://" would otherwise pass, and every canonical and
    // feed URL would come out as "https:/notes/...".
    let isAbsoluteHttp =
        match Uri.TryCreate(trimmed, UriKind.Absolute) with
        | true, uri -> (uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps) && uri.Host <> ""
        | _ -> false

    if trimmed = "" then
        Error $"{source}: base URL is empty"
    elif not isAbsoluteHttp then
        Error $"{source}: \"{trimmed}\" must be an absolute http(s) URL (e.g. https://example.com)"
    else
        Ok(trimmed.TrimEnd '/' + "/")

let validateProjectRoot (source: string) (root: string) =
    if not (Directory.Exists root) then
        Error $"{source}: \"{root}\" does not exist"
    else
        let missing =
            [ "global.json"; "site.yaml" ]
            |> List.filter (fun f -> not (File.Exists(Path.Combine(root, f))))

        if not (List.isEmpty missing) then
            let names = String.concat " and " missing
            Error $"{source}: \"{root}\" is not a Yggdrasil project root — missing {names}"
        else
            Ok(Path.GetFullPath root)

let rec private searchUpwards (dir: DirectoryInfo) =
    if isNull dir then
        None
    elif File.Exists(Path.Combine(dir.FullName, "global.json")) then
        Some dir.FullName
    else
        searchUpwards dir.Parent

let resolveProjectRoot (argv: string array) =
    match argv with
    | [| root |] -> validateProjectRoot "project root argument" root
    | _ ->
        match env "SITE_ROOT" with
        | Some root -> validateProjectRoot "SITE_ROOT" root
        | None ->
            match searchUpwards (DirectoryInfo AppContext.BaseDirectory) with
            | Some root -> validateProjectRoot "detected project root" root
            | None -> Error "could not locate the project root (no global.json found above the assembly)"

let resolveBaseUrl (configuredUrl: string) =
    match env "SITE_URL" with
    | Some url -> validateBaseUrl "SITE_URL" url
    | None -> validateBaseUrl "site.yaml: url" configuredUrl

let resolve (configuredUrl: string) =
    resolveBaseUrl configuredUrl
    |> Result.map (fun baseUrl ->
        { BaseUrl = baseUrl
          SkipAssets = (env "SKIP_ASSETS").IsSome })
