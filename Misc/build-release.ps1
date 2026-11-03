# Builds the signed arm64 APK that goes on a GitHub release.
# Needs the signing key next to this repository, in ..\keys. The key must never be committed: every update must carry it.
$ErrorActionPreference = 'Stop'
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
Write-Output "Built $output"
Write-Output "Publish it with: gh release create v$version `"$output`" LICENSE --title `"MusicBay $version`" --notes `"What changed`""
