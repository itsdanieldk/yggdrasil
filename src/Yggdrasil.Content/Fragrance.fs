namespace Yggdrasil.Content

open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

open System
open System.Globalization

[<CLIMutable>]
type FragranceDto =
    { Name: string
      House: string
      Url: string
      Rating: Nullable<float>
      Note: string
      Concentration: string
      Wishlist: Nullable<bool>
      Draft: Nullable<bool> }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Fragrance =

    let private deserializer =
        DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()

    let formatRating (rating: float) =
        if rating = Math.Truncate rating then
            string (int rating)
        else
            rating.ToString CultureInfo.InvariantCulture

    let meta (fragrance: Fragrance) =
        [ Some fragrance.House
          fragrance.Concentration
          fragrance.Rating |> Option.map (fun r -> $"{formatRating r}/10") ]
        |> List.choose id

    let private allowedKeys =
        set [ "name"; "house"; "url"; "rating"; "note"; "concentration"; "wishlist"; "draft" ]

    let private required (path: string) (field: string) (value: string) =
        if isNull value then
            Error $"{path}: {field}: required field is missing"
        else
            Ok value

    let private validate (path: string) (wishlist: bool) (rating: float option) =
        match wishlist, rating with
        | true, Some _ -> Error $"{path}: wishlist entries must not have a rating"
        | false, None -> Error $"{path}: rating is required unless wishlist is true"
        | _, Some r when Double.IsNaN r || r < 0.0 || r > 10.0 ->
            Error $"{path}: rating {r} is outside 0-10"
        | _ -> Ok()

    let decode (path: string) (id: string) (yaml: string) =
        let parsed =
            try
                Ok(deserializer.Deserialize<FragranceDto> yaml)
            with ex ->
                Error $"{path}: invalid YAML: {ex.Message}"

        result {
            do! Parser.rejectUnknownKeys path allowedKeys yaml
            let! dto = parsed
            let wishlist = dto.Wishlist.GetValueOrDefault false
            let rating = Option.ofNullable dto.Rating
            do! validate path wishlist rating
            let! name = required path "name" dto.Name
            let! house = required path "house" dto.House
            let! url = required path "url" dto.Url

            let! url =
                if Util.isSafeUrl url then
                    Ok url
                else
                    Error $"{path}: url: \"{url}\" is not an http(s), mailto or site-relative URL"

            return
                { Id = id
                  Name = name
                  House = house
                  Url = url
                  Rating = rating
                  Note = Option.ofObj dto.Note
                  Concentration = Option.ofObj dto.Concentration
                  Image = $"/images/fragrances/{id}/bottle.png"
                  Image2x = $"/images/fragrances/{id}/bottle@2x.png"
                  Wishlist = wishlist
                  Draft = dto.Draft.GetValueOrDefault false }
        }
