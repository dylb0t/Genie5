# Genie4.Core extraction starter

This is an initial cross-platform core library extracted from the Genie4 codebase.

Included here:
- `LocalDirectory` behavior, rewritten as `LocalDirectoryService`
- path resolution and directory validation
- settings persistence and parsing from `Lists/Config.cs`
- reusable parsing helpers from `Utility/Utility.cs`
- non-UI numeric and string helpers

Not included yet:
- WinForms, dialogs, custom controls, mapper UI
- `System.Drawing`-based font/color settings
- plugin loading and dynamic UI integration
- networking/game session flow
- updater and Windows shell calls

## Suggested integration path

1. Add this project to the Genie4 solution.
2. Replace direct `LocalDirectory` usage with `LocalDirectoryService`.
3. Replace config reads/writes with `Genie4.Core.Config.GenieConfig`.
4. Continue moving queueing, scripting, XML config, and connection code into this library.
5. After that, build a new macOS UI host against `Genie4.Core`.
