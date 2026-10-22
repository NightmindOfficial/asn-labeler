# ZXing.Net 0.16.11

Unmodified .NET Framework 4.0 assembly from the official NuGet package:
https://api.nuget.org/v3-flatcontainer/zxing.net/0.16.11/zxing.net.0.16.11.nupkg

Upstream: https://github.com/micjahn/ZXing.Net/tree/v0.16.11.0
Copyright Michael Jahn and ZXing contributors. License: Apache-2.0 (LICENSE).

SHA256 of zxing.dll:
643A5A3DB0AE02998B507BEB82DBC362A2D5593B429963DB17EB78089AABB95A

The build references and embeds this assembly and its license. Runtime loading is
in-memory, without extracting or installing a DLL. No NuGet restore or network is
needed to build with the vendored file. The app exposes the license via --licenses.

Release source packages include the matching source-v0.16.11.0.zip archive:
https://codeload.github.com/micjahn/ZXing.Net/zip/refs/tags/v0.16.11.0
Run windows-app/tools/fetch-zxing-source.ps1 to download and verify it before
packaging. It is excluded from Git and is not embedded in the executable.
