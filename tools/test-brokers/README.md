# Test brokers for secured connections

`secure-broker.sh` starts a disposable single-node Kafka with SASL (PLAIN, SCRAM-SHA-256,
SCRAM-SHA-512), SASL_SSL and SSL listeners, a test CA, and a `secured.demo` topic. It is used to
verify KafkaLens' TLS/SASL support (#13, #79) and the live tests in
`Core.Tests/Security/LiveBrokerSecurityTests.cs`.

```bash
KAFKA_HOME=~/kafka_2.13-4.1.2          # any Kafka 3.7+/4.x distribution
tools/test-brokers/secure-broker.sh "$KAFKA_HOME" /tmp/kl-secure-broker start

KAFKALENS_TEST_SECURE_BROKER=/tmp/kl-secure-broker \
  dotnet test Core.Tests -c Release --filter "FullyQualifiedName~LiveBrokerSecurityTests"

tools/test-brokers/secure-broker.sh "$KAFKA_HOME" /tmp/kl-secure-broker stop
```

| Listener | Address | KafkaLens settings |
|---|---|---|
| SASL_PLAINTEXT | `localhost:29092` | *SASL without encryption*, user `alice`, password `alice-secret` |
| SASL_SSL | `localhost:29095` | *SASL over SSL/TLS*, CA certificate `<work-dir>/ca.pem` |
| SSL | `localhost:29096` | *SSL/TLS*, CA certificate `<work-dir>/ca.pem` |

The credentials and CA are for local testing only.
