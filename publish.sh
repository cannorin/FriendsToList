#!/bin/bash
dotnet publish -c Release --self-contained --runtime win-x64 -o bin/publish/windows
dotnet publish -c Release --self-contained --runtime linux-x64 -o bin/publish/linux
dotnet publish -c Release --self-contained --runtime osx-x64 -o bin/publish/osx

