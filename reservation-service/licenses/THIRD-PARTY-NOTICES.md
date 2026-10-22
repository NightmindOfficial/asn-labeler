# Third-party components

The project's AGPL-3.0-only license applies to original project material, not
to the third-party components below. Preserve their notices and licenses.

| Component | Version | License / source |
| --- | --- | --- |
| ZXing.Net | 0.16.11 | Apache-2.0; Copyright Michael Jahn and ZXing contributors; https://github.com/micjahn/ZXing.Net/tree/v0.16.11.0 |
| Material Icons | Vendored font; SHA-256 recorded in vendor README | Apache-2.0; Google; https://github.com/google/material-design-icons |
| Waitress | 3.0.2 | Zope Public License 2.1; https://github.com/Pylons/waitress/tree/v3.0.2 |
| Brother b-PAC and printer drivers | Installed separately | Proprietary Brother terms; not included in source or application packages |

The Windows application embeds the unchanged ZXing assembly and Material Icons
font, together with their complete license texts. Copies of those texts remain
under windows-app/vendor. The ZXing source archive is provided alongside its
binary for inspection and rebuilding; its original Apache license is unchanged.

The service installs Waitress from the pinned requirements file. Its upstream
license is included in the service's licenses directory. The Python base image
and its operating-system packages carry their own upstream notices and licenses.

Windows/.NET and Windows fonts are system prerequisites, not bundled here.
Generated label images and user documents do not acquire the program's license
merely because they were processed by it.

The b-PAC additional permission covers only our client code. It does not grant
permission to redistribute Brother DLLs, installers, example files, or trademarks.
The current build creates label templates in code and does not package SDK files.
Before publishing, verify provenance of any future imported examples/assets and
review the exact Brother license applicable to the installed SDK/runtime.
