# Desktop release signing

The [Publish Release workflow](../.github/workflows/publish_app.yml) uses the version in
`Directory.Build.props`. A manual run produces CI artifacts. Use a trusted ref and have the signing
environment reviewers approve the run. Version tags also enable the release job after every package job
succeeds. During P0, use manual runs for validation.

## Windows x64

Create a GitHub Actions environment named `windows-release-signing`. Restrict it to trusted branches
approved for signing rehearsals and protected release tags. Configure required reviewers and prevent
self-review where supported. Create an Azure Artifact Signing account, complete identity validation and
create a **Public Trust** certificate profile. Register a Microsoft Entra application with a GitHub
federated credential for this repository. For the default GitHub OIDC claims, use issuer
`https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange` and subject
`repo:fatichar/KafkaLens:environment:windows-release-signing`. Grant the service principal
**Artifact Signing Certificate Profile Signer** at the certificate profile scope.

Set these Actions environment variables:

| Name | Value |
| --- | --- |
| `AZURE_SIGNING_CLIENT_ID` | Entra application (client) ID |
| `AZURE_SIGNING_TENANT_ID` | Entra directory (tenant) ID |
| `AZURE_SIGNING_ENDPOINT` | HTTPS data endpoint for the signing account region |
| `AZURE_SIGNING_ACCOUNT` | Artifact Signing account name |
| `AZURE_SIGNING_PROFILE` | Public Trust certificate profile name |

Azure holds the private signing key. CI obtains short-lived credentials through OIDC. Windows SDK
SignTool and the Azure client sign and verify KafkaLens executables and assemblies. Inno Setup uses
the same signer for Setup and its uninstaller. Verification requires a trusted Authenticode chain and
a SHA-256 RFC 3161 timestamp. The local Windows packaging script remains available for unsigned
development builds.

External validation requires a configured Azure account and GitHub environment, a successful manual
signing run on a Windows runner, and a clean Windows x64 VM with Defender SmartScreen enabled.
Download the original signed installer through a browser so it gets Mark of the Web. Record the
publisher, install and uninstall results, and the exact text or absence of any SmartScreen reputation
warning. A verified Authenticode signature alone cannot establish SmartScreen reputation.

## macOS Apple Silicon and Intel

Create a GitHub Actions environment named `macos-release-signing`. Restrict it to trusted signing
rehearsal branches and protected release tags, and configure required reviewers. Obtain a
**Developer ID Application** certificate and private key through an Apple Developer Program
membership. Export them as a password-protected PKCS#12 file. Include the matching Developer ID
intermediate certificate in the export if the runner does not have that chain. Create an App Store
Connect team API key authorized to submit notarizations and save its `.p8` file to the maintainer's
credential store.

| Environment secret | Value |
| --- | --- |
| `MACOS_CERTIFICATE_BASE64` | Single-line Base64 of the PKCS#12 file |
| `MACOS_CERTIFICATE_PASSWORD` | PKCS#12 password |
| `MACOS_NOTARY_KEY_BASE64` | Single-line Base64 of the App Store Connect `.p8` key |

| Environment variable | Value |
| --- | --- |
| `MACOS_SIGNING_IDENTITY` | Full certificate name, `Developer ID Application: … (TEAMID)` |
| `MACOS_NOTARY_KEY_ID` | App Store Connect key ID |
| `MACOS_NOTARY_ISSUER_ID` | App Store Connect issuer UUID |

Use a temporary keychain and runner-local files for signing and notarization. CI signs native
libraries before signing the containing app, uses the hardened runtime and grants JIT/unsigned
executable memory only to the .NET launchers. Apple notarizes the ZIP payload. CI staples the ticket
to `KafkaLens.app` and validates the signature, ticket and Gatekeeper distribution assessment after
extracting the final ZIP. Publish each architecture's ZIP as produced by the workflow.

External validation requires both signed CI artifacts and clean macOS Apple Silicon and Intel
machines. Download each ZIP through a browser and open the extracted app from Applications under
normal Gatekeeper settings. The macOS update action opens the release download. Quit KafkaLens and
replace the entire app in Applications. Users upgrading from 1.0.1 or earlier must download the
ZIP directly from GitHub Releases because those versions still use the old archive updater.
The workflow includes these migration instructions in the published release notes.
