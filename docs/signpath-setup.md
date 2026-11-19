# Signing releases with SignPath (free for open source)

[SignPath Foundation](https://signpath.org/) provides free Authenticode code
signing for open-source projects. A signed release is the single most effective
fix for SmartScreen "unknown publisher" warnings and for the reputation-based
engines in antivirus products; it is also what this project's
[Virus false positives](../README.md#virus-false-positives) section points at.

Signing is **off by default** in this repository; CI builds and releases work
unsigned until an administrator enables it (see below).

## One-time setup (maintainer)

1. Apply at <https://about.signpath.org/product-foundation> (or
   <https://signpath.org/> → *Foundation* → *Apply for free signing*) with:
   - the repository URL: `https://github.com/ziyyhhk/Hide-Any-Apps`
   - your contact details and a short description of the project
2. After approval, create (using the SignPath web console, signed in with the
   GitHub identity you used for the application):
   - an **Organization** (usually auto-created for you) and note its
     *organization id*;
   - a **Project** (e.g. `hide-any-apps`) and note its *project slug*;
   - a **Signing policy** (e.g. `release-signing`) and note its slug;
   - an **Artifact configuration** whose root element is `pe-file` (used for
     `HideAnyWindowService.exe`, `HideAnyWindowManager.exe` and
     `HideAnyApps-Setup.exe`) and note its slug.
3. In GitHub → *Settings → Secrets and variables → Actions* for this
   repository, create:

   | Kind | Name | Value |
   |---|---|---|
   | Secret | `SIGNPATH_API_TOKEN` | API token of a SignPath user with submitter permissions |
   | Variable | `SIGNPATH_ORG_ID` | your organization id |
   | Variable | `SIGNPATH_PROJECT_SLUG` | `hide-any-apps` (whatever you chose) |
   | Variable | `SIGNPATH_SIGNING_POLICY_SLUG` | `release-signing` |
   | Variable | `SIGNPATH_PE_CONFIGURATION_SLUG` | your pe-file artifact configuration slug |
   | Variable | `SIGNPATH_ENABLED` | `true` |

4. Push the next version tag (e.g. `git tag v1.0.3 && git push origin v1.0.3`).
   The [release workflow](../.github/workflows/release.yml) builds the three
   binaries, signs them **in order** (service → manager → installer, so Inno
   packages already-signed binaries), generates `SHA256SUMS.txt`, and publishes
   the release with both files attached.

## Notes

- Releases created **before** `SIGNPATH_ENABLED=true` stay unsigned; SmartScreen
  reputation accumulates per publisher certificate, so the first signed release
  may still warn on download counts that are too low. That is normal and fades
  within days-to-weeks of signed releases accumulating downloads.
- The AHK service exe builds from the portable (unsigned) AutoHotkey base
  shipped by the official GitHub release, so its PE has no stale certificate
  table and can be signed without any fixup step.
- Do **not** add your own self-signed certificate to `TrustedPublisher` or
  `Root` stores to work around this. The legacy `HideAnyWindowSetup.iss` that
  did exactly that was removed in v1.0.2.
