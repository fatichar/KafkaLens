param(
    [Parameter(Mandatory)]
    [string] $Installer
)

$ErrorActionPreference = 'Stop'
$installerPath = (Resolve-Path -LiteralPath $Installer).Path
$installerSignature = Get-AuthenticodeSignature -LiteralPath $installerPath
if ($installerSignature.Status -ne 'Valid' -or !$installerSignature.TimeStamperCertificate) {
    throw 'Expected a trusted, timestamped installer.'
}

$testDir = Join-Path $env:RUNNER_TEMP ("kafkalens-smoke-" + [guid]::NewGuid())
$uninstaller = Join-Path $testDir 'unins000.exe'
$exePath = Join-Path $testDir 'KafkaLens.exe'
$installFailure = $null
try {
    # Inno's dialog override also enables /CURRENTUSER and suppresses the mode chooser.
    $install = Start-Process -FilePath $installerPath -Wait -PassThru -ArgumentList (
        "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /CURRENTUSER /DIR=`"$testDir`""
    )
    if ($install.ExitCode -ne 0) { throw "Installer exit code: $($install.ExitCode)" }

    foreach ($name in 'KafkaLens.exe', 'KafkaLens.Updater.exe', 'unins000.exe') {
        $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $testDir $name)
        if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate -or
            $signature.SignerCertificate.Subject -ne $installerSignature.SignerCertificate.Subject) {
            throw "Installed file lacks the expected timestamped signature: $name"
        }
    }
}
catch {
    $installFailure = $_
    throw
}
finally {
    try {
        if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
            $signature = Get-AuthenticodeSignature -LiteralPath $uninstaller
            if ($signature.Status -eq 'Valid' -and $signature.SignerCertificate.Subject -eq $installerSignature.SignerCertificate.Subject) {
                # On Windows, -Wait also waits for the uninstaller's child processes.
                $uninstall = Start-Process -FilePath $uninstaller -Wait -PassThru -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
                if ($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath $exePath)) {
                    throw "Uninstall did not remove KafkaLens (exit code $($uninstall.ExitCode))."
                }
            }
        }
    }
    catch {
        if ($null -eq $installFailure) { throw }
        Write-Warning "Uninstall cleanup also failed: $_"
    }
}

Write-Output 'Verified signed Windows install and uninstall on the CI runner.'
