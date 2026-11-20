# WinPlayer

A Windows video player (WPF + libmpv) with dual, independently styled subtitles.

## Build

`libmpv-2.dll` is not stored in the repo (it is larger than GitHub's 100 MB file limit).

1. Download a Windows x64 libmpv build (e.g. `mpv-dev-x86_64` from https://sourceforge.net/projects/mpv-player-windows/files/libmpv/).
2. Put `libmpv-2.dll` in `src/native/`.
3. `dotnet build src` (or publish a single file:
   `dotnet publish src -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`).
