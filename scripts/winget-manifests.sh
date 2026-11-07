#!/bin/sh
# Writes the WinGet manifests for a Velixo CLI release, in the folder layout of microsoft/winget-pkgs.
#
#   scripts/winget-manifests.sh 1.0.2 checksums.txt out/
#
# writes out/manifests/v/Velixo/CLI/1.0.2/Velixo.CLI{,.installer,.locale.en-US}.yaml. checksums.txt
# is the release's own file, as `sha256sum velixo-*` writes it. The package is the signed velixo.exe
# inside the release's Windows zips, installed as a portable command named velixo.
set -eu

if [ "$#" -ne 3 ]; then
  echo "usage: $0 VERSION CHECKSUMS_FILE OUTPUT_DIR" >&2
  exit 2
fi

version=$1
checksums=$2
dir="$3/manifests/v/Velixo/CLI/$version"
base="https://github.com/VelixoSolutions/velixo-cli/releases/download/v$version"
schema=1.10.0

sum() {
  file="velixo-$version-$1.zip"
  value=$(awk -v f="$file" '$2 == f || $2 == "*" f { print toupper($1) }' "$checksums")
  if [ -z "$value" ]; then
    echo "$checksums has no sum for $file" >&2
    exit 1
  fi
  echo "$value"
}

win_x64=$(sum win-x64)
win_arm64=$(sum win-arm64)

mkdir -p "$dir"

cat > "$dir/Velixo.CLI.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.version.$schema.schema.json

PackageIdentifier: Velixo.CLI
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
EOF

cat > "$dir/Velixo.CLI.installer.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json

PackageIdentifier: Velixo.CLI
PackageVersion: $version
InstallerType: zip
NestedInstallerType: portable
NestedInstallerFiles:
- RelativeFilePath: velixo.exe
  PortableCommandAlias: velixo
Commands:
- velixo
Installers:
- Architecture: x64
  InstallerUrl: $base/velixo-$version-win-x64.zip
  InstallerSha256: $win_x64
- Architecture: arm64
  InstallerUrl: $base/velixo-$version-win-arm64.zip
  InstallerSha256: $win_arm64
ManifestType: installer
ManifestVersion: $schema
EOF

cat > "$dir/Velixo.CLI.locale.en-US.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json

PackageIdentifier: Velixo.CLI
PackageVersion: $version
PackageLocale: en-US
Publisher: Velixo Solutions Inc.
PublisherUrl: https://velixo.com
PublisherSupportUrl: https://github.com/VelixoSolutions/velixo-cli/issues
PackageName: Velixo CLI
PackageUrl: https://cli.velixo.com
License: Apache-2.0
LicenseUrl: https://github.com/VelixoSolutions/velixo-cli/blob/main/LICENSE
ShortDescription: Query an ERP, run ERP commands and imports from the command line.
Moniker: velixo
Tags:
- acumatica
- business-central
- cli
- erp
- sage-intacct
ReleaseNotesUrl: https://github.com/VelixoSolutions/velixo-cli/releases/tag/v$version
ManifestType: defaultLocale
ManifestVersion: $schema
EOF

echo "$dir"
