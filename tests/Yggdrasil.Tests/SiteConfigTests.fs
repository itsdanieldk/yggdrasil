module Yggdrasil.Tests.SiteConfigTests

open Yggdrasil.Content
open Yggdrasil.Tests.Support

open System.IO

open Expecto

[<Tests>]
let tests =
    testList "SiteConfig" [

        let withConfig (yaml: string) (f: Result<SiteConfig, string list> -> unit) =
            let path = Path.Combine(Path.GetTempPath(), $"yggdrasil-site-{System.Guid.NewGuid():N}.yaml")
            File.WriteAllText(path, yaml)

            try
                f (Site.load path)
            finally
                File.Delete path

        let minimal =
            """
name: example
author: A Person
tagline: Builder
email: a@example.com
url: https://example.com/
avatar:
  path: /avatar
  alt: A picture
homepage:
  notes: 2
  projects: 1
pages:
  notes: { title: Notes, description: d }
  projects: { title: Projects, description: d }
  fragrances: { title: Fragrances, description: d }
  tags: { title: Tags, description: d }
"""

        test "the committed site.yaml loads" {
            // Assert
            Expect.isNotEmpty config.Name "name"
            Expect.isNotEmpty config.Author "author"
            Expect.isNotEmpty config.Tagline "tagline"
            Expect.stringEnds config.BaseUrl "/" "base URL is normalised with a trailing slash"
        }

        test "a minimal config parses, defaulting the optional sections" {
            // Act & Assert
            withConfig minimal (function
                | Error es -> failtestf "expected Ok, got: %s" (String.concat "; " es)
                | Ok c ->
                    Expect.equal c.Name "example" "name"
                    Expect.equal c.NotesOnHomepage 2 "homepage notes"
                    Expect.isEmpty c.Socials "socials default to none"
                    Expect.isEmpty c.OgDefaultTags "og tags default to none"
                    Expect.equal c.Person.WorksFor None "person section is optional"
                    Expect.isEmpty c.Person.KnowsAbout "knowsAbout defaults to empty")
        }

        test "a URL without a trailing slash is normalised" {
            // Arrange & Act & Assert
            withConfig (minimal.Replace("https://example.com/", "https://example.com")) (function
                | Ok c -> Expect.equal c.BaseUrl "https://example.com/" "exactly one trailing slash"
                | Error es -> failtestf "expected Ok, got: %s" (String.concat "; " es))
        }

        test "every missing required field is reported at once, not just the first" {
            // Act & Assert
            withConfig "name: only-a-name\n" (function
                | Ok _ -> failtest "expected Error"
                | Error es ->
                    // The point of aggregating: one pass tells you everything to fix.
                    Expect.isGreaterThan es.Length 3 "several problems reported together"
                    let joined = String.concat "\n" es
                    for field in [ "author"; "tagline"; "email"; "url"; "avatar" ] do
                        Expect.stringContains joined field $"names the missing {field}")
        }

        test "home and about are described by their own frontmatter, so pages holds only the index pages" {
            // Act & Assert
            withConfig minimal (function
                | Ok c ->
                    Expect.equal c.Pages.Notes.Title "Notes" "notes"
                    Expect.equal c.Pages.Projects.Title "Projects" "projects"
                    Expect.equal c.Pages.Fragrances.Title "Fragrances" "fragrances"
                    Expect.equal c.Pages.Tags.Title "Tags" "tags"
                | Error es -> failtestf "expected Ok, got: %s" (String.concat "; " es))
        }

        test "a pages key other than the four index pages is rejected" {
            // Arrange & Act & Assert — home is what a site.yaml from before typed pages still carries.
            for key in [ "home"; "blog" ] do
                withConfig (minimal + $"  {key}: {{ title: T, description: d }}\n") (function
                    | Ok _ -> failtest $"pages.{key} should not be silently ignored"
                    | Error es -> Expect.stringContains (String.concat "\n" es) key "names the key")
        }

        test "a missing page key is named" {
            // Arrange & Act & Assert
            withConfig (minimal.Replace("  tags: { title: Tags, description: d }\n", "")) (function
                | Ok _ -> failtest "expected Error"
                | Error es -> Expect.stringContains (String.concat "\n" es) "pages.tags" "names the missing key")
        }

        test "a page key with an empty body is caught here, not by the renderer" {
            // Arrange & Act & Assert
            withConfig (minimal.Replace("  tags: { title: Tags, description: d }", "  tags:")) (function
                | Ok _ -> failtest "an empty page section should not reach SiteConfig.Page"
                | Error es -> Expect.stringContains (String.concat "\n" es) "pages.tags" "names the empty key")
        }

        test "a blank required field is rejected like a missing one" {
            // Arrange & Act & Assert
            withConfig (minimal.Replace("author: A Person", "author: \"   \"")) (function
                | Ok _ -> failtest "expected Error"
                | Error es -> Expect.stringContains (String.concat "\n" es) "author" "whitespace is not a value")
        }

        test "every unknown top-level key is named, not just the first" {
            // Arrange & Act & Assert
            withConfig (minimal + "nmae: typo\ntagln: typo\n") (function
                | Ok _ -> failtest "typo'd keys should not be silently dropped"
                | Error es ->
                    let joined = String.concat "\n" es
                    Expect.stringContains joined "\"nmae\"" "names the first"
                    Expect.stringContains joined "\"tagln\"" "names the second")
        }

        test "an empty file is reported as empty, not as a null dereference" {
            // Arrange & Act & Assert
            withConfig "" (function
                | Ok _ -> failtest "an empty site.yaml should not load"
                | Error es ->
                    Expect.stringContains (String.concat "\n" es) "YAML file is empty" "says the file is empty")
        }

        test "a javascript: URL anywhere in the config is rejected, naming the field" {
            // Arrange
            let withNav =
                minimal + "nav:\n  - { label: bad, href: 'javascript:alert(1)' }\n"

            // Act & Assert
            withConfig withNav (function
                | Ok _ -> failtest "a javascript: href should not load"
                | Error es ->
                    let joined = String.concat "\n" es
                    Expect.stringContains joined "nav[0].href" "names the offending field"
                    Expect.stringContains joined "javascript:alert(1)" "quotes the offending value")
        }

        test "a missing file is reported by path" {
            // Act
            let result = Site.load "/definitely/not/here/site.yaml"

            // Assert
            match result with
            | Ok _ -> failtest "expected Error"
            | Error es -> Expect.stringContains (List.head es) "not found" "says the file is missing"
        }

        test "the committed site.yaml describes every index page" {
            // Assert
            let pages = config.Pages

            for meta in [ pages.Notes; pages.Projects; pages.Fragrances; pages.Tags ] do
                Expect.isNotEmpty meta.Title "a title"
                Expect.isNotEmpty meta.Description $"{meta.Title} has a description"
        }
    ]
