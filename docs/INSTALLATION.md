# Install NOVA Island

## Run from source

1. Use Windows 11 24H2 or newer. Check your version by running `winver`.
2. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the [Microsoft Edge WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).
3. Clone the repository using the URL under GitHub's **Code** button, or choose **Download ZIP** and extract it.
4. Open PowerShell in the folder containing `NovaIsland.csproj`.

```powershell
dotnet --list-sdks
dotnet build NovaIsland.csproj -c Release
dotnet run --project NovaIsland.csproj -c Release --no-build
```

Hover over the pill to open it. Move away to collapse it. Scroll or press the arrow keys to switch pages. Press Ctrl+C in the launch terminal to stop the app.

## Install a signed MSIX

If you have a signed package from a trusted publisher, open the `.msix` file in Windows and choose **Install**, or run this from the download folder with the actual filename:

```powershell
Add-AppxPackage -Path '.\NovaIsland_1.0.0.0_x64.msix'
```

Launch **NOVA Island** from Start. To uninstall it, find NOVA Island under **Settings → Apps → Installed apps**.

To build and sign your own package or register a developer build, see the [MSIX packaging guide](../Packaging/README.md). The default build produces an unsigned package. Windows toast history requires package registration and Windows consent.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| The .NET SDK cannot build the project | Confirm `dotnet --list-sdks` includes a 10.0 SDK. Run the build from the folder containing `NovaIsland.csproj`. |
| The native interface appears | Install or repair the WebView2 Runtime, then restart NOVA. WebView2 initialization failures trigger the native fallback. |
| Media controls have no track | Start playback in an app that exposes a Windows media session. |
| Weather is unavailable | Enable Windows location access for desktop apps, or set both `NOVA_WEATHER_LATITUDE` and `NOVA_WEATHER_LONGITUDE` before launching. Weather lookup also needs internet access. |
| Windows rejects the MSIX | Check that the package is signed by a trusted publisher, matches your system architecture, and is allowed by device policy. See the [packaging guide](../Packaging/README.md) for developer registration and error details. |
