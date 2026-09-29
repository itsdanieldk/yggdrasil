namespace Yggdrasil.Content

open System
open System.Collections.Generic
open System.Text.RegularExpressions

open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

[<CLIMutable>]
type FrontmatterDto =
    { Title: string
      Description: string
      Date: string
      [<YamlMember(Alias = "updatedDate")>]
      UpdatedDate: string
      Tags: string[]
      Draft: Nullable<bool>
      Featured: Nullable<bool>
      Heading: string
      Emoji: string
      [<YamlMember(Alias = "demoURL")>]
      DemoUrl: string
      [<YamlMember(Alias = "repoURL")>]
      RepoUrl: string }

module Parser =

    let private delimiter =
        Regex(@"^---\s*$", RegexOptions.Multiline)

    let split (path: string) (contents: string) =
        match delimiter.Split(contents, 3) with
        | [| ""; frontmatter; body |] -> Ok(frontmatter, body.TrimStart '\n')
        | _ -> Error $"{path}: expected YAML frontmatter delimited by ---"

    let private deserializer =
        DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()

    let deserialize (path: string) (frontmatter: string) =
        try
            Ok(deserializer.Deserialize<FrontmatterDto> frontmatter)
        with ex ->
            Error $"{path}: invalid YAML frontmatter: {ex.Message}"

    let private keyReader = DeserializerBuilder().Build()

    let pageKeys = set [ "title"; "description"; "heading"; "emoji" ]

    let noteKeys =
        set [ "title"; "description"; "date"; "updatedDate"; "tags"; "draft"; "featured" ]

    let projectKeys = Set.union noteKeys (set [ "demoURL"; "repoURL" ])

    let rejectUnknownKeys (path: string) (allowed: Set<string>) (yaml: string) =
        let keys =
            try
                match keyReader.Deserialize<Dictionary<string, obj>> yaml with
                | null -> []
                | mapping -> List.ofSeq mapping.Keys
            with _ ->
                []

        match keys |> List.filter (allowed.Contains >> not) with
        | [] -> Ok()
        | unknown ->
            let names = unknown |> List.map (fun key -> $"\"{key}\"") |> String.concat ", "
            Error $"{path}: unknown key(s) {names} — check the spelling"

    let private required path field (value: string) =
        if isNull value then
            Error $"{path}: {field}: required field is missing"
        else
            Ok value

    let private decodeCommon path id (dto: FrontmatterDto) rawBody renderedBody =
        result {
            let! title = required path "title" dto.Title
            let! description = required path "description" dto.Description
            let! date = DateParser.tryParse path "date" (Option.ofObj dto.Date)
            let! updatedDate = DateParser.tryParseOptional path "updatedDate" (Option.ofObj dto.UpdatedDate)

            let tags =
                if isNull dto.Tags then
                    []
                else
                    List.ofArray dto.Tags

            let draft = dto.Draft.GetValueOrDefault false
            let featured = dto.Featured.GetValueOrDefault false

            return
                {| Id = id
                   Title = title
                   Description = description
                   Date = date
                   UpdatedDate = updatedDate
                   Body = renderedBody
                   ReadingTime = Util.readingTime rawBody
                   Tags = tags
                   Draft = draft
                   Featured = featured |}
        }

    let decodePage path id (dto: FrontmatterDto) renderedBody =
        result {
            let! title = required path "title" dto.Title
            let! description = required path "description" dto.Description

            return
                { Id = id
                  Title = title
                  Description = description
                  Heading = (if isNull dto.Heading then title else dto.Heading)
                  Emoji = Option.ofObj dto.Emoji
                  Body = renderedBody }
        }

    let decodeNote path id dto rawBody renderedBody =
        decodeCommon path id dto rawBody renderedBody
        |> Result.map (fun c ->
            { Id = c.Id
              Title = c.Title
              Description = c.Description
              Date = c.Date
              UpdatedDate = c.UpdatedDate
              Body = c.Body
              ReadingTime = c.ReadingTime
              Tags = c.Tags
              Draft = c.Draft
              Featured = c.Featured })

    let private optionalUrl path field (value: string) =
        match Option.ofObj value with
        | None -> Ok None
        | Some url when Util.isSafeUrl url -> Ok(Some url)
        | Some url -> Error $"{path}: {field}: \"{url}\" is not an http(s), mailto or site-relative URL"

    let decodeProject path id (dto: FrontmatterDto) rawBody renderedBody =
        decodeCommon path id dto rawBody renderedBody
        |> Result.bind (fun c ->
            result {
                let! demo = optionalUrl path "demoURL" dto.DemoUrl
                let! repo = optionalUrl path "repoURL" dto.RepoUrl
                return c, demo, repo
            })
        |> Result.map (fun (c, demoUrl, repoUrl) ->
            { Id = c.Id
              Title = c.Title
              Description = c.Description
              Date = c.Date
              UpdatedDate = c.UpdatedDate
              Body = c.Body
              ReadingTime = c.ReadingTime
              Tags = c.Tags
              Draft = c.Draft
              Featured = c.Featured
              DemoUrl = demoUrl
              RepoUrl = repoUrl })
