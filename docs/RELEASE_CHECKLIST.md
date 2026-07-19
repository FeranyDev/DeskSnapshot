# DeskSnapshot release checklist

Use this checklist for every public release. A checked item should be backed by a workflow result or a maintainer verification, not assumption.

## Source and version

- [ ] The release commit is on protected `main`, reviewed, and has a clean worktree.
- [ ] `CHANGELOG.md`, `README.md`, `ROADMAP.md`, and user-facing Chinese/English resources reflect the release.
- [ ] The tag uses numeric `vMAJOR.MINOR.PATCH` and points to the intended commit.
- [ ] No backup JSON, personal paths, generated artifacts, PFX/P12/PVK/SNK, passwords, or Base64 private-key material is tracked.

## Quality

- [ ] All MSTest tests pass in Release configuration.
- [ ] Debug and Release x64 builds complete without errors or warnings.
- [ ] Backup, preview, comparison, display profile, monitor mapping, restore safety backup, tray mode, normal startup, minimized startup, and language switching have been smoke-tested on Windows Explorer.
- [ ] Portable and MSIX start successfully on a clean test account.

## Signing

- [ ] The `signing` GitHub Environment requires approval and is restricted to protected `main`/release tags; only the Release workflow references it.
- [ ] `DESKSNAPSHOT_SIGNING_CERTIFICATE_BASE64` and `DESKSNAPSHOT_SIGNING_CERTIFICATE_PASSWORD` exist only as Environment Secrets.
- [ ] `DESKSNAPSHOT_SIGNING_CERTIFICATE_THUMBPRINT` matches `%LOCALAPPDATA%\DeskSnapshot\Signing\certificate.json`.
- [ ] The certificate Subject and packaged MSIX Publisher are both `CN=DeskSnapshot`.
- [ ] The certificate is not expired and has a planned rotation date.
- [ ] The Release workflow reports successful signature, hash, and pinned-thumbprint verification for `DeskSnapshot.exe`, `DeskSnapshot.dll`, and the MSIX.
- [ ] The PFX does not appear in Actions artifacts or release assets; only `DeskSnapshot-Signing.cer` is public.

## Release assets

- [ ] The Release contains one Portable ZIP, one MSIX, `DeskSnapshot-Signing.cer`, and `SHA256SUMS.txt`.
- [ ] Every listed SHA-256 matches the downloaded asset.
- [ ] The Portable and MSIX signatures have the pinned certificate thumbprint.
- [ ] A clean test machine can trust the CER and install or upgrade the MSIX.
- [ ] Generated release notes are accurate, categorized, and contain no private information.

## After publication

- [ ] The GitHub Release and Build badges are healthy.
- [ ] The published tag and assets are not replaced in place; corrections use a new patch version.
- [ ] The Release restore step deleted the temporary PFX immediately, and its cleanup step removed the imported private key.
- [ ] Known issues and support instructions are updated when necessary.
