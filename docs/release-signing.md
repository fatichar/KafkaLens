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
