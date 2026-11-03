# Builds the signed arm64 APK and the update.json that go on a GitHub release.
# Needs the signing key next to this repository, in ..\keys. The key must never be committed: every update must carry it.
# NotesFile holds what changed, as plain "- " lines: the app shows it to users exactly as written when it offers the update.
param([Parameter(Mandatory = $true)][string]$NotesFile)
$ErrorActionPreference = 'Stop'
$notes = ([IO.File]::ReadAllText((Resolve-Path $NotesFile)) -replace "`r`n", "`n").Trim()
if (-not $notes) { throw "Release notes are empty: $NotesFile" }
$repo = Split-Path $PSScriptRoot -Parent
$root = Split-Path $repo -Parent
$keyStore = Join-Path $root 'keys\musicbay-release.jks'
$password = Join-Path $root 'keys\musicbay-release.pass'
if (-not (Test-Path $keyStore)) { throw "Signing key not found: $keyStore" }

# through the environment: the signing tool cannot take a password file whose path has spaces
$env:MUSICBAY_KEY_PASS = [IO.File]::ReadAllText($password)
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet10"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$project = Join-Path $repo 'Seeker\Seeker.csproj'
$arguments = @(
    $project, '-c', 'Release', '-f', 'net10.0-android36.0',
    '-p:AndroidPackageFormat=apk', '-p:RuntimeIdentifier=android-arm64', '-p:RuntimeIdentifiers=android-arm64',
    '-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$keyStore", '-p:AndroidSigningKeyAlias=musicbay',
    '-p:AndroidSigningKeyPass=env:MUSICBAY_KEY_PASS', '-p:AndroidSigningStorePass=env:MUSICBAY_KEY_PASS',
    "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk", "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk",
    '-nologo', '-v:q', '-clp:ErrorsOnly'
)
# always from clean: an incremental build can produce an APK that crashes at launch
dotnet build @arguments -t:Clean
if ($LASTEXITCODE -ne 0) { throw 'Clean failed' }
dotnet build @arguments -t:SignAndroidPackage
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$version = ([xml](Get-Content (Join-Path $repo 'Seeker\Properties\AndroidManifest.xml'))).manifest.versionName
$output = Join-Path $root "release\MusicBay-$version.apk"
Copy-Item (Join-Path $repo 'Seeker\bin\Release\net10.0-android36.0\android-arm64\app.musicbay-Signed.apk') $output -Force
$update = Join-Path $root 'release\update.json'
$json = @{ version = "v$version"; notes = $notes; apk = "https://github.com/ssenwako/MusicBay/releases/download/v$version/MusicBay-$version.apk" } | ConvertTo-Json
[IO.File]::WriteAllText($update, $json, (New-Object Text.UTF8Encoding($false)))
Write-Output "Built $output"
Write-Output "Publish it with: gh release create v$version `"$output`" `"$update`" LICENSE --title `"MusicBay $version`" --notes-file `"$NotesFile`""
