namespace Yggdrasil.Content

open System
open System.IO
open System.Text
open System.Collections.Generic

open TextMateSharp.Themes
open TextMateSharp.Registry
open TextMateSharp.Grammars
open TextMateSharp.Internal.Themes.Reader
open TextMateSharp.Internal.Grammars.Reader

type private GrammarRegistryOptions(grammarDir: string, grammarFiles: IDictionary<string, string>, themeFile: string) =

    member _.ReadTheme() =
        use reader = new StreamReader(Path.Combine(grammarDir, themeFile))
        ThemeReader.ReadThemeSync reader

    interface IRegistryOptions with
        member _.GetGrammar scopeName =
            match grammarFiles.TryGetValue scopeName with
            | true, file ->
                use reader = new StreamReader(Path.Combine(grammarDir, file))
                GrammarReader.ReadGrammarSync reader
            | _ -> null

        member _.GetInjections _scopeName =
            null

        member _.GetTheme _scopeName =
            null

        member this.GetDefaultTheme() =
            this.ReadTheme()

module Highlight =

    // One theme for both site themes: code looks the same in light and dark mode, so the colours can be
    // emitted inline once and need no dark-mode CSS.
    let private themeFile = "catppuccin-frappe.json"

    let private scopeByLang =
        Map
            [ "fsharp", "source.fsharp"
              "fs", "source.fsharp"
              "f#", "source.fsharp"
              "scala", "source.scala"
              "bash", "source.shell"
              "shell", "source.shell"
              "sh", "source.shell"
              "shellscript", "source.shell" ]

    let supportedLanguages =
        scopeByLang
        |> Map.toList
        |> List.map fst
        |> List.sort

    let private grammarFiles =
        dict
            [ "source.fsharp", "fsharp.tmLanguage.json"
              "source.scala", "scala.tmLanguage.json"
              "source.shell", "shell.tmLanguage.json" ]

    type Highlighter =
        private
            { Theme: Theme
              DefaultFg: string
              DefaultBg: string
              Grammars: Map<string, IGrammar> }

    let private guiColor (theme: Theme) (key: string) (fallback: string) =
        let dict = theme.GetGuiColorDictionary()
        if not (isNull dict) && dict.ContainsKey key then
            dict.[key]
        else
            fallback

    let create (grammarDir: string) =
        let options = GrammarRegistryOptions(grammarDir, grammarFiles, themeFile)
        let registry = Registry(options :> IRegistryOptions)
        let theme = Theme.CreateFromRawTheme(options.ReadTheme(), options)

        { Theme = theme
          DefaultFg = guiColor theme "editor.foreground" "#c6d0f5"
          DefaultBg = guiColor theme "editor.background" "#303446"
          // Loaded eagerly, so a registered grammar whose file is missing fails here rather than mid-build.
          Grammars = grammarFiles.Keys |> Seq.map (fun scope -> scope, registry.LoadGrammar scope) |> Map.ofSeq }

    let private scopeMatches (selector: string) (scope: string) =
        scope = selector
        || scope.Length > selector.Length
           && scope.StartsWith(selector, StringComparison.Ordinal)
           && scope.[selector.Length] = '.'

    let private ruleApplies (rule: ThemeTrieElementRule) (scopesInnerFirst: string list) =
        if isNull (box rule.parentScopes) || rule.parentScopes.Count = 0 then
            true
        else
            match scopesInnerFirst with
            | own :: ancestors when scopeMatches rule.parentScopes.[0] own ->
                let mutable remaining = ancestors
                let mutable i = 1
                let mutable ok = true

                while ok && i < rule.parentScopes.Count do
                    let selector = rule.parentScopes.[i]

                    match remaining |> List.skipWhile (fun s -> not (scopeMatches selector s)) with
                    | [] -> ok <- false
                    | _ :: outer ->
                        remaining <- outer
                        i <- i + 1

                ok
            | _ -> false

    let private resolveStyle (theme: Theme) (scopes: List<string>) =
        let mutable fg = -1
        let mutable style = FontStyle.NotSet
        let scopesInnerFirst = scopes |> List.ofSeq |> List.rev

        for rule in theme.Match scopes do
            if ruleApplies rule scopesInnerFirst then
                if fg = -1 && rule.foreground > 0 then
                    fg <- rule.foreground

                if style = FontStyle.NotSet && rule.fontStyle <> FontStyle.NotSet then
                    style <- rule.fontStyle

        (if fg > 0 then Some(theme.GetColor fg) else None), style

    let private escapeInto (sb: StringBuilder) (s: string) =
        for c in s do
            match c with
            | '&' -> sb.Append "&amp;" |> ignore
            | '<' -> sb.Append "&lt;" |> ignore
            | '>' -> sb.Append "&gt;" |> ignore
            | c -> sb.Append c |> ignore

    let private styleDecls (style: FontStyle) =
        [ if int style > 0 then
              if style.HasFlag FontStyle.Italic then
                  "font-style:italic"
              if style.HasFlag FontStyle.Bold then
                  "font-weight:bold"
              if style.HasFlag FontStyle.Underline && style.HasFlag FontStyle.Strikethrough then
                  "text-decoration:underline line-through"
              elif style.HasFlag FontStyle.Underline then
                  "text-decoration:underline"
              elif style.HasFlag FontStyle.Strikethrough then
                  "text-decoration:line-through" ]

    let private emitToken (sb: StringBuilder) (h: Highlighter) (text: string) (fg: string option) (style: FontStyle) =
        let decls =
            [ match fg with
              | Some c when not (String.Equals(c, h.DefaultFg, StringComparison.OrdinalIgnoreCase)) -> $"color:{c}"
              | _ -> ()
              yield! styleDecls style ]

        if List.isEmpty decls then
            escapeInto sb text
        else
            sb.Append("<span style=\"").Append(String.concat ";" decls).Append "\">" |> ignore
            escapeInto sb text
            sb.Append "</span>" |> ignore

    let private openBlock (sb: StringBuilder) (h: Highlighter) =
        sb
            .Append("<pre class=\"tm\" style=\"background-color:")
            .Append(h.DefaultBg)
            .Append("\"><code style=\"color:")
            .Append(h.DefaultFg)
            .Append("\">")
        |> ignore

    let plainBlock (h: Highlighter) (code: string) =
        let sb = StringBuilder()
        openBlock sb h
        escapeInto sb (code.Replace("\r\n", "\n").TrimEnd '\n')
        sb.Append "</code></pre>" |> ignore
        sb.ToString()

    let private highlightWith (h: Highlighter) (grammar: IGrammar) (code: string) =
        let sb = StringBuilder()
        openBlock sb h
        let normalized = code.Replace("\r\n", "\n")

        let trimmed =
            if normalized.EndsWith '\n' then normalized.[.. normalized.Length - 2] else normalized

        let lines = trimmed.Split '\n'
        let mutable state: IStateStack = null

        let styleCache = Dictionary<string, string option * FontStyle>()

        let resolve (scopes: List<string>) =
            let key = String.Join(">", scopes)

            match styleCache.TryGetValue key with
            | true, v -> v
            | _ ->
                let v = resolveStyle h.Theme scopes
                styleCache.[key] <- v
                v

        lines
        |> Array.iteri (fun i line ->
            if i > 0 then
                sb.Append '\n' |> ignore

            let result = grammar.TokenizeLine(line, state, TimeSpan.FromSeconds 10.0)
            state <- result.RuleStack

            for token in result.Tokens do
                let startIdx = token.StartIndex
                let endIdx = min token.EndIndex line.Length

                if startIdx < endIdx then
                    let text = line.Substring(startIdx, endIdx - startIdx)
                    let fg, style = resolve token.Scopes
                    emitToken sb h text fg style)

        sb.Append "</code></pre>" |> ignore
        sb.ToString()

    // An untagged fence is a deliberate plain block. A tagged one that can't be highlighted is an error, so a
    // typo'd label fails the build instead of quietly losing its colours.
    let highlight (h: Highlighter) (sourcePath: string) (lang: string) (code: string) =
        let langKey = (if isNull lang then "" else lang).Trim().ToLowerInvariant()

        if langKey = "" then
            Ok(plainBlock h code)
        else
            match Map.tryFind langKey scopeByLang with
            | None ->
                let supported = String.concat ", " supportedLanguages

                Error(
                    $"{sourcePath}: code fence language \"{langKey}\" is not supported; use one of {supported}, "
                    + "leave the fence untagged for plain text, or add a grammar (see assets/grammars/README.md)"
                )
            | Some scope ->
                try
                    // TextMateSharp compiles grammar rules lazily and is not thread-safe, and the tests share one
                    // highlighter across Expecto's parallel runner.
                    Ok(lock h (fun () -> highlightWith h h.Grammars.[scope] code))
                with ex ->
                    Error $"{sourcePath}: code fence \"{langKey}\" could not be highlighted: {Util.exceptionDetail ex}"
