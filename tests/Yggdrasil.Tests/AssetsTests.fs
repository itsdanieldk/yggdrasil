module Yggdrasil.Tests.AssetsTests

open Yggdrasil.Generate

open System.IO
open System.Runtime.InteropServices

open Expecto

[<Tests>]
let tests =
    testList "Assets" [

        testList "resolveAssets" [
            let cases =
                [ "macOS arm64", Assets.MacOS, Architecture.Arm64, false, "tailwindcss-macos-arm64", "darwin-arm64"
                  "macOS x64", Assets.MacOS, Architecture.X64, false, "tailwindcss-macos-x64", "darwin-x64"
                  "Linux arm64 glibc", Assets.Linux, Architecture.Arm64, false, "tailwindcss-linux-arm64", "linux-arm64"
                  "Linux arm64 musl", Assets.Linux, Architecture.Arm64, true, "tailwindcss-linux-arm64-musl", "linux-arm64"
                  "Linux x64 glibc", Assets.Linux, Architecture.X64, false, "tailwindcss-linux-x64", "linux-x64"
                  "Linux x64 musl", Assets.Linux, Architecture.X64, true, "tailwindcss-linux-x64-musl", "linux-x64"
                  "Windows x64", Assets.Windows, Architecture.X64, false, "tailwindcss-windows-x64.exe", "win32-x64" ]

            for name, os, arch, musl, tailwind, esbuild in cases do
                test $"{name} resolves to its published binaries" {
                    // Act
                    let result = Assets.resolveAssets os arch musl

                    // Assert
                    match result with
                    | Ok a ->
                        Expect.equal a.TailwindAsset tailwind "tailwind asset"
                        Expect.equal a.EsbuildPkg esbuild "esbuild package"
                    | Error e -> failtestf "expected Ok for %s, got: %s" name e
                }

            test "every supported platform has both of its downloads pinned" {
                // Act & Assert
                for name, os, arch, musl, _, _ in cases do
                    match Assets.resolveAssets os arch musl with
                    | Ok a ->
                        Expect.equal a.TailwindSha256.Length 64 $"{name}: tailwind pinned by SHA-256"
                        Expect.stringStarts a.EsbuildIntegrity "sha512-" $"{name}: esbuild pinned by npm integrity"
                    | Error e -> failtestf "expected Ok for %s, got: %s" name e
            }

            test "Windows arm64 falls back to the x64 pair" {
                // Act
                let result = Assets.resolveAssets Assets.Windows Architecture.Arm64 false

                // Assert
                match result with
                | Ok a ->
                    Expect.equal a.TailwindAsset "tailwindcss-windows-x64.exe" "tailwind asset"
                    Expect.equal a.EsbuildPkg "win32-x64" "esbuild package"
                | Error e -> failtestf "expected Ok, got: %s" e
            }

            test "musl is ignored on macOS and Windows" {
                // Act
                let mac = Assets.resolveAssets Assets.MacOS Architecture.Arm64 true
                let win = Assets.resolveAssets Assets.Windows Architecture.X64 true

                // Assert
                Expect.equal
                    (mac |> Result.map (fun a -> a.TailwindAsset))
                    (Ok "tailwindcss-macos-arm64")
                    "macOS has no musl variant"

                Expect.equal
                    (win |> Result.map (fun a -> a.TailwindAsset))
                    (Ok "tailwindcss-windows-x64.exe")
                    "Windows has no musl variant"
            }

            test "a 32-bit host is rejected, naming the platform" {
                // Act
                let result = Assets.resolveAssets Assets.Linux Architecture.X86 false

                // Assert
                match result with
                | Ok a -> failtestf "expected Error, got %s" a.TailwindAsset
                | Error e ->
                    Expect.stringContains e "Linux" "names the OS"
                    Expect.stringContains e "X86" "names the architecture"
                    Expect.stringContains e "Supported:" "lists what does work"
            }

            test "an unsupported OS is rejected rather than served a Windows .exe" {
                // Act
                let result = Assets.resolveAssets (Assets.Unsupported "FreeBSD 14.0") Architecture.X64 false

                // Assert
                match result with
                | Ok a -> failtestf "expected Error, got %s" a.TailwindAsset
                | Error e -> Expect.stringContains e "FreeBSD 14.0" "names the detected OS"
            }
        ]

        testList "digest" [
            test "a download is hashed in the format of the pin it is checked against" {
                // Arrange
                let path = Path.GetTempFileName()

                // Act
                let sha256, integrity =
                    try
                        File.WriteAllText(path, "abc")
                        Assets.digest (String.replicate 64 "0") path, Assets.digest "sha512-" path
                    finally
                        File.Delete path

                // Assert — the standard test vectors for "abc".
                Expect.equal
                    sha256
                    "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
                    "a Tailwind pin: SHA-256 as lowercase hex"

                Expect.equal
                    integrity
                    "sha512-3a81oZNherrMQXNJriBBMRLm+k6JqX6iCp7u5ktV05ohkpkqJ0/BqDa6PCOj/uu9RU1EI2Q86A4qmslPpUyknw=="
                    "an esbuild pin: npm's integrity, SHA-512 as base64"
            }
        ]
    ]
