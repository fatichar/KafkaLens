#! /bin/bash -x

dotnet publish AvaloniaApp.Desktop.csproj -c Release --sc -r win-x64 -p:PublishTrimmed=false -o ../../Releases/Windows/