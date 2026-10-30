#!/usr/bin/env bash
# Frame every .png in a folder (not subfolders) with the iPhone 17 Pro hardware frame.
# Usage: bash frame.sh <folder>   → writes <folder>/framed/<same name>.png
set -euo pipefail

dir="${1:?usage: frame.sh <folder>}"
repo="$(cd "$(dirname "$0")/../../../.." && pwd)"
frame="$repo/assets/screenshots/iOS/iphone-17-pro-frame.png"
size="$(magick identify -format '%wx%h' "$frame")"
# The frame's transparent screen hole is 1206x2622, centered: exactly an iPhone 17 Pro screenshot.
screen="1206x2622"

shopt -s nullglob
shots=("$dir"/*.png)
[ ${#shots[@]} -gt 0 ] || { echo "No .png files in $dir" >&2; exit 1; }
mkdir -p "$dir/framed"

for shot in "${shots[@]}"; do
	got="$(magick identify -format '%wx%h' "$shot")"
	if [ "$got" != "$screen" ]; then
		echo "skip $(basename "$shot"): $got, expected $screen (iPhone 17 Pro)" >&2
		continue
	fi
	magick -size "$size" xc:none "$shot" -gravity center -composite "$frame" -composite "$dir/framed/$(basename "$shot")"
	echo "framed $(basename "$shot")"
done
