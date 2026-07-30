# Release Setup Guide

How to configure GitHub Actions so that publishing a GitHub Release builds,
signs, and ships BuzzahBuddy to TestFlight and the Play Console internal track.

The workflow is [`.github/workflows/release.yml`](../.github/workflows/release.yml).
It runs on `release: published`, derives the version from the tag, and produces:

| Platform | Artifact | Destination |
| --- | --- | --- |
| Android | signed `.aab` | Play Console **internal testing** track + GitHub Release asset |
| iOS | signed `.ipa` | **TestFlight** + GitHub Release asset |

Promotion from internal testing / TestFlight to production is manual, in each
console. The workflow never touches a production track.

---

## 0. Prerequisites

Do these once, before the first CI release. **The workflow cannot do them for you.**

### Decide the bundle ID — this is irreversible

The project currently declares `com.rbonestell.buzzahbuddy`
(`BuzzahBuddy/BuzzahBuddy.csproj`). Once an app is submitted under a bundle ID,
that ID is permanently bound to that store listing. The repository now lives
under the BlueBuzzah organization; if the app should ship under a BlueBuzzah
developer account rather than a personal one, change `<ApplicationId>` **now**.
Changing it later means a new listing with no reviews, no ratings, and no
upgrade path for installed users.

### Create the store records and do one manual upload

Both stores require a first release by hand:

- **Google Play** — the Developer API refuses to create the initial release for
  an app that has never had a bundle uploaded. Build an AAB locally, upload it
  through the Play Console web UI once, then CI takes over.
- **App Store Connect** — an app record must exist (My Apps → **+** → New App,
  using the bundle ID above) before `altool` will accept an upload.

