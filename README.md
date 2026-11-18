# Personal Space

A Windows physics-brawler prototype built in Unity 6000.3.24f1.

## Run the prototype

1. Open the project in Unity 6000.3.24f1.
2. Download **Human Basic Motions FREE Unity** from [Kevin Iglesias on itch.io](https://kevdev.itch.io/basic-motions-free).
3. In Unity, choose **Assets > Import Package > Custom Package** and open the downloaded package. The prototype uses `HumanM_Model`, `HumanM@Idle01`, `HumanM@Walk01_Forward`, `HumanM@Run01_Forward`, and `HumanM@Jump01`. Keep the imported assets and their Unity metadata at the publisher's paths.
4. Open `Assets/Scenes/MainMenu.unity` and press Play, then choose **Play City Stage**. Open `CityStage.unity` directly for faster testing. If you use a different version of the pack and a reference is missing, assign the model and four clips on the **Motion Pack Player** object.

Animation source files are downloaded separately under the [publisher's license](https://www.keviniglesias.com/#license).

## Controls

- **WASD** to walk, **Shift** to run, and **Space** to jump.
- Hold a **mouse button** to reach toward a nearby object with that hand and grab on contact. Release to let go. Tap with no contact to jab.
- Grab a fighter and move away to drag them. Two hands give you a stronger pull.
- Grab near a ledge, then hold **Space** to pull up through the planted arm. Move toward the ledge to press over it. The free hand reaches above the planted hand so you can grab higher. Steer sideways to twist while hanging.
- The camera automatically follows the action and frames nearby fighters together.
- **P** raises or lowers the hydraulic press.

City Stage is the default test map. It contains the furnished apartment, hydraulic press bay, and a playable street outside the north doorway. The standalone ApartmentTest scene is available in Assets/Scenes.

## Knockout tests

- **K** knocks out the dummy. **Shift+K** knocks out your own fighter.
- A knocked-out fighter stays collidable. Controls return when the timer ends, and the body straightens up under physics.
- Use **P** in the press bay to test environmental impacts. Knockout duration, recovery speed, and the impact threshold are adjustable on Motion Pack Player.
- **Esc** returns from City Stage to the main menu.

## Possible menu additions

Local versus, online play, stage selection, and settings can be added as those features come online.