# MSIX packaging

From the project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Packaging/Build-MSIX.ps1
```

The script restores official Windows SDK build tools, publishes a self-contained app, generates logos, and validates/packages with MakeAppx. Output: `artifacts/NovaIsland_1.0.0.0_x64.msix`. Use `-Architecture arm64` for an ARM64 package; that architecture has not been runtime-tested here.

## Local developer registration

1. Enable **Developer Mode** in Windows Settings (`ms-settings:developers`) and accept its administrator confirmation.
2. Run the `Add-AppxPackage` command below using the exact staging manifest path printed by the build script:

```powershell
Add-AppxPackage -Register 'FULL_PATH_TO_STAGING_MANIFEST'
```

3. Launch **NOVA Island** from Start. Launching the project with `dotnet run` is still an unpackaged test.

Developer registration uses the staging folder in place. Keep that folder until you unregister the development package. A later build prints a new staging folder; use that path when registering the new build.

Registration attempted on 2 November 2026 was blocked by Windows error `0x80073CFF`, including after the Developer Mode confirmation. The queried development-mode setting was absent. Package creation/validation succeeded; registration and installed capability checks remain blocked by this machine's developer/sideloading configuration. Managed PCs may require an administrator to change the relevant policy.

## Signing for installation

The default MSIX is **unsigned**, so double-click installation requires signing first. Supply a code-signing certificate already in your current-user Personal certificate store whose subject matches the manifest publisher, `CN=NovaIsland`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Packaging/Build-MSIX.ps1 -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT
```

For distribution, configure your actual package identity and publisher in `Package.appxmanifest` and use a trusted signing certificate. The build script does not create certificates or modify certificate trust.

## Capabilities and runtime checks

The manifest declares `internetClient`, `runFullTrust`, `userNotificationListener`, `location`, `wiFiControl`, `radios`, `bluetooth`, and `microphone` in their proper namespaces. It follows Microsoft's documented desktop packaging template and is validated by MakeAppx. Internet access is used by the backend for optional LRCLIB lyrics and Open-Meteo weather lookup; location access enables weather positioning; microphone access enables optional voice commands.

- Hotspot: inspect the tile tooltip for the actual capability/state; test start and stop in the registered app with an internet connection.
- Notifications: select **Enable access** and respond to Windows consent. Permission is checked again before reading or dismissing notifications. **Clear shown** removes the displayed snapshot, not unseen notifications.
- Wi-Fi: Windows 11 may require Location and desktop-app access even with the manifest capability.
- Radio, pairing, and display controls still depend on hardware and Windows policy.

References: [Desktop packaging template](https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-manual-conversion), [notification listener](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/notification-listener), [tethering capability](https://learn.microsoft.com/uwp/api/windows.networking.networkoperators.networkoperatortetheringmanager.createfromconnectionprofile).
