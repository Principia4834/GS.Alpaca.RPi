# build-linux-shim.ps1
$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
$nativeLinuxWin = Resolve-Path (Join-Path $repoRoot 'native\linux')
Write-Host "Building native linux shim in WSL from $nativeLinuxWin"
$wslNative = (& wsl wslpath -a "$($nativeLinuxWin.Path)") -replace "`r","" -replace "`n",""
if (-not $wslNative) { Write-Error "wslpath failed"; exit 1 }
Write-Host "WSL path: $wslNative"
& wsl bash -lc "cd '$wslNative' && make clean && make"
if ($LASTEXITCODE -ne 0) { Write-Error "make failed in WSL"; exit $LASTEXITCODE }
Write-Host "Build succeeded."