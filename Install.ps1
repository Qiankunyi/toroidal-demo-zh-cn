[CmdletBinding()]
param(
    [string]$GameDirectory,
    [string]$LoaderZip
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameDirectory)) {
    $GameDirectory = (Read-Host 'Paste the full path to the Steam Demo folder').Trim().Trim('"')
    if (!$GameDirectory) { throw 'No game directory was provided.' }
}
$game = (Get-Item -LiteralPath $GameDirectory).FullName
$data = 'ToroidalAwaitingtheSpringDemo_Data'
$supported = [ordered]@{
    'ToroidalAwaitingtheSpringDemo.exe' = '84328D61619C5833A1AADA4134DE9976DFDE4F2E3A97D176AFA3F40C9FE8FF5F'
    'UnityPlayer.dll' = '477C15A09D560E547D237596AD4EF1C55317C9DD049E38F62C24BB9BD4FB3331'
    "$data\Managed\Assembly-CSharp.dll" = 'BC1394EF1C85FCF4C1E0D3E9270312E99F6845B09B87EC43F70D5B6E845A25A5'
    "$data\StreamingAssets\rdsm.tmp" = '462E21F60FAA958BEFEB17423094E7118CEA2010D47564387DA2A406C17A3C00'
}

if (Get-Process -Name ToroidalAwaitingtheSpringDemo -ErrorAction SilentlyContinue) {
    throw 'Close the demo before installing the patch.'
}
foreach ($entry in $supported.GetEnumerator()) {
    $file = Join-Path $game $entry.Key
    if (!(Test-Path -LiteralPath $file -PathType Leaf) -or
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Value) {
        throw "Unsupported game build or incorrect directory: $($entry.Key)"
    }
}
foreach ($name in 'winhttp.dll', 'doorstop_config.ini', 'BepInEx') {
    if (Test-Path -LiteralPath (Join-Path $game $name)) {
        throw "Existing item would conflict with this patch: $name"
    }
}
foreach ($name in 'wdsm.tmp', 'ldsm.tmp') {
    if (!(Test-Path -LiteralPath (Join-Path $game "$data\StreamingAssets\$name") -PathType Leaf)) {
        throw "Required save template not found: $name"
    }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('toroidal-zh-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$added = [Collections.Generic.List[string]]::new()
$addedDirectories = [Collections.Generic.List[string]]::new()

function Add-Directory([string]$Path) {
    if ([IO.Directory]::Exists($Path)) { return }
    [void][IO.Directory]::CreateDirectory($Path)
    $addedDirectories.Add($Path)
}

function Add-File([string]$Source, [string]$Destination) {
    $inputFile = [IO.File]::OpenRead($Source)
    try {
        $outputFile = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        $added.Add($Destination)
        try { $inputFile.CopyTo($outputFile) } finally { $outputFile.Dispose() }
    } finally { $inputFile.Dispose() }
}

try {
    if (!$LoaderZip) {
        $LoaderZip = Join-Path $scratch 'BepInEx_win_x64_5.4.23.4.zip'
        $url = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip'
        Invoke-WebRequest -Uri $url -OutFile $LoaderZip -UseBasicParsing -TimeoutSec 60
    }
    if ((Get-FileHash -LiteralPath $LoaderZip -Algorithm SHA256).Hash -ne
        'F881201B79DA03E513BF97CDF39607FFA7F9E0D31A519B1AEECA8EB60F8309E7') {
        throw 'Unexpected BepInEx archive checksum.'
    }
    $loader = Join-Path $scratch 'loader'
    Expand-Archive -LiteralPath $LoaderZip -DestinationPath $loader
    $bep = Join-Path $game 'BepInEx'
    $core = Join-Path $bep 'core'
    $plugins = Join-Path $bep 'plugins'
    $zh = Join-Path $plugins 'ToroidalChinese'
    foreach ($directory in $bep, $core, $plugins, $zh) { Add-Directory $directory }
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $loader 'BepInEx\core') -File) {
        Add-File $file.FullName (Join-Path $core $file.Name)
    }
    foreach ($name in 'ToroidalChinese.dll', 'menu.tsv', 'story.tsv', 'guide.tsv') {
        Add-File (Join-Path $PSScriptRoot "patch\$name") (Join-Path $zh $name)
    }
    $fontSource = Join-Path $PSScriptRoot 'patch\fonts'
    $fonts = Join-Path $zh 'fonts'
    Add-Directory $fonts
    foreach ($directory in Get-ChildItem -LiteralPath $fontSource -Directory -Recurse | Sort-Object FullName) {
        Add-Directory (Join-Path $fonts $directory.FullName.Substring($fontSource.Length + 1))
    }
    foreach ($file in Get-ChildItem -LiteralPath $fontSource -File -Recurse) {
        Add-File $file.FullName (Join-Path $fonts $file.FullName.Substring($fontSource.Length + 1))
    }
    Add-File (Join-Path $loader 'doorstop_config.ini') (Join-Path $game 'doorstop_config.ini')
    Add-File (Join-Path $loader 'winhttp.dll') (Join-Path $game 'winhttp.dll')
    Write-Host "Installed. Launch ToroidalAwaitingtheSpringDemo.exe from $game"
} catch {
    for ($i = $added.Count - 1; $i -ge 0; $i--) {
        [IO.File]::Delete($added[$i])
    }
    for ($i = $addedDirectories.Count - 1; $i -ge 0; $i--) {
        $directory = $addedDirectories[$i]
        if ([IO.Directory]::Exists($directory) -and [IO.Directory]::GetFileSystemEntries($directory).Length -eq 0) {
            [IO.Directory]::Delete($directory)
        }
    }
    throw
} finally {
    # Remove only the freshly generated installer staging folder under TEMP.
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolved = [IO.Path]::GetFullPath($scratch)
    if ($resolved.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved) -match '^toroidal-zh-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
    }
}
