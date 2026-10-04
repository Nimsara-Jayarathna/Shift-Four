# Shift Four

A short first-person game set during a locked-down lab shift. Cross four connected sections, disable four security drones, activate four consoles, and reach the marked exit. This one Unity project is the shared deliverable for **SE3062 Intelligent Systems** and **SE3032 Graphics and Visualization**.

**Team:** exactly four students. **Engine:** Unity 6.3 LTS, initially pinned to `6000.3.0f1`. **Language:** C#. **Models:** Blender. **Repository:** one GitHub repository.

## Start here — first time only

1. Install Unity Hub and the **Unity 6.3.0f1** Editor with desktop build support for the platform on which you will demonstrate. If everyone already has another **identical** Unity 6.3 patch, agree on it together before importing.
2. Extract this ZIP. In Unity Hub choose **Add project from disk** and select the extracted `shift-four` directory that contains `Assets`, `Packages`, and `ProjectSettings`. Do not create a new Unity project around these folders.
3. Let Unity import the AI Navigation package and scripts. If it prompts to upgrade the project because your editor patch differs, use the team-agreed patch on every computer.
4. Under **Edit > Project Settings > Player > Other Settings**, set **Active Input Handling** to **Input Manager (Old)** or **Both**. The starter controls use Unity's built-in input names. Restart the Editor if Unity asks. Under **Project Settings > Editor**, use **Force Text** asset serialization and **Visible Meta Files**.
5. In the top menu choose **Tools > Shift Four > Generate Greybox Once**. This creates `Assets/Scenes/MainLab.unity`, four editable section prefabs, four drone prefabs, materials, the NavMesh, and the build scene entry. Do this **once, on one computer**. The menu refuses to overwrite an existing scene.
6. Open `Assets/Scenes/MainLab.unity` and press **Play**. Click the Game view if necessary to lock the cursor. Walk with WASD, aim with the mouse, shoot with left click, interact with E. Escape releases the cursor. Defeat the drones, activate the consoles, and walk onto the green exit square. R restarts after a win or loss.
7. Stop Play mode. Save the scene and project. Confirm no red Console errors, then follow [Git setup](docs/GIT_WORKFLOW.md). Commit all generated `.meta` files, the scene, prefabs, and NavMesh data.

If **Tools > Shift Four** is absent, a compiler/package error prevented the editor script from loading. Check the Unity Console before trying the menu. See [Setup and troubleshooting](docs/SETUP.md).

## What the baseline already provides

- One scene containing four connected rooms and fixed cover, with a shortcut door and a second route around it.
- Player movement, aiming, raycast shooting, damage, three-second recovery delay, death, restart, four reusable consoles, and a gated exit.
- Four simultaneously active placeholder drones with separate Scout, Flanker, Guard, and Interceptor scripts; visible current decisions in the HUD.
- A small shared A* graph for room-to-room route choice and a NavMeshAgent-based route follower. The door changes the graph edge and NavMesh obstacle.
- An editor generator that creates **separate Unity prefabs**, so each member can own their section and drone without editing the main scene together.

**This is an initial baseline, not a finished submission.** The drones use simple starter policies, art is made of Unity primitives, and movement feedback is basic. Each student must develop their own assessed component, make regular attributable commits, test it, and explain it in the viva. Unity is not available in the archive-generation environment, so the generated scene must be opened and verified in your local Editor before treating it as a working build.

## Individual work

| Member | Section and IS agent | Prescribed GV responsibility | Detailed guide |
| --- | --- | --- | --- |
| 1 | Checkpoint / Scout | Overall layout, NavMesh, lighting, textures | [Member 1](docs/members/01-world-scout.md) |
| 2 | Storage / Flanker | Player physics, shooting, interactions, door | [Member 2](docs/members/02-systems-flanker.md) |
| 3 | Server / Guard | Two original Blender models, UVs, import | [Member 3](docs/members/03-models-guard.md) |
| 4 | Control / Interceptor | Smooth route following, turning, animations | [Member 4](docs/members/04-movement-interceptor.md) |

Write names in [the ownership table](docs/SCOPE.md#4-team-ownership) before the concept pitch. Every member owns **one agent and one GV role**; the lab section is an additional, practical division of the work.

## Essential documents

- [Locked game scope and grading evidence](docs/SCOPE.md)
- [Environment setup and first run](docs/SETUP.md)
- [How the AI and gameplay pieces connect](docs/ARCHITECTURE.md)
- [Git and scene ownership](docs/GIT_WORKFLOW.md)
- [Integration and playthrough checks](docs/INTEGRATION.md)
- [Individual member guides](docs/members/)

## Repository rule

Use **one repo and one Unity project**. `main` stays playable. Short branches and non-squashed pull requests preserve every student's history. Commit `Assets/`, `Packages/`, `ProjectSettings/`, `ArtSource/`, docs, and all `.meta` files. Do not commit Unity's generated `Library/`, `Temp/`, or build output. If the two courses ask for separate submissions, use the same source project and prepare the requested evidence for each course.
