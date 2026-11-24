# Connecting to secured Kafka clusters

KafkaLens can connect to brokers that require TLS encryption. Configure it per cluster in
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
    }
  ]
}
```

On servers, secrets are read from environment variables first, then from the OS keyring if one exists.
The variable name is `KAFKALENS_SECRET__CLUSTER__<ID>__<SECRET>` with non-alphanumeric characters
replaced by `_`, e.g. `KAFKALENS_SECRET__CLUSTER__PROD__SSL_KEY_PASSWORD`.

To serve the backend itself over TLS, set `Kestrel:EndpointDefaults:CertificatePath` to a PFX file
(and `Kestrel:EndpointDefaults:HttpsPort`); its password comes from
`KAFKALENS_SECRET__SERVER__HTTPS_CERTIFICATE_PASSWORD`. In KafkaLens, add the client with an
`https://host:port` address.
