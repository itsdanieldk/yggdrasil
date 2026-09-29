namespace Yggdrasil.Content

open System
open System.IO
open System.Collections.Generic

open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

type Social = { Name: string; Href: string }

type NavItem = { Label: string; Href: string }

type PageMeta = { Title: string; Description: string }

type Avatar = { Path: string; Alt: string }

type Person =
    { WorksFor: string option
      AlumniOf: string option
      KnowsAbout: string list }

type SiteConfig =
    { BaseUrl: string
      Name: string
      Author: string
      Tagline: string
      Email: string
      Avatar: Avatar
      Person: Person
      Nav: NavItem list
      Socials: Social list
      NotesOnHomepage: int
      ProjectsOnHomepage: int
      OgDefaultTags: string list
      Pages: Map<string, PageMeta> }

    member this.Page(key: string) =
        match Map.tryFind key this.Pages with
        | Some meta -> meta
        | None -> failwith $"site.yaml: pages.{key} is not defined"

module Site =

    [<CLIMutable>]
    type SocialDto = { Name: string; Href: string }

    [<CLIMutable>]
    type NavItemDto = { Label: string; Href: string }

    [<CLIMutable>]
    type AvatarDto = { Path: string; Alt: string }

    [<CLIMutable>]
    type PageMetaDto = { Title: string; Description: string }

    [<CLIMutable>]
    type PersonDto =
        { WorksFor: string
          AlumniOf: string
          KnowsAbout: string[] }

    [<CLIMutable>]
    type HomepageDto =
        { Notes: Nullable<int>
          Projects: Nullable<int> }

    [<CLIMutable>]
    type SiteDto =
        { Name: string
          Author: string
          Tagline: string
          Email: string
          Url: string
          Avatar: AvatarDto
          Person: PersonDto
          Nav: NavItemDto[]
          Socials: SocialDto[]
          OgDefaultTags: string[]
          Homepage: HomepageDto
          Pages: Dictionary<string, PageMetaDto> }

    let requiredPageKeys =
        [ "home"; "notes"; "projects"; "fragrances"; "tags"; "about" ]

    let private deserializer =
        DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build()

    let private req (errors: ResizeArray<string>) (field: string) (value: string) =
        if String.IsNullOrWhiteSpace value then
            errors.Add $"site.yaml: {field}: required field is missing"
            ""
        else
            value.Trim()

    let private reqUrl (errors: ResizeArray<string>) (field: string) (value: string) =
        let trimmed = req errors field value

        if trimmed <> "" && not (Util.isSafeUrl trimmed) then
            errors.Add $"site.yaml: {field}: \"{trimmed}\" is not an http(s), mailto or site-relative URL"

        trimmed

    let private parse (dto: SiteDto) =
        let errors = ResizeArray<string>()
        let name = req errors "name" dto.Name
        let author = req errors "author" dto.Author
        let tagline = req errors "tagline" dto.Tagline
        let email = req errors "email" dto.Email
        let url = reqUrl errors "url" dto.Url

        let avatar: Avatar =
            if obj.ReferenceEquals(dto.Avatar, null) then
                errors.Add "site.yaml: avatar: required section is missing"
                { Path = ""; Alt = "" }
            else
                { Path = reqUrl errors "avatar.path" dto.Avatar.Path
                  Alt = req errors "avatar.alt" dto.Avatar.Alt }

        let optional (value: string) =
            if String.IsNullOrWhiteSpace value then None else Some(value.Trim())

        let person: Person =
            if obj.ReferenceEquals(dto.Person, null) then
                { WorksFor = None; AlumniOf = None; KnowsAbout = [] }
            else
                { WorksFor = optional dto.Person.WorksFor
                  AlumniOf = optional dto.Person.AlumniOf
                  KnowsAbout =
                    if isNull dto.Person.KnowsAbout then
                        []
                    else
                        dto.Person.KnowsAbout |> Array.toList |> List.filter (String.IsNullOrWhiteSpace >> not) }

        let nav: NavItem list =
            if isNull dto.Nav then
                []
            else
                dto.Nav
                |> Array.toList
                |> List.mapi (fun i n ->
                    { Label = req errors $"nav[{i}].label" n.Label
                      Href = reqUrl errors $"nav[{i}].href" n.Href })

        let socials: Social list =
            if isNull dto.Socials then
                []
            else
                dto.Socials
                |> Array.toList
                |> List.mapi (fun i s ->
                    { Name = req errors $"socials[{i}].name" s.Name
                      Href = reqUrl errors $"socials[{i}].href" s.Href })

        let notesOnHome, projectsOnHome =
            if obj.ReferenceEquals(dto.Homepage, null) then
                errors.Add "site.yaml: homepage: required section is missing"
                0, 0
            else
                dto.Homepage.Notes.GetValueOrDefault 3, dto.Homepage.Projects.GetValueOrDefault 3

        let pages =
            if isNull dto.Pages then
                errors.Add "site.yaml: pages: required section is missing"
                Map.empty
            else
                for key in requiredPageKeys do
                    if not (dto.Pages.ContainsKey key) then
                        errors.Add $"site.yaml: pages.{key}: required page metadata is missing"

                for kv in dto.Pages do
                    if obj.ReferenceEquals(kv.Value, null) then
                        errors.Add $"site.yaml: pages.{kv.Key}: title and description are missing"

                dto.Pages
                |> Seq.filter (fun kv -> not (obj.ReferenceEquals(kv.Value, null)))
                |> Seq.map (fun kv ->
                    let meta: PageMeta =
                        { Title = req errors $"pages.{kv.Key}.title" kv.Value.Title
                          Description = req errors $"pages.{kv.Key}.description" kv.Value.Description }

                    kv.Key, meta)
                |> Map.ofSeq

        if errors.Count > 0 then
            Error(List.ofSeq errors)
        else
            Ok
                { BaseUrl = url.TrimEnd '/' + "/"
                  Name = name
                  Author = author
                  Tagline = tagline
                  Email = email
                  Avatar = avatar
                  Person = person
                  Nav = nav
                  Socials = socials
                  NotesOnHomepage = notesOnHome
                  ProjectsOnHomepage = projectsOnHome
                  OgDefaultTags =
                    if isNull dto.OgDefaultTags then [] else List.ofArray dto.OgDefaultTags
                  Pages = pages }

    let load (path: string) =
        if not (File.Exists path) then
            Error [ $"{path}: site config not found" ]
        else
            try
                parse (deserializer.Deserialize<SiteDto>(File.ReadAllText path))
            with ex ->
                Error [ $"{path}: invalid YAML: {Util.exceptionDetail ex}" ]

    let absoluteUrl (config: SiteConfig) (path: string) =
        config.BaseUrl.TrimEnd '/' + "/" + path.TrimStart '/'

    let ogImagePath (collection: string) (id: string) =
        $"/og/{collection}/{id}.png"

    let defaultOgImagePath =
        "/og/default.png"
