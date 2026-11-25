# P0 public-launch handoff

All P0 capabilities belong in the **free Community edition**. Do not introduce licensing, paid-feature gates or unrelated P1/P2 work. The linked issues are the requirements and acceptance criteria; this document only coordinates implementation.

## Work queue

| Order / lane | Issue | Existing work / action |
|---|---|---|
| Connectivity 1 | [#90 — Credential storage](https://github.com/fatichar/KafkaLens/issues/90) | Establish reusable secret storage before adding authentication credentials; coordinate with the schema-registry work. |
| Connectivity 2 | [#13 — SSL](https://github.com/fatichar/KafkaLens/issues/13) | Inspect current configuration first; implement missing TLS support without weakening certificate validation. |
| Connectivity 3 | [#79 — SASL](https://github.com/fatichar/KafkaLens/issues/79) | Reuse #90 and #13. |
| Connectivity 4 | [#32 — Connection test/status](https://github.com/fatichar/KafkaLens/issues/32) | [PR #33](https://github.com/fatichar/KafkaLens/pull/33) is merged; [#76](https://github.com/fatichar/KafkaLens/pull/76) fixes reconnect/topic loading. Validate and fill only remaining gaps (including new auth errors). |
| Connectivity 5 | [#81 — Schema Registry](https://github.com/fatichar/KafkaLens/issues/81) | [PR #74](https://github.com/fatichar/KafkaLens/pull/74) is open, already implements the main feature and touches shared models, cluster dialogs, gRPC and Windows installer. Review, port/rebase and squash its relevant work onto P0 rather than rewriting it; check remaining acceptance criteria, including registry credentials. |
| Connectivity 6 | [#80 — Confluent Cloud](https://github.com/fatichar/KafkaLens/issues/80) | Validate after #13/#79/#90/#32 and #81 where applicable. |
| Release 1 | [#88 — Windows signing](https://github.com/fatichar/KafkaLens/issues/88) | Use existing release scripts/workflows; coordinate installer edits with PR #74. |
| Release 2 | [#89 — macOS notarization](https://github.com/fatichar/KafkaLens/issues/89) | Coordinate shared release workflow edits with #88. |
| Release 3 | [#91 — Clean-system smoke tests](https://github.com/fatichar/KafkaLens/issues/91) | Prepare matrix/checks in parallel; final validation after signing and connectivity. [PR #63](https://github.com/fatichar/KafkaLens/pull/63) (closed, unmerged) is a reference for headless tests, not proof of clean-system testing. |
| Telemetry | [#94 — Privacy review](https://github.com/fatichar/KafkaLens/issues/94) | [PR #75](https://github.com/fatichar/KafkaLens/pull/75) and [#77](https://github.com/fatichar/KafkaLens/pull/77) are merged. Audit the current implementation and [approved plan](docs/analytics-plan.md) / [privacy policy](docs/privacy.html); retain approved opt-out with first-run notice unless maintainer directs otherwise. |
| Docs | [#96 — Repository README](https://github.com/fatichar/KafkaLens/issues/96) | Existing [docs/README.md](docs/README.md) contains build instructions; [PR #78](https://github.com/fatichar/KafkaLens/pull/78) is open with refreshed website, screenshots, demo and usage guide. Reuse these assets after integration; do not recreate them. Confirm README claims against landed features. |

Also inspect open [PR #73](https://github.com/fatichar/KafkaLens/pull/73) before modifying cluster configuration or ViewModels: it changes overlapping models and dialogs. PR status above was checked 2026-11-25; refresh statuses before each task. Open PRs are not assumed merged, accepted or safe to apply verbatim.

## Agent workflow

- Use branch `P0` as integration base. Pick one issue; check its current status, linked PRs, current code and concurrent work before starting. Existing completed work needs validation, not duplicate implementation.
- Independent lanes may run in parallel on separate branches/worktrees based on P0. Serialize overlapping connectivity/configuration and release workflow changes. Integrate in dependency order; rebase against current P0 before landing. Never push directly to `master`, force-push P0, or merge unrelated PRs.
- Exactly one implementation commit per issue on P0 (squash a reviewed task branch if needed); include `#NN` in the commit message. If already satisfied, record validation in the handoff/task report instead of manufacturing a commit. Do not close issues until changes reach the default branch and acceptance is verified.
- Follow the existing .NET 10/Avalonia architecture, test patterns and direct/gRPC compatibility. Run `dotnet build KafkaLens.slnx -c Release` and `dotnet test KafkaLens.slnx -c Release`; add targeted tests and report actual results. See [build docs](docs/README.md).
- Never commit secrets or use production/customer data. Signing certificates, Apple credentials, cloud test accounts and clean OS machines may require maintainer access. Implement testable scaffolding and document exact blockers; do not claim signing, cloud connectivity or platform smoke tests passed without evidence.
- Report issue, commit, tests, remaining acceptance gaps and dependencies. Keep changes focused; no release/tag or production deployment from this branch.
