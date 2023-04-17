#! /bin/bash -x

dotnet publish AvaloniaApp.Desktop.csproj -c Release --sc -r linux-x64 -p:PublishTrimmed=false -o ../../Releases/KafkaLens_lin/