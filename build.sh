#!/usr/bin/env sh
dotnet build
cp bin/Debug/netstandard2.1/com.rhythmdr.multiplayer.dll out/com.rhythmdr.multiplayer.dll
dotnet build /p:BPE5=1
cp bin/Debug/netstandard2.1/com.rhythmdr.multiplayer.dll out/com.rhythmdr.bpe5multiplayer.dll
