# Navisworks toast behavior

The toast follows the RVT activity-card behavior: Success, Failed and Capture counters; a latest-image thumbnail; a hover timeline with three visible rows; sanitized outcome and measured-duration tooltips; and motion that respects reduced-motion preferences. The timeline follows incoming results at the bottom and preserves the reading position when scrolled back.

Toast settings provide independent horizontal/vertical corners, optional title-row dragging and reset. Position is relative to the owner window, persists atomically and is clamped to the visible area. Toast display does not activate Navisworks. Clicking the card or the status window's History button opens a searchable in-memory history with an outcome filter. History retains the latest 1,000 completed calls, including calls made with toast display disabled, and clears when the add-in exits.

## Automated verification

```powershell
dotnet test tests/Bimwright.Nwd.Tests/Bimwright.Nwd.Tests.csproj
dotnet run --project tests/Bimwright.Nwd.Toast.Tests/Bimwright.Nwd.Toast.Tests.csproj
```

Use `-- --history` to run only the notifier, card-click and History lifecycle check. The WPF executable checks hover/collapse, live arrivals, scrolling, reduced motion, four corners, dragging/reset and History. These checks exercise the Windows UI but do not establish live acceptance inside each Navisworks release. Build and live checks require the matching installed Autodesk SDK/application.
