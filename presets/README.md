# Presets

Presets are the lists AM Speed Up uses to decide what to offer to remove. They're plain JSON files so anyone can see exactly what the app does. The app only removes things the user has ticked in the preview and confirmed.

## Format

```json
{
  "$schema": "./preset.schema.json",
  "id": "windows-junk",
  "name": "Windows junk apps",
  "description": "Games and promotional apps that come with Windows.",
  "tier": "free",
  "items": [
    {
      "type": "store",
      "match": "king.com.CandyCrush*",
      "name": "Candy Crush games",
      "description": "Promotional games that came with Windows.",
      "recommendation": "remove"
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `id` | Unique id, lowercase letters, numbers and dashes |
| `name`, `description` | Heading and one-line explanation shown in the app |
| `tier` | `free` or `pro`. Pro presets are shown but locked on the free version |
| `type` | `store` for Microsoft Store apps, `desktop` for traditional programs |
| `match` | Wildcard pattern (`*` and `?`). For Store apps it matches the package name, for desktop programs the name shown in Installed apps. At least 4 real characters |
| `publisher` | Optional wildcard for the publisher (desktop programs only), to avoid matching a different program with a similar name |
| `recommendation` | `remove` (ticked by default), `optional` (shown, not ticked) or `keep` (shown with the reason, not ticked). Use `keep` for tools that install driver or BIOS updates |
| `antivirus` | `true` for antivirus. After removing it, the app checks Microsoft Defender is switched on |
| `vendorTool` | `mcafee` or `norton`: remove with the maker's own official removal tool instead of the normal uninstaller |

## Safety

- Anything on the app's built-in protected list is skipped, with a warning in the log, even if a preset includes it. The list covers things like the Microsoft Store, App Installer, Microsoft Edge, WebView2, .NET and Visual C++ runtimes, Windows Security, drivers and anything signed as part of Windows. It's defined in [`src/AMSpeedUp.Core/Safety/ProtectedList.cs`](../src/AMSpeedUp.Core/Safety/ProtectedList.cs) and can't be changed by a preset.
- Patterns that are too broad (fewer than 4 real characters, like `*` or `Mic*`) are rejected.
- Mistakes in a preset are written to the log and that entry is skipped. The rest of the preset still works.
- If the same app is matched by more than one preset, the most cautious recommendation wins (`keep` beats `optional`, which beats `remove`).
