[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Some terminal hosts omit these standard variables; NuGet needs them for machine-wide settings.
if (-not ${env:ProgramFiles(x86)}) { ${env:ProgramFiles(x86)} = [Environment]::GetFolderPath('ProgramFilesX86') }
if (-not $env:ProgramData) { $env:ProgramData = [Environment]::GetFolderPath('CommonApplicationData') }

& dotnet restore (Join-Path $PSScriptRoot 'BuildTools.proj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Windows SDK tools restore failed.' }
$tools = (& dotnet msbuild (Join-Path $PSScriptRoot 'BuildTools.proj') -nologo -getProperty:PkgMicrosoft_Windows_SDK_BuildTools).Trim()
$makeappx = Get-ChildItem (Join-Path $tools 'bin') -Filter MakeAppx.exe -Recurse | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
if (-not $makeappx) { throw 'MakeAppx was not found in the restored Windows SDK tools.' }

$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts ('msix-stage-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item $stage -ItemType Directory -Force | Out-Null
& dotnet publish (Join-Path $root 'NovaIsland.csproj') -c Release -r "win-$Architecture" --self-contained true -o $stage --nologo /p:DebugType=None /p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

[xml]$manifest = Get-Content (Join-Path $PSScriptRoot 'Package.appxmanifest')
$manifest.Package.Identity.ProcessorArchitecture = $Architecture
$manifest.Save((Join-Path $stage 'AppxManifest.xml'))

Add-Type -AssemblyName System.Drawing
$assets = Join-Path $stage 'Assets'
New-Item $assets -ItemType Directory -Force | Out-Null
foreach ($logo in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
    $size = [int]$logo[1]
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $font = New-Object System.Drawing.Font('Segoe UI', ($size * 0.43), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(11, 11, 12))
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rectangle = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
        $graphics.DrawString('N', $font, [System.Drawing.Brushes]::White, $rectangle, $format)
        $bitmap.Save((Join-Path $assets $logo[0]), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $format.Dispose(); $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
$package = Join-Path $artifacts ("NovaIsland_" + $manifest.Package.Identity.Version + "_$Architecture.msix")
$packLog = Join-Path $artifacts 'makeappx.log'
& $makeappx.FullName pack /d $stage /p $package /o *> $packLog
if ($LASTEXITCODE -ne 0) { Get-Content $packLog -Tail 15; throw 'MSIX validation/packaging failed.' }
if ($CertificateThumbprint) {
    $signTool = Join-Path $makeappx.Directory.FullName 'signtool.exe'
    & $signTool sign /fd SHA256 /sha1 $CertificateThumbprint $package
    if ($LASTEXITCODE -ne 0) { throw 'Signing failed. The certificate subject must match CN=NovaIsland.' }
}
Write-Output "Package: $package"
Write-Output "Developer registration manifest: $(Join-Path $stage 'AppxManifest.xml')"
if (-not $CertificateThumbprint) { Write-Output 'Package is unsigned. See Packaging/README.md for developer registration or signing.' }
