module Yggdrasil.Generate.Assets

open System.IO
open System.Net.Http
open System.Diagnostics
open System.Formats.Tar
open System.IO.Compression
open System.Runtime.InteropServices

let private tailwindVersion = "4.3.3"
let private esbuildVersion = "0.28.1"

type TargetOs =
    | MacOS
    | Linux
    | Windows
    | Unsupported of string

type ToolAssets =
    { TailwindAsset: string
      EsbuildPkg: string }

let resolveAssets (os: TargetOs) (arch: Architecture) (isMusl: bool) =
    let assets tailwind esbuild =
        Ok { TailwindAsset = tailwind; EsbuildPkg = esbuild }

    match os, arch with
    | MacOS, Architecture.Arm64 -> assets "tailwindcss-macos-arm64" "darwin-arm64"
    | MacOS, Architecture.X64 -> assets "tailwindcss-macos-x64" "darwin-x64"
    | Linux, Architecture.Arm64 when isMusl -> assets "tailwindcss-linux-arm64-musl" "linux-arm64"
    | Linux, Architecture.Arm64 -> assets "tailwindcss-linux-arm64" "linux-arm64"
    | Linux, Architecture.X64 when isMusl -> assets "tailwindcss-linux-x64-musl" "linux-x64"
    | Linux, Architecture.X64 -> assets "tailwindcss-linux-x64" "linux-x64"
    | Windows, (Architecture.X64 | Architecture.Arm64) -> assets "tailwindcss-windows-x64.exe" "win32-x64"
    | _ ->
        let name =
            match os with
            | MacOS -> "macOS"
            | Linux -> "Linux"
            | Windows -> "Windows"
            | Unsupported other -> other

        Error(
            $"no prebuilt Tailwind binary exists for {name} {arch}. "
            + "Supported: macOS x64/arm64, Linux x64/arm64 (glibc and musl), Windows x64.")

let private detectMusl () =
    if RuntimeInformation.RuntimeIdentifier.Contains("musl", System.StringComparison.OrdinalIgnoreCase) then
        true
    elif Directory.Exists "/lib" then
        Directory.EnumerateFiles("/lib", "ld-musl-*") |> Seq.isEmpty |> not
    else
        false

let private detectOs () =
    if RuntimeInformation.IsOSPlatform OSPlatform.OSX then MacOS
    elif RuntimeInformation.IsOSPlatform OSPlatform.Linux then Linux
    elif RuntimeInformation.IsOSPlatform OSPlatform.Windows then Windows
    else Unsupported RuntimeInformation.OSDescription

let private hostAssets () =
    let os = detectOs ()
    let isMusl = os = Linux && detectMusl ()
    match resolveAssets os RuntimeInformation.ProcessArchitecture isMusl with
    | Ok a -> a
    | Error message -> failwith message

let private http = new HttpClient()

let private download (url: string) (dest: string) =
    printfn "  downloading %s" url
    let tmp = dest + ".part"
    try
        use resp = http.GetAsync(url).Result
        if not resp.IsSuccessStatusCode then
            failwithf
                "failed to download %s (HTTP %d %s).%s"
                url
                (int resp.StatusCode)
                (string resp.StatusCode)
                (if resp.StatusCode = System.Net.HttpStatusCode.NotFound then
                     " A 404 usually means no prebuilt binary is published for this platform."
                 else
                     "")
        // Close the file before moving it: on Windows File.Move fails if the handle is still open,
        // whereas POSIX happily renames an open file — hence this only bit the Windows CI runner.
        (
            use fs = File.Create tmp
            resp.Content.CopyToAsync(fs).GetAwaiter().GetResult()
        )
        // Windows Defender/Search can briefly hold a freshly written binary open to scan it, which
        // surfaces as a sharing-violation IOException on the rename; retry a few times before failing.
        let rec move attempt =
            try
                File.Move(tmp, dest, true)
            with :? IOException when attempt < 5 ->
                System.Threading.Thread.Sleep(100 * attempt)
                move (attempt + 1)

        move 1
    with _ ->
        if File.Exists tmp then File.Delete tmp
        reraise ()

