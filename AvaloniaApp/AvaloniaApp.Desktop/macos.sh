#! /bin/bash -x

dotnet publish AvaloniaApp.Desktop.csproj -c Release --sc -r osx-arm64 -p:PublishTrimmed=false -o ../../Releases/KafkaLens_mac/