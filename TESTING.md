# Testing AM Speed Up

This is how to test the app by hand without risking a PC you care about. The automated tests are covered in the [README](README.md#running-the-tests).

The order is always the same: try it in **test mode** first, then for real, and only ever for real on a PC you can throw away or roll back.

## 1. Get a copy to test

- **A release:** download `AMSpeedUp.exe` from the [Releases page](https://github.com/afzalmussa/am-speed-up/releases).
- **A branch that isn't released yet:** open the branch's latest run on the [Actions page](https://github.com/afzalmussa/am-speed-up/actions), scroll to **Artifacts** and download **AMSpeedUp-exe**. The same run has a **screenshots** artifact with a picture of every screen, including the 125% and 150% layout checks.
- **Pro without a key:** build it yourself in Debug (`dotnet build AMSpeedUp.sln`), set `AMSPEEDUP_DEV_PRO=1`, then run `src\AMSpeedUp\bin\Debug\net10.0-windows\win-x64\AMSpeedUp.exe`. The header says "Pro (developer mode)". Release builds ignore the setting, so the downloaded app always needs a real key.

## 2. Choose where to test

### Windows Sandbox (quick, but limited)

Windows Sandbox is a throwaway copy of Windows that's wiped when you close it. It comes with Windows 10 and 11 Pro, Enterprise and Education.

1. Open **Turn Windows features on or off**, tick **Windows Sandbox**, click OK and restart.
2. Open **Windows Sandbox** from the Start menu.
3. Copy `AMSpeedUp.exe` from your PC and paste it into the Sandbox window.

The Sandbox is good for test mode, the layout and the licence window. It's not much use for real removals, because it doesn't have the Microsoft Store or the usual preinstalled apps, and System Restore may not be available there. If restore points aren't available, AM Speed Up should explain that and ask before carrying on, which is worth checking in its own right.

### A virtual machine (best for real changes)

Use Hyper-V (built into Windows Pro), VirtualBox or VMware with a fresh Windows 10 or 11 install. A fresh consumer install usually comes with Candy Crush, Solitaire, Tips and similar apps, which is what you want.

1. Finish Windows setup inside the VM.
2. Install a few things to test with. For example, Spotify or Netflix from the Microsoft Store, and Steam or a free game launcher that adds itself to startup.
3. In **System Properties > System Protection**, check protection is **On** for drive C:. Click **Configure** to switch it on if not.
4. **Take a snapshot** (Hyper-V calls it a checkpoint). You'll go back to it after each real test.

If you can get a PC with a McAfee or Norton trial on it, that's the only real way to test antivirus removal, because it depends on McAfee's and Norton's own tools.

## 3. Test mode first

Start the app in test mode from Command Prompt or PowerShell, in the folder with the file:

```powershell
.\AMSpeedUp.exe --dry-run
```

A yellow banner says "Test mode is on". Go through every tab and every button as if you meant it. Every preview should say it's test mode, nothing should actually change, and the log ("View log") should list each thing with "Would". If anything changes on the PC in test mode, that's a serious bug: please report it.

## 4. Then for real

In the VM, after taking a snapshot, start `AMSpeedUp.exe` normally and work through the checklists below. Go back to the snapshot whenever you want a clean start.

## Where the app keeps things

| What | Where |
| --- | --- |
| Logs (one file per day) | `C:\ProgramData\AMSoftware\SpeedUp\logs` |
| Removed apps list (for "Removed earlier") | `C:\ProgramData\AMSoftware\SpeedUp\undo-history.json` |
| Saved licence (encrypted, only readable on this PC) | `C:\ProgramData\AMSoftware\SpeedUp\licence.dat` |
| Copy used by the monthly tune-up | `C:\Program Files\AM Software\SpeedUp\AMSpeedUp.exe` |
| Before and after reports | Your Documents folder |

To start again from scratch without a snapshot, turn off the monthly tune-up, close the app and delete `C:\ProgramData\AMSoftware\SpeedUp`.

## Manual test checklist

Tick each one off in test mode, then again for real.

### Opening the app

- [ ] Windows asks for administrator permission. Clicking **No** closes it without an error.
- [ ] Opening it a second time while it's open says it's already running.
- [ ] The header shows the logo, "Free version" or "Pro", and the **Get Pro**, **Enter licence key** and **About** buttons.
- [ ] The footer links work: amsoftware.uk, Get help, View log, Report a problem and Check for updates.
- [ ] **About** shows the version and the Donate and Visit amsoftware.uk buttons.

### Remove bloat

- [ ] Before scanning, nothing is listed and the page says looking doesn't change anything.
- [ ] **Scan my PC** finds the preinstalled apps, grouped by list, each with a short description.
- [ ] "Recommended to remove" items are ticked. "Your choice" and "Worth keeping" items aren't.
- [ ] On the free version, HP, Dell, Lenovo, Acer and Asus extras show as needing Pro and can't be ticked.
- [ ] The preview lists exactly the ticked items, and says how each one can be undone (Store apps can be reinstalled, desktop programs only through the restore point).
- [ ] Clicking **Cancel** or closing the preview changes nothing.
- [ ] Confirming makes a restore point first. Check it in **System Restore** ("AM Speed Up: before removing apps").
- [ ] The removed apps disappear from the Start menu and from **Settings > Apps**.
- [ ] Removed Store apps appear under **Removed earlier**, and **Reinstall** opens the Microsoft Store at that app.
- [ ] A desktop program with no quiet uninstaller opens its normal uninstaller, with an explanation first.
- [ ] **Open System Restore** opens Windows' System Restore.
- [ ] Nothing on the protected list ever appears: Microsoft Store, App Installer, Edge, WebView2, .NET, Visual C++ runtimes, Windows Security or drivers.

### Restore point problems

- [ ] Remove something, then remove something else straight away. Windows only allows one new restore point a day, so the app should explain that and offer to carry on using the earlier one.
- [ ] Switch System Protection off for drive C:, then remove something. The app should explain that System Restore is off, offer a button to open the settings, and make **Cancel** the main button.
- [ ] Choosing **Cancel** in either case changes nothing.

### Trial antivirus (if you have a PC with McAfee or Norton)

- [ ] It's offered under **Trial antivirus**, and the preview says the maker's own removal tool will be used.
- [ ] The app downloads the tool, checks its signature and explains the steps before opening it.
- [ ] After removal (and the restart the tool may ask for), the app checks Microsoft Defender is on and says so.
- [ ] To test the warning: in **Windows Security**, turn off **Real-time protection**, then start the app with `.\AMSpeedUp.exe --check-antivirus`. It should warn you, with a button that opens Windows Security. Windows switches real-time protection back on by itself after a while.

### Startup

- [ ] Opening the tab lists programs from the Run keys, the Startup folders and sign-in tasks.
- [ ] Known programs (like Spotify or Steam) have a plain description and advice. Unknown ones say "Not sure what this is? Leave it on."
- [ ] Windows Security's icon and other protected items can't be switched off.
- [ ] Switching items doesn't change anything until you click apply, and **Undo my changes** puts the switches back.
- [ ] The preview lists each change. After applying, **Task Manager > Startup apps** shows the same on/off state.
- [ ] After restarting the PC, switched-off programs don't start, and you can still open them yourself.
- [ ] Switching one back on and restarting makes it start again.

### Free up space

- [ ] Each kind of clutter shows how much it would free before anything is deleted.
- [ ] With Edge, Chrome or Firefox open, its cache says the browser is open and can't be ticked. Closing it and clicking **Check again** makes it available.
- [ ] The Recycle Bin is never ticked for you, and **Look inside first** opens it.
- [ ] Windows Update leftovers shows progress while it runs, and the window stays usable.
- [ ] After freeing space, the free space shown for drive C: goes up. Browser passwords, bookmarks and history are still there.

### Licence and Pro

- [ ] **Get Pro** opens the checkout page in the browser.
- [ ] Entering a made-up key says it wasn't recognised, without crashing.
- [ ] Entering a real key turns the header to Pro and opens up the Pro features.
- [ ] The log never shows the full key (only the first and last four characters).
- [ ] Closing and reopening the app keeps Pro.
- [ ] With the internet switched off (disconnect the VM's network), Pro keeps working.
- [ ] **Remove this PC** returns the app to the free version and frees the slot in Lemon Squeezy.
- [ ] Activating on more PCs than the key allows gives a clear message.

### Pro features

- [ ] On the free version, each Pro button explains Pro instead of doing anything, with a way to buy and a way to enter a key.
- [ ] **Do everything recommended** checks all three tabs, then shows one preview with the startup changes, removals and clean-ups together. The Recycle Bin isn't in it. Afterwards all three tabs show the new state.
- [ ] **Before and after report** saves a page in Documents. It opens in the browser, shows free space and counts before and after, and lists what changed. **Show in folder** selects the file.
- [ ] Large file and duplicate finder: finds big files and copies in your own folders, with nothing ticked. You can't tick every copy of a duplicate. Moved files are in the Recycle Bin and can be restored.
- [ ] Monthly tune-up: turning it on shows a preview, then **Task Scheduler** has "AM Speed Up monthly tune-up" under "AM Software", and the copy is in Program Files.
- [ ] In Task Scheduler, right-click the task and choose **Run**. No window opens, and the log shows "Monthly tune-up started" and what it cleared. It never removes programs or empties the Recycle Bin.
- [ ] Turning the tune-up off removes the task and the Program Files copy, even on the free version.

### Scaling and small screens

- [ ] In **Settings > System > Display**, set **Scale** to 125%, then 150%. At each, every tab and window is readable, nothing is cut off, and long pages scroll.
- [ ] On a small laptop screen (1366 x 768), the main window fits on screen at both settings.
- [ ] Every button can be reached with the Tab key, and Enter never confirms a preview by accident.

### The log

- [ ] Every change you made today is in the log with whether it worked.
- [ ] Anything skipped or protected has a line saying why.
- [ ] Test mode runs are clearly marked as test mode.

## Reporting a problem

[Open an issue](https://github.com/afzalmussa/am-speed-up/issues) and attach the day's log file from `C:\ProgramData\AMSoftware\SpeedUp\logs`. Say what you clicked, what you expected and what happened. A screenshot helps.
