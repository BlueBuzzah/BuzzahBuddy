#!/usr/bin/env bash
# iPhone 17 Pro / iPhone Duo / iPad Pro 13-inch simulator helpers for App Store screenshots.
# DEVICE=iPhone (default), Duo or iPad.
#   bash sim.sh prepare [path/to/BuzzahBuddy.app]  boot, clean status bar, Demo Mode on, install, launch
#   bash sim.sh shot <name>                         save assets/screenshots/iOS/<device>/<date>/<date>-<name>.png
#   bash sim.sh tap <AutomationId>                  tap an element by its AutomationId (needs idb)
#   bash sim.sh tab <title>                         tap a tab: Session, Gloves, History, Settings
#   bash sim.sh scroll <points>                     from the top, scroll the page down by <points>
#   bash sim.sh ids                                 list AutomationIds and labels on screen
#   bash sim.sh udid                                print the simulator's UDID
set -euo pipefail

repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
bundle="com.rbonestell.buzzahbuddy"

# Screen size in points. The iPhone and iPad get their own simulators so other agents' runs don't collide
# with them. The Duo is shot unfolded, on its inner display; the iPad in landscape. sx is where `scroll`
# drags: mid-screen, or the iPad's scrolling left column.
case "${DEVICE:=iPhone}" in
iPhone | iphone) DEVICE=iPhone model="iPhone 17 Pro" sim="iPhone 17 Pro Screenshots" w=402 h=874 sx=201 display= ;;
Duo | duo) DEVICE=Duo model="iPhone Duo" sim="iPhone Duo" w=669 h=951 sx=334 display=--display=primary-1 ;;
iPad | ipad) DEVICE=iPad model="iPad Pro 13-inch (M5)" sim="iPad Pro 13-inch Screenshots" w=1376 h=1032 sx=344 display= ;;
*) echo "DEVICE must be iPhone, Duo or iPad, not $DEVICE" >&2; exit 1 ;;
esac

# Booted simulator with that name if there is one, else the one on the newest iOS runtime, else a new one.
udid="$(xcrun simctl list devices available -j | python3 -c '
import json, re, sys
devs = [(tuple(map(int, re.findall(r"\d+", rt))), d["state"] == "Booted", d["udid"])
        for rt, ds in json.load(sys.stdin)["devices"].items() if "iOS" in rt
        for d in ds if d["name"] == sys.argv[1]]
print(max(devs, key=lambda d: (d[1], d[0]))[2] if devs else "")' "$sim")"
[ -n "$udid" ] || udid="$(xcrun simctl create "$sim" "$model")"

case "${1:-}" in
prepare)
	app="${2:-$repo/BuzzahBuddy/bin/Debug/net10.0-ios/iossimulator-arm64/BuzzahBuddy.app}"
	[ -d "$app" ] || { echo "No app at $app. Build first (see SKILL.md)." >&2; exit 1; }
	xcrun simctl boot "$udid" 2>/dev/null || true
	xcrun simctl bootstatus "$udid" -b >/dev/null
	# simctl can't rotate; the frontmost window's frame shows the orientation.
	[ "$(idb ui describe-all --udid "$udid" --json | python3 -c 'import json, sys; print(round(json.load(sys.stdin)[0]["frame"]["width"]))')" = "$w" ] ||
		{ echo "$model ($udid) isn't in the orientation this pass needs ($w points wide). Rotate it (see SKILL.md)." >&2; exit 1; }
	# Windowed Apps (the iPad default) adds a resize corner and the app's name to the status bar.
	[ "$DEVICE" != iPad ] || [ "$(xcrun simctl spawn "$udid" defaults read com.apple.springboard SBMedusaMultitaskingEnabled 2>/dev/null)" = 0 ] ||
		{ echo "Set the iPad to Settings > Multitasking & Gestures > Full Screen Apps (see SKILL.md)." >&2; exit 1; }
	xcrun simctl ui "$udid" appearance dark # store shots are dark mode only, for now
	xcrun simctl status_bar "$udid" override --time 9:41 --dataNetwork wifi --wifiMode active --wifiBars 3 \
		--cellularMode active --cellularBars 4 --batteryState charged --batteryLevel 100
	# A clean install: no saved glove, theme or history from earlier runs.
	xcrun simctl uninstall "$udid" "$bundle" 2>/dev/null || true
	xcrun simctl terminate "$udid" com.apple.Preferences 2>/dev/null || true # or the status bar shows "◀ Settings"
	xcrun simctl install "$udid" "$app"
	# MAUI Preferences are NSUserDefaults, which reads "-key value" launch arguments first:
	# skip the first-run notices, start in Demo Mode, theme follows the OS (System = 0).
	xcrun simctl launch "$udid" "$bundle" -WelcomeNoticesAccepted YES -demo_mode YES -ThemePreference 0 >/dev/null
	echo "$udid"
	;;
