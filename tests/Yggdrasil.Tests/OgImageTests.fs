module Yggdrasil.Tests.OgImageTests

open Yggdrasil.Content
open Yggdrasil.Generate
open Yggdrasil.Tests.Support

open System
open System.IO

open Expecto

let private fontsDir = Path.Combine(projectRoot, "assets", "fonts")

let private pngSize (path: string) =
    let bytes = File.ReadAllBytes path
    let signature = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

    if bytes.Length < 24 then
        failtestf "%s is only %d bytes — not a PNG" path bytes.Length

    if bytes[0..7] <> signature then
        failtestf "%s does not start with the PNG signature" path

    let readInt offset =
        (int bytes[offset] <<< 24)
        ||| (int bytes[offset + 1] <<< 16)
        ||| (int bytes[offset + 2] <<< 8)
        ||| int bytes[offset + 3]

    readInt 16, readInt 20

let private mkTmp () =
    Path.Combine(Path.GetTempPath(), "yggdrasil-og-" + Guid.NewGuid().ToString "N")

let private noteWith id title tags =
    { Fixtures.note id with
        Title = title
        Tags = tags }

[<Tests>]
let tests =

    testList "OgImage" [
        test "writes a 1200x630 card for the default page and every note/project, at the paths the head/JSON-LD reference" {
            // Arrange
            let tmp = mkTmp ()

            let cards =
                Site.defaultOgImagePath
                :: [ for n in content.Notes -> Site.ogImagePath "notes" n.Id ]
                @ [ for p in content.Projects -> Site.ogImagePath "projects" p.Id ]

            try
                // Act
                let result = OgImage.generateAll config fontsDir tmp content.Notes content.Projects
                Expect.isOk result "every card is drawn"

                // Assert
                for rel in cards do
                    let file = Path.Combine(tmp, rel.TrimStart '/')
                    Expect.isTrue (File.Exists file) $"a card exists at {rel}"
                    Expect.equal (pngSize file) (1200, 630) $"{rel} is 1200x630"
            finally
                Directory.Delete(tmp, true)
        }

        test "renders long titles, empty tags, and more than three tags without error" {
            // Arrange
            let tmp = mkTmp ()

            let notes =
                [ noteWith "long-title" (String.replicate 30 "Verylongword ") [ "a"; "b"; "c"; "d"; "e" ]
                  noteWith "short-no-tags" "Hi" [] ]

            try
                // Act
                Expect.isOk (OgImage.generateAll Fixtures.config fontsDir tmp notes []) "every card is drawn"

                // Assert
                for n in notes do
                    let file = Path.Combine(tmp, (Site.ogImagePath "notes" n.Id).TrimStart '/')
                    Expect.equal (pngSize file) (1200, 630) $"{n.Id} is 1200x630"
            finally
                Directory.Delete(tmp, true)
        }

        test "text the card font cannot draw is an error naming the characters, not an empty box" {
            // Arrange
            let tmp = mkTmp ()
            let notes = [ noteWith "arrow" "F# → Scala" [ "λ-calculus" ] ]

            try
                // Act
                let result = OgImage.generateAll Fixtures.config fontsDir tmp notes []

                // Assert
                match result with
                | Ok() -> failtest "expected the missing glyphs to be reported"
                | Error errors ->
                    let joined = String.concat "\n" errors
                    Expect.stringContains joined "/og/notes/arrow.png" "names the card"
                    Expect.stringContains joined "\"→\"" "names the character in the title"
                    Expect.stringContains joined "\"λ\"" "names the character in the tag"
                    Expect.isFalse (Directory.Exists tmp) "draws nothing when a card would be broken"
            finally
                if Directory.Exists tmp then
                    Directory.Delete(tmp, true)
        }
    ]
