$ErrorActionPreference = "Stop"

$workspace = Split-Path -Parent $PSScriptRoot
$buildRoot = "C:\GlassRadio-Build"
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path $buildRoot "installer-$runId"
$publishDir = Join-Path $runRoot "publish\github-win-x64"
$outputDir = Join-Path $runRoot "artifacts"
$intermediateDir = Join-Path $runRoot "wix-intermediate"
$wix = "C:\Program Files\WiX Toolset v7.0\bin\wix.exe"
$balRoot = Join-Path $buildRoot "tooling\wix-bal"
$balVersion = "7.0.0"
$balPackageId = "wixtoolset.bootstrapperapplications.wixext"
$balPackageRoot = Join-Path $balRoot "package-$balVersion"
$balExtension = Join-Path $balPackageRoot "wixext7\WixToolset.BootstrapperApplications.wixext.dll"
$solution = Join-Path $workspace "GlassRadio.Windows.sln"
$wxs = Join-Path $PSScriptRoot "GlassRadio.wxs"
$bundleWxs = Join-Path $PSScriptRoot "GlassRadio-Setup.wxs"
$msi = Join-Path $outputDir "GlassRadio-1.0.1-x64.msi"
$setup = Join-Path $outputDir "GlassRadio-Setup-1.0.1-x64.exe"
New-Item -ItemType Directory -Force -Path $publishDir, $outputDir | Out-Null
if (-not (Test-Path -LiteralPath $wix)) { throw "WiX toolset was not found at the external tooling path: $wix" }

