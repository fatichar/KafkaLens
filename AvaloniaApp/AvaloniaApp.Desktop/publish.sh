#! /bin/bash -x

dotnet publish AvaloniaApp.Desktop.csproj -c Release --sc --os=linux -a x64 -o ../../Releases/Linux/