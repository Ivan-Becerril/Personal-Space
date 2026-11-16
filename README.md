# Personal Space

A Windows physics-brawler prototype built in Unity 6000.3.24f1.

## Run the prototype

1. Open the project in Unity 6000.3.24f1.
2. Download **Human Basic Motions FREE Unity** from [Kevin Iglesias on itch.io](https://kevdev.itch.io/basic-motions-free).
3. In Unity, choose **Assets > Import Package > Custom Package** and open the downloaded package. The prototype uses `HumanM_Model`, `HumanM@Idle01`, `HumanM@Walk01_Forward`, `HumanM@Run01_Forward`, and `HumanM@Jump01`. Keep the imported assets and their Unity metadata at the publisher's paths.
4. Open `Assets/Scenes/ActiveRagdoll.unity` and press Play. If you use a different version of the pack and a reference is missing, assign the model and four clips on the **Motion Pack Player** object.

Animation source files are downloaded separately under the [publisher's license](https://www.keviniglesias.com/#license).

## Controls

- **WASD** to walk, **Shift** to run, and **Space** to jump.
- **Left click** and **right click** jab with the corresponding hand.
- **Middle mouse drag** or **Q/E** to orbit, **scroll** to zoom, and **R** to recenter.
- **P** raises or lowers the hydraulic press.

The arena includes a runway, low hurdles, jump platforms, and a practice dummy for testing limb contact.