function Repair-MsiWinUiLanguageMetadata {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [string] $AllowedRoot
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify an MSI outside the dated installer run root: $resolvedPath"
    }

    $targetNames = @{
        "microsoft.ui.xaml.dll" = $true
        "microsoft.ui.xaml.dll.mui" = $true
        "microsoft.ui.xaml.phone.dll" = $true
        "microsoft.ui.xaml.phone.dll.mui" = $true
    }
    $invalidWinUiMsiLanguages = @("1152", "1153", "1169")
    $installer = $null
    $database = $null
    $view = $null
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.OpenDatabase($resolvedPath, 1)
        $view = $database.OpenView('SELECT `File`,`FileName`,`Language` FROM `File`')
        $view.Execute()
        $targetIds = @{}
        while ($record = $view.Fetch()) {
            $fileName = [string] $record.StringData(2)
            if ($fileName.Contains('|')) {
                $longName = $fileName.Substring($fileName.IndexOf('|') + 1)
            }
            else {
                $longName = $fileName
            }
            $normalizedName = $longName.ToLowerInvariant()
            $language = [string] $record.StringData(3)
            $hasMalformedLanguage = (-not [string]::IsNullOrWhiteSpace($language)) -and ($language -notmatch '^\d+$')
            $hasInvalidWinUiMsiLanguage = $invalidWinUiMsiLanguages -contains $language
            if ($targetNames.ContainsKey($normalizedName) -and ($hasMalformedLanguage -or $hasInvalidWinUiMsiLanguage)) {
                $targetIds[[string] $record.StringData(1)] = $normalizedName
            }
            [Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null
        }
        $view.Close()
        [Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
        $view = $null

        if ($targetIds.Count -ne 8) {
            throw "Expected eight WinUI language-metadata rows, found $($targetIds.Count)."
        }

        foreach ($fileId in $targetIds.Keys) {
            $query = 'SELECT `File`,`Language` FROM `File` WHERE `File`=' + "'$fileId'"
            $view = $database.OpenView($query)
            $view.Execute()
            $record = $view.Fetch()
            if ($null -eq $record) {
                throw "Expected MSI File row was not found during language repair: $fileId"
            }
            $record.StringData(2) = $null
            $view.Modify(2, $record)
            [Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null
            $record = $null
            $view.Close()
            [Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
            $view = $null
        }
        $database.Commit()
        Write-Output "REPAIRED_MSI_WINUI_LANGUAGE_ROWS=$($targetIds.Count)"
    }
    finally {
        if ($null -ne $record) { [Runtime.InteropServices.Marshal]::ReleaseComObject($record) | Out-Null }
        if ($null -ne $view) {
            try { $view.Close() } catch {}
            [Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
        }
        if ($null -ne $database) { [Runtime.InteropServices.Marshal]::ReleaseComObject($database) | Out-Null }
        if ($null -ne $installer) { [Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null }
    }
}

# Keep the Burn extension in the approved external tooling root. The WiX executable is installed
# machine-wide, but the extension is a build dependency and must not be installed into the user's
# WiX extension cache or copied into the source workspace. The package/version are pinned so a
# clean machine gets the same extension that was used by the verified probe.
if (-not (Test-Path -LiteralPath $balExtension)) {
    $packageCache = Join-Path $balRoot "cache"
    $packageFile = Join-Path $packageCache "$balPackageId.$balVersion.nupkg"
    $packageUri = "https://api.nuget.org/v3-flatcontainer/$balPackageId/$balVersion/$balPackageId.$balVersion.nupkg"
    New-Item -ItemType Directory -Force -Path $packageCache, $balPackageRoot | Out-Null
    if (-not (Test-Path -LiteralPath $packageFile)) {
        try {
            Invoke-WebRequest -Uri $packageUri -OutFile $packageFile
        }
        catch {
            throw "Could not acquire the pinned WiX Burn extension from NuGet: $($_.Exception.Message)"
        }
    }
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($packageFile, $balPackageRoot)
    }
    catch {
        throw "Could not extract the pinned WiX Burn extension into the external tooling root: $($_.Exception.Message)"
    }
}
if (-not (Test-Path -LiteralPath $balExtension)) {
    throw "WiX Burn bootstrapper extension is missing after external acquisition: $balExtension"
}

& dotnet restore $solution -p:BuildRoot=$runRoot --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Glass Radio restore failed." }

& dotnet publish (Join-Path $workspace "src\GlassRadio\GlassRadio.csproj") `
  --configuration Release `
  -p:Platform=x64 `
  --no-restore `
  --runtime win-x64 `
  --self-contained true `
  -p:BuildRoot=$runRoot `
  -p:WindowsAppSDKSelfContained=true `
  -p:DebugSymbols=false `
  -p:DebugType=None `
  --output $publishDir `
  --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Glass Radio publish failed." }

# Windows App SDK's unpackaged publish target can leave an internal MSIX payload
# under the external intermediate tree. It is not a product artifact and must not
# survive the installer run. Remove only exact generated MSIX directories inside
# this dated run root; the final deliverables remain conventional MSI and EXE.
$intermediateRoot = [IO.Path]::GetFullPath((Join-Path $runRoot "artifacts\obj"))
$removedMsixDirectories = 0
if (Test-Path -LiteralPath $intermediateRoot) {
    foreach ($directory in @(Get-ChildItem -LiteralPath $intermediateRoot -Directory -Recurse -Filter "MSIX")) {
        $resolved = [IO.Path]::GetFullPath($directory.FullName)
        $allowedPrefix = $intermediateRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a path outside the installer intermediate root: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
        $removedMsixDirectories++
    }
}
Write-Output "REMOVED_INTERNAL_WINAPPSDK_MSIX_DIRECTORIES=$removedMsixDirectories"

& $wix build $wxs `
  -d PublishDir=$publishDir `
  -arch x64 `
  -out $msi `
  -pdbtype none `
  -intermediateFolder (Join-Path $intermediateDir "github-win-x64")
if ($LASTEXITCODE -ne 0) { throw "Glass Radio installer build failed." }

Repair-MsiWinUiLanguageMetadata -Path $msi -AllowedRoot $runRoot

Write-Output "INSTALLER=$msi"

& $wix build $bundleWxs `
  -ext $balExtension `
  -d Msi=$msi `
  -arch x64 `
  -out $setup `
  -pdbtype none `
  -intermediateFolder (Join-Path $intermediateDir "glassradio-setup")
if ($LASTEXITCODE -ne 0) { throw "Glass Radio setup bootstrapper build failed." }

Write-Output "SETUP=$setup"
Write-Output "RUN_ROOT=$runRoot"
