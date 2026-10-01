# Flassh Arcade

A collection of remakes of old Flash-style games, all behind one menu. Pick a game from the home page, and return to it from any game's menu.

## Games

- **Planet Crasher**: a remake of the Flash game **Cosmic Crush**. Crash into anything smaller than you to absorb it and grow, from asteroid to star, while avoiding anything bigger and its pull. Become a black hole and swallow the whole arena to win.
- **Little Fishy**: inspired by the Flash game **Fishy**. Eat fish smaller than you to grow, and steer clear of the bigger ones. Grow into a massive goldfish to win.
- **Towering Survival**: Tetris-style pieces fall and stack up while lava rises from below. Climb and wall-jump up the pile to get as high as you can, without getting crushed by a falling piece.
- **Medieval World Conquest**: a single-player take on browser strategy games like Tribal Wars. Grow your village, train an army, raid and conquer villages with noblemen, and hold half the realm to win against AI lords who do the same. You can join or found tribes in a simulated MMO, and choose whether your world keeps running while the game is closed. Quests and helpers like the Account Manager and Loot Assistant ease you in.

## Play

Download the latest `.zip` from the [Releases](../../releases) page, unzip it, and run `Flassh Arcade.exe` (Windows). Builds up to v0.3 contain only Planet Crasher and are named `Planet Crasher.exe`.

**Controls:** Arrow keys / WASD to move · Esc to pause

## Open the project

Requires **Unity 6000.6.3f1**. Open the folder in Unity Hub, open `Assets/Arcade/Scenes/Home`, and press Play to start at the game menu. You can also open a game's own scene, like `Assets/Games/PlanetCrasher/Scenes/PlanetCrasher`, to jump straight into it. Art and UI are generated from code. Each game has sound and music hooks: drop audio files into its `Resources/<Game>/Sounds` and `Resources/<Game>/Music/Theme` and they play.

```
Assets/
  Arcade/                 the home menu and code shared by all games
    Scenes/  Scripts/  Editor/
  Games/
    PlanetCrasher/        Scenes/  Scripts/
    LittleFishy/          Scenes/  Scripts/
    ToweringSurvival/     Scenes/  Scripts/
    MedievalWorldConquest/
      Scenes/  Resources/ (UI styles)  Tests/
      Scripts/Simulation/  game rules as plain C# (own assembly, unit tested)
      Scripts/Game/        Unity side: controller, saves, village art
      Scripts/UI/          UI Toolkit screens
```

Medieval World Conquest's simulation has automated tests: in Unity, open **Window → General → Test Runner**, choose **EditMode**, and click **Run All**.

To add a game, create `Assets/Games/<Name>/Scenes/<Name>.unity`, write a controller component in `Assets/Games/<Name>/Scripts` that builds the game at runtime, and add an entry to `ArcadeCatalog.Games` in `Assets/Arcade/Scripts/ArcadeCatalog.cs`. The home page and the build's scene list pick it up automatically.
