param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$installerScript = Join-Path $PSScriptRoot 'install_windows.iss'
$installerVersion = Select-String -LiteralPath $installerScript -Pattern '^\s*AppVersion\s*=\s*(\S+)\s*$'
if (@($installerVersion).Count -ne 1 -or $installerVersion.Matches[0].Groups[1].Value -cne $Version) {
    throw "Inno Setup AppVersion must match the release version ($Version) from Directory.Build.props."
}

foreach ($variable in 'AZURE_SIGNING_ENDPOINT', 'AZURE_SIGNING_ACCOUNT', 'AZURE_SIGNING_PROFILE') {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($variable))) {
        throw "Missing Windows signing configuration: $variable"
    }
}

$releases = Join-Path $PSScriptRoot '../Releases'
$publishDir = Join-Path $releases 'KafkaLens_win'
foreach ($executable in 'KafkaLens.exe', 'KafkaLens.Updater.exe') {
    if (!(Test-Path -LiteralPath (Join-Path $publishDir $executable) -PathType Leaf)) {
        throw "Missing published executable: $executable"
    }
}

$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$sdk = Get-ChildItem -LiteralPath $sdkBin -Directory |
    Where-Object { $_.Name -match '^10\.0\.\d+\.\d+$' -and
        [version]$_.Name -ge [version]'10.0.22621.0' -and
        (Test-Path (Join-Path $_.FullName 'x64/signtool.exe')) } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
if (!$sdk) { throw 'Windows SDK 10.0.22621.0 or later with x64 SignTool is required.' }
$env:KAFKALENS_SIGNTOOL = Join-Path $sdk.FullName 'x64/signtool.exe'

$signingDir = Join-Path $env:RUNNER_TEMP ("kafkalens-signing-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $signingDir | Out-Null
try {
    $clientVersion = '1.0.95'
    $clientSha256 = '3bfcf1e0a3cb42af1692f0a8ed45c15de070c2de86f28a59b2795d904d8a920f'
    $clientZip = Join-Path $signingDir 'signing-client.zip'
    $clientUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.trusted.signing.client/$clientVersion/microsoft.trusted.signing.client.$clientVersion.nupkg"
    Invoke-WebRequest -Uri $clientUrl -OutFile $clientZip
    if ((Get-FileHash -LiteralPath $clientZip -Algorithm SHA256).Hash -ne $clientSha256) {
        throw "SHA-256 mismatch for the Azure Artifact Signing client $clientVersion."
    }
    Expand-Archive -LiteralPath $clientZip -DestinationPath $signingDir
    $env:KAFKALENS_SIGNING_DLIB = Join-Path $signingDir 'bin/x64/Azure.CodeSigning.Dlib.dll'
    $env:KAFKALENS_SIGNING_METADATA = Join-Path $signingDir 'metadata.json'
    @{
        Endpoint = $env:AZURE_SIGNING_ENDPOINT
        CodeSigningAccountName = $env:AZURE_SIGNING_ACCOUNT
        CertificateProfileName = $env:AZURE_SIGNING_PROFILE
        ExcludeCredentials = @(
            'EnvironmentCredential', 'WorkloadIdentityCredential', 'ManagedIdentityCredential',
            'SharedTokenCacheCredential', 'VisualStudioCredential', 'VisualStudioCodeCredential',
            'AzurePowerShellCredential', 'AzureDeveloperCliCredential', 'InteractiveBrowserCredential',
            'BrokerCredential'
        )
    } | ConvertTo-Json | Set-Content -LiteralPath $env:KAFKALENS_SIGNING_METADATA

    $signScript = Join-Path $PSScriptRoot 'sign-windows-file.ps1'
    Get-ChildItem -LiteralPath $publishDir -File -Recurse |
        Where-Object { $_.Name -like 'KafkaLens*' -and $_.Extension -in '.exe', '.dll' } |
        ForEach-Object { & $signScript $_.FullName }

    $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
    if (!$iscc) { $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
    $pwsh = (Get-Command pwsh.exe).Source
    $signTool = '/SAzureArtifactSigning=$q{0}$q -NoProfile -NonInteractive -File $q{1}$q $f' -f $pwsh, $signScript
    & $iscc '/DReleaseSigning' $signTool $installerScript

    $setup = Join-Path $releases 'KafkaLensSetup.exe'
    & $signScript -Path $setup -VerifyOnly
    Move-Item -LiteralPath $setup -Destination (Join-Path $releases "KafkaLens-$Version-win-x64-installer.exe")
}
finally {
    Remove-Item -LiteralPath $signingDir -Recurse -Force
}
