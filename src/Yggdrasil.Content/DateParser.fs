namespace Yggdrasil.Content

open System
open System.Globalization

module DateParser =

    let private parseIso (path: string) (field: string) (value: string) =
        match DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, date -> Ok date
        | _ -> Error $"{path}: {field}: invalid ISO date \"{value}\""

    let tryParse (path: string) (field: string) (value: string option) =
        match value with
        | Some v when not (String.IsNullOrWhiteSpace v) -> parseIso path field v
        | _ -> Error $"{path}: {field}: required date is missing"

    let tryParseOptional (path: string) (field: string) (value: string option) =
        match value with
        | Some v when not (String.IsNullOrWhiteSpace v) -> parseIso path field v |> Result.map Some
        | _ -> Ok None

    let toIsoDatetime (date: DateOnly) =
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00.000Z"