shot)
	name="${2:?usage: sim.sh shot <name>}"
	day="$(date +%F)"
	dir="$repo/assets/screenshots/iOS/$DEVICE/$day"
	mkdir -p "$dir"
	xcrun simctl io "$udid" screenshot --type=png $display "$dir/$day-$name.png" 2>/dev/null
	echo "$dir/$day-$name.png"
	;;
tap)
	# Tap the element whose AutomationId (AXUniqueId) matches, waiting up to 10 s for it to appear
	# and scrolling up while it sits below the tab bar.
	id="${2:?usage: sim.sh tap <AutomationId>}"
	for _ in $(seq 20); do
		xy="$(idb ui describe-all --udid "$udid" --json 2>/dev/null | python3 -c '
import json, sys
for e in json.load(sys.stdin):
    if e.get("AXUniqueId") == sys.argv[1]:
        f = e["frame"]; print(round(f["x"] + f["width"] / 2), round(f["y"] + f["height"] / 2)); break' "$id" || true)"
		if [ -n "$xy" ] && [ "${xy#* }" -gt $((h - 114)) ]; then
			idb ui swipe --udid "$udid" ${xy% *} $((h - 224)) ${xy% *} $((h - 624)) --duration 1.5 # slow, so no fling; in the element's column
			sleep 2 # let it stop: a tap on a moving page only stops it
		elif [ -n "$xy" ]; then
			idb ui tap --udid "$udid" $xy
			exit 0
		else
			sleep 0.5
		fi
	done
	echo "No element with AutomationId $id on screen. Try: bash sim.sh ids" >&2
	exit 1
	;;
tab)
	# idb's describe-all hides tab bar items, so probe the bar point by point for the tab's title.
	name="${2:?usage: sim.sh tab <Session|Gloves|History|Settings>}"
	for x in $(seq 30 30 $((w - 20))); do
		xy="$(idb ui describe-point --udid "$udid" "$x" $((h - 52)) --json 2>/dev/null | python3 -c '
import json, sys
e = json.load(sys.stdin); f = e["frame"]
if e.get("AXLabel") == sys.argv[1]: print(round(f["x"] + f["width"] / 2), round(f["y"] + f["height"] / 2))' "$name" || true)"
		if [ -n "$xy" ]; then idb ui tap --udid "$udid" $xy; exit 0; fi
	done
	echo "No tab named $name" >&2
	exit 1
	;;
scroll)
	# Back to the top, then a slow drag (no fling) so the page moves exactly <points> (max ~600).
	points="${2:?usage: sim.sh scroll <points>}"
	x=$sx from=$((h - 114))
	idb ui swipe --udid "$udid" $x 200 $x $((h - 74)) --duration 0.2
	idb ui swipe --udid "$udid" $x 200 $x $((h - 74)) --duration 0.2
	sleep 1.5
	idb ui swipe --udid "$udid" $x $from $x $((from - points)) --duration 1.5
	sleep 3 # let the scroll indicator fade
	;;
ids)
	# AutomationIds and labels on screen now.
	idb ui describe-all --udid "$udid" --json | python3 -c '
import json, sys
for e in json.load(sys.stdin):
    print(e.get("AXUniqueId") or "-", "|", e.get("AXLabel"))'
	;;
udid) echo "$udid" ;;
*) sed -n '2,9p' "$0" >&2; exit 1 ;;
esac
