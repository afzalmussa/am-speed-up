<p align="center">
  <img src="assets/logo.png" alt="AM Software logo" width="120">
</p>

# AM Speed Up

AM Speed Up helps you make a slow Windows PC feel quicker, without needing to be good with computers. It removes programs you don't need (like preinstalled games and trial antivirus), lets you choose what starts when you turn your PC on, and clears out files Windows no longer needs. Before anything changes, it shows you exactly what will happen and waits for you to say yes.

It's made by [AM Software](https://www.amsoftware.uk), a small computer repair business.

> **Work in progress.** AM Speed Up is still being built and there's no download yet. When the first version is released, you'll be able to get it here:
> https://github.com/afzalmussa/am-speed-up/releases/latest/download/AMSpeedUp.exe

<!-- Screenshot: main window -->

## Is it safe?

AM Speed Up is designed for people who are nervous about breaking something. These safety rules are built into the app and are always free:

- **You see everything first.** Every change is shown as a checklist, and nothing happens until you confirm it.
- **A restore point is made first.** Before removing anything, the app asks Windows to make a System Restore point, so you can wind your PC back if you need to.
- **Important parts of Windows can't be removed.** Things like the Microsoft Store, Microsoft Edge, Windows Security and drivers are on a protected list the app will never touch.
- **You're never left without antivirus.** If a trial antivirus is removed, the app checks that Windows' own antivirus (Microsoft Defender) is switched on, and tells you if it isn't.
- **Everything is written down.** Every action is saved in a log file you can open with the "View log" button.

The full details will be added here before the first release.

## Privacy

AM Speed Up doesn't collect information about you or your PC, and has no tracking or analytics. It only goes online to:

- **Check a Pro licence key** with Lemon Squeezy, who sell AM Speed Up Pro. Only the key and a random label for the PC are sent.
- **Download McAfee's or Norton's own removal tool** from their website, if you choose to remove one of them.

Links you click, like "Get help", open in your web browser.

## Free and Pro

AM Speed Up will have a free version and a Pro version you get with a licence key. The safety features above are in both. Details will be added before the first release.

## Help and support

- Need help? Contact AM Software at https://www.amsoftware.uk/contact
- Found a problem? [Report it here](https://github.com/afzalmussa/am-speed-up/issues)

---

## For developers

### Building from source

You'll need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/afzalmussa/am-speed-up.git
cd am-speed-up
dotnet build AMSpeedUp.sln
dotnet test AMSpeedUp.sln
```

To build the single-file app:

```powershell
dotnet publish src/AMSpeedUp -c Release -o publish
```

This produces `publish/AMSpeedUp.exe`. It asks for administrator permission when it opens, because it changes system settings.

To try the app without changing anything on your PC, start it with `--dry-run`. It does everything as normal but only writes to the log what it would have done:

```powershell
publish\AMSpeedUp.exe --dry-run
```

`AMSpeedUp.Core` and its tests have no Windows-only code, so they also build and run on Linux and macOS:

```bash
dotnet test tests/AMSpeedUp.Tests
```

### Project layout

| Folder | What's in it |
| --- | --- |
| `src/AMSpeedUp` | The WPF app (windows, view models, styles) |
| `src/AMSpeedUp.Core` | Logic, presets, licensing and service interfaces. No UI and no Windows-only APIs |
| `src/AMSpeedUp.Windows` | The real Windows implementations of the service interfaces |
| `tests/AMSpeedUp.Tests` | xUnit tests for Core. Run on any OS |
| `tests/AMSpeedUp.UI.Tests` | xUnit tests for the app: view models, plus smoke tests that load every window. Windows only. With `SCREENSHOT_DIR` set, it also saves a picture of each window |
| `tests/AMSpeedUp.Windows.Tests` | Integration tests that run real Windows PowerShell (read-only, nothing on the PC is changed). Windows only |
| `presets` | The lists of apps the app can offer to remove. See [presets/README.md](presets/README.md) |
| `data` | `startup-known.json`: plain descriptions and advice for common startup programs |
| `assets` | Logo, icon and fonts |

### Releases

Pushing a tag like `v0.1.0` runs the release workflow, which tests the code, builds `AMSpeedUp.exe` and attaches it to a GitHub Release named after the tag. The file is always called `AMSpeedUp.exe` so the latest-download link keeps working.

### Contributions

Thanks for your interest. Code contributions aren't being accepted at the moment, but bug reports and ideas are very welcome through [Issues](https://github.com/afzalmussa/am-speed-up/issues).

## Licence

**Using the app:** anyone, including businesses and other organisations, may download and use the official releases of AM Speed Up from the [Releases page](https://github.com/afzalmussa/am-speed-up/releases). Pro features need a licence key.

**Using the source code:** the code is available under the [PolyForm Noncommercial License 1.0.0](LICENSE). You're welcome to read it, build it and change it for personal and other noncommercial use, but you may not sell it, redistribute it or use the code commercially without permission from AM Software. This is a "source available" licence rather than an open source one.

The exact terms, including the extra permission for using official releases, are in [LICENSE](LICENSE).

Third-party components and their licences are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
