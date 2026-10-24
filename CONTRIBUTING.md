# Contributing

Focused bug reports and pull requests are welcome. Start with
[GitHub Issues](https://github.com/NightmindOfficial/asn-labeler/issues).
Never include credentials, private documents or an unredacted `.env`.

## Release Planning

Before each hotfix/minor/major, review open issues and their component
(`Client` / `Server`) and target-release labels. Use the actual labels in the
repository and agree on scope. Client and server versions are independent.
See [Releasing](docs/RELEASING.md).

## Development and Tests

From the repository root on Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windows-app/build.ps1
./windows-app/dist/asn-labeler-test.exe --self-test
./windows-app/dist/asn-labeler-test.exe --development
```

The build uses the x64 .NET Framework compiler and checksum-verifies vendored
dependencies. The development executable is not a release build.

Server development uses Python 3.12:

```powershell
python -m venv .venv
./.venv/Scripts/python.exe -m pip install --require-hashes -r reservation-service/requirements.txt
./.venv/Scripts/python.exe reservation-service/package_source.py --output reservation-service/source.zip
./.venv/Scripts/python.exe -m unittest discover -s reservation-service/tests
./.venv/Scripts/python.exe -m unittest discover -s tools/tests
./.venv/Scripts/python.exe -m unittest discover -s windows-app/tests -p test_release_packaging.py
```

On Linux use `.venv/bin/python`. Tests use temporary databases and mock Paperless
responses. Hardware tests remain separate. CI also checks Docker, the Windows
installer and the admin interface with simulated browser responses.

## Change Checklist

- Add regression tests; preserve ASN ownership and uncertain-print safeguards.
- Check UI text/layout in German and English. Use synthetic screenshot data.
- State `.env` changes explicitly: keys added/removed/renamed, changed defaults,
  required action and compatibility. If unchanged, say so.
- Update component changelogs. Do not bump an unrelated component for docs alone.
- Keep private notes and credentials out of commits; preserve license notices.

Contributions use the project's [license](LICENSE) and applicable
[b-PAC additional permission](LICENSE-BPAC-EXCEPTION.txt).
