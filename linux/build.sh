#!/bin/sh
set -eu
project=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
output=${1:-"$project/linux/build"}
mkdir -p -- "$output"
mcs -define:LINUX -optimize+ -target:exe -out:"$output/MovementController.exe" \
    -r:System.Drawing -r:System.Windows.Forms -r:System.Web.Extensions \
    "$project"/*.cs "$project"/linux/*.cs
cp -- "$project/pokemon-names.txt" "$project/linux/run.sh" "$output/"
chmod +x "$output/run.sh"
printf 'Built %s\n' "$output/MovementController.exe"

