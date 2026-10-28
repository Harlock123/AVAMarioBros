# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A single-screen platform game in the style of Nintendo's 1983 arcade game Mario Bros., in C# on .NET 10 and
Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). Everything in it
is original, with no Nintendo characters: the graphics are drawn in code, and the sounds and tunes are
synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAMarioBros.slnx                    # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAMarioBros.slnx                     # every test runs headless: no window opens
dotnet run --project src/AVAMarioBros             # play it; --no-audio for silence
MARIOBROS_SHOTS=$PWD/Images dotnet test tests/AVAMarioBros.Tests --filter ScreenshotTests   # refresh the README's pictures
MARIOBROS_PROBE=1 dotnet test tests/AVAMarioBros.Tests --filter PilotProbe   # how the computer player does, phase by phase ("lazy" too)
python3 tools/make_sounds.py                      # rebuild src/AVAMarioBros/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAMarioBros/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the steps of a phase.
  - `GameEngine.Play.cs` steps a phase: the mechanic, bumping ledges from below, the pests, coins, the jolt
    block, and the bonus phase every third.
  - `Entities.cs` has `Controls`, the room's measures and ledges (`Field`; y grows downwards, and the sides
    wrap round) and the things in it.
  - `MechanicPilot.cs` is the computer player, used by the demonstration and the tests. It works out once where
    every walk-off and jump lands, then plans its way between ledges.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAMarioBros/Views/`: `GameCanvas` (split over partial files) draws the 1024x768 frame and runs the
  60 Hz timer.
- `src/AVAMarioBros/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAMarioBros.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `PilotProbe` does nothing unless
  `MARIOBROS_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`, never from
  `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** `Controls` here is only `Dx` (-1, 0 or 1) and `Jump`; up keys and the
  D-pad's up jump. Test new controls through `InputHandler`, not by setting `Controls` directly.
- **The pilot learns the room.** `MechanicPilot` works out its routes from the mechanic's own movement, so a
  change to how he walks or jumps changes what it can reach. Run the probe after such a change.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAMarioBros`); tests load
  the table from a temporary folder and run the engine with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
