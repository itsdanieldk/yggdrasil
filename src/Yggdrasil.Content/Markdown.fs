namespace Yggdrasil.Content

open System
open System.IO
open System.Text
open System.Text.RegularExpressions

open Markdig
open Markdig.Syntax
open Markdig.Renderers
open Markdig.Renderers.Html
open Markdig.Extensions.EmphasisExtras
open Markdig.Extensions.AutoIdentifiers

// Collects fence errors rather than failing, so Render can report them together with the file's other errors.
type private HighlightCodeBlockRenderer(highlighter: Highlight.Highlighter, path: string, errors: ResizeArray<string>) =
    inherit HtmlObjectRenderer<CodeBlock>()

    override _.Write(renderer: HtmlRenderer, codeBlock: CodeBlock) =
        let lang =
            match codeBlock with
            | :? FencedCodeBlock as fenced -> fenced.Info
            | _ -> null

        let sb = StringBuilder()

        for i in 0 .. codeBlock.Lines.Count - 1 do
            sb.Append(codeBlock.Lines.Lines.[i].Slice.ToString()).Append '\n' |> ignore

        let code = sb.ToString()

        match Highlight.highlight highlighter path lang code with
        | Ok html -> renderer.Write html |> ignore
        | Error error ->
            errors.Add error
            renderer.Write(Highlight.plainBlock highlighter code) |> ignore

module Markdown =

    let private lineStartParagraphRegex = Regex(@"^<p>", RegexOptions.Multiline)

    let staggerParagraphs (html: string) =
        let mutable i = 0
        lineStartParagraphRegex.Replace(
            html,
            fun _ ->
                i <- i + 1
                $"<p class=\"animate\" style=\"--i:{i}\">"
        )

    let private headingRegex =
        Regex("<(h[2-4]) id=\"([^\"]+)\">(.*?)</\\1>", RegexOptions.Singleline)

    // The anchor follows the text and is hidden from assistive tech. Inside the heading with an aria-label,
    // it made a screen reader's heading list read "Link to this section" before every heading.
    let private addHeadingAnchors (html: string) =
        headingRegex.Replace(
            html,
            fun (m: Match) ->
                let tag = m.Groups.[1].Value
                let id = m.Groups.[2].Value
                let anchor = $"<a class=\"heading-anchor\" href=\"#{id}\" aria-hidden=\"true\" tabindex=\"-1\">#</a>"
                $"<{tag} id=\"{id}\">{m.Groups.[3].Value}{anchor}</{tag}>"
        )

    let private pngSignature =
        [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

    let private pngDimensions (imagePath: string) =
        if not (File.Exists imagePath) then
            Error "file not found"
        else
            use fs = File.OpenRead imagePath
            let header = Array.zeroCreate 24

            if fs.Read(header, 0, 24) < 24 then
                Error "file is too small to be a PNG"
            elif Array.sub header 0 8 <> pngSignature then
                Error "not a PNG (bad signature)"
            else
                let readBE offset =
                    int header.[offset] <<< 24
                    ||| (int header.[offset + 1] <<< 16)
                    ||| (int header.[offset + 2] <<< 8)
                    ||| int header.[offset + 3]

                Ok(readBE 16, readBE 20)

    let private imgRegex =
        Regex "<img\\s[^>]*>"

    let private attrRegex =
        Regex "([a-zA-Z-]+)=\"([^\"]*)\""

    let private rewrittenAttributes =
        set [ "src"; "alt"; "loading"; "decoding"; "width"; "height" ]

    let private isAbsoluteSrc (src: string) =
        src.StartsWith '/'
        || [ "http://"; "https://"; "data:" ]
           |> List.exists (fun prefix -> src.StartsWith(prefix, StringComparison.Ordinal))

    let private rewriteRelativeImages (path: string) (html: string) =
        let errors = ResizeArray<string>()
        let dir = Path.GetDirectoryName path
        let slug = Path.GetFileName dir
        let collection = Path.GetFileName(Path.GetDirectoryName dir)

        let rewritten =
            imgRegex.Replace(
                html,
                fun (m: Match) ->
                    let attributes =
                        [ for a in attrRegex.Matches m.Value -> a.Groups.[1].Value, a.Groups.[2].Value ]

                    let attribute name =
                        attributes |> List.tryPick (fun (k, v) -> if k = name then Some v else None)

                    let src = defaultArg (attribute "src") ""
                    let alt = defaultArg (attribute "alt") ""

                    let extra =
                        attributes
                        |> List.filter (fun (k, _) -> not (rewrittenAttributes.Contains k))
                        |> List.map (fun (k, v) -> $" {k}=\"{v}\"")
                        |> String.concat ""

                    let local = src.StartsWith("./", StringComparison.Ordinal)

                    if isAbsoluteSrc src then
                        m.Value
                    // The .webp is named after the file alone, so ./a/x.png and ./b/x.png would share one.
                    elif local && src.Substring(2).Contains '/' then
                        errors.Add $"{path}: image \"{src}\": images sit beside index.md, not in a subfolder"
                        m.Value
                    elif local then
                        let file = src.Substring 2

                        match pngDimensions (Path.Combine(dir, file)) with
                        | Ok(width, height) ->
                            let rootname = Path.GetFileNameWithoutExtension file
                            let outSrc = $"/images/{collection}/{slug}/{rootname}.webp"
                            $"<img alt=\"{alt}\"{extra} loading=\"lazy\" decoding=\"async\" width=\"{width}\" height=\"{height}\" src=\"{outSrc}\">"
                        | Error reason ->
                            errors.Add $"{path}: image \"{src}\": {reason}"
                            m.Value
                    else
                        errors.Add $"{path}: image \"{src}\": relative image sources must start with \"./\""
                        m.Value
            )

        rewritten, List.ofSeq errors

    type Renderer(highlighter: Highlight.Highlighter) =

        let pipeline =
            MarkdownPipelineBuilder()
                .UsePipeTables()
                .UseFootnotes()
                .UseTaskLists()
                .UseAutoLinks()
                .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
                .UseSmartyPants()
                .UseAutoIdentifiers(AutoIdentifierOptions.None)
                .Build()

        member _.Render(path: string, body: string) =
            let fenceErrors = ResizeArray<string>()
            let doc = Markdig.Markdown.Parse(body, pipeline)
            use writer = new StringWriter()
            let renderer = HtmlRenderer writer
            pipeline.Setup renderer

            renderer.ObjectRenderers.RemoveAll(fun r -> r :? CodeBlockRenderer)
            |> ignore

            renderer.ObjectRenderers.Add(HighlightCodeBlockRenderer(highlighter, path, fenceErrors))
            renderer.Render doc
            |> ignore
            writer.Flush()

            let html, imageErrors = writer.ToString() |> addHeadingAnchors |> rewriteRelativeImages path

            match List.ofSeq fenceErrors @ imageErrors with
            | [] -> Ok html
            | errors -> Error errors
