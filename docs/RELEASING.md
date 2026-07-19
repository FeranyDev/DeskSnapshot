# DeskSnapshot release and signing guide

DeskSnapshot uses one reusable self-signed code-signing certificate for local official packages and GitHub Releases. Reusing that certificate keeps the Publisher and signer thumbprint stable across official upgrades. Ordinary Build artifacts use a disposable per-run certificate and are not official release packages.

The configured certificate Subject and default MSIX Publisher are `CN=DeskSnapshot`. The application display name remains `DeskSnapshot`; the Subject is the package signing identity.

Self-signing does not provide public trust. Users must trust the included public `.cer` once before installing the MSIX. The private key must never be committed, uploaded as an artifact, printed in logs, or attached to a Release.

## Initialize the certificate once

Install and authenticate GitHub CLI, then run:

```powershell
./scripts/initialize-signing-certificate.ps1 -ConfigureGitHub
```

The script performs the sensitive setup without printing the private key:

1. Creates one exportable RSA-3072/SHA-256 code-signing certificate in `Cert:\CurrentUser\My`, or reuses the previously configured certificate.
2. Saves only its thumbprint and public `.cer` under `%LOCALAPPDATA%\DeskSnapshot\Signing`.
3. Prompts for a strong PFX password.
4. Exports a temporary PFX outside the repository.
5. Sends its Base64 content and password through standard input to encrypted GitHub Environment Secrets.
6. Clears the byte buffer and deletes the temporary PFX in a `finally` block.

The encrypted Secrets are stored in the `signing` Environment:

- `DESKSNAPSHOT_SIGNING_CERTIFICATE_BASE64`
- `DESKSNAPSHOT_SIGNING_CERTIFICATE_PASSWORD`

The same environment also stores the non-secret variable `DESKSNAPSHOT_SIGNING_CERTIFICATE_THUMBPRINT`. The Release job rejects a PFX whose thumbprint differs from this pinned local identity, preventing an accidental certificate replacement from silently changing the official signer.

Configure the `signing` Environment with required reviewers and deployment restrictions for `main` and protected `v*.*.*` tags. Only `release.yml` uses this environment; ordinary pushes, Build jobs, and pull requests cannot access the reusable private key.

Back up the certificate securely outside the repository if loss recovery is required. Losing both the local private key and GitHub Secret means future packages cannot use the same signer.

## Local package

After initialization, no certificate path or password is needed locally:

```powershell
./scripts/publish.ps1 -Mode All -Version 1.0.0 -Clean
```

The script loads the fixed thumbprint from `%LOCALAPPDATA%`, derives the MSIX Publisher from the certificate Subject, and signs `DeskSnapshot.exe`, `DeskSnapshot.dll`, and the MSIX with SHA-256 plus an RFC 3161 timestamp. It fails closed when the certificate is missing or expired.

Unsigned output must be explicitly requested and must not be published:

```powershell
./scripts/publish.ps1 -Mode All -Version 1.0.0 -Clean -AllowUnsigned
```

## GitHub Actions and Releases

For pushes to `main`, `build.yml` creates an RSA-3072/SHA-256 code-signing certificate in memory with the required `CN=DeskSnapshot` Publisher, exports it only to the runner temporary directory with a masked random password, signs and verifies the test packages, and deletes the PFX in an `always()` step. This job does not reference the `signing` Environment or reusable signing Secrets. Its artifacts have a different signer on every run and must not be presented as official releases or relied on for upgrade identity.

For `vMAJOR.MINOR.PATCH` tags, `release.yml`:

1. Runs all unit tests.
2. Restores, validates, imports, and immediately deletes the same temporary PFX from the protected `signing` Environment.
3. Builds and signs the Portable EXE/DLL and MSIX.
4. Verifies every signature, file hash, and signer thumbprint without modifying the runner's system root store.
5. Creates the Portable ZIP, MSIX, public CER, and `SHA256SUMS.txt`.
6. Generates categorized release notes and publishes the files to GitHub Releases.
7. Removes the temporary PFX even when an earlier step fails.

Publish with:

```powershell
git tag -a v1.0.0 -m "DeskSnapshot 1.0.0"
git push origin v1.0.0
```

The public CER is intentionally included in the Release so users can establish trust. It contains no private key. The PFX is never uploaded as an Actions artifact or Release asset.

## Rotation and recovery

- Do not rerun initialization with a different Subject merely to package a new version; reuse the existing thumbprint.
- Renew or rotate deliberately before expiry. A new self-signed certificate requires users to trust the new public certificate.
- Keep `main`, release tags, workflows, and the `signing` Environment protected because approved workflows can use the signing key.
- If a GitHub Secret is suspected to be exposed, delete both signing Secrets immediately, remove affected releases, and rotate the certificate.

To deliberately replace an existing certificate and update GitHub atomically, run:

```powershell
./scripts/initialize-signing-certificate.ps1 -Subject "CN=DeskSnapshot" -Rotate -ConfigureGitHub
```

The script retains the previous local certificate for rollback, but switches the local configuration, public CER, GitHub PFX Secrets, and pinned thumbprint to the new certificate only after remote configuration succeeds. Remove the old certificate manually only after signed local and remote packages have been verified.
