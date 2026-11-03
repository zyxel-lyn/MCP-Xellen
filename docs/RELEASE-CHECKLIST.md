# Release readiness checklist

This checklist separates a public source publication from a tested, beginner-friendly binary release.

## Completed in the initial public commit

- [x] Windows Forms launcher source is in the repository.
- [x] Local paths and the maintainer's Funnel hostname are not hard-coded in the public source.
- [x] User-specific endpoint is read from local `config.json`.
- [x] Installer pins Desktop Commander and Supergateway versions.
- [x] Installer generates a per-installation 256-bit token.
- [x] Secrets, local configuration, logs, and build output are excluded from Git.
- [x] README and Bahasa Indonesia installation guide are available.
- [x] Security limitations and third-party components are documented.
- [x] Local .NET publish succeeded.
- [x] GitHub Actions Windows x64 build completed successfully.

## Required before calling it a stable release

- [ ] Run the full installer on a clean Windows 10/11 x64 machine.
- [ ] Confirm `npm ci` installs the pinned dependencies without manual repair.
- [ ] Launch the installed app and verify local `/healthz` returns success.
- [ ] Confirm an unauthenticated MCP request returns HTTP 401.
- [ ] Configure a fresh Tailscale account/tailnet and Funnel hostname.
- [ ] Verify HTTPS Funnel access from a separate network.
- [ ] Complete Z.ai Website Agent Mode `initialize`, `notifications/initialized`, and `tools/list`.
- [ ] Test one harmless read-only Desktop Commander tool call.
- [ ] Verify token rotation and restart behavior.
- [ ] Package a downloadable Windows release and publish checksums.
- [ ] Add a version tag and GitHub Release notes.
- [ ] Review dependency licenses and update third-party notices.

## Suggested release sequence

1. Complete the clean-machine installation test.
2. Fix any installer or runtime issues found.
3. Create a self-contained or runtime-documented Windows x64 release package.
4. Publish a version tag such as `v0.1.0`.
5. Attach the ZIP and SHA-256 checksum to the GitHub Release.
6. Update the README download instructions and supported version.
7. Ask an external tester to follow the Indonesian and English guides without maintainer assistance.

Until these steps are complete, describe the project as an early public source release, not a stable one-click installer.
