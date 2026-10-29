# Targets with a colon are escaped so `make start:ios` works.
# dotnet run starts in the project folder; with --project it can't find the built iOS app.

.PHONY: build test start\:ios start\:android

build:
	dotnet build

test:
	dotnet test BuzzahBuddy.Tests

# The simulator runs headless; open its window (DeviceHub since Xcode 27, Simulator before).
start\:ios:
	cd BuzzahBuddy && dotnet run -f net10.0-ios -p:TargetFrameworks=net10.0-ios -p:WaitForExit=false

start\:android:
	cd BuzzahBuddy && dotnet run -f net10.0-android -p:TargetFrameworks=net10.0-android -p:WaitForExit=false
