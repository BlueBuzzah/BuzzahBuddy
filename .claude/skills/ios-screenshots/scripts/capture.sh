#!/usr/bin/env bash
# Capture the App Store shot list on the iPhone 17 Pro simulator, in Demo Mode.
# Usage: bash capture.sh [path/to/BuzzahBuddy.app]   (APPEARANCE=light for light mode)
# Numbers in the names are the App Store order; capture order differs because Gloves locks during a session.
set -euo pipefail
sim() { bash "$(dirname "$0")/sim.sh" "$@"; }

sim prepare "$@"
sim tap FindDevicesButton
sim tap ConnectDeviceButton
sleep 4 # connecting lands on the Session tab

sim tab Gloves
sleep 2
sim shot 02-gloves

sim tap CalibrateMotorsButton # Test Motors: skip to step 3 and check a few fingers
sleep 2
sim tap NextStepButton
sleep 1
sim tap NextStepButton
sleep 1.5
for finger in PrimaryIndexButton PrimaryMiddleButton SecondaryIndexButton; do
	sim tap "$finger"
	sleep 1.2
done
sleep 1
sim shot 05-test-motors
sim tap BackButton

sim tab Session
sleep 1
sim tap SessionControlButton
sleep 8 # let the ring start moving
sim shot 01-session-running

sim tab History # 60 days of sample history in Demo Mode
sleep 2
sim scroll 250 # past the Demo Mode banner
sim shot 03-history-overview
sim scroll 510
sim shot 04-history-calendar
