# Connecting KafkaLens to Confluent Cloud

Confluent Cloud clusters use **SASL_SSL** with the **PLAIN** mechanism. The user name is a Kafka
**API key** and the password is its **secret**. Certificates are issued by a public CA, so no CA file is needed.

## 1. Collect the connection details (Confluent Cloud console)

1. Open your environment → your Kafka cluster → **Cluster settings**. Copy the **Bootstrap server**,
   e.g. `pkc-abc12.eu-west-1.aws.confluent.cloud:9092`.
2. **API keys** → **Add key**. For a personal key choose *My account*; for a dedicated read-only key choose
   a *Service account* (recommended). Copy the **key** and **secret** — the secret is shown only once.
3. If you use a service account, grant it read access (**Accounts & access → Service account → ACLs**):

   | Resource | Name | Operations |
   |---|---|---|
   | Cluster | `kafka-cluster` | `DESCRIBE` |
   | Topic | `*` (or a prefix such as `orders.`) | `DESCRIBE`, `READ` |
   | Consumer group | prefix `KafkaLens` | `READ`, `DESCRIBE` |

   KafkaLens only reads. It never needs `WRITE`, `CREATE`, `DELETE` or `ALTER`.
4. Optional, for Avro/Protobuf/JSON Schema topics: **Schema Registry** (environment level) → copy the
   **endpoint** (e.g. `https://psrc-xyz.eu-central-1.aws.confluent.cloud`) and create a *Schema Registry*
   API key/secret (a different key from the Kafka one).

## 2. Add the cluster in KafkaLens

1. **Cluster → Edit Clusters → Add**.
2. **Cluster Name**: anything, e.g. `Confluent Cloud – prod`.
3. **Bootstrap Servers**: the bootstrap server from step 1.
4. **Security**: *SASL over SSL/TLS (SASL_SSL)*. (KafkaLens shows a hint if a `*.confluent.cloud` address is
   used with other settings.)
5. **SASL mechanism**: `PLAIN`. **User name**: API key. **Password**: API secret. Leave the TLS fields empty.
6. Optional: expand **Schema Registry** and enter the endpoint, the Schema Registry API key (user name) and
   secret (password). Click **Test Schema Registry**.
7. Click **Test Connection**, then **OK** and **Save**.

The API secrets are stored in your OS credential store (Windows Credential Manager, macOS Keychain,
Linux Secret Service), never in KafkaLens' settings files. See [secure-connections.md](secure-connections.md).

## 3. Browse

**Cluster → Open Cluster → your cluster**, pick a topic, and fetch messages as usual. With a Schema
Registry configured, schema-encoded topics are decoded automatically.

## Troubleshooting

| What you see | What to do |
|---|---|
| *Authentication failed: check the user name, password and SASL mechanism.* | The key/secret pair is wrong, deleted, or for another cluster. Keys are cluster-specific; create one for this cluster. New keys can take a minute to become active. |
| *Not authorized: … lack the required ACLs* | Add the ACLs from step 1.3 to the service account. |
| *No topics are visible …* (in the topic list) | Confluent Cloud hides topics the key may not `DESCRIBE`. Add topic `DESCRIBE`/`READ` ACLs. |
| *TLS handshake failed …* | Usually a TLS-intercepting proxy. Point *CA certificate* at your proxy's CA file. Do not use a plaintext listener. |
| *Cannot reach the brokers …* | Check the bootstrap address and port `9092`, and that outbound access to `*.confluent.cloud:9092` is allowed. Private-networking clusters need VPN/peering access. |
| Schema Registry *HTTP 401* | Use the **Schema Registry** API key, not the Kafka key. |

## Validation status

The configuration path (SASL_SSL + PLAIN, OS trust store, verified host names), error messages and
the hint are covered by automated tests. End-to-end tests against a real Confluent Cloud cluster run
when credentials are supplied:

```bash
KAFKALENS_TEST_CCLOUD_BOOTSTRAP=pkc-...:9092 KAFKALENS_TEST_CCLOUD_KEY=... KAFKALENS_TEST_CCLOUD_SECRET=... \
  dotnet test Core.Tests -c Release --filter "FullyQualifiedName~ConfluentCloudLiveTests"
```
