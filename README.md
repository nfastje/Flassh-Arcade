# Flassh Arcade

A collection of remakes of old Flash-style games, all behind one menu. Pick a game from the home page, and return to it from any game's menu.

## Games

- **Planet Crasher**: a remake of the Flash game **Cosmic Crush**. Start as a tiny asteroid, crash into anything smaller than you to absorb it and grow, and avoid anything bigger. Touching it is instant death, and its gravity pulls you in. Everything else is eating too, so the universe keeps changing around you. Grow from asteroid to moon, planet, gas giant and star, then become a **black hole** and swallow the whole arena to win. Fly off the edge of the circular arena and you'll wrap around to the other side.
- **Little Fishy**: inspired by the Flash game **Fishy**. Start as a little goldfish and eat any fish smaller than you to grow, but touch a bigger one and you're lunch. Fish swim across the tank at random sizes and speeds, colored by size: goldfish orange for the smallest, then pink, purple and dark blue for the biggest. Bones in the corner tally what you've eaten. Swim off the left or right edge to wrap around. Grow into a massive goldfish to win.
- **Towering Survival**: Tetris-style pieces fall at random and stack up while lava slowly rises from the floor. Climb the pile, wall-jumping off blocks and wrapping around the screen's edges, to get as high as you can before the lava catches you. Don't let a falling piece land on you.
- **Medieval World Conquest** (early development): a single-player take on browser strategy games like Tribal Wars. Build up a village, raise an army, and conquer the realm's villages against AI rivals. Choose the world speed, and whether the world keeps running while the game is closed. So far: grow your village's economy by upgrading its Headquarters, timber camp, clay pit, iron mine, farm and warehouse, and watch the buildings change as they level up. Then build a barracks, stable and workshop to train spearmen, swordsmen, axemen, archers, scouts, cavalry, rams and catapults, and a wall to ring your village. Explore the generated world map: forests, hills, lakes and hundreds of barbarian villages, with travel times for every unit. Village points show roughly how strong each village is, and barbarian villages grow over time. Then go to war: send attacks at barbarian villages to plunder their resources, use rams against walls, catapults against the building of your choice, and scouts to spy. Send support to reinforce any village. Read every battle in the Reports tab, and watch out for incoming attacks. You're not alone: rival lords (choose how skilled they are for each new world) build up their own villages, raid the barbarians, scout, and attack each other and you once the opening days of beginner protection are over. The 250 × 250 world starts as a small settled circle round your village and spreads outward day by day, with new barbarian villages and newcomer lords arriving on its frontier, so you'll meet both old powers from the middle and fresh villages to crush before they grow. Most newcomers are noobs: easy prey who give up after being beaten a few times, leaving barbarian villages behind. Between them sit plenty of inactive players, whose villages grew for a while and then stopped: good farms, and good first conquests. Building, training, production and population follow Tribal Wars' own numbers, and the screens follow its classic layout: click a building to open it (the Headquarters for upgrades, new buildings and renaming your village, the barracks, stable, workshop and academy to recruit, the rally point to send troops). Research each new kind of unit at the smithy before you can train it, trade at the market (send resources between villages, or swap them through offers with the rival lords), and dig a hiding place to keep some of your stores from raiders. Villages grow on the map (and the minimap) as their points rise, just like Tribal Wars. Build an academy to train noblemen: each one that survives a winning attack lowers a village's loyalty, and at zero the village is yours. Win villages over to build an empire you can switch between, and watch out, because the rival lords do the same. Choose when creating a world whether noblemen have a flat price or need gold coins, as on Tribal Wars' coin worlds: coins minted at the academy buy noble slots, each dearer than the last, so every conquest makes the next harder. Hold enough of the realm to win; lose everything and you can start again on the frontier. Keep an eye on all your villages at once in the Villages overview, find your way round the map by its K00–K99 continents. Or turn on diplomacy for a simulated MMO: lords band into tribes that defend their members, share what they see, pick targets together, make pacts and war, and sometimes fall apart; found your own tribe or join one (and change your mind whenever you like), answer the messages lords send you, and win the world with your tribe and up to two allies by holding half of it for ten days, before a rival side does. As the strongest lords close in on that, the realm quietly splits into two to four factions, and the smaller tribes of each feed its leaders with their best lords, their villages and their resources. Send noblemen as a noble train: one attack per nobleman, landing a split second apart, each with just enough escort. You can't see inside an incoming attack, only its speed (and so its slowest unit): rams and noblemen mean danger, unless it's one of the fakes rival lords send to draw your support away. See where you stand in the Ranking tab, with daily, weekly and all-time statistics (plunder, troops defeated, villages conquered), and keep up to three worlds going at once.

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
