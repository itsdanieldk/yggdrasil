namespace Yggdrasil.Content

[<AutoOpen>]
module Prelude =

    type ResultBuilder() =
        
        member _.Bind(x, f) =
            Result.bind f x

        member _.Return x =
            Ok x
        
        member _.ReturnFrom(x: Result<_, _>) =
            x

        // `let! … and! …` checks independent values together and reports every failure, where a chain
        // of `let!`s would stop at the first. Keep `let!` for steps that need an earlier result.
        member _.MergeSources(left: Result<'a, 'e list>, right: Result<'b, 'e list>) =
            match left, right with
            | Ok l, Ok r -> Ok(l, r)
            | Error l, Error r -> Error(l @ r)
            | Error e, Ok _
            | Ok _, Error e -> Error e

    let result = ResultBuilder()

    let collectResults (results: Result<'a, 'e> list) =
        let oks, errors =
            results
            |> List.fold
                (fun (oks, errors) r ->
                    match r with
                    | Ok v -> v :: oks, errors
                    | Error e -> oks, e :: errors)
                ([], [])

        match errors with
        | [] -> Ok(List.rev oks)
        | _ -> Error(List.rev errors)
