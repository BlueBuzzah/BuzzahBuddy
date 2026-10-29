# Targets with a colon are escaped so `make start:ios` works.
PROJECT := BuzzahBuddy/BuzzahBuddy.csproj

.PHONY: build test start\:ios start\:android

build:
	dotnet build

test:
	dotnet test BuzzahBuddy.Tests

start\:ios:
	dotnet build $(PROJECT) -f net10.0-ios -p:TargetFrameworks=net10.0-ios -t:Run -p:WaitForExit=false

start\:android:
	dotnet build $(PROJECT) -f net10.0-android -p:TargetFrameworks=net10.0-android -t:Run
