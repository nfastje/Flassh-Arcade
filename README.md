# Flassh Arcade

A collection of remakes of old Flash-style games, all behind one menu. Pick a game from the home page, and return to it from any game's menu.

## Games

- **Planet Crasher**: a remake of the Flash game **Cosmic Crush**. Start as a tiny asteroid, crash into anything smaller than you to absorb it and grow, and avoid anything bigger. Touching it is instant death, and its gravity pulls you in. Everything else is eating too, so the universe keeps changing around you. Grow from asteroid to moon, planet, gas giant and star, then become a **black hole** and swallow the whole arena to win. Fly off the edge of the circular arena and you'll wrap around to the other side.
- **Little Fishy**: inspired by the Flash game **Fishy**. Start as a little goldfish and eat any fish smaller than you to grow, but touch a bigger one and you're lunch. Fish swim across the tank at random sizes and speeds, coloured by size: goldfish orange for the smallest, then pink, purple and dark blue for the biggest. Bones in the corner tally what you've eaten. Swim off the left or right edge to wrap around. Grow into a massive goldfish to win.
- **Towering Survival**: Tetris-style pieces fall at random and stack up while lava slowly rises from the floor. Climb the pile, wall-jumping off blocks and wrapping around the screen's edges, to get as high as you can before the lava catches you. Don't let a falling piece land on you.
- **Medieval World Conquest** (early development): a single-player take on browser strategy games like Tribal Wars. Build up a village, raise an army, and conquer the realm's villages against AI rivals. Choose the world speed, and whether the world keeps running while the game is closed. So far: grow your village's economy by upgrading its Town Hall, timber camp, clay pit, iron mine, farm and warehouse, and watch the buildings change as they level up. Then build a barracks, stable and workshop to train spearmen, swordsmen, axemen, archers, scouts, cavalry, rams and catapults, and a wall to ring your village. Explore the generated world map: forests, hills, lakes and hundreds of barbarian villages, with travel times for every unit. Village points show roughly how strong each village is, and barbarian villages grow over time. Then go to war: send attacks at barbarian villages to plunder their resources, use rams against walls, catapults against the building of your choice, and scouts to spy. Send support to reinforce any village. Read every battle in the Reports tab, and watch out for incoming attacks. You're not alone: rival lords (choose how thickly they settle, and how skilled they are, for each new world) build up their own villages, raid the barbarians, scout, and attack each other and you once the opening days of beginner protection are over. The 250 × 250 world starts as a small settled circle round your village and spreads outward day by day, with new barbarian villages and newcomer lords arriving on its frontier, so you'll meet both old powers from the middle and fresh villages to crush before they grow. Villages grow on the map (and the minimap) as their points rise, just like Tribal Wars. See where you stand in the Ranking tab.

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
    MedievalWorldConquest/
      Scenes/  Resources/ (UI styles)  Tests/
      Scripts/Simulation/  game rules as plain C# (own assembly, unit tested)
      Scripts/Game/        Unity side: controller, saves, village art
      Scripts/UI/          UI Toolkit screens
```

Medieval World Conquest's simulation has automated tests: in Unity, open **Window → General → Test Runner**, choose **EditMode**, and click **Run All**.

To add a game, create `Assets/Games/<Name>/Scenes/<Name>.unity`, write a controller component in `Assets/Games/<Name>/Scripts` that builds the game at runtime, and add an entry to `ArcadeCatalog.Games` in `Assets/Arcade/Scripts/ArcadeCatalog.cs`. The home page and the build's scene list pick it up automatically.
