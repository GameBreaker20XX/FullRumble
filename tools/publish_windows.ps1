$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $env:TEMP ("FullRumble-publish-" + [guid]::NewGuid().ToString('N'))
$output = Join-Path $work 'app'
$updaterOutput = Join-Path $work 'updater'
$archive = Join-Path $root 'publish\FullRumble-Windows.zip'
$sums = Join-Path $root 'publish\SHA256SUMS'

New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $updaterOutput | Out-Null

try {
    & dotnet publish "$root\NascarRumbleRecomp\NascarRumbleRecomp.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -o $output

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    foreach ($name in @('Updater.exe', 'Updater.deps.json', 'Updater.runtimeconfig.json', 'Updater.pdb')) {
        $path = Join-Path $output $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }

    & dotnet publish "$root\FullRumbleUpdater\FullRumbleUpdater.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishAot=true `
        -p:StripSymbols=true `
        -p:IlcOptimizationPreference=Size `
        -o $updaterOutput

    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed with exit code $LASTEXITCODE." }
    $updaterExe = Join-Path $updaterOutput 'Updater.exe'
    if (-not (Test-Path -LiteralPath $updaterExe)) { throw "Native update helper executable was not produced: $updaterExe" }
    Copy-Item -LiteralPath $updaterExe -Destination (Join-Path $output 'FullRumbleUpdateHelper.exe')
    Copy-Item -LiteralPath (Join-Path $root 'CONTROLS.md') -Destination (Join-Path $output 'CONTROLS.md')

    & python (Join-Path $root 'tools\legal_lint.py') $output --artifacts
    if ($LASTEXITCODE -ne 0) { throw "Legal lint failed with exit code $LASTEXITCODE." }

    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -Force
    if ($LASTEXITCODE -ne 0) { throw "Archive creation failed with exit code $LASTEXITCODE." }

    $hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -Path $sums -Encoding ascii

    Write-Host "Self-contained Windows publish created in temporary staging."
    Write-Host "Archive created at: $archive"
    Write-Host "SHA-256 recorded at: $sums"
}
finally {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
