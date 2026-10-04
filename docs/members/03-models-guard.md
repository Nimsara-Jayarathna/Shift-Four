# Member 3 — Model Creator and Guard

**Your IS assessment:** the Guard's cover decisions. **Your GV assessment:** create and import at least **two original 3D models**, with sensible topology and UV mapping. You own the Server section.

## Start from the baseline

Open `Assets/Prefabs/Sections/Server.prefab`, `Assets/Prefabs/Agents/Guard.prefab`, and `Assets/Scripts/AI/Agents/GuardBrain.cs`. The drone and door visuals are placeholders. Create the replacements yourself in Blender; keep the editable source in `ArtSource/` and the exported models in `Assets/Models/`.

## Work in this order

1. Model **one drone** and **one sliding lab door** from scratch in Blender. Use simple low-polygon geometry suitable for a small game. Apply transforms, unwrap UVs, assign a small consistent material palette, and keep screenshots of mesh topology and UVs for your viva.
2. Export each model in a Unity-friendly format such as FBX. Import it into `Assets/Models/`. Check orientation, scale, materials, shading and mesh pivots in Unity. Retain both the `.blend` and exported files in Git (use Git LFS only if the binaries become unusually large and the whole team agrees).
3. Replace the **visual child** of the four drone prefabs with your shared drone model or a model prefab variant. Keep the NavMeshAgent, capsule collider, health, motor and brain components on each drone root. Replace the visual child of the sliding door while preserving its root collider, Rigidbody and `LabDoor` component. Coordinate any main-scene door edit with Member 1.
4. Arrange fixed server racks/pillars in your Server prefab. Pick reachable `coverPoints` for the Guard; avoid points inside colliders. In `GuardBrain.cs`, improve protection, travel distance and exposed-cover scoring and add a useful fallback when no cover point is viable.
5. Run with all four drones and inspect imported asset size and frame rate. Do not let a detailed model hide its colours or interfere with raycast shooting.

## Evidence to produce

Show both original editable Blender files, topology, UVs, Unity imports, the drone/door in the scene, and Guard changing cover after the player moves. Example commits: `feat(models): model and unwrap security drone`, `feat(models): import sliding door`, `feat(ai): score protected guard positions`.

**Done when:** the source proves both models are original, they render and animate correctly, the player and drones still collide/see/shoot correctly, and the Guard can justify its cover choice and fallback.
