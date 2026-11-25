# Connecting to secured Kafka clusters

KafkaLens can connect to brokers that require TLS encryption and/or SASL authentication
(PLAIN, SCRAM-SHA-256, SCRAM-SHA-512). Configure it per cluster in
**Cluster → Edit Clusters → Add/Edit**, under **Security**.

## Where credentials are stored

Passwords (for example a TLS private-key password) are **never written to `cluster_info.json`**.
They are kept in the operating system's credential store:

| OS | Store |
|---|---|
| Windows | Windows Credential Manager (generic credentials named `KafkaLens:cluster/<id>/<secret>`) |
| macOS | Login Keychain (service `KafkaLens`) |
| Linux | Secret Service — GNOME Keyring, KWallet, KeePassXC… — via `secret-tool` (package `libsecret-tools`) |

`cluster_info.json` only records *which* secrets exist (`"storedSecrets": [...]`).

If no credential store is available (for example Linux without `secret-tool` or a running keyring),
KafkaLens keeps passwords **in memory for the current session only** and tells you so in the cluster
dialog; you will have to re-enter them after a restart. Nothing is written to disk in that case.

Plaintext secrets found in a configuration file (a `"secrets": { "sasl.password": "…" }` object on a
cluster, e.g. written by hand) are moved into the credential store the next time KafkaLens starts and
removed from the file. Without a credential store the file is left untouched so nothing is lost.

Secrets are removed from the credential store when a cluster is deleted, or when you switch to
settings that no longer need them (e.g. from TLS with an encrypted key back to plaintext).

## TLS (SSL)

Choose **Security → SSL/TLS**.

| Field | Meaning |
|---|---|
| CA certificate (PEM) | CA that signed the broker certificates. Leave empty to use the operating system trust store. |
| Client certificate / private key (PEM) | Only for brokers that require mutual TLS (client authentication). Both must be set. |
| Private key password | Only if the private key is encrypted. Stored in the credential store. |

Broker certificates are **always** verified: they must chain to a trusted CA and match the host name in
the bootstrap address. There is no option to disable verification; for self-signed or private CAs,
point *CA certificate* at the CA's PEM file instead.

Use **Test Connection** to check the settings before saving. Errors such as an untrusted certificate or
a host-name mismatch are shown with technical details.

## SASL authentication (PLAIN, SCRAM)

Choose **Security → SASL over SSL/TLS (SASL_SSL)** — or, only on fully trusted networks,
**SASL without encryption (SASL_PLAINTEXT)**.

| Field | Meaning |
|---|---|
| SASL mechanism | `PLAIN`, `SCRAM-SHA-256` or `SCRAM-SHA-512`, as configured on the broker listener. |
| User name (API key) | SASL user name; for Confluent Cloud the API key. Saved in `cluster_info.json`. |
| Password (API secret) | Stored in the credential store, never in the settings file or logs. When editing, leave it empty to keep the saved password. |

With SASL_SSL the TLS fields above (CA certificate, optional client certificate) apply as well.

SASL_PLAINTEXT sends the user name and password **unencrypted**; the dialog shows a warning and the
cluster list marks it *(unencrypted)*. Prefer SASL_SSL.

Passwords are removed from error messages and logs.

## Troubleshooting messages

Connection tests, the cluster status and topic loading explain the most common failures:

| Message | Typical cause |
|---|---|
| *Authentication failed: check the user name, password and SASL mechanism.* | Wrong credentials, wrong mechanism (e.g. PLAIN vs SCRAM), or a deleted API key. |
| *Not authorized: … lack the required ACLs* | Login worked but the principal may not describe the cluster/topics or read them. |
| *TLS handshake failed: …* | Unknown CA (set *CA certificate*), host name not in the broker certificate, or TLS selected for a plaintext listener. |
| *Cannot reach the brokers: …* | Wrong address/port, VPN down, firewall, or plaintext selected for a TLS listener. |
| *The SASL password is missing.* | No credential store was available last session; re-enter the password. |

The original Kafka error is kept under *Show error details*.

### Verifying with a local test broker

`tools/test-brokers/secure-broker.sh` starts a disposable broker with SASL_PLAINTEXT, SASL_SSL and SSL
listeners (user `alice` / `alice-secret`, test CA) — see `tools/test-brokers/README.md`. The opt-in tests
in `Core.Tests/Security/LiveBrokerSecurityTests.cs` run against it.

## KafkaLens backend (gRPC server)

Clusters served by a KafkaLens backend are configured on the server, in its `cluster_info.json`:

```json
{
  "clusters": [
    {
      "id": "prod",
      "name": "Production",
      "address": "b-1.example.com:9093,b-2.example.com:9093",
      "protocol": null,
      "security": {
        "protocol": "Ssl",
        "sslCaLocation": "/etc/kafkalens/ca.pem",
        "sslCertificateLocation": "/etc/kafkalens/client.pem",
        "sslKeyLocation": "/etc/kafkalens/client.key"
      },
      "storedSecrets": [ "ssl.key.password" ]
    },
    {
      "id": "auth",
      "name": "Authenticated",
      "address": "kafka.internal:9094",
      "protocol": null,
      "security": { "protocol": "SaslSsl", "saslMechanism": "ScramSha512", "saslUsername": "kafkalens" },
      "storedSecrets": [ "sasl.password" ]
    }
  ]
}
```

On servers, secrets are read from environment variables first, then from the OS keyring if one exists.
The variable name is `KAFKALENS_SECRET__CLUSTER__<ID>__<SECRET>` with non-alphanumeric characters
replaced by `_`, e.g. `KAFKALENS_SECRET__CLUSTER__PROD__SSL_KEY_PASSWORD` or
`KAFKALENS_SECRET__CLUSTER__AUTH__SASL_PASSWORD`. Desktop users of the backend never see these credentials.

To serve the backend itself over TLS, set `Kestrel:EndpointDefaults:CertificatePath` to a PFX file
(and `Kestrel:EndpointDefaults:HttpsPort`); its password comes from
`KAFKALENS_SECRET__SERVER__HTTPS_CERTIFICATE_PASSWORD`. In KafkaLens, add the client with an
`https://host:port` address.
