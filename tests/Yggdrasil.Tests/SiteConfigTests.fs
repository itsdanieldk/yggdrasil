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
  home: { title: Home, description: d }
  notes: { title: Notes, description: d }
  projects: { title: Projects, description: d }
  fragrances: { title: Fragrances, description: d }
  tags: { title: Tags, description: d }
  about: { title: About, description: d }
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

        test "an unknown key is an error rather than being ignored" {
            // Arrange & Act & Assert
            withConfig (minimal + "nmae: typo\n") (function
                | Ok _ -> failtest "a typo'd key should not be silently dropped"
                | Error es -> Expect.stringContains (String.concat "\n" es) "invalid YAML" "reports the parse failure")
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

        test "Page metadata comes back for every key the generator renders" {
            // Assert
            for key in Site.requiredPageKeys do
                Expect.isNotEmpty (config.Page key).Title $"{key} has a title"
        }
    ]
