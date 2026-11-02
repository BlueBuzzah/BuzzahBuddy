#!/usr/bin/env bash
# Capture the Play Store shot list on the Pixel 9 Pro (default) or landscape 10" tablet emulator, in Demo Mode.
# Usage: bash capture.sh [Phone|Tablet] [path/to/app.apk]
# Numbers in the names are the store order; capture order differs because Gloves locks during a session.
set -euo pipefail
emu() { bash "$(dirname "$0")/emu.sh" "$@"; }
export DEVICE="${1:-Phone}"
shift || true

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
emu back

emu tab Session
sleep 1
emu tap SessionControlButton
sleep 8 # let the ring start moving
emu shot 01-session-running

emu tab History # 60 days of sample history in Demo Mode
sleep 2
emu tap PreviousMonthButton # last month is full of sample days; this month may have only a few
sleep 1
read -r overview calendar <<<"$([ "$DEVICE" = Tablet ] && echo 170 650 || echo 213 680)" # dp; the tablet's banner is shorter
emu scroll "$overview" # Overview heading at the top, past the Demo Mode banner
emu shot 03-history-overview
emu scroll "$calendar"
emu shot 04-history-calendar
