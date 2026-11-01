#!/usr/bin/env bash
# Pixel 9 Pro / 10" tablet emulator helpers for Play Store screenshots. DEVICE=Phone (default) or Tablet.
#   bash emu.sh prepare [path/to/app.apk]  boot, clean status bar, Demo Mode on, install, launch
#   bash emu.sh shot <name>                 save assets/screenshots/Android/<device>/<date>/<date>-<name>.png
#   bash emu.sh tap <AutomationId>          tap an element by its AutomationId
#   bash emu.sh tab <title>                 tap a tab: Session, Gloves, History, Settings
#   bash emu.sh scroll <dp>                 from the top, scroll the page down by <dp>
#   bash emu.sh ids                         list AutomationIds and labels on screen
#   bash emu.sh back                        press Back (leave a pushed page)
#   bash emu.sh serial                      print the emulator's adb serial
set -euo pipefail

repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
pkg="com.rbonestell.buzzahbuddy"
# The tablet gets its own AVD so other agents' runs don't collide with it, and is shot in landscape.
case "${DEVICE:=Phone}" in
Phone | phone) DEVICE=Phone avd=Pixel_9_Pro rotation=0 ;;
Tablet | tablet) DEVICE=Tablet avd=Tablet_Screenshots rotation=1 ;;
*) echo "DEVICE must be Phone or Tablet, not $DEVICE" >&2; exit 1 ;;
esac
sdk="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
emulator="$sdk/emulator/emulator"
# The SDK's adb, not whatever is first on PATH: a different adb version restarts the shared adb server,
# and tools that watch it (VS Code's .NET MAUI extension) restart it back, over and over.
adb() { "$sdk/platform-tools/adb" "$@"; }

serial_of_avd() {
	for s in $(adb devices | awk '/^emulator-/ {print $1}'); do
		[ "$(adb -s "$s" emu avd name 2>/dev/null | head -1 | tr -d '\r')" = "$avd" ] && { echo "$s"; return; }
	done
	true # none running: empty, not a failure
}

serial="$(serial_of_avd)"
if [ -z "$serial" ] && [ "${1:-}" = prepare ]; then
	"$emulator" -list-avds | grep -qx "$avd" || { echo "No $avd AVD. See SKILL.md (Needs)." >&2; exit 1; }
	nohup "$emulator" -avd "$avd" -no-window -no-audio -no-boot-anim -no-snapshot-save >/dev/null 2>&1 &
	until serial="$(serial_of_avd)" && [ -n "$serial" ]; do sleep 2; done
fi
[ -n "$serial" ] || { echo "The $avd emulator isn't running. Run: bash emu.sh prepare" >&2; exit 1; }
ash() { adb -s "$serial" shell "$@"; }

# Screen size in pixels and pixels per dp, so positions don't depend on the phone. sx is where drags go:
# mid-screen, or the tablet's scrolling left column (a drag between columns moves nothing).
geometry() {
	read -r w h < <(ash wm size | tr -d '\r' | awk -F'[ x]' '/Physical/ {print $3, $4}')
	sx=$([ "$DEVICE" = Tablet ] && echo $((w / 4)) || echo $((w / 2)))
	px_per_dp=$(ash wm density | tr -d '\r' | awk '/Physical/ {print $3 / 160}')
	px() { awk -v d="$1" -v r="$px_per_dp" 'BEGIN {printf "%d", d * r}'; }
}

