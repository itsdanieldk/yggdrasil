namespace Yggdrasil.Content

open System
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
        | _ -> Error [ $"{path}: expected YAML frontmatter delimited by ---" ]

    let private deserializer =
        DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()

    let deserialize (path: string) (frontmatter: string) =
        Yaml.parse<FrontmatterDto> deserializer path "YAML frontmatter" frontmatter

    let pageKeys = set [ "title"; "description"; "heading"; "emoji" ]

    let noteKeys =
        set [ "title"; "description"; "date"; "updatedDate"; "tags"; "draft"; "featured" ]

    let projectKeys = Set.union noteKeys (set [ "demoURL"; "repoURL" ])

    let private requiredDate path field (value: string) =
        DateParser.tryParse path field (Option.ofObj value) |> Result.mapError List.singleton

    let private optionalDate path field (value: string) =
        DateParser.tryParseOptional path field (Option.ofObj value) |> Result.mapError List.singleton

    // The rendered body arrives as a Result so that image and code-fence errors are reported together
    // with any frontmatter errors in the same file.
    let decodeNote path id (dto: FrontmatterDto) rawBody (renderedBody: Result<string, string list>) =
        result {
            let! title = Util.required path "title" dto.Title
            and! description = Util.required path "description" dto.Description
            and! date = requiredDate path "date" dto.Date
            and! updatedDate = optionalDate path "updatedDate" dto.UpdatedDate
            and! body = renderedBody

            return
                { Id = id
                  Title = title
                  Description = description
                  Date = date
                  UpdatedDate = updatedDate
                  Body = body
                  ReadingTime = Util.readingTime rawBody
                  Tags = if isNull dto.Tags then [] else List.ofArray dto.Tags
                  Draft = dto.Draft.GetValueOrDefault false
                  Featured = dto.Featured.GetValueOrDefault false }
        }

    let decodePage path id (dto: FrontmatterDto) (renderedBody: Result<string, string list>) =
        result {
            let! title = Util.required path "title" dto.Title
            and! description = Util.required path "description" dto.Description
            and! body = renderedBody

            return
                { Id = id
                  Title = title
                  Description = description
                  Heading = defaultArg (Util.optional dto.Heading) title
                  Emoji = Util.optional dto.Emoji
                  Body = body }
        }

    let private optionalUrl path field (value: string) =
        match Util.optional value with
        | None -> Ok None
        | Some url when Util.isSafeUrl url -> Ok(Some url)
        | Some url -> Error [ $"{path}: {field}: \"{url}\" is not an http(s), mailto or site-relative URL" ]

    let decodeProject path id (dto: FrontmatterDto) rawBody (renderedBody: Result<string, string list>) =
        result {
            let! note = decodeNote path id dto rawBody renderedBody
            and! demoUrl = optionalUrl path "demoURL" dto.DemoUrl
            and! repoUrl = optionalUrl path "repoURL" dto.RepoUrl

            return
                { Id = note.Id
                  Title = note.Title
                  Description = note.Description
                  Date = note.Date
                  UpdatedDate = note.UpdatedDate
                  Body = note.Body
                  ReadingTime = note.ReadingTime
                  Tags = note.Tags
                  Draft = note.Draft
                  Featured = note.Featured
                  DemoUrl = demoUrl
                  RepoUrl = repoUrl }
        }
