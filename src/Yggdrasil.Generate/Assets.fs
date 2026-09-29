module Yggdrasil.Generate.Assets

open System.IO
open System.Net.Http
open System.Diagnostics
open System.Formats.Tar
open System.IO.Compression
open System.Security.Cryptography
open System.Runtime.InteropServices

let private tailwindVersion = "4.3.3"
let private esbuildVersion = "0.28.1"

// Every download is checked against these before it is used, so a version bump means replacing them too. Both are
// copied as published: Tailwind's from the release's sha256sums.txt, esbuild's from each npm package's `integrity`.
let private tailwindSha256 =
    function
    | "tailwindcss-linux-arm64" -> "55fd0b241214eff3de1e8ee4f22796662f2d2e7a49bcfca7477cfd0bac398195"
    | "tailwindcss-linux-arm64-musl" -> "71ea4be79c9de9827545682df3e040053fb535d37c71ed2cfdedf9385a0868e0"
    | "tailwindcss-linux-x64" -> "dc61b3ac6b8c9ca874c0cc4c57b2409791a64c5540404ca5f5367360babc313a"
    | "tailwindcss-linux-x64-musl" -> "a04d34ceacc8f52cbe8920ad846cdeb61d3d0021dba32db0d1f77c9d9fad7a6c"
    | "tailwindcss-macos-arm64" -> "cdf646702987a743464dff4d9c60fd4480d1c1e73dd819a9a67f1078815dce9d"
    | "tailwindcss-macos-x64" -> "7922e0953f2110c05976e3bf58f14e643d90427575e766b7d433f5f80cbee7e1"
    | "tailwindcss-windows-x64.exe" -> "e0e260ce048014e9268f6237ff18f8ccf02cef521cbd0ae04e82c2cdf7aa3955"
    | asset -> failwith $"no checksum is pinned for {asset}"

let private esbuildIntegrity =
    function
    | "darwin-arm64" ->
        "sha512-TZbWkQY7kvTAXbXUT7uVACR5cMHsDiSz9z7ZKAX/RTq/WJEk3QyRr0wZpNhBDX+/0CtdqUIJlOiodQcta6tY3Q=="
    | "darwin-x64" ->
        "sha512-zfdzgK9ACBNZLI/CyHTOx81SyNbM6YXn7rxSgX97VjyiPl9W1i4Ka4fgKECEoFCKGpvBj5qArWIGgQjOwkgskQ=="
    | "linux-arm64" ->
        "sha512-yHs+0uc8+nvEAfAfxrWQKK5peSNzBc4PegcMO0EJ2hT71uA7vB8Ihg2e77R2P7SG5uYjPbHlLLmve4LLLRCf0g=="
    | "linux-x64" ->
        "sha512-u/anNYF2mmVOEDwLtnQ1wOr3EZ9sTNGLWrsYGYwHWzGA3Si84IOkHXlbWTD1NB+9/1lcnweYKO54uhxZydNzfA=="
    | "win32-x64" ->
        "sha512-bm4Mowrv+GXMlpWX++EcXw/iLyd1o3+bJkC2DkWXYVvgZCqD/bSj9ctZeAMC3cIxgjRVR2Dufaiu4YPxr5gW1A=="
    | pkg -> failwith $"no checksum is pinned for @esbuild/{pkg}"

type TargetOs =
    | MacOS
    | Linux
    | Windows
    | Unsupported of string

type ToolAssets =
    { TailwindAsset: string
      TailwindSha256: string
      EsbuildPkg: string
      EsbuildIntegrity: string }

let resolveAssets (os: TargetOs) (arch: Architecture) (isMusl: bool) =
    let assets tailwind esbuild =
        Ok
            { TailwindAsset = tailwind
              TailwindSha256 = tailwindSha256 tailwind
              EsbuildPkg = esbuild
              EsbuildIntegrity = esbuildIntegrity esbuild }

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

// The file's digest in the pinned value's own format, so a pin is compared exactly as it was published.
let digest (pinned: string) (path: string) =
    use stream = File.OpenRead path

    if pinned.StartsWith("sha512-", System.StringComparison.Ordinal) then
        "sha512-" + System.Convert.ToBase64String(SHA512.HashData stream)
    else
        System.Convert.ToHexStringLower(SHA256.HashData stream)

// Windows Defender/Search can briefly hold a freshly written binary open to scan it, which
// surfaces as a sharing-violation IOException on the rename; retry a few times before failing.
let private moveIntoPlace (tmp: string) (dest: string) =
    let rec move attempt =
        try
            File.Move(tmp, dest, true)
        with :? IOException when attempt < 5 ->
            System.Threading.Thread.Sleep(100 * attempt)
            move (attempt + 1)

    move 1

let private download (url: string) (pinned: string) (dest: string) =
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

        // Checked before the move: whatever reaches dest is trusted by every later build, and by the CI cache.
        let actual = digest pinned tmp

        if actual <> pinned then
            failwith (
                $"{url} does not match its pinned checksum (expected {pinned}, got {actual}). "
                + "After a version bump, update the pins in Assets.fs.")

        moveIntoPlace tmp dest
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

let private ensureTailwind (binDir: string) (asset: string) (sha256: string) =
    let name =
        if isWindows && asset.EndsWith(".exe", System.StringComparison.Ordinal) then
            asset.Substring(0, asset.Length - 4) + $"-{tailwindVersion}.exe"
        else
            $"{asset}-{tailwindVersion}"

    let dest = Path.Combine(binDir, name)

    if not (File.Exists dest) then
        download $"https://github.com/tailwindlabs/tailwindcss/releases/download/v{tailwindVersion}/{asset}" sha256 dest
        chmodExec dest

    dest

let private ensureEsbuild (binDir: string) (pkg: string) (integrity: string) =
    let dest = Path.Combine(binDir, $"esbuild-{esbuildVersion}" + (if isWindows then ".exe" else ""))

    if not (File.Exists dest) then
        let tgz = Path.Combine(binDir, "esbuild.tgz")
        download $"https://registry.npmjs.org/@esbuild/{pkg}/-/{pkg}-{esbuildVersion}.tgz" integrity tgz

        // Extracted to a temporary name and moved into place, as download does: an interrupted extraction would
        // otherwise leave a truncated binary at dest, which the File.Exists check above then trusts for good.
        let tmp = dest + ".part"

        let extracted =
            use fs = File.OpenRead tgz
            use gz = new GZipStream(fs, CompressionMode.Decompress)
            use tar = new TarReader(gz)
            let mutable found = false
            let mutable entry = tar.GetNextEntry()

            while not (isNull entry) do
                if entry.Name.EndsWith("bin/esbuild", System.StringComparison.Ordinal)
                   || entry.Name.EndsWith("esbuild.exe", System.StringComparison.Ordinal) then
                    entry.ExtractToFile(tmp, true)
                    found <- true
                    entry <- null
                else
                    entry <- tar.GetNextEntry()

            found

        File.Delete tgz

        if not extracted then
            failwithf "esbuild archive %s did not contain the expected binary" tgz

        moveIntoPlace tmp dest
        chmodExec dest

    dest

let build (binDir: string) (assetsDir: string) (distDir: string) =
    Directory.CreateDirectory binDir |> ignore
    let tools = hostAssets ()
    let tailwind = ensureTailwind binDir tools.TailwindAsset tools.TailwindSha256
    let esbuild = ensureEsbuild binDir tools.EsbuildPkg tools.EsbuildIntegrity

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
