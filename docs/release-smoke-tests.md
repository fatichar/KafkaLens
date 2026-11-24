# Desktop release smoke tests

Use this runbook for [#91](https://github.com/fatichar/KafkaLens/issues/91). Start with the
downloadable artifacts from a successful manual run of **Publish Release** on the integrated
candidate commit. Keep the CI run URL and SHA-256 of each downloaded asset with the test results.
See [release signing](release-signing.md) for credential setup. Use clean OS snapshots with
standard user accounts and normal OS security settings. Install only OS runtime prerequisites.
Use a separate test broker with synthetic data so each client can be tested without a Kafka server,
.NET SDK, or source checkout on the client machine.

## Candidate matrix

Record the exact OS version and build, OS architecture, artifact filename and SHA-256 for every
row. Repeat for any additional OS versions advertised for the release.

| OS and CPU | Asset | Install and remove | Verify trust |
| --- | --- | --- | --- |
| Windows 11 x64 | `KafkaLens-<version>-win-x64-installer.exe` | Install from Explorer, select **Install for me only**, and remove via Settings → Apps → Installed apps | Download in a browser, verify the displayed publisher, record any SmartScreen warning verbatim, and check the installed signatures |
| macOS Apple Silicon | `KafkaLens-<version>-macos-arm64.zip` | Extract with Archive Utility, drag the app to Applications, then move it to Trash for removal | Launch from Finder with Gatekeeper enabled and verify the signature and staple |
| macOS Intel | `KafkaLens-<version>-macos-x64.zip` | Same steps on a native Intel machine | Same checks on Intel |
| Ubuntu 24.04 Desktop x64 | `KafkaLens-<version>-linux-x64.zip` | Extract as a normal user, run `KafkaLens` directly and delete the extracted directory for removal | Record the SHA-256 and confirm the extracted launchers are executable |

On Windows, run `Get-AuthenticodeSignature -LiteralPath <path>` for the installer,
`KafkaLens.exe`, `KafkaLens.Updater.exe` and `unins000.exe`. Require `Valid` and a timestamp.
On macOS run the following on the app in Applications and save the output:

```sh
codesign --verify --deep --strict --verbose=2 /Applications/KafkaLens.app
xcrun stapler validate /Applications/KafkaLens.app
syspolicy_check distribution /Applications/KafkaLens.app
```

If a verification command is absent from a clean machine, record that tooling blocker and capture
the checks from CI separately from the Finder launch result. Avoid overriding OS security prompts
or removing browser quarantine during the test.

## Per-platform journey

1. Restore the clean snapshot, download the asset and install it as described above. Open the app
   from the normal entry point. Confirm the UI appears, About reports the candidate version, and
   the first-run notice behaves as expected for default settings. Opt out before exercising the
   shared test broker. Relaunch and check that the setting persists.
2. Use a reachable non-production Kafka broker with a test topic containing one known record.
   Run the following where the Kafka CLI is installed. Substitute the address of your test broker
   and choose a new topic name for each run. Save only test fixture names and redacted evidence.

   ```sh
   broker=kafka-smoke.example.test:9092
   topic=kafkalens-release-smoke
   kafka-topics.sh --bootstrap-server "$broker" --create --topic "$topic" --partitions 1 --replication-factor 1
   printf 'release-smoke-payload\n' | kafka-console-producer.sh --bootstrap-server "$broker" --topic "$topic"
   ```
3. Add the broker from Edit Clusters and test the connection. Connect, open the fixture topic,
   fetch the known record from the beginning and confirm the payload. Repeat the relevant steps for
   TLS/SASL and Schema Registry or a gRPC client when those paths are present in the integrated
   candidate. Record those variants separately, along with expected error cases for bad test
   credentials or an unreachable endpoint.
4. Install the previous supported version in a separate clean snapshot. Exercise **Check for
   Updates**, confirm the offered asset matches the OS and CPU, apply it and verify the new version
   and retained test settings. Windows should launch the signed installer. From a notarized
   macOS build, choose **Download Update**, quit the old version and replace the whole app in
   Applications. For an older unpackaged macOS build, download the candidate directly and
   record the manual migration separately. Linux should update in a writable extraction
   directory. Relaunch and repeat the topic fetch.
5. Remove the candidate using the matrix instructions. Confirm the launcher or app bundle and
   installed shortcuts are gone. Record whether per-user settings are retained, then discard the snapshot.

The application checks the public GitHub latest-release endpoint. A workflow artifact alone
cannot exercise candidate discovery in the previous binary. For a manual rehearsal, install the
candidate over the approved previous version from its downloaded asset and record that result
separately. Keep the end-to-end update check pending until the candidate is available through the
configured endpoint in an authorized release exercise.

On Linux, upgrading from 1.0.1 or earlier requires a manual download. Those releases contain
an updater that can resolve archive paths from the filesystem root. Extract the candidate to
a new directory and launch it with the same user profile. Exercise in-app updates separately
from a build that contains the corrected archive updater.

## Automated checks and P0 status

- Windows CI runs a silent install in a temporary directory, verifies the signatures of the
  installed app, updater and uninstaller, and runs the uninstaller.
- macOS CI verifies the signature, staple and Gatekeeper distribution assessment from each final ZIP.
- Linux CI unpacks the final ZIP, checks executable modes and the bundled runtime, and waits for
  the main window on an isolated X11 display with telemetry disabled.
- The solution's existing Avalonia Headless suite covers UI interactions with fake Kafka data.
  [PR #63](https://github.com/fatichar/KafkaLens/pull/63) is a headless test reference.
  These automated checks supplement the clean-system workflow.

The P0 Linux x64 ZIP displayed the versioned main window in an Ubuntu 24.04 container with no .NET
SDK or network access. The Release build passed. The solution test suite reported 707 passes and
one failure in `FetchMessages_FromMultiplePartitions_IsNowParallel`. The same timing assertion
also failed on the original P0 commit (`89a4694`).

Copy this table into the release verification record. For each status use **Pass**, **Fail**,
**Blocked** or **Pending** and link evidence or describe the blocker. Record the candidate commit,
run URL, tester/date, OS build and the asset hash above the table.

| OS / CPU | Install & trust | Launch | Connect | Browse & fetch | Update | Uninstall | Evidence / blocker |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Windows x64 | Pending | Pending | Pending | Pending | Pending | Pending | Azure signing environment and fresh Windows VM with SmartScreen |
| macOS arm64 | Pending | Pending | Pending | Pending | Pending | Pending | Apple signing and notarization credentials and clean Apple Silicon system |
| macOS x64 | Pending | Pending | Pending | Pending | Pending | Pending | Apple signing and notarization credentials and clean Intel system |
| Linux x64 | Pending | Pending | Pending | Pending | Pending | Pending | Clean desktop VM and candidate ZIP |

Final acceptance requires the integrated connectivity changes, access to disposable Kafka fixtures
for the relevant authentication modes, clean systems on all four rows, and a candidate available
through the configured update endpoint for the update test. The public launch handoff leaves those
checks open while candidate signing and connectivity integration are pending.
