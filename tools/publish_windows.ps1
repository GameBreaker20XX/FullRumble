$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $env:TEMP ("FullRumble-publish-" + [guid]::NewGuid().ToString('N'))
$output = Join-Path $work 'app'
$archive = Join-Path $root 'publish\FullRumble-Windows.zip'
$sums = Join-Path $root 'publish\SHA256SUMS'

New-Item -ItemType Directory -Force -Path $output | Out-Null

try {
    & dotnet publish "$root\NascarRumbleRecomp\NascarRumbleRecomp.csproj" `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -o $output

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

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
