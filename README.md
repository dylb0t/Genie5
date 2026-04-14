# Genie5

A cross-platform port of [Genie4](https://github.com/GenieClient/Genie4), the DragonRealms MUD client, built with .NET 8 and Avalonia UI.

## Features

- Connects to Simutronics games via SGE authentication or direct TCP
- Parses the Simutronics GSL/XML game protocol
- Dockable panel layout (main output, sub-streams, room panel)
- Status bar: vitals, roundtime/casttime countdown, compass rose, body diagram, hand contents, prepared spell, status indicators
- Highlights, aliases, triggers, and variable engine ported from Genie4
- Connection profiles with encrypted password storage
- Command history (up/down arrow)
- Raw output tab for inspecting unprocessed server data

## Building

Requires .NET 8 SDK.

```
dotnet build Genie5.slnx
dotnet run --project src/Genie5.Ui
```

## Architecture

```
Genie5.slnx
├── src/Genie4.Core          # Platform-neutral game engine library
│   ├── Gsl/                 # GSL/XML protocol parser and game state
│   ├── Networking/          # TCP client, SGE authentication client
│   ├── Commanding/          # Command engine, ICommandHost interface
│   ├── Aliases/             # Alias expansion engine
│   ├── Triggers/            # Trigger matching engine
│   ├── Highlights/          # Text highlight rule engine
│   ├── Variables/           # Variable store and expansion
│   ├── Queue/               # Command and event queues
│   ├── Profiles/            # Connection profiles and encrypted passwords
│   ├── Persistence/         # JSON save/load for aliases, triggers, highlights
│   └── Config/              # GenieConfig, local directory service
│
└── src/Genie5.Ui            # Avalonia 11 UI host
    ├── MainWindow.*         # Main window — engine wiring, dock layout, input
    ├── GenieDockFactory     # Dock.Avalonia layout builder
    ├── StatusBarView.*      # Bottom status bar (vitals, RT, compass, hands)
    ├── CompassView          # Custom-drawn compass rose (pure Control)
    ├── BodyView             # Custom-drawn body silhouette (pure Control)
    ├── GameOutputView.*     # Scrolling styled text panel (main + stream tabs)
    ├── GameOutputViewModel  # Document VM for each output panel
    ├── RoomView.*           # Room title/desc/objects/players panel
    ├── RoomViewModel        # Document VM for room panel
    ├── AnsiParser           # ANSI escape + GSL segment → RenderLine
    ├── ScrollbackBuffer     # Bounded ring buffer of RenderLines
    ├── FormConfig.*         # Aliases / Triggers / Highlights editor
    └── Dialog*              # Connect, profile connect/edit dialogs
```

### Data flow

```
TcpGameClient.LineReceived
  └─► GslParser.ParseLine(rawLine)
        ├─► RenderLine → RawOutputVm (verbatim, Raw tab)
        ├─► GslSegments → AnsiParser → highlights → GameOutputVm (main tab)
        └─► GslEvents  → GslGameState.Apply()
                           ├─► StateChanged → StatusBarView.Refresh()
                           ├─► StateChanged → CompassView.InvalidateVisual()
                           ├─► StateChanged → BodyView.InvalidateVisual()
                           └─► StateChanged → RoomViewModel.Update()
```

### Key design decisions

- **Stream routing** — named `pushStream` blocks route text to sub-stream `DocumentDock` panels. Unknown stream IDs (e.g., `room`) are suppressed as text; their structured data still arrives via component/vitals events.
- **Custom controls** (`CompassView`, `BodyView`) are pure `Control` subclasses using `Render(DrawingContext)` overrides — no AXAML, no bitmaps. The compass draws 8 triangular arrows with `StreamGeometry`; lit exits use a bright fill. I may change this in later builds to make it look better.
- **`GslGameState`** is the single mutable game state object. It is only written on the UI thread (via `Dispatcher.UIThread.Post`) and read by all UI components through the `StateChanged` event.

## TODO

### Known bugs
- [x] Compass exits do not light up — `CompassEvent` is emitted but exits are not reflected in `CompassView` rendering
- [x] Direct TCP connect handshake — sending `look` or CR after connect does not trigger the server handshake
- [ ] Highlights editor: colour picker is limited; more colour options needed
- [ ] Profile management is buggy (save/load edge cases)
- [x] Double echo on sent commands
- [ ] Moving windows from one panel from another leaves tab in another window
- [ ] Mapper window unreadable, scrolling and zooming do not appear to be ergo

### Planned features
- [ ] Per-body-part wound tracking — `BodyView` currently shows global bleeding only; GSL wound data exists per zone
- [ ] Kneeling/Prone/Standing person images
- [ ] Script engine — port Genie4 script runner (`.cmd` files)
- [ ] Map window — port Genie4 mapper
- [x] Spell tracking counter
- [ ] Auto-repeat / speedwalk
- [ ] Font and colour preference UI
- [ ] Import genie configurations - Should automatically detect what config it is (highlights/presets etc) and import appropriately.
- [ ] Clickable compass to go a direction
- [ ] Ability to run both genie scripts and lich scripts
- [ ] Select to copy (setable)
- [ ] Sound support
- [ ] Remote control configuable button panel
- [ ] Ability to choose between MDI and Tabbed windows
- [ ] Global variables section


