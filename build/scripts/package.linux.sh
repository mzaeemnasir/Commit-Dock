#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

# ICU versions to support (Debian has no virtual package, must list all)
# Format: space-separated version numbers
ICU_VERSIONS="78 77 76 74 72 71 70 69 68 67 66 65 63"

arch=
appimage_arch=
target=
case "$RUNTIME" in
    linux-x64)
        arch=amd64
        appimage_arch=x86_64
        target=x86_64;;
    linux-arm64)
        arch=arm64
        appimage_arch=arm_aarch64
        target=aarch64;;
    *)
        echo "Unknown runtime $RUNTIME"
        exit 1;;
esac

APPIMAGETOOL_URL=https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage

cd build

if [[ ! -f "appimagetool" ]]; then
    curl -o appimagetool -L "$APPIMAGETOOL_URL"
    chmod +x appimagetool
fi

rm -f CommitDock/*.dbg
rm -f CommitDock/*.pdb

mkdir -p CommitDock.AppDir/opt
mkdir -p CommitDock.AppDir/usr/share/metainfo
mkdir -p CommitDock.AppDir/usr/share/applications

cp -r CommitDock CommitDock.AppDir/opt/commitdock
desktop-file-install resources/_common/applications/commitdock.desktop --dir CommitDock.AppDir/usr/share/applications \
    --set-icon io.github.mzaeemnasir.CommitDock --set-key=Exec --set-value=AppRun
mv CommitDock.AppDir/usr/share/applications/{commitdock,io.github.mzaeemnasir.CommitDock}.desktop
cp resources/_common/icons/commitdock.png CommitDock.AppDir/io.github.mzaeemnasir.CommitDock.png
ln -rsf CommitDock.AppDir/opt/commitdock/commitdock CommitDock.AppDir/AppRun
ln -rsf CommitDock.AppDir/usr/share/applications/io.github.mzaeemnasir.CommitDock.desktop CommitDock.AppDir
cp resources/appimage/commitdock.appdata.xml CommitDock.AppDir/usr/share/metainfo/io.github.mzaeemnasir.CommitDock.appdata.xml

chmod -R a+rX CommitDock.AppDir

ARCH="$appimage_arch" ./appimagetool -v \
    -u "gh-releases-zsync|mzaeemnasir|Commit-Dock|latest|CommitDock-v*-linux-$arch.AppImage.zsync" \
    CommitDock.AppDir "CommitDock-v$VERSION-linux-$arch.AppImage"

mkdir -p resources/deb/opt/commitdock/
mkdir -p resources/deb/usr/bin
mkdir -p resources/deb/usr/share/applications
mkdir -p resources/deb/usr/share/icons
cp -f CommitDock/* resources/deb/opt/commitdock
ln -rsf resources/deb/opt/commitdock/commitdock resources/deb/usr/bin
cp -r resources/_common/applications resources/deb/usr/share
cp -r resources/_common/icons resources/deb/usr/share

# Calculate installed size in KB
installed_size=$(du -sk resources/deb | cut -f1)

# Generate ICU dependencies string for Debian
# Debian lacks libicu virtual package, must list all versions with OR operator
icu_deps="libicu"
for v in $ICU_VERSIONS; do
    icu_deps="$icu_deps | libicu$v"
done

# Update the control file (replace placeholder, not whole Depends line)
sed -i -e "s/^Version:.*/Version: $VERSION/" \
    -e "s/^Architecture:.*/Architecture: $arch/" \
    -e "s/^Installed-Size:.*/Installed-Size: $installed_size/" \
    -e "s/@ICU_DEPS@/$icu_deps/" \
    resources/deb/DEBIAN/control

# Build deb package with gzip compression
dpkg-deb -Zgzip --root-owner-group --build resources/deb "CommitDock-v$VERSION-linux-$arch.deb"

rpmbuild -bb --target="$target" resources/rpm/SPECS/build.spec --define "_topdir $(pwd)/resources/rpm" --define "_version $VERSION"
mv "resources/rpm/RPMS/$target/commitdock-$VERSION-1.$target.rpm" "./CommitDock-v$VERSION-linux-$target.rpm"
