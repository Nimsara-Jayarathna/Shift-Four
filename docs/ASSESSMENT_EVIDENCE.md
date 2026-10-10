# Assessment Evidence Checklist

Use this file during development, not only on submission day. The supplied briefs make individual implementation, Git history, technical explanation, and role quality important, so evidence must be created while the project is being built.

## 1. Team evidence

- [ ] All four agents operate simultaneously in one playable build.
- [ ] AI decision logic is separate from visual/movement code.
- [ ] The level looks visually coherent rather than four unrelated rooms.
- [ ] Physics, interactions, environment geometry, navigation, and animation work together.
- [ ] One complete start-to-win playthrough succeeds.
- [ ] One complete start-to-loss/restart path succeeds.
- [ ] No recurring null references, agent freezes, severe stutters, or broken routes.
- [ ] The exact source commit used for the final demo is identifiable/tagged.

## 2. Pamudi - World Builder / Scout

### GV proof

- [ ] show connected four-section layout and alternate route;
- [ ] show NavMesh and explain rebake workflow;
- [ ] explain cohesive lighting/texturing choice;
- [ ] explain at least one practical optimization/design decision.

### IS proof

- [ ] show Scout observation of sight/sound;
- [ ] show last-known player position and search behavior;
- [ ] show a numeric priority/score/cooldown decision;
- [ ] show lost-target fallback to patrol;
- [ ] show an A* route used by Scout.

### Git proof

- [ ] multiple truthful commits across layout, NavMesh, visuals, Scout logic, fixes.

## 3. Nimsara - Systems Engineer / Flanker

### GV proof

- [ ] player movement and collisions;
- [ ] raycast shooting/interaction;
- [ ] physical door movement + collider behavior;
- [ ] cover blocking and health/recovery behavior;
- [ ] explain why the chosen physical interaction approach is stable/appropriate.

### IS proof

- [ ] show at least two flank candidates;
- [ ] show reachability and path-cost consideration;
- [ ] show the winning candidate's score/reason;
- [ ] invalidate/block a flank and demonstrate fallback/reselection;
- [ ] trace its A* route.

### Git proof

- [ ] multiple truthful commits across player, door, combat/health, Flanker logic, fixes.

## 4. Nimthara - Core Developer / Guard

### GV proof

- [ ] show editable Blender source for the security drone;
- [ ] show editable Blender source for the lab door;
- [ ] show topology and UV mapping for both;
- [ ] show correct Unity import, scale, materials/pivots;
- [ ] explain polygon/texture choices and an optimization decision.

### IS proof

- [ ] show candidate cover points;
- [ ] explain protection, route cost, and firing-visibility score;
- [ ] show Guard relocate after exposure;
- [ ] show no-valid-cover fallback;
- [ ] trace its A* route.

### Additional integration proof

- [ ] reusable console cannot count twice;
- [ ] HUD shows health, console progress, remaining drones;
- [ ] exit/win/lose/restart conditions are correct.

### Git proof

- [ ] multiple truthful commits across modeling/UV/import, console/HUD, Guard logic, fixes.

## 5. Asmadala - Agent Controller / Interceptor

### GV proof

- [ ] show ordered path points turning into smooth movement;
- [ ] show turning/braking/arrival behavior;
- [ ] show hover/fire/hit/shutdown or death animation feedback;
- [ ] explain how path updates avoid excessive recalculation;
- [ ] show movement remains correct for all four agents.

### IS proof

- [ ] explain A* `f = g + h` and the heuristic;
- [ ] show open/closed or equivalent search state and parent reconstruction in code;
- [ ] show a blocked/no-path case;
- [ ] show Interceptor's recent-position direction estimate and candidate junction scores;
- [ ] show failed prediction fallback.

### Git proof

- [ ] multiple truthful commits across A*, movement, animation, Interceptor logic, fixes.

## 6. Pull request evidence

Every feature PR should state:

- owner/member;
- module classification: GV / IS / shared;
- assessment requirement supported;
- exact files changed;
- Play mode test performed;
- edge case tested;
- whether `MainLab.unity`, NavMesh, a shared prefab, or shared interface changed;
- reviewer name/result.

Use `.github/PULL_REQUEST_TEMPLATE.md`.

## 7. Three-minute GV demo order

A reliable order is:

| Approx. time | Evidence |
| --- | --- |
| 0:00-0:25 | Connected lab, controls, HUD/objective |
| 0:25-0:55 | Door interaction, console, shooting, cover, health recovery |
| 0:55-2:15 | Scout, Flanker, Guard, Interceptor making distinct decisions; include one route change |
| 2:15-2:40 | Original models, lighting/textures, movement/animation |
| 2:40-3:00 | Final progress, exit, win condition |

Rehearse the path so evidence is visible without waiting for random behavior.

## 8. Viva preparation - everyone

Every member should be able to answer:

1. What exact GV role were you assigned and where is your work in the project?
2. What exact IS agent do you own and what makes it different from the other three?
3. What information can your agent observe and remember?
4. What goals/candidates can it choose from?
5. What numbers, priorities, weights, cooldowns, or heuristics influence its choice?
6. Why is this architecture more suitable than a simple `if/else` chase?
7. How does A* calculate `g`, `h`, `f`, and reconstruct a route?
8. What happens when the door closes or a destination becomes unreachable?
9. What is your agent's explicit fallback?
10. Show your own commits and navigate to your own implementation quickly.

## 9. Final release evidence

Before merging `develop -> main`:

- [ ] clean Unity import/open;
- [ ] no red Console errors in the release path;
- [ ] full integration checklist passed;
- [ ] desktop build tested on the demonstration machine;
- [ ] all four member contribution histories visible;
- [ ] final video/demo recorded from the same stable source;
- [ ] submitted commit tagged and not rewritten.
