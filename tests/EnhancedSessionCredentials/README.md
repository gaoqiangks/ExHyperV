# Enhanced-session credential regressions

Run on Windows with the .NET 8 SDK or a compatible newer SDK:

```powershell
dotnet run --project tests/EnhancedSessionCredentials/EnhancedSessionCredentials.csproj -c Release
```

The runner compiles the production credential store and RDP settings directly. It uses synthetic passwords encrypted with Windows DPAPI and a temporary store under its own output directory; it never reads or modifies the user's saved credentials and never connects to a VM.

Coverage: repeated VM renames, same-name/different-ID isolation, GUID input normalization, disabled and incomplete entries, malformed or missing files, corrupted ciphertext, and ciphertext encrypted for a different VM ID. Successful renames must leave the saved file byte-for-byte unchanged. The process returns a nonzero exit code on failure.
