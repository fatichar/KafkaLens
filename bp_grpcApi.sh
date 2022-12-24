# if [ -z "$TAG" ]
# then
#     TAG=$({ date '+%Y%m%d%H%M%S' && echo - && git rev-parse --short HEAD && echo - && git branch | grep \* | cut -d ' ' -f2; } | tr -d '\n')
#     echo "Using TAG: "$TAG
# fi

# SERVICE=kafkalens_grpc_api

# echo "Building and deploying" $SERVICE

TAG=20221208205859-14824b9-grpc

# docker build -f Dockerfile_grpcApi . -t $SERVICE:$TAG

../../tarana/projects/common/scripts/release_docker_to_artifactory.sh pravin.chaudhary AKCp5emG5o82vbeocyNmCAZ83UDaAqBbitEr7XYf1LBoKMPsFqAy2BvKadf6qGMMxxMYBvvur $SERVICE $TAG