Local commands for that first manual build are in [§5](#5-building-locally).

---

## 1. Android signing secrets

You already have the upload keystore. Extract what the workflow needs.

### Confirm the alias

```bash
keytool -list -keystore upload.keystore
```

Note the alias name — it is the `ANDROID_KEY_ALIAS` value. Many keystores use
the same password for the store and the key; if yours does, both password
secrets get the same value.

### Base64-encode the keystore

GitHub secrets hold text, so the binary keystore has to be encoded:

```bash
# macOS
base64 -i upload.keystore | pbcopy

# Linux
base64 -w0 upload.keystore
```

> `-w0` on Linux matters — without it, `base64` wraps at 76 columns. The
> workflow's `base64 --decode` tolerates newlines, but wrapped output is easy to
> truncate when pasting.

### Secrets to set

| Secret | Value |
| --- | --- |
| `ANDROID_KEYSTORE_BASE64` | output of the command above |
| `ANDROID_KEYSTORE_PASSWORD` | keystore (store) password |
| `ANDROID_KEY_ALIAS` | alias from `keytool -list` |
| `ANDROID_KEY_PASSWORD` | key password (often the same as the store password) |

**Back up the keystore somewhere outside GitHub.** If you are enrolled in Play
App Signing, losing the upload key is recoverable via a Google support request.
If you are not, losing it means you can never update the app again.

---

## 2. Google Play service account

The workflow authenticates to the Play Developer API with a service account.

1. **Play Console** → **Setup** → **API access**. If prompted, link a Google
   Cloud project (create one if you have none).
2. Follow the link into the **Google Cloud Console** → **IAM & Admin** →
   **Service Accounts** → **Create service account**. Name it something like
   `buzzahbuddy-ci`. No Google Cloud roles are needed — permissions are granted
   on the Play side.
3. On the new service account: **Keys** → **Add key** → **Create new key** →
   **JSON**. The file downloads once and cannot be re-downloaded.
4. Back in the **Play Console** → **Users and permissions** → **Invite new
   user**, paste the service account email (`...@....iam.gserviceaccount.com`).
   Under **App permissions**, add BuzzahBuddy. Grant at minimum:
   - **Release to testing tracks**
   - **View app information**
5. Wait a few minutes. Play permission changes are not instant, and a fresh
   service account commonly returns `401` on its first API call.

| Secret | Value |
| --- | --- |
| `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON` | the **entire contents** of the downloaded JSON file |

Paste the raw JSON — do not base64-encode it. The workflow passes it to
`serviceAccountJsonPlainText`.

---

## 3. Apple signing

Three separate things are needed, and they are easy to confuse: a **certificate**
(proves who built it), a **provisioning profile** (ties the cert to a bundle ID
and a distribution channel), and an **API key** (authorizes the upload).

### 3a. Distribution certificate → `.p12`

On a Mac:

1. **Keychain Access** → menu **Keychain Access** → **Certificate Assistant** →
   **Request a Certificate From a Certificate Authority**. Enter your email,
   select **Saved to disk**, and save the `.certSigningRequest` file.
2. [developer.apple.com](https://developer.apple.com/account/resources/certificates/list)
   → **Certificates** → **+** → **Apple Distribution** → upload the CSR →
   download the resulting `.cer`.
3. Double-click the `.cer` to install it into your login keychain.
4. In Keychain Access, find the certificate, **expand it** so the private key is
   included in the selection, right-click → **Export 2 items…** → save as
   `.p12` and set an export password.

   > Exporting the certificate *without* its private key produces a `.p12` that
   > imports cleanly in CI and then fails at `codesign` time. If the disclosure
   > triangle next to the certificate does not expand, the private key is not in
   > this keychain and you must export from the machine that generated the CSR.

5. Encode it:

   ```bash
   base64 -i Certificates.p12 | pbcopy
   ```

### 3b. App ID and provisioning profile

1. [developer.apple.com](https://developer.apple.com/account/resources/identifiers/list)
   → **Identifiers** → **+** → **App IDs** → **App**. Set the Bundle ID to an
   explicit `com.rbonestell.buzzahbuddy` (not a wildcard). No capabilities need
   enabling — Core Bluetooth central requires only the `Info.plist` usage
   strings, which the project already has.
2. **Profiles** → **+** → **Distribution → App Store Connect** → select the App
   ID → select the Apple Distribution certificate → **name the profile**. Write
   that name down verbatim; it becomes `APPLE_PROVISIONING_PROFILE_NAME`.
3. Download the `.mobileprovision` and encode it:

   ```bash
   base64 -i BuzzahBuddy_AppStore.mobileprovision | pbcopy
   ```

### 3c. Codesign identity string

`APPLE_CODESIGN_IDENTITY` must exactly match the certificate's common name.
With the certificate installed locally:

```bash
security find-identity -v -p codesigning
```

Copy the quoted name, e.g. `Apple Distribution: Bobby Bonestell (AY2GDE9QM7)`.

### 3d. App Store Connect API key

1. [App Store Connect](https://appstoreconnect.apple.com/access/integrations/api)
   → **Users and Access** → **Integrations** → **App Store Connect API**.
2. Note the **Issuer ID** shown at the top of the page.
3. Under **Team Keys** (not *Individual Keys* — those need different filename
   and flag handling that this workflow doesn't do), **+** to generate a key.
   Role: **App Manager** (sufficient for TestFlight uploads; **Admin** is not
   required).
4. Download the `.p8`. **It can only be downloaded once.** Note the **Key ID**
   from the table.

---

## 4. Setting the secrets and variables

Note that two values are repository **variables**, not secrets — deliberately.
GitHub masks secret values everywhere in logs, and masking the signing identity
turns every codesign failure into an unreadable `***`. Neither value is
sensitive: the certificate name and profile name are useless without the private
key.

### Via the web UI

**Settings** → **Secrets and variables** → **Actions**, then the **Secrets** and
**Variables** tabs respectively.

### Via the `gh` CLI

Run these from the repository root, in a directory holding the credential files
you gathered above. (The repo root is itself named `BuzzahBuddy` and contains a
nested project folder of the same name — don't `cd` into it.)

```bash
# Android
gh secret set ANDROID_KEYSTORE_BASE64 < <(base64 -i upload.keystore)
gh secret set ANDROID_KEYSTORE_PASSWORD
gh secret set ANDROID_KEY_ALIAS
gh secret set ANDROID_KEY_PASSWORD
gh secret set GOOGLE_PLAY_SERVICE_ACCOUNT_JSON < play-service-account.json

# Apple signing
gh secret set APPLE_CERTIFICATE_P12 < <(base64 -i Certificates.p12)
gh secret set APPLE_CERTIFICATE_PASSWORD
gh secret set APPLE_PROVISIONING_PROFILE_BASE64 < <(base64 -i BuzzahBuddy_AppStore.mobileprovision)

# App Store Connect API
gh secret set APPSTORE_KEY_ID
gh secret set APPSTORE_ISSUER_ID
gh secret set APPSTORE_PRIVATE_KEY < AuthKey_XXXXXXXXXX.p8

# Non-secret configuration
gh variable set APPLE_CODESIGN_IDENTITY --body "Apple Distribution: Your Name (TEAMID)"
gh variable set APPLE_PROVISIONING_PROFILE_NAME --body "BuzzahBuddy App Store"
```

Commands without a value or redirect prompt for the value interactively, which
keeps it out of your shell history.

### Full checklist

**Secrets**

- [ ] `ANDROID_KEYSTORE_BASE64`
- [ ] `ANDROID_KEYSTORE_PASSWORD`
- [ ] `ANDROID_KEY_ALIAS`
- [ ] `ANDROID_KEY_PASSWORD`
- [ ] `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`
- [ ] `APPLE_CERTIFICATE_P12`
- [ ] `APPLE_CERTIFICATE_PASSWORD`
- [ ] `APPLE_PROVISIONING_PROFILE_BASE64`
- [ ] `APPSTORE_KEY_ID`
- [ ] `APPSTORE_ISSUER_ID`
- [ ] `APPSTORE_PRIVATE_KEY`

**Variables**

- [ ] `APPLE_CODESIGN_IDENTITY`
- [ ] `APPLE_PROVISIONING_PROFILE_NAME`

---

## 5. Building locally

For the required first manual upload, and for reproducing a CI failure.

The Android command reads its passwords from files, the same way the workflow
does, so they stay out of your shell history and the build log. Create them
first, and delete them when you're done:

```bash
printf '%s' 'YOUR_STORE_PASSWORD' > store.pass
printf '%s' 'YOUR_KEY_PASSWORD'   > key.pass
chmod 600 store.pass key.pass
```

```bash
# Android AAB → bin/Release/net9.0-android/publish/*-Signed.aab
dotnet publish BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-android -c Release \
  -p:TargetFrameworks=net9.0-android \
  -p:AndroidPackageFormats=aab \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=$PWD/upload.keystore \
  -p:AndroidSigningKeyAlias=YOUR_ALIAS \
  -p:AndroidSigningKeyPass=file:$PWD/key.pass \
  -p:AndroidSigningStorePass=file:$PWD/store.pass

# iOS IPA → bin/Release/net9.0-ios/ios-arm64/publish/*.ipa  (macOS only)
dotnet publish BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-ios -c Release \
  -p:TargetFrameworks=net9.0-ios \
  -p:ArchiveOnBuild=true \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:CodesignKey="Apple Distribution: Your Name (TEAMID)" \
  -p:CodesignProvision="BuzzahBuddy App Store"
```

Upload the `.ipa` with [Transporter](https://apps.apple.com/us/app/transporter/id1450874784)
and the `.aab` through the Play Console web UI.

> **These commands ship build number 2**, the csproj's `ApplicationVersion`.
> That number is now permanently burned on both stores, which is why
> `BUILD_OFFSET` in `release.yml` is `2` — it keeps CI's first build (run 1 →
> `1 + 2 = 3`) above it. If you override `-p:ApplicationVersion=` here, or
> upload by hand again later, raise `BUILD_OFFSET` to match. See [§6](#6-cutting-a-release).

`-p:TargetFrameworks=` is not optional. The project multi-targets
`net9.0;net9.0-android;net9.0-ios`, and restore will try to resolve workload
packs for every target framework unless pinned — failing with `NETSDK1147` on
whichever platform's workload is not installed.

---

## 6. Cutting a release

```bash
git tag v0.2.0
git push origin v0.2.0
gh release create v0.2.0 --generate-notes
```

Or create the release through the GitHub UI. Publishing it (not drafting it)
fires the workflow.

**Versioning.** The tag sets the user-visible version; the workflow run number
plus `BUILD_OFFSET` sets the build number. The `0.1.1` / `2` values in the
csproj become defaults for local builds only — CI overrides both.

- Tag `v0.2.0` → `ApplicationDisplayVersion=0.2.0`
- Run #7, `BUILD_OFFSET: 2` → `ApplicationVersion=9`

`BUILD_OFFSET` must stay **above the highest build number ever accepted outside
CI**. It ships at `2` because the mandatory manual bootstrap ([§0](#0-prerequisites),
[§5](#5-building-locally)) uploads with the csproj's `ApplicationVersion=2`. At
`0` the first CI release would compute build 1 and be rejected by both stores.

Tags must be one to three dot-separated integers after the optional leading `v`.
`v0.2.0-beta1` fails fast in the `version` job rather than 15 minutes later
inside App Store Connect.

**If a build number gets burned outside CI** — a manual upload, a workflow file
rename that resets `run_number` — bump `BUILD_OFFSET` at the top of
`release.yml`. Both stores permanently reject any build number less than or
equal to one already accepted.

---

## 7. Before you trust the first CI build

The project sets `PublishTrimmed=true` with `TrimMode=partial` for Release
configuration only (`BuzzahBuddy.csproj`), and CI's `build.yml` only ever builds
Debug. **No trimmed build of this app has been exercised.** Trimming plus
Plugin.BLE plus MAUI's reflection-based XAML is the classic source of "works in
the simulator, crashes on launch in TestFlight."

Install the first TestFlight build and the first internal-track build on real
hardware and exercise BLE scan → connect → run a therapy profile before
promoting anything to production.

---

## 8. Troubleshooting

| Symptom | Cause |
| --- | --- |
| `NETSDK1147: To build this project, the following workloads must be installed` | The `-p:TargetFrameworks=` pin is missing from a publish command. |
| No `*-Signed.aab` in `publish/` | `AndroidKeyStore=true` did not take effect, or the keystore path is wrong. Publish always emits an unsigned AAB alongside the signed one — the workflow's "Locate signed AAB" step fails deliberately rather than upload the wrong file. |
| Keystore password appears in the build log | A bare `-p:AndroidSigningStorePass=<value>` was used. The `env:` prefix is unsupported when the package format is `aab`; only `file:` keeps it out of the log. |
| `errorCode: 401` from the Play upload step | Service account permissions not yet propagated, or the account was never invited in **Users and permissions**. Wait and retry. |
| Play upload fails on a brand-new app | The first bundle must be uploaded through the Play Console UI. See [§0](#0-prerequisites). |
| `No signing certificate "iOS Distribution" found` | The `.p12` was exported without its private key, or `APPLE_CODESIGN_IDENTITY` does not match the certificate's common name exactly. |
| `Provisioning profile ... doesn't match the bundle identifier` | The profile was created for a different App ID than `<ApplicationId>` in the csproj. |
| `altool` rejects `CFBundleShortVersionString` | The tag was not 1–3 dot-separated integers. The `version` job should have caught this — check it ran. |
| `The provided entity includes an attribute with a value that has already been used` (iOS) or `Version code N has already been used` (Android) | Build number reused. Raise `BUILD_OFFSET` in `release.yml` above the highest number either store has accepted, then re-publish the release. |
| Play Console warns "native code without debug symbols" | `AndroidGenerateNativeDebugSymbols` isn't set, so native crashes and ANRs won't symbolicate. Cosmetic today — the app is almost entirely managed .NET, whose stack traces are unaffected. Enable it if native crash reports ever matter. |
| iOS build passes, app crashes on launch from TestFlight | Almost certainly trimming. See [§7](#7-before-you-trust-the-first-ci-build). |
| `altool` authentication fails despite correct Key ID / Issuer ID | You generated an **Individual Key** instead of a **Team Key**. Individual keys require the file to be named `ApiKey_<id>.p8` and an extra `--api-key-subject user` flag. Regenerate as a Team Key under **Integrations → App Store Connect API → Team Keys**. |
| `altool: --upload-app is not a recognized option` | Apple removed the deprecated alias. Switch to `--upload-package`, which additionally needs `--bundle-id`, `--bundle-version`, `--bundle-short-version-string`, and the app's numeric `--apple-id` from App Store Connect. `notarytool` is **not** a substitute — it does notarization only and cannot upload. |
| Build lands in TestFlight but is stuck on "Missing Compliance" | `ITSAppUsesNonExemptEncryption` missing from `Platforms/iOS/Info.plist`. It is set to `false` there — BLE plus OS TLS only, no custom crypto. If the key is gone, every build needs the export-compliance question answered by hand before testers can install. |
