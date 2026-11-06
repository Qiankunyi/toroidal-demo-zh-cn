[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][string]$LoaderDirectory
)

$ErrorActionPreference = 'Stop'
$core = Join-Path (Resolve-Path -LiteralPath $LoaderDirectory).Path 'BepInEx\core'
$managed = Join-Path (Resolve-Path -LiteralPath $GameDirectory).Path 'ToroidalAwaitingtheSpringDemo_Data\Managed'
$loaderDlls = 'BepInEx.dll', '0Harmony.dll'
$gameDlls = 'netstandard.dll', 'UnityEngine.dll', 'UnityEngine.CoreModule.dll',
    'UnityEngine.UI.dll', 'UnityEngine.UIModule.dll', 'UnityEngine.TextRenderingModule.dll',
    'UnityEngine.TextCoreFontEngineModule.dll', 'UnityEngine.TextCoreTextEngineModule.dll',
    'Unity.TextMeshPro.dll'
$references = @($loaderDlls | ForEach-Object { '/reference:' + (Join-Path $core $_) })
$references += @($gameDlls | ForEach-Object { '/reference:' + (Join-Path $managed $_) })
$sources = @('ToroidalChinese.cs', 'GuideOverlay.cs', 'SpriteCaptions.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ })
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'patch\ToroidalChinese.dll'
& $compiler /nologo /target:library /codepage:65001 "/out:$output" @references @sources
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
Write-Host "Built $output"
