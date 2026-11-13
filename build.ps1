param(
    [switch]$NoIcon
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$sourceDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$outFile = Join-Path $distDir 'GamepadLauncherStarter.exe'
$manifest = Join-Path $root 'assets\app.manifest'
$icon = Join-Path $root 'assets\app.ico'

if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
}

if (-not $NoIcon) {
    if (-not (Test-Path -LiteralPath $icon)) {
        & (Join-Path $root 'tools\make-icon.ps1') -OutPath $icon
    }
}

$compilerCandidates = @(
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe',
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)

$compiler = $null
foreach ($candidate in $compilerCandidates) {
    if (Test-Path -LiteralPath $candidate) {
        $compiler = $candidate
        break
    }
}
if ($null -eq $compiler) {
    throw 'C# compiler (csc.exe) was not found.'
}

$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath $frameworkDir)) {
    $frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}

$referenceNames = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'System.Windows.Forms.dll',
    'System.Drawing.dll',
    'System.Xml.dll',
    'System.Runtime.Serialization.dll',
    'Accessibility.dll'
)

$compilerArgs = New-Object System.Collections.Generic.List[string]
$compilerArgs.Add('/nologo')
$compilerArgs.Add('/target:winexe')
$compilerArgs.Add('/platform:anycpu')
$compilerArgs.Add('/optimize+')
$compilerArgs.Add('/codepage:65001')
$compilerArgs.Add('/nostdlib+')
$compilerArgs.Add("/out:$outFile")
$compilerArgs.Add("/win32manifest:$manifest")
if (Test-Path -LiteralPath $icon) {
    $compilerArgs.Add("/win32icon:$icon")
    $compilerArgs.Add("/resource:$icon,app.ico")
}
if ($compiler -like '*Roslyn*') {
    $compilerArgs.Add('/langversion:5')
}
foreach ($referenceName in $referenceNames) {
    $referencePath = Join-Path $frameworkDir $referenceName
    if (Test-Path -LiteralPath $referencePath) {
        $compilerArgs.Add("/r:$referencePath")
    }
}

$sources = Get-ChildItem -Path $sourceDir -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) {
    throw 'No source files found.'
}

Write-Host "Compiler: $compiler"
Write-Host "Building $outFile ..."
& $compiler @compilerArgs @sources
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$info = Get-Item -LiteralPath $outFile
Write-Host ("Build OK: {0} ({1:N0} bytes)" -f $info.FullName, $info.Length)
