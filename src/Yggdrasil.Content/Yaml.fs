namespace Yggdrasil.Content

open System.Collections.Generic

open YamlDotNet.Serialization

module Yaml =

    let private keyReader = DeserializerBuilder().Build()

    // YamlDotNet returns null rather than failing for an empty or comment-only document, and every caller
    // dereferences the result, so an empty file used to escape the Result contract as a
    // NullReferenceException.
    let parse<'T> (deserializer: IDeserializer) (path: string) (what: string) (yaml: string) =
        try
            let value = deserializer.Deserialize<'T> yaml

            if isNull (box value) then
                Error [ $"{path}: {what} is empty" ]
            else
                Ok value
        with ex ->
            Error [ $"{path}: invalid {what}: {Util.exceptionDetail ex}" ]

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
            Error [ $"{path}: unknown key(s) {names} — check the spelling" ]
