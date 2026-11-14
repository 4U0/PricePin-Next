# Releasing PricePin

Releases are built by [`.github/workflows/release.yml`](../.github/workflows/release.yml) with
[Velopack](https://velopack.io). Installed copies of PricePin check GitHub Releases every few
hours, download new versions in the background and offer a restart.

## Publish a release

1. Set `<Version>` in [`Directory.Build.props`](../Directory.Build.props), for example `1.1.0`.
2. Add a `## 1.1.0` section to [`CHANGELOG.md`](../CHANGELOG.md). It becomes the release notes.
3. Merge to `main`, then tag and push:

   ```powershell
   git tag v1.1.0
   git push origin v1.1.0
   ```

   You can also run the **Release** workflow by hand from the Actions tab and type the version.

The workflow tests, builds x64 and ARM64, makes delta updates against the previous release,
and publishes one GitHub release containing:

| File | What it is |
| --- | --- |
| `PricePin-win-x64-Setup.exe`, `PricePin-win-arm64-Setup.exe` | Installer (per user, no admin) |
| `PricePin-win-x64-Portable.zip`, `PricePin-win-arm64-Portable.zip` | Portable copy, doesn't self-update |
| `*.nupkg`, `releases.*.json`, `RELEASES-*`, `assets.*.json` | Update packages. Leave these in place: installed copies read them |

A version with a suffix (`1.1.0-beta.1`) is published as a pre-release, which installed copies ignore.

Every pull request also builds a test installer (`PricePin-win-x64-Setup` under the run's
*Artifacts*) versioned like `1.0.0-ci.42`. It updates itself to the next real release.

## Code signing (optional)

Unsigned installers trigger a SmartScreen warning until they build up reputation. To sign:

1. Get a certificate. Open-source projects can apply for a free one from
   [SignPath Foundation](https://signpath.org/apply). Azure Trusted Signing or any
   code-signing certificate also works.
2. In the repository's *Settings → Secrets and variables → Actions → Variables*, add
   `VPK_SIGN_TEMPLATE`: a command that signs one file, with `{{file}}` where the path goes.
   Velopack runs it for `PricePin.exe`, its updater and the installer. For example, with a
   certificate available to signtool:

   ```text
   signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /n "Your Name" {{file}}
   ```

   SignPath and Azure Trusted Signing each document their own command-line signer that fits here.
   Any secrets the command needs should be added as repository secrets and passed in the
   *Build packages* step's `env`.

## winget (Windows Package Manager)

The first version has to be submitted by hand; after that the workflow can send each release.

1. Publish a release (above) so the installer URLs exist.
2. Install [wingetcreate](https://github.com/microsoft/winget-create) and run:

   ```powershell
   wingetcreate new https://github.com/4U0/PricePin-Next/releases/download/v1.0.0/PricePin-win-x64-Setup.exe `
                    https://github.com/4U0/PricePin-Next/releases/download/v1.0.0/PricePin-win-arm64-Setup.exe
   ```

   Use `4U0.PricePin` as the package identifier, `exe` as the installer type, `user` as the
   scope, and `--silent` as the silent install switch. Let it open the pull request to
   [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) and wait for it to be merged.
3. Fork `microsoft/winget-pkgs` to your account, create a classic personal access token with
   the `public_repo` scope, and save it as the repository secret `WINGET_TOKEN`.
4. Add the repository variable `WINGET_ENABLED` = `true`. If you picked a different
   identifier, also add `WINGET_ID`.

From then on the **Update winget** job opens the winget pull request for every stable release.