let private isWindows =
    RuntimeInformation.IsOSPlatform OSPlatform.Windows

let private chmodExec (path: string) =
    if not isWindows then
        let current = File.GetUnixFileMode path
        File.SetUnixFileMode(
            path,
            current
            ||| UnixFileMode.UserExecute
            ||| UnixFileMode.GroupExecute
            ||| UnixFileMode.OtherExecute)

let private run (exe: string) (args: string list) (workingDir: string) =
    let psi = ProcessStartInfo(exe,
        WorkingDirectory = workingDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true)

    args |> List.iter psi.ArgumentList.Add
    use p = Process.Start psi

    let errTask = p.StandardError.ReadToEndAsync()
    let out = p.StandardOutput.ReadToEnd()
    let err = errTask.GetAwaiter().GetResult()
    p.WaitForExit()

    if p.ExitCode <> 0 then
        failwithf "%s exited %d\n%s\n%s" (Path.GetFileName exe) p.ExitCode out err

let private ensureTailwind (binDir: string) (asset: string) =
    let name =
        if isWindows && asset.EndsWith(".exe", System.StringComparison.Ordinal) then
            asset.Substring(0, asset.Length - 4) + $"-{tailwindVersion}.exe"
        else
            $"{asset}-{tailwindVersion}"

    let dest = Path.Combine(binDir, name)

    if not (File.Exists dest) then
        download $"https://github.com/tailwindlabs/tailwindcss/releases/download/v{tailwindVersion}/{asset}" dest
        chmodExec dest

    dest

let private ensureEsbuild (binDir: string) (pkg: string) =
    let dest = Path.Combine(binDir, $"esbuild-{esbuildVersion}" + (if isWindows then ".exe" else ""))

    if not (File.Exists dest) then
        let tgz = Path.Combine(binDir, "esbuild.tgz")
        download $"https://registry.npmjs.org/@esbuild/{pkg}/-/{pkg}-{esbuildVersion}.tgz" tgz

        let extracted =
            use fs = File.OpenRead tgz
            use gz = new GZipStream(fs, CompressionMode.Decompress)
            use tar = new TarReader(gz)
            let mutable found = false
            let mutable entry = tar.GetNextEntry()

            while not (isNull entry) do
                if entry.Name.EndsWith("bin/esbuild", System.StringComparison.Ordinal)
                   || entry.Name.EndsWith("esbuild.exe", System.StringComparison.Ordinal) then
                    entry.ExtractToFile(dest, true)
                    found <- true
                    entry <- null
                else
                    entry <- tar.GetNextEntry()

            found

        File.Delete tgz

        if not extracted then
            failwithf "esbuild archive %s did not contain the expected binary" tgz

        chmodExec dest

    dest

let build (binDir: string) (assetsDir: string) (distDir: string) =
    Directory.CreateDirectory binDir |> ignore
    let tools = hostAssets ()
    let tailwind = ensureTailwind binDir tools.TailwindAsset
    let esbuild = ensureEsbuild binDir tools.EsbuildPkg

    let cssOut = Path.Combine(distDir, "assets", "css", "app.css")
    let jsOut = Path.Combine(distDir, "assets", "js", "app.js")
    Directory.CreateDirectory(Path.GetDirectoryName cssOut) |> ignore
    Directory.CreateDirectory(Path.GetDirectoryName jsOut) |> ignore

    printfn "  tailwind -> %s" cssOut
    run tailwind [ "--input"; Path.Combine(assetsDir, "css", "app.css"); "--output"; cssOut; "--minify" ] assetsDir

    printfn "  esbuild  -> %s" jsOut
    run
        esbuild
        [ Path.Combine(assetsDir, "js", "app.js")
          "--bundle"
          "--target=es2022"
          $"--outfile={jsOut}"
          "--minify" ]
        assetsDir
