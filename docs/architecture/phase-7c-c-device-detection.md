# Phase 7C-C: Automatic local printer and scanner detection

## Architecture

The existing `devices.DesktopDevices` entity remains the authoritative device record. Hardware enumeration runs in the Windows WPF desktop host because the remote ASP.NET Core API cannot enumerate USB or locally installed devices attached to another workstation.

### Printers

The desktop host queries WMI `Win32_Printer` for installed Windows printers. It does not hardcode printer names or models. The local discovery key is hashed before it crosses the API boundary. Windows network-installed printers are visible to WMI when they are available to the Windows user/session running the desktop application. WMI is therefore the printer enumeration boundary, not a server-side scanner.

### Scanners

The desktop host enumerates Windows Image Acquisition devices through the `WIA.DeviceManager` COM Automation object. WIA is used instead of a TWAIN-specific adapter because it is a Windows-native device-enumeration boundary and is already appropriate for a Windows-only desktop host.

### Synchronization

The desktop agent performs an initial scan and then polls every 10 seconds. Each authenticated snapshot is sent to `POST /api/v1/devices/sync`.

- New devices are inserted into the existing `DesktopDevices` table.
- Seen devices are refreshed and marked `IsOnline = true`.
- Previously registered devices absent from the snapshot are marked `IsOnline = false`.
- Device records are never deleted for physical disconnect.
- `IsActive` and `RevokedAt` continue to represent administrative lifecycle/revocation.
- Device identifiers are SHA-256 hashed before persistence.
- An identifier already belonging to another user is never silently reassigned.

The API derives the owner from the authenticated access-token subject claim. The desktop application keeps the access token in memory and continues to protect only the refresh token with the existing DPAPI-backed store.

## Verification

On Windows/.NET 8 run:

```powershell
git fetch origin
git reset --hard origin/rename-and-audit
dotnet restore
dotnet build
dotnet test
```

Then sign in through the WPF desktop app and verify that installed printers and WIA-visible scanners appear as online devices. Remove a device or stop its visibility to Windows and verify the record remains but changes to `IsOnline = false` on the next reconciliation cycle.

The CI/remote repository can validate compilation only; actual printer/scanner enumeration requires a Windows workstation with the target hardware and WIA/WMI providers installed.
