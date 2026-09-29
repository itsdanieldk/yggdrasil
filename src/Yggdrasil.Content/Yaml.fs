namespace Yggdrasil.Content

open System
open System.Collections.Generic
open System.IO

open Microsoft.FSharp.Reflection
open YamlDotNet.RepresentationModel
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

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

    let private unknownKeysError (path: string) (unknown: string list) =
        match unknown with
        | [] -> Ok()
        | _ ->
            let names = unknown |> List.map (fun key -> $"\"{key}\"") |> String.concat ", "
            Error [ $"{path}: unknown key(s) {names} — check the spelling" ]

    let rejectUnknownKeys (path: string) (allowed: Set<string>) (yaml: string) =
        let keys =
            try
                match keyReader.Deserialize<Dictionary<string, obj>> yaml with
                | null -> []
                | mapping -> List.ofSeq mapping.Keys
            with _ ->
                []

        keys |> List.filter (allowed.Contains >> not) |> unknownKeysError path

    // Every key, at any depth, that the DTO 'T doesn't declare, named by its path ("pages.blog",
    // "nav[0].target"). A strict deserializer rejects these too, but only the first one, and by .NET type name.
    // Keys are matched camelCased, as the site deserializer names them.
    let rejectUnknownKeysOf<'T> (path: string) (yaml: string) =
        let rec unknown (dtoType: Type) (prefix: string) (node: YamlNode) =
            match node with
            | :? YamlMappingNode as mapping when FSharpType.IsRecord dtoType ->
                let fields =
                    dtoType.GetProperties()
                    |> Array.map (fun p -> CamelCaseNamingConvention.Instance.Apply p.Name, p.PropertyType)
                    |> dict

                [ for KeyValue(key, value) in mapping.Children do
                      let name =
                          match key with
                          | :? YamlScalarNode as scalar -> scalar.Value
                          | other -> string other

                      match fields.TryGetValue name with
                      | true, fieldType -> yield! unknown fieldType $"{prefix}{name}." value
                      | false, _ -> yield prefix + name ]
            | :? YamlSequenceNode as sequence when dtoType.IsArray ->
                sequence.Children
                |> Seq.mapi (fun i item -> unknown (dtoType.GetElementType()) $"{prefix.TrimEnd '.'}[{i}]." item)
                |> List.concat
            | _ -> []

        let keys =
            try
                let stream = YamlStream()
                stream.Load(new StringReader(yaml))

                if stream.Documents.Count = 0 then
                    []
                else
                    unknown typeof<'T> "" stream.Documents.[0].RootNode
            with _ ->
                []

        unknownKeysError path keys
