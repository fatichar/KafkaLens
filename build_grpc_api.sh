if [ -z "$TAG" ]
then
   TAG=$({ date '+%Y%m%d%H%M%S' && echo - && git rev-parse --short HEAD && echo - && git branch | grep \* | cut -d ' ' -f2; } | tr -d '\n')
   echo "Using TAG: "$TAG
fi

docker build -f Dockerfile_grpcApi . -t kafkalens_grpc_api:"$TAG"