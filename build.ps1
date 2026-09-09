$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

$Required = @(
    (Join-Path $Root 'lib\BepInEx.dll'),
    (Join-Path $Root 'lib\0Harmony.dll'),
    (Join-Path $Root 'lib\UnityEngine.dll'),
    (Join-Path $Root 'lib\UnityEngine.CoreModule.dll')
)

$Missing = $Required | Where-Object { -not (Test-Path $_) }

$StraySources = Get-ChildItem (Join-Path $Root 'lib') -Filter '*.cs' -File -ErrorAction SilentlyContinue
if ($StraySources) {
    Write-Host 'Ignoring stray .cs files in lib (only BabyGotBoarConvertDiag.cs at project root is compiled):' -ForegroundColor Yellow
    $StraySources | ForEach-Object { Write-Host "  $($_.FullName)" }
    Write-Host ''
}
if ($Missing) {
    Write-Host 'Missing reference DLLs:' -ForegroundColor Red
    $Missing | ForEach-Object { Write-Host "  $_" }
    Write-Host ''
    Write-Host 'Copy them from the running Valheim/BepInEx installation into the lib folder first.'
    exit 1
}

Push-Location $Root
try {
    dotnet build .\BabyGotBoarConvertDiag.csproj -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    $Built = Join-Path $Root 'bin\Release\netstandard2.1\BabyGotBoarConvertDiag.dll'
    if (-not (Test-Path $Built)) {
        throw "Build reported success but output DLL was not found at $Built"
    }

    Write-Host ''
    Write-Host 'Build succeeded:' -ForegroundColor Green
    Write-Host $Built
}
finally {
    Pop-Location
}