# Every on-screen node as "id|label|x|y" (center in pixels). On Android MAUI sets the AutomationId
# as the view's resource-id.
nodes() {
	ash uiautomator dump /sdcard/ui.xml >/dev/null 2>&1 || true # fails while the UI is mid-animation
	ash cat /sdcard/ui.xml | python3 -c '
import re, sys, xml.etree.ElementTree as ET
for n in ET.parse(sys.stdin).iter("node"):
    x1, y1, x2, y2 = map(int, re.findall(r"\d+", n.get("bounds")))
    rid = n.get("resource-id", "").split("/")[-1]
    label = n.get("content-desc") or n.get("text")
    print(rid, label, (x1 + x2) // 2, (y1 + y2) // 2, sep="|")'
}

case "${1:-}" in
prepare)
	apk="${2:-$(ls "$repo"/BuzzahBuddy/bin/Debug/net10.0-android/"$pkg"-Signed.apk 2>/dev/null || true)}"
	[ -f "$apk" ] || { echo "No APK. Build first (see SKILL.md)." >&2; exit 1; }
	until [ "$(ash getprop sys.boot_completed | tr -d '\r')" = 1 ]; do sleep 2; done
	ash settings put system accelerometer_rotation 0
	ash settings put system user_rotation $rotation
	ash cmd uimode night yes >/dev/null # store shots are dark mode only, for now
	# System UI demo mode: 9:41 clock, full battery, full signal, no notifications.
	ash settings put global sysui_demo_allowed 1
	for args in "enter" "clock -e hhmm 0941" "battery -e level 100 -e plugged false" \
		"network -e wifi show -e level 4 -e fully true" "network -e mobile hide" \
		"notifications -e visible false"; do
		ash am broadcast -a com.android.systemui.demo -e command $args >/dev/null
	done
	# A clean install: no saved glove, theme or history from earlier runs.
	adb -s "$serial" uninstall "$pkg" >/dev/null 2>&1 || true
	adb -s "$serial" install -g "$apk" >/dev/null 2>&1 # -g: grant Bluetooth permissions, no prompt
	# MAUI Preferences are SharedPreferences; a debug build lets run-as seed them before first launch:
	# skip the first-run notices, start in Demo Mode, theme follows the OS (System = 0).
	echo '<?xml version="1.0" encoding="utf-8" standalone="yes" ?><map><boolean name="WelcomeNoticesAccepted" value="true" /><boolean name="demo_mode" value="true" /><int name="ThemePreference" value="0" /></map>' |
		ash "run-as $pkg sh -c 'mkdir -p shared_prefs && cat > shared_prefs/${pkg}_preferences.xml'"
	ash monkey --pct-syskeys 0 -p "$pkg" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
	echo "$serial"
	;;
shot)
	name="${2:?usage: emu.sh shot <name>}"
	day="$(date +%F)"
	dir="$repo/assets/screenshots/Android/$DEVICE/$day"
	mkdir -p "$dir"
	adb -s "$serial" exec-out screencap -p >"$dir/$day-$name.png"
	echo "$dir/$day-$name.png"
	;;
tap | tab)
	# Waits ~3 s for the element, then scrolls down looking for it: the dump has only views on screen.
	key="${2:?usage: emu.sh $1 <name>}"
	geometry
	above_tabs=$((h - $(px 100))) # tab bar plus gesture bar (80 dp), plus a little
	field=$([ "$1" = tap ] && echo 1 || echo 2)
	for try in $(seq 20); do
		xy="$(nodes | awk -F'|' -v k="$key" -v f="$field" '$f == k {print $3, $4; exit}')"
		if [ -n "$xy" ] && { [ "$1" = tab ] || [ "${xy#* }" -le "$above_tabs" ]; }; then
			ash input tap $xy
			exit 0
		elif [ "$1" = tap ] && { [ -n "$xy" ] || [ "$try" -gt 6 ]; }; then
			x=${xy%% *} x=${x:-$sx} # in the element's column once it's found
			ash input swipe $x $((h * 7 / 10)) $x $((h * 4 / 10)) 600 # behind the tab bar, or off screen
			sleep 0.8
		else
			sleep 0.5
		fi
	done
	echo "Nothing named $key on screen. Try: bash emu.sh ids" >&2
	exit 1
	;;
scroll)
	# Back to the top, then slow drags (no fling) so the page moves exactly <dp>; one drag unless the
	# distance won't fit on screen (a landscape tablet).
	dist="${2:?usage: emu.sh scroll <dp>}"
	geometry
	x=$sx from=$((h * 78 / 100)) left=$(px "$dist")
	ash input swipe $x $((h / 5)) $x $((h * 82 / 100)) 150
	ash input swipe $x $((h / 5)) $x $((h * 82 / 100)) 150
	sleep 1.5
	while [ "$left" -gt 0 ]; do
		step=$((left < from - h / 20 ? left : from - h / 20))
		ash input swipe $x $from $x $((from - step)) 1500
		left=$((left - step))
		sleep 1
	done
	sleep 2 # let the scroll indicator fade
	;;
ids) nodes | awk -F'|' '$1 ~ /^[A-Z]/ || ($1 == "" && $2 != "") {print ($1 == "" ? "-" : $1), "|", $2}' ;;
back) ash input keyevent BACK ;;
serial) echo "$serial" ;;
*) sed -n '2,10p' "$0" >&2; exit 1 ;;
esac
