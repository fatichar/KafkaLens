if [ -z "$TAG" ]
then
   TAG=$({ date '+%Y%m%d%H%M%S' && echo - && git rev-parse --short HEAD && echo - && git branch | grep \* | cut -d ' ' -f2; } | tr -d '\n')
   echo "Using TAG: ""$TAG"
fi

SERVICE=kafkalens_grpc_api

echo "Building and pushing" $SERVICE

docker build -f Dockerfile_grpcApi . -t $SERVICE:"$TAG"

../tcc/common/scripts/release_docker_to_artifactory.sh pravin.chaudhary AKCp5emG5o82vbeocyNmCAZ83UDaAqBbitEr7XYf1LBoKMPsFqAy2BvKadf6qGMMxxMYBvvur $SERVICE $TAG