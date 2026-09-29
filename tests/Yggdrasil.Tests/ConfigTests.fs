module Yggdrasil.Tests.ConfigTests

open Yggdrasil.Generate

open System
open System.IO

open Expecto

[<Tests>]
let tests =
    testList "Config" [

        let withTempDir (files: string list) (f: string -> unit) =
            let dir = Path.Combine(Path.GetTempPath(), $"yggdrasil-cfg-{Guid.NewGuid():N}")
            Directory.CreateDirectory dir |> ignore

            try
                for name in files do
                    File.WriteAllText(Path.Combine(dir, name), "")

                f dir
            finally
                Directory.Delete(dir, true)

        let expectError label result =
            match result with
            | Ok value -> failtestf "%s: expected Error, got Ok %A" label value
            | Error(e: string) -> e

        testList "base URL" [
            test "an absolute https URL is accepted and normalised with one trailing slash" {
                // Arrange
                let inputs = [ "https://example.com"; "https://example.com/"; "  https://example.com//  " ]

                // Act & Assert
                for input in inputs do
                    match Config.validateBaseUrl "SITE_URL" input with
                    | Ok url -> Expect.equal url "https://example.com/" $"normalised {input}"
                    | Error e -> failtestf "expected Ok for %s, got: %s" input e
            }

            test "http is accepted, for local and staging hosts" {
                // Act
                let result = Config.validateBaseUrl "SITE_URL" "http://localhost:8799"

                // Assert
                match result with
                | Ok url -> Expect.equal url "http://localhost:8799/" "http is allowed"
                | Error e -> failtestf "expected Ok, got: %s" e
            }

            test "a bare host is rejected — it would silently produce relative canonicals" {
                // Act
                let error = Config.validateBaseUrl "SITE_URL" "example.com" |> expectError "bare host"

                // Assert
                Expect.stringContains error "SITE_URL" "names the source"
                Expect.stringContains error "example.com" "quotes the value"
                Expect.stringContains error "absolute" "says what is wrong"
            }

            test "a typo'd scheme is rejected" {
                // Act
                let error = Config.validateBaseUrl "SITE_URL" "htp://example.com" |> expectError "typo scheme"

                // Assert
                Expect.stringContains error "htp://example.com" "quotes the value"
            }

            test "a site-relative base is rejected even though isSafeUrl allows it in an href" {
                // Act
                let error = Config.validateBaseUrl "SITE_URL" "/" |> expectError "root-relative"

                // Assert
                Expect.stringContains error "absolute" "explains the stricter rule"
            }

            test "a javascript: URL is rejected" {
                // Act
                let error =
                    Config.validateBaseUrl "SITE_URL" "javascript:alert(1)"
                    |> expectError "javascript scheme"

                // Assert
                Expect.stringContains error "javascript:alert(1)" "quotes the value"
            }

            test "a URL without a usable host is rejected — https:// would produce canonicals like https:/notes" {
                // Act & Assert
                for input in [ "https://"; "http://"; "https://exa mple.com" ] do
                    let error = Config.validateBaseUrl "SITE_URL" input |> expectError input
                    Expect.stringContains error "absolute http(s) URL" $"explains why \"{input}\" is rejected"
            }

            test "an empty value is rejected rather than silently becoming \"/\"" {
                // Act
                let error = Config.validateBaseUrl "SITE_URL" "   " |> expectError "blank"

                // Assert
                Expect.stringContains error "empty" "says the value is empty"
            }
        ]

        testList "project root" [
            test "a directory with both marker files is accepted" {
                // Arrange
                withTempDir [ "global.json"; "site.yaml" ] (fun dir ->
                    // Act
                    let result = Config.validateProjectRoot "SITE_ROOT" dir

                    // Assert
                    match result with
                    | Ok resolved -> Expect.equal resolved (Path.GetFullPath dir) "returns the full path"
                    | Error e -> failtestf "expected Ok, got: %s" e)
            }

            test "a directory missing site.yaml is rejected, naming the file" {
                // Arrange
                withTempDir [ "global.json" ] (fun dir ->
                    // Act
                    let error = Config.validateProjectRoot "SITE_ROOT" dir |> expectError "no site.yaml"

                    // Assert
                    Expect.stringContains error "site.yaml" "names the missing marker"
                    Expect.stringContains error "SITE_ROOT" "names the source")
            }

            test "an unrelated directory is rejected — this is what gates the recursive delete" {
                // Act
                let error =
                    Config.validateProjectRoot "SITE_ROOT" (Path.GetTempPath())
                    |> expectError "temp dir"

                // Assert
                Expect.stringContains error "not a Yggdrasil project root" "explains the refusal"
            }

            test "a nonexistent directory is rejected" {
                // Act
                let error =
                    Config.validateProjectRoot "SITE_ROOT" "/definitely/not/here"
                    |> expectError "missing dir"

                // Assert
                Expect.stringContains error "does not exist" "says the path is absent"
            }
        ]
    ]
