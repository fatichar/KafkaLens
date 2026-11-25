#!/usr/bin/env bash
# Starts a throw-away single-node Kafka (KRaft) with authenticated listeners for testing KafkaLens:
#   localhost:29092  SASL_PLAINTEXT  (PLAIN, SCRAM-SHA-256, SCRAM-SHA-512)
#   localhost:29095  SASL_SSL        (same mechanisms; certificate for "localhost" signed by a test CA)
#   localhost:29096  SSL             (TLS only, no authentication)
# User: alice / alice-secret.  Test CA: $WORK/ca.pem
#
# Usage: secure-broker.sh <kafka-home> <work-dir> [start|stop]
# Requires Java 17+, openssl, a Kafka 3.7+/4.x distribution. Never use these credentials anywhere real.
set -euo pipefail
KAFKA_HOME="$1"; WORK="$2"; ACTION="${3:-start}"
mkdir -p "$WORK"
PIDFILE="$WORK/kafka.pid"

if [ "$ACTION" = "stop" ]; then
  [ -f "$PIDFILE" ] && kill "$(cat "$PIDFILE")" 2>/dev/null || true
  rm -f "$PIDFILE"; exit 0
fi

cd "$WORK"
if [ ! -f ca.pem ]; then
  openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=KafkaLens Test CA" -keyout ca.key -out ca.pem 2>/dev/null
  openssl req -newkey rsa:2048 -nodes -subj "/CN=localhost" -keyout broker.key -out broker.csr 2>/dev/null
  printf "subjectAltName=DNS:localhost\n" > san.ext
  openssl x509 -req -in broker.csr -CA ca.pem -CAkey ca.key -CAcreateserial -days 2 -extfile san.ext -out broker.crt 2>/dev/null
  # Kafka PEM keystore: private key (PKCS#8) followed by the certificate chain.
  openssl pkcs8 -topk8 -nocrypt -in broker.key -out broker.pk8
  cat broker.pk8 broker.crt ca.pem > broker-keystore.pem
fi

cat > server.properties <<PROPS
process.roles=broker,controller
node.id=1
controller.quorum.voters=1@localhost:29093
listeners=INTERNAL://localhost:29094,CONTROLLER://localhost:29093,SASL_PLAINTEXT://localhost:29092,SASL_SSL://localhost:29095,SSL://localhost:29096
advertised.listeners=INTERNAL://localhost:29094,SASL_PLAINTEXT://localhost:29092,SASL_SSL://localhost:29095,SSL://localhost:29096
listener.security.protocol.map=INTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT,SASL_PLAINTEXT:SASL_PLAINTEXT,SASL_SSL:SASL_SSL,SSL:SSL
controller.listener.names=CONTROLLER
inter.broker.listener.name=INTERNAL
sasl.enabled.mechanisms=PLAIN,SCRAM-SHA-256,SCRAM-SHA-512
listener.name.sasl_plaintext.plain.sasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required user_alice="alice-secret";
listener.name.sasl_plaintext.scram-sha-256.sasl.jaas.config=org.apache.kafka.common.security.scram.ScramLoginModule required;
listener.name.sasl_plaintext.scram-sha-512.sasl.jaas.config=org.apache.kafka.common.security.scram.ScramLoginModule required;
listener.name.sasl_ssl.plain.sasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required user_alice="alice-secret";
listener.name.sasl_ssl.scram-sha-256.sasl.jaas.config=org.apache.kafka.common.security.scram.ScramLoginModule required;
listener.name.sasl_ssl.scram-sha-512.sasl.jaas.config=org.apache.kafka.common.security.scram.ScramLoginModule required;
ssl.keystore.type=PEM
ssl.keystore.location=$WORK/broker-keystore.pem
ssl.truststore.type=PEM
ssl.truststore.location=$WORK/ca.pem
log.dirs=$WORK/data
num.partitions=2
offsets.topic.replication.factor=1
transaction.state.log.replication.factor=1
transaction.state.log.min.isr=1
auto.create.topics.enable=true
PROPS

if [ ! -d data ]; then
  "$KAFKA_HOME/bin/kafka-storage.sh" format -t "$("$KAFKA_HOME/bin/kafka-storage.sh" random-uuid)" -c server.properties \
    --add-scram 'SCRAM-SHA-256=[name=alice,password=alice-secret]' \
    --add-scram 'SCRAM-SHA-512=[name=alice,password=alice-secret]' >/dev/null
fi

"$KAFKA_HOME/bin/kafka-server-start.sh" server.properties > kafka.log 2>&1 &
echo $! > "$PIDFILE"
for _ in $(seq 1 60); do
  if grep -q "Kafka Server started\|KafkaRaftServer.*started\|Transitioning from RECOVERY to RUNNING" kafka.log 2>/dev/null; then break; fi
  sleep 1
done
# A topic with a few messages so browsing can be checked.
"$KAFKA_HOME/bin/kafka-topics.sh" --bootstrap-server localhost:29094 --create --if-not-exists --topic secured.demo --partitions 2 >/dev/null
printf 'k1:{"hello":"secure world"}\nk2:{"n":2}\n' | "$KAFKA_HOME/bin/kafka-console-producer.sh" \
  --bootstrap-server localhost:29094 --topic secured.demo --property parse.key=true --property key.separator=: >/dev/null
echo "Broker ready. CA: $WORK/ca.pem"
