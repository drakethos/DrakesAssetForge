# Collects everything Forge Runtime compiles against into one folder (FORGE_REFS) for CI:
# game + Unity + BepInEx DLLs from the Pfhoenix reference package, Jotunn from Thunderstore.
$ErrorActionPreference = 'Stop'

$temp = $env:RUNNER_TEMP
if (-not $temp) { $temp = [IO.Path]::GetTempPath() }
$refs = Join-Path $temp 'forge_refs'
New-Item -ItemType Directory -Force $refs | Out-Null

$pfhoenix = $env:PFHOENIX_VERSION
if (-not $pfhoenix) { $pfhoenix = '1.0.23' }
$jotunn = $env:JOTUNN_VERSION
if (-not $jotunn) { $jotunn = '2.30.2' }

Invoke-WebRequest "https://www.nuget.org/api/v2/package/Pfhoenix.Valheim.ModProjectReferences/$pfhoenix" -OutFile (Join-Path $temp 'pfhoenix.zip')
Expand-Archive (Join-Path $temp 'pfhoenix.zip') (Join-Path $temp 'pfhoenix') -Force
Copy-Item (Join-Path $temp 'pfhoenix/lib/net46/*.dll') $refs

Invoke-WebRequest "https://thunderstore.io/package/download/ValheimModding/Jotunn/$jotunn/" -OutFile (Join-Path $temp 'jotunn.zip')
Expand-Archive (Join-Path $temp 'jotunn.zip') (Join-Path $temp 'jotunn') -Force
Copy-Item (Get-ChildItem (Join-Path $temp 'jotunn') -Recurse -Filter Jotunn.dll | Select-Object -First 1).FullName $refs

foreach ($dll in 'BepInEx.dll', 'Jotunn.dll', 'assembly_valheim.dll', 'UnityEngine.CoreModule.dll', 'netstandard.dll') {
    if (-not (Test-Path (Join-Path $refs $dll))) { throw "Missing $dll in staged references" }
}

if ($env:GITHUB_ENV) { "FORGE_REFS=$refs" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8 }
Write-Host "Staged references in $refs"
