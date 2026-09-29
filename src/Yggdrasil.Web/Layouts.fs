namespace Yggdrasil.Web

open Yggdrasil.Content

open Giraffe.ViewEngine

open System

type ArticleMeta =
    { Published: DateOnly
      Modified: DateOnly
      Tags: string list }

type PageContext =
    { Config: SiteConfig
      CanonicalPath: string
      Title: string
      Description: string
      OgType: string
      OgImage: string option
      JsonLd: string option
      ActivePath: string option
      Content: XmlNode list
      Article: ArticleMeta option
      NoIndex: bool }

module Layouts =

    let private homeTitle (config: SiteConfig) =
        $"{config.Name} — {config.Author} · {config.Tagline}"

    let pageTitle (ctx: PageContext) =
        if ctx.CanonicalPath = "/" then
            homeTitle ctx.Config
        else
            $"{ctx.Title} | {ctx.Config.Name}"

    let canonicalUrl (ctx: PageContext) =
        Site.absoluteUrl ctx.Config ctx.CanonicalPath

    let ogImageUrl (ctx: PageContext) =
        Site.absoluteUrl ctx.Config (defaultArg ctx.OgImage Site.defaultOgImagePath)

    let private themeScript =
        """
      function toggleTheme(dark) {
        const css = document.createElement("style");
        css.appendChild(document.createTextNode("* { transition: none !important; }"));
        document.head.appendChild(css);

        document.documentElement.classList.toggle("dark", dark);

        void window.getComputedStyle(css).opacity;
        document.head.removeChild(css);
      }

      function readStoredTheme() {
        try {
          return localStorage.theme ?? null;
        } catch {
          return null;
        }
      }

      window.__theme = { toggle: toggleTheme };

      const stored = readStoredTheme();
      if (stored === "light" || stored === "dark") {
        toggleTheme(stored === "dark");
      } else {
        toggleTheme(window.matchMedia("(prefers-color-scheme: dark)").matches);
      }

      window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", (event) => {
        const current = readStoredTheme();
        if (current === "system" || !current) {
          toggleTheme(event.matches);
        }
      });
    """

    let private vercelStub =
        """window.va = window.va || function () { (window.vaq = window.vaq || []).push(arguments); };"""

    let private headNode (ctx: PageContext) =
        let title' = pageTitle ctx
        let desc = ctx.Description
        let canonical = canonicalUrl ctx
        let ogImage = ogImageUrl ctx

        let articleMeta =
            match ctx.Article with
            | Some a ->
                [ meta [ _property "article:published_time"; _content (DateParser.toIsoDatetime a.Published) ]
                  meta [ _property "article:modified_time"; _content (DateParser.toIsoDatetime a.Modified) ]
                  meta [ _property "article:author"; _content ctx.Config.Author ] ]
                @ [ for tag in a.Tags -> meta [ _property "article:tag"; _content tag ] ]
            | None -> []

        head [] [
            meta [ _charset "utf-8" ]
            meta [ _name "viewport"; _content "width=device-width,initial-scale=1" ]
            link [ _rel "icon"; _type "image/x-icon"; _href "/favicon/favicon.ico" ]
            link [ _rel "icon"; _type "image/png"; _sizes "16x16"; _href "/favicon/favicon-16x16.png" ]
            link [ _rel "icon"; _type "image/png"; _sizes "32x32"; _href "/favicon/favicon-32x32.png" ]
            link [ _rel "apple-touch-icon"; _sizes "180x180"; _href "/favicon/apple-touch-icon.png" ]
            link [ _rel "manifest"; _href "/site.webmanifest" ]
            link [ _rel "preload"; _href "/fonts/fira-sans-latin-400-normal.woff2"; attr "as" "font"; _type "font/woff2"; flag "crossorigin" ]
            link [ _rel "preload"; _href "/fonts/metamorphous-latin-400-normal.woff2"; attr "as" "font"; _type "font/woff2"; flag "crossorigin" ]

            if ctx.NoIndex then
                meta [ _name "robots"; _content "noindex, follow" ]
            else
                link [ _rel "canonical"; _href canonical ]

            title [] [ encodedText title' ]
            meta [ _name "description"; _content desc ]
            meta [ _name "author"; _content ctx.Config.Author ]

            meta [ _property "og:type"; _content ctx.OgType ]
            meta [ _property "og:site_name"; _content ctx.Config.Name ]
            meta [ _property "og:locale"; _content "en_US" ]
            meta [ _property "og:url"; _content canonical ]
            meta [ _property "og:title"; _content title' ]
            meta [ _property "og:description"; _content desc ]
            meta [ _property "og:image"; _content ogImage ]
            meta [ _property "og:image:width"; _content "1200" ]
            meta [ _property "og:image:height"; _content "630" ]
            meta [ _property "og:image:alt"; _content title' ]

            for node in articleMeta do
                node

            meta [ _name "twitter:card"; _content "summary_large_image" ]
            meta [ _name "twitter:url"; _content canonical ]
            meta [ _name "twitter:title"; _content title' ]
            meta [ _name "twitter:description"; _content desc ]
            meta [ _name "twitter:image"; _content ogImage ]
            meta [ _name "twitter:image:alt"; _content title' ]

            link [ _rel "alternate"; _type "application/rss+xml"; _title ctx.Config.Name; _href (Site.absoluteUrl ctx.Config "/rss.xml") ]

            meta [ _name "theme-color"; _content "#f0efed"; _media "(prefers-color-scheme: light)" ]
            meta [ _name "theme-color"; _content "#1c1917"; _media "(prefers-color-scheme: dark)" ]

            link [ _rel "stylesheet"; _href "/assets/css/app.css" ]
            script [ _defer; _type "text/javascript"; _src "/assets/js/app.js" ] []
            script [] [ rawText themeScript ]

            match ctx.JsonLd with
            | Some jsonLd -> script [ _type "application/ld+json" ] [ rawText jsonLd ]
            | None -> ()

            script [] [ rawText vercelStub ]
            script [ _defer; _src "/_vercel/insights/script.js" ] []
        ]

    let root (ctx: PageContext) =
        html [ _lang "en" ] [
            headNode ctx
            body [] [
                a
                    [ _href "#main-content"
                      _class "sr-only focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-50 focus:px-4 focus:py-2 focus:bg-black focus:text-white dark:focus:bg-white dark:focus:text-black focus:rounded" ]
                    [ encodedText "Skip to main content" ]
                Components.siteHeader ctx.Config ctx.ActivePath
                main [ _id "main-content" ] ctx.Content
                Components.siteFooter ctx.Config
                // The copy buttons' only visual feedback is an icon swap; app.js announces success here instead.
                div [ _id "status"; attr "role" "status"; _class "sr-only" ] []
            ]
        ]

    let render (ctx: PageContext) =
        RenderView.AsString.htmlDocument (root ctx)

    // The defaults every page starts from. Views name what they change with { … with … }, where eight
    // positional arguments, four of them string options, let a swapped pair compile unnoticed.
    let page (config: SiteConfig) (canonicalPath: string) (title: string) (description: string) content =
        { Config = config
          CanonicalPath = canonicalPath
          Title = title
          Description = description
          OgType = "website"
          OgImage = None
          JsonLd = None
          ActivePath = None
          Content = content
          Article = None
          NoIndex = false }
