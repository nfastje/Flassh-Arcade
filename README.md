# Flassh Arcade

A collection of remakes of old Flash-style games, all behind one menu. Pick a game from the home page, and return to it from any game's menu.

## Games

- **Planet Crasher**: a remake of the Flash game **Cosmic Crush**. Start as a tiny asteroid, crash into anything smaller than you to absorb it and grow, and avoid anything bigger. Touching it is instant death, and its gravity pulls you in. Everything else is eating too, so the universe keeps changing around you. Grow from asteroid to moon, planet, gas giant and star, then become a **black hole** and swallow the whole arena to win. Fly off the edge of the circular arena and you'll wrap around to the other side.
- **Little Fishy**: inspired by the Flash game **Fishy**. Start as a little goldfish and eat any fish smaller than you to grow, but touch a bigger one and you're lunch. Fish swim across the tank at random sizes and speeds, coloured by size: goldfish orange for the smallest, then pink, purple and dark blue for the biggest. Bones in the corner tally what you've eaten. Swim off the left or right edge to wrap around. Grow into a massive goldfish to win.
- **Towering Survival** (demo): Tetris-style pieces fall at random and stack up while lava slowly rises from the floor. Climb the pile, wall-jumping off blocks and wrapping around the screen's edges, to get as high as you can before the lava catches you. Don't let a falling piece land on you.

## Play

Download the latest `.zip` from the [Releases](../../releases) page, unzip it, and run `Flassh Arcade.exe` (Windows). Builds up to v0.3 contain only Planet Crasher and are named `Planet Crasher.exe`.

**Controls:** Arrow keys / WASD to move · Esc to pause

## Open the project

Requires **Unity 6000.6.3f1**. Open the folder in Unity Hub, open `Assets/Arcade/Scenes/Home`, and press Play to start at the game menu. You can also open a game's own scene, like `Assets/Games/PlanetCrasher/Scenes/PlanetCrasher`, to jump straight into it. Everything (art, sound, UI) is generated from code.

```
Assets/
  Arcade/                 the home menu and code shared by all games
    Scenes/  Scripts/  Editor/
  Games/
    PlanetCrasher/        Scenes/  Scripts/
    LittleFishy/          Scenes/  Scripts/
    ToweringSurvival/     Scenes/  Scripts/
```

To add a game, create `Assets/Games/<Name>/Scenes/<Name>.unity`, write a controller component in `Assets/Games/<Name>/Scripts` that builds the game at runtime, and add an entry to `ArcadeCatalog.Games` in `Assets/Arcade/Scripts/ArcadeCatalog.cs`. The home page and the build's scene list pick it up automatically.
