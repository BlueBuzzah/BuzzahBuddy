#!/usr/bin/env bash
# Build, install, and launch BuzzahBuddy on a physical iPhone.
#
# Why not `dotnet build -t:Run`: that install step goes through mlaunch's legacy
# lockdown path, which hangs indefinitely on iOS 17+ devices. devicectl (CoreDevice)
# is the supported path and installs in seconds.
#
# Env overrides: IOS_UDID, IOS_PROVISION, IOS_CONFIG.
set -euo pipefail

cd "$(dirname "$0")"

BUNDLE_ID=com.rbonestell.buzzahbuddy
CONFIG=${IOS_CONFIG:-Debug}
PROVISION=${IOS_PROVISION:-BuzzahBuddy Development}
APP="BuzzahBuddy/bin/$CONFIG/net10.0-ios/ios-arm64/BuzzahBuddy.app"

# Physical iOS devices list as "NAME (os version) (UDID)"; the Mac has no version.
if [ -z "${IOS_UDID:-}" ]; then
    udids=$(xcrun xctrace list devices |
        sed -n '/== Devices ==/,/== Devices Offline ==/p' |
        grep -E '\([0-9]+\.[0-9.]*\) *\(' |
        sed -E 's/.*\((.*)\)$/\1/')
    count=$(printf '%s' "$udids" | grep -c . || true)
    if [ "$count" -ne 1 ]; then
        echo "Expected exactly 1 connected iOS device, found $count." >&2
        [ "$count" -gt 1 ] && echo "$udids" >&2
        echo "Connect the device (unlocked, trusted) or set IOS_UDID." >&2
        exit 1
    fi
    IOS_UDID=$udids
fi

# CodesignKey is deliberately omitted: the provisioning profile resolves the identity,
# so this stays machine-neutral.
dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-ios \
    -c "$CONFIG" \
    -p:RuntimeIdentifier=ios-arm64 \
    -p:CodesignProvision="$PROVISION"

xcrun devicectl device install app --device "$IOS_UDID" "$APP"
xcrun devicectl device process launch --device "$IOS_UDID" "$BUNDLE_ID"
