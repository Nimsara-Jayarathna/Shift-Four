# Setup and first run

This guide assumes no previous Unity project experience. The `shift-four` folder **is already the project root**. You do not need Python, C, a backend, or a database.

## 1. Install and open

Install Unity Hub, then install Editor `6000.3.0f1` (Unity 6.3 LTS) and desktop build support for your demo computer. All four students use one exact editor patch. Extract the ZIP to a normal development directory such as `Documents/Projects/shift-four`. In Hub select **Add project from disk** and point at that directory. Wait for package import and compilation to complete.

The project uses the built-in 3D renderer, the AI Navigation package, `UnityEngine.Input` for controls, and `OnGUI` for a tiny HUD. No Asset Store download is needed. If Unity asks to enable or change the input backend, set **Project Settings > Player > Other Settings > Active Input Handling** to **Input Manager (Old)** or **Both** and restart. Set **Project Settings > Editor > Asset Serialization** to **Force Text**, and make meta files visible.

## 2. Generate the initial scene

Select **Tools > Shift Four > Generate Greybox Once**. Wait for the success dialog. Open `Assets/Scenes/MainLab.unity` and press Play. The generated scene and prefabs are now real editable Unity assets. The menu deliberately blocks a second run; it does not reset teammates' work. Back up through Git, then edit those assets normally.

If the menu does not exist, open **Window > General > Console**, inspect the **first red error**, and resolve it. Typical causes are an incomplete AI Navigation import or a different Editor version. Do not copy random scripts into another project. If the NavMesh does not show in the Scene view, select **NavMesh surface — baked greybox**, use its inspector to bake again, and verify the floor appears walkable. Member 1 owns subsequent rebakes after layout changes.

## 3. Confirm the controls and full loop

1. Use WASD and the mouse to move through all four connected sections. Press Escape to release the cursor.
2. Shoot a coloured drone and check that its health reaches zero and the HUD drone count falls.
3. Stand behind a shelf or pillar while a drone attacks: it must stop hitting you through solid cover. When damage stops, health should recover after about three seconds.
4. Face a console from nearby and press E. The display changes colour and the HUD console count increases once.
5. Face the sliding shortcut door and press E. It moves aside and the route graph can use that shortcut. Press E again to close it.
6. Activate four consoles, disable four drones, then step on the green square in the Control section. Confirm the success message. Lose all health and confirm loss/restart too.

These are **first-run checks**, not claims that this source was executed in the Editor before delivery. Record any local defect as a GitHub issue and fix it before assigning visual polish.

## 4. Build and share

The generator adds `MainLab` to the build scene list. In Unity open **File > Build Profiles**, select a desktop target, and build to a folder outside Git (for example `../ShiftFourBuild`). Play the exported build on the demo computer. Commit the Unity source, not the generated build.

After the scene is created and tested, initialize Git and push the project as described in [GIT_WORKFLOW.md](GIT_WORKFLOW.md). Another member then clones the **repository**, opens it through Unity Hub, and uses the already committed scene. They must **not** run the generator again.
