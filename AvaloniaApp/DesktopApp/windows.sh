#! /bin/bash -x

dotnet publish AvaloniaApp.Desktop.csproj -c Release -r win-x64 --sc -p:PublishTrimmed=false -o ../../Releases/KafkaLens_win/

"/C/Program Files (x86)/Inno Setup 6/ISCC.exe" ../../Installer/install_windows.iss