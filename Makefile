# Targets with a colon are escaped so `make start:ios` works.
# dotnet run starts in the project folder; with --project it can't find the built iOS app.

.PHONY: build test start\:ios start\:android

build:
	dotnet build

test:
	dotnet test BuzzahBuddy.Tests

# Deletes the cached app manifest first so Info.plist edits always reach the app.
# The simulator runs headless; open its window (DeviceHub since Xcode 27, Simulator before).
start\:ios:
	rm -f BuzzahBuddy/obj/*/net10.0-ios/*/AppManifest.plist BuzzahBuddy/obj/*/net10.0-ios/*/_CompileAppManifest.inputs
	cd BuzzahBuddy && dotnet run -f net10.0-ios -p:TargetFrameworks=net10.0-ios -p:WaitForExit=false

start\:android:
	cd BuzzahBuddy && dotnet run -f net10.0-android -p:TargetFrameworks=net10.0-android -p:WaitForExit=false
