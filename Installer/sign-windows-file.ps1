param(
    [Parameter(Mandatory)]
    [string] $Path,
    [switch] $VerifyOnly
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if (!$VerifyOnly) {
    & $env:KAFKALENS_SIGNTOOL sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 `
        /dlib $env:KAFKALENS_SIGNING_DLIB /dmdf $env:KAFKALENS_SIGNING_METADATA $Path
}
& $env:KAFKALENS_SIGNTOOL verify /pa /all /tw $Path
$signature = Get-AuthenticodeSignature -LiteralPath $Path
if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate) {
    throw "Expected a trusted, timestamped Authenticode signature on $Path."
}
