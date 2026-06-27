# Releasing TherionProc (REL-04)

## Release channel

Pushing a `v*` tag (e.g. `v0.3.0`) triggers [`.github/workflows/release.yml`](../.github/workflows/release.yml),
which builds **self-contained, single-file** publishes for:

- `win-x64`
- `linux-x64`
- `osx-arm64`

Each is archived (`.zip` on Windows, `.tar.gz` on Unix), uploaded as a workflow artifact, and — for
tag builds — attached to the corresponding GitHub Release.

```bash
git tag v0.3.0
git push origin v0.3.0
```

`workflow_dispatch` lets you produce the artifacts without cutting a release.

## Code signing & notarization (manual / secrets-gated)

Signing requires certificates that must live in CI secrets, so it is intentionally **not** wired
into the workflow above. Add it per platform when certs are available:

- **Windows** — Authenticode sign the published `.exe` with `signtool` using a code-signing cert
  (`AzureSignTool` works well with a cert in Key Vault). Optionally wrap in an MSI/MSIX.
- **macOS** — sign with a Developer ID Application cert (`codesign --deep --options runtime`), then
  notarize with `xcrun notarytool` and `staple`. Package as a `.dmg`.
- **Linux** — package as an `AppImage` (or `.deb`/`.rpm`); optionally GPG-sign the artifact.

Store the cert/password as repo secrets and add a signing step after *Publish* and before *Archive*.

## Auto-update

The app is distributed as self-contained builds, so an updater only needs to compare versions and
fetch the newer archive:

1. On launch (opt-in), query the GitHub Releases API (`/releases/latest`) and compare its tag to the
   running assembly version.
2. If newer, surface a notification (the UX-07 toast/bell center is the natural surface) linking to
   the release, or download + swap on next restart.

For a turnkey updater, [Velopack](https://github.com/velopack/velopack) (successor to Squirrel)
integrates with this single-file publish model and handles delta updates and the restart dance; it
can be added later without changing the release workflow's artifacts.
