#!/usr/bin/env bash
# Capture the Play Store shot list on the Pixel 9 Pro emulator, in Demo Mode.
# Usage: bash capture.sh [path/to/app.apk]   (APPEARANCE=light for light mode)
# Numbers in the names are the store order; capture order differs because Gloves locks during a session.
set -euo pipefail
emu() { bash "$(dirname "$0")/emu.sh" "$@"; }

emu prepare "$@"
sleep 8 # first launch
emu tap FindDevicesButton
emu tap ConnectDeviceButton
sleep 4 # connecting lands on the Session tab

emu tab Gloves
sleep 2
emu shot 02-gloves

emu tap CalibrateMotorsButton # Test Motors: skip to step 3 and check a few fingers
sleep 2
emu tap NextStepButton
sleep 1
emu tap NextStepButton
sleep 1.5
for finger in PrimaryIndexButton PrimaryMiddleButton SecondaryIndexButton; do
	emu tap "$finger"
	sleep 1.2
done
sleep 1
emu shot 05-test-motors
adb -s "$(emu serial)" shell input keyevent BACK

emu tab Session
sleep 1
emu tap SessionControlButton
sleep 8 # let the ring start moving
emu shot 01-session-running

emu tab History # 60 days of sample history in Demo Mode
sleep 2
emu scroll 213 # past the Demo Mode banner
emu shot 03-history-overview
emu scroll 571
emu shot 04-history-calendar
