# Project Directives

## Vision
- Unity 6000.3.9f1, URP. FPS co-op scavenging (Lethal Company–style), up to 4 players.
- Networking: **NGO (Netcode for GameObjects) + UTP**. Not Mirror.
- Host-authoritative gameplay ownership. UGS Multiplayer (Relay) — no standalone Lobby/Relay.
- Multi-platform (Steam/Epic potential), avoid store-locked design.

## MVP Flow (Do Not Break)
1. MainMenu → Lobby → GameMap
2. 4-player spawn must stay functional
3. Scene transitions: MainMenu→Lobby via `NetworkSessionManager.LoadLobbyScene()` (host calls `NetworkSceneManager.LoadScene`, clients auto-sync via NGO). Lobby→GameMap via `NetworkLobbyFlow.RequestStartGameFromLobby()` (`NetworkSceneManager.LoadScene`).
4. Host leave/shutdown path handled safely.

## Architecture & Singletons
All use `Instance` + `DontDestroyOnLoad` (no DI):
- `GameManager` — scene names + `SceneManager.LoadScene()` for MainMenu return
- `NetworkSessionManager` — wraps NGO NetworkManager, session lifecycle, Relay, ready state
- `UgsServiceManager` — UGS init + anonymous auth
- `LoadingScreenManager` — runtime loading overlay (Korean UI: "로딩 중...")

`RuntimeBootstrap` in each scene auto-creates any missing singleton on Awake.

## Networking Patterns
- **ServerRpc (11 total)**: `MoveServerRpc` (owner→server, every Update, bundles all input) + `SetReadyServerRpc` + `PlayerLootCarrier.InteractServerRpc` / `ThrowServerRpc` / `SelectSlotServerRpc` (farming + item hotbar, see Duffel Bag section) + `LootRespawnButton.RequestRespawnServerRpc` (RangeCourse table loot, `RequireOwnership = false`) + `PlayerWeaponLoadout.SelectSlotServerRpc` / `FireServerRpc` / `ReloadServerRpc` / `SetAimServerRpc` (weapons, see Weapons section) + `PlayerAimPitch.SetPitchServerRpc` (look pitch, only when changed ≥0.3°, max 15/s)
- **ClientRpc**: NONE — transform synced via `NetworkTransform`, state via `NetworkVariable`
- **NetworkVariable write**: always `Server` permission
- **Player prefab** (`Assets/Prefabs/Player.prefab`) registered in DefaultNetworkPrefabs.asset. Components: `CharacterController`, `NetworkObject`, `NetworkTransform` (Server auth mode), `PlayerInput` (actions: `PlayerActions.inputactions`), `PlayerNetworkController`, `PlayerStamina` (auto via `[RequireComponent]`)

## Input System
- Action map name: **`"Keyboard&Mouse"`** (note the ampersand)
- Actions include `Move`, `Look`, `Jump`, `Sprint`, `Crouch`, `Prone`, `Interact` (F), `Throw` (G), `LeanLeft` (Q), `LeanRight` (E), `PrimaryWeapon` (1), `SecondaryWeapon` (2), `Fire` (LMB), `Reload` (R), `Aim` (RMB), `ItemSlot1`–`ItemSlot4` (3–6), `Holster` (X), `CycleSlot` (mouse wheel, Value/Axis). `Interact`/`Throw` are bound by `PlayerLootCarrier`; weapon actions by `PlayerWeaponLoadout`; item slot/holster/cycle by `PlayerHotbarInput` — not `PlayerNetworkController`.
- Owner-client only processes input. Non-owners: `playerInput.enabled = false`.
- Scene-aware: input enabled only in `GameMap`/`Hideout` scenes. Disabled in Lobby/MainMenu.
- Look input has `ScaleVector2(x=0.1,y=0.1)` processor (scaled at input level, NOT in code).
- Two action assets exist: `PlayerActions.inputactions` (wired to Player prefab) and `InputSystem_Actions.inputactions` (StarterAssets default, unused by gameplay).

## PlayerNetworkController (`Assets/Scripts/Network/Player/PlayerNetworkController.cs`)
- `[RequireComponent(CharacterController, NetworkTransform, PlayerStamina)]`
- **CharacterController** — not Rigidbody. Custom gravity: `verticalVelocity += gravity * dt`.
- `Update()` on owner only: reads input → calls `MoveServerRpc()`. Server validates movement, stamina, posture, parkour.
- Camera pitch is driven locally (clamped [-80, 80]). `PlayerNetworkController` never sends it. The companion `PlayerAimPitch` (NetworkBehaviour, both player prefabs, `[DefaultExecutionOrder(-50)]`) reads the camera's real elevation on the owner and sends it via `SetPitchServerRpc`. The server stores it in `netPitch` (short, 0.1° units, Server write). `Pitch` is the local value on the owner and an exponentially smoothed value on remotes. It is used only for 3rd-person visuals (`PlayerWeaponAimPose`).
- Aim sway: local-only procedural camera shake when `stamina.ShouldSway`.
- Movement is host-authoritative: client sends move/rotate/jump/sprint/crouch/prone in one RPC, server executes `CharacterController.Move()`.
- Crouch/Prone: toggles (C → crouch toggle, Z → prone toggle). CC height lerped via `postureTransitionSpeed`.
- Speeds (serialized): walk 1.2, tactical walk 2.4, full sprint 4.5, crouch 0.65, prone 0.3. `GetSpeed()` multiplies by every `IPlayerMovementModifier.SpeedMultiplier` on the player and `CanFullSprint()` requires all of them to `AllowFullSprint` (currently `PlayerLootCarrier` and `PlayerWeaponLoadout`). `LookSensitivityMultiplier` (owner-only, default 1) scales mouse look; set by `PlayerWeaponView` while aiming.
- NetworkVariables: `isReady` (bool), `netPosture` (PlayerPosture byte) — both `WritePermission.Server`.
- **Player visual (temporary)**: both `Player.prefab` and AI `TestPlayer.prefab` use child `CharacterVisual_HumanDummyF` (nested `Assets/Kevin Iglesias/Human Character Dummy/Models/HumanCharacterDummy_F.fbx`, material `Assets/Animations/Player/DummyURP.mat`, controller `PlayerLocomotion`). It is the body the weapon animations were authored on. It is kept at scale 1 (exact fit to those clips). `Cam` local y was raised from 1.44 to **1.57** to equal its bind-pose head bone height (same relation the old 1.44 camera had to the old models). Crouch/prone camera heights are ratios of this value. The previous visuals (`Assets/Models/gametest.fbx`, `X1_EP.fbx`) are still in the project but were removed from the prefabs. Keep exactly one Animator under the player, because several scripts use `GetComponentInChildren<Animator>(true)`.
- **RobotKyle.prefab** is a non-networked visual model (StarterAssets third-person, no `NetworkObject`). Leftover template content.

## Farming — Duffel Bag (`Assets/Scripts/Items/Duffel/`)
- One team `DuffelBag` per run, spawned by `DuffelBagSpawner` (GameMap, RangeCourse). Prefab `Assets/Prefabs/DuffelBag.prefab`, visual `Assets/Models/DuffelBag/SM_DuffelBag.prefab` (procedural low-poly mesh; regenerate via `Tools/OSCAR-MIKE/Generate Duffel Bag Model`, source `Assets/Scripts/Items/Editor/DuffelBagMeshGenerator.cs`).
- The bag stores **value only** (`storedValue`), not items. Deposited loot is despawned.
- `PlayerLootCarrier` (on Player.prefab and AI TestPlayer.prefab): 4 **fixed** personal slots (`NetworkList<LootSlotEntry>`, always `slotCount` entries, empty entry = free slot) + `selectedSlot` (`NetworkVariable<sbyte>`, Server write; −1 = holding a weapon, 0–3 = holding that slot, empty slot = empty hands). There is no separate hand slot any more: `HandItem` = the selected slot. Picked loot is despawned and kept as `LootSlotEntry`. Dropping the hand item re-spawns it from `lootTables` (`FindDefinition` is public and works on every peer).
- **Item hotbar** (Ticket 6): 1/2 weapons, 3–6 item slots (same key again → back to weapon), X holster, wheel cycles 1→6 skipping empty item slots / blocked primary. `PlayerHotbarInput` (local MonoBehaviour, both player prefabs) reads 3–6/X/wheel and calls `PlayerLootCarrier.RequestSelectSlot` or `PlayerWeaponLoadout.RequestSlot`. `PlayerWeaponLoadout.SelectSlotServerRpc` calls `PlayerLootCarrier.ServerHolster()` first, so 1/2 always return to a weapon. `PlayerWeaponLoadout.Holstered` (= `selectedSlot ≥ 0`) blocks fire/reload/aim on owner and server, `PlayerWeaponView` hides the gun models, `PlayerWeaponAnimator` drops the UpperBody layer to 0 (aim pose and hand IK follow), and leaving the holster applies `DrawTime`. `PlayerHeldItemView` (local, both prefabs, `[DefaultExecutionOrder(10)]`) shows a stripped render copy of the held item's `NetworkPrefab` in front of the right hand (all peers; owner copy on `LocalPlayerVisual`) and under the owner camera, scaled down to `maxWorldSize`/`maxViewSize`.
- F: pick up item (empty hands → straight into the selected slot; holding an item → first free slot and hold it; holding a weapon → first free slot only) / deposit **only the held item** into a bag (on the ground, on a teammate's back, or your own; one item per F — then the next filled slot to the right, wrapping, is held automatically, or back to weapon when none is left; holding a weapon deposits nothing and the prompt says `HoldToDeposit` "[3~6] 넣을 아이템을 손에 드세요") / shoulder a bag when nothing is held. G: drop the hand item (selection stays → empty hands) or, if nothing is held, throw bag (crouched/prone = put down). Death drops the held item and returns to weapon.
- **HUD layout**: bottom-centre = item grid (`PlayerLootHud`, 4 square cells `slotSize` 88 with key 3–6, item icon, small value, selected frame; name only as fallback when no icon) with held item name / carried value / duffel status above it. Icons come from `LootItemIconCache`: the item's `NetworkPrefab` is copied (`LootItemVisual.Create`, shared with `PlayerHeldItemView`) and rendered once by a hidden camera 5 km below the map (two local point lights, transparent background, 128² `RenderTexture` cached per item id) — no icon assets needed; bottom-left = health (`PlayerHealthHud`) + stamina (`PlayerStaminaHud`); bottom-right = ammo + weapon slots (`PlayerWeaponHud`, dimmed while holstered). Loot/health/stamina canvases use `CanvasScaler` 1920×1080, match 0.5.
- Carrying is a fixed debuff (×0.7 speed, no full sprint), regardless of value. `CanUsePrimaryWeapon == false` while carrying (hook for the future weapon system; `RangeTestGun` already honors it).
- Hits: `DuffelBagHitbox` on layer `Hitbox` (collides with nothing, raycasts only). `DuffelBag.ServerApplyHit(damage)` removes `ceil(value × damage × 0.005)`. The money FX plays on all peers via the `hitSequence` NetworkVariable.
- The carried bag is NGO-parented to the player (`TrySetParent`). It is released on death and in `OnNetworkPreDespawn`.

## Table Loot — RangeCourse (`Assets/Scripts/Items/TableLoot/`)
- The east room of RangeCourse (x 51–90, z −9–43; entrance gap in the west wall at x≈50.9, z −0.7–3.6) has 12 `item table` objects with `LootTableSurface` (BoxCollider top, `itemCount` 2–4, `edgeInset`).
- Per table: `grade` (`LootTableGrade` 일반/고급/희귀/영웅/전설) and `allowedCategories` (`LootCategoryMask` flags, Nothing = all), both in `LootTableGrade.cs`. Gizmo colour shows the grade.
- `TableLootSpawner` (GameObject `Table Loot Spawner`, server-only MonoBehaviour) uses `CatalogLootTable` (+ optional `zone` for `allowedZoneIds`; it does not use `LootTable.TryRoll` or an economy profile). Per item: (1) a value tier 1–5 (tier t = valueSteps 2t−1..2t) is rolled with the table grade's `gradeWeights` row (defaults 일반 60/25/10/4/1, 고급 35/35/20/8/2, 희귀 15/30/30/18/7, 영웅 5/15/30/30/20, 전설 0/5/20/35/40). Tiers with no allowed item are dropped and the rest renormalised; if nothing is left, it falls back to the highest available tier. (2) An item that can produce that tier is picked by `SpawnWeight`. `SimulateTierCounts(table, n)` is an editor/debug helper. The spawner places each item so its renderer bottom touches the tabletop, without overlap. It spawns once when the host starts (polled in `Update`, like `DuffelBagSpawner`). `ServerRespawn()` despawns its own items still lying around (picked-up items are already despawned) and rolls a new set.
- `LootRespawnButton` (GameObject `Loot Respawn Button`, in-scene `NetworkObject`, on the outer face of the west wall next to the entrance): the local player looks at it within 2.5 m and presses F (`Interact`) → `RequestRespawnServerRpc` (server checks distance, alive, 2 s cooldown). IMGUI prompt "[F] 아이템 재스폰"; the cap press anim plays on all peers via `pressSequence`. F also reaches `PlayerLootCarrier` (no target → deposits into your own bag if you carry one).

## Weapons (`Assets/Scripts/Weapons/`)
- Throwables are out of scope for now. Two slots: **Primary** (1, M4A1) and **Secondary** (2, Glock 17), both given on spawn.
- `WeaponDefinition` (ScriptableObject): stats (damage, fire interval, auto/semi, magazine/reserve, reload/draw time, range, spread) + view/world model prefab & offsets. Assets: `Assets/Prefabs/Weapons/Primary/Weapon_M4A1.asset`, `Assets/Prefabs/Weapons/Sidearms/Weapon_Glock17.asset`; model prefabs `M4A1.prefab` / `Glock17.prefab` (grip at origin, muzzle +Z).
- `PlayerWeaponLoadout` (NetworkBehaviour on Player.prefab and AI TestPlayer.prefab — RangeCourse/EnemyAITest use TestPlayer): owner sends slot/fire/reload RPCs; server validates (alive, draw/reload, fire rate, ammo, no FullSprint/Vault/Dive/Slide, origin within 2.5 m) and does the hitscan. NetworkVariables: `activeSlot`, `primaryAmmo`/`secondaryAmmo` (`WeaponAmmo`), `reloading`, `lastShot` (`ShotEvent` = sequence + `ShotImpact` {point, normal, `ImpactKind`}, remote muzzle/tracer/impact FX), `hitSequence` (owner hit marker; low bit = head). Spread is applied by the owner before sending the direction, so the owner's local tracer and the server hit use the same ray (`FindImpact`).
- **Aim (ADS)**: RMB, hold or toggle per `GameSettings.AimMode` (PlayerPrefs, `Assets/Scripts/Core/GameSettings.cs`; also `AimSensitivity`). Owner sends `SetAimServerRpc` on change → `aiming` NetworkVariable; `PlayerWeaponLoadout` implements `IPlayerMovementModifier` (aiming: speed × `aimMoveSpeedMultiplier`, no full sprint). Owner-local `AimBlend` (0–1 over `aimTime`, drops to 0 while reloading/drawing/sprinting/vaulting) drives: view model hip pose → `WeaponDefinition.GetAimPose()` — computed from model-space `sightRear`/`sightFront` + `aimEyeRelief` so the rear→front sight line lies exactly on the camera's forward axis (the M4A1 sight line is ~1.15° below the bore, so the gun is pitched up; `aimViewOffset` is only a fallback when no sight points are set). Sight points were measured from the meshes: M4A1 aperture centre (0.00025, 0.12425, 0.02) / front post tip (0, 0.1162, 0.4247); Glock tritium dots (0.0001, 0.0155, -0.0731) / (0, 0.0152, 0.0811). Also camera FOV → `aimFov`, look sensitivity × tan(aimFov/2)/tan(baseFov/2) × `AimSensitivity`, spread × `aimSpreadMultiplier`, view kick × `aimKickMultiplier`, crosshair hidden (hit marker still shown).
- **Settings menu**: `GameSettingsMenu` (IMGUI, on both player prefabs) opens with Esc while gameplay input is enabled (not in `EnemyAITest`, which has its own Esc menu); disables gameplay input while open. Options: 조준 방식 (누르고 있기/토글), 조준 감도.
- **Recoil & spray** (owner-local, no network state): `WeaponDefinition` holds `recoilPitchCurve`/`recoilYawCurve` (x = shot index, y = **cumulative** degrees; per-shot kick = curve(n+1) − curve(n)), `recoilRandomYaw`, `recoilRecovery`, `recoilReturnRatio`, `spreadCurve` (shot index → cone half-angle, falls back to `spreadDegrees` when empty) and `patternResetTime`. Custom inspector `Weapons/Editor/WeaponDefinitionEditor.cs` draws a 2D preview (aim path, spread circles, simulated hits). `WeaponRecoil` (`[DefaultExecutionOrder(50)]`, on both player prefabs) layers the offset onto the camera after `PlayerNetworkController` overwrites its pitch, so pulling the mouse down compensates; `PlayerWeaponLoadout` (`[DefaultExecutionOrder(60)]`) fires along that camera, applies `spreadCurve × posture/move/airborne/sway multipliers`, then calls `WeaponRecoil.Kick`. Shot index resets on slot change, reload, or after `patternResetTime`.
- `BulletTracer` (pooled by `PlayerWeaponView`, material `Assets/Prefabs/Weapons/FX/M_BulletTracer.mat`): visual-only streak from the muzzle to the impact point (owner: 1st-person muzzle, immediately; others: 3rd-person muzzle when `lastShot` changes).
- Impact FX play when the tracer arrives (`WeaponImpactPool`, prefabs in `Assets/Prefabs/Weapons/FX/`): `ImpactKind.Surface` (static world) = sparks + dust + `BulletHole` (max 32, hidden after 20 s; random roll, scale 0.75–1.3 and one of 4 noisy variants from the 2×2 atlas `T_BulletHole.png` via `_BaseMap_ST` — `M_BulletHole` must stay on URP/Unlit, since URP particle shaders ignore tiling); `Target` (dummies, duffel bag, dynamic rigidbodies) = sparks + dust, no hole; `Flesh` (`EnemyHealth`/other `PlayerHealth`) = blood; `None` (max range) = nothing.
- Hit order: `RangeTargetHitbox` → `DuffelBagHitbox` → `EnemyHealth` (in parent) → `PlayerHealth` (only if `friendlyFire`). Region from optional `WeaponHitbox` marker, default Body. Non-trigger colliders block the shot.
- Duffel rule: while `PlayerLootCarrier.CanUsePrimaryWeapon == false` the server forces Secondary and refuses Primary.
- **3rd-person weapon animation**: clips from `Assets/Animations/M4A1/M4A1_Character.fbx` / `Glock/Glock_Character.fbx` (Humanoid, renamed `M4A1_*` / `Glock_*`; no fire/reload/draw clips, gun-part bones not kept). `PlayerLocomotion.controller` layer order: Base → `UpperBody` (states `Rifle_/Pistol_` × `Stand/Crouch/Prone`, Speed blend trees, AnyState by `WeaponSlot` int + `Posture`) → `WeaponAim` (`*_Aim_Idle`). The old additive `Lean` layer was **removed**. Its clips (`Anim/Lean/*`) and the `Lean`/`LeanPoseTime` parameters still exist, and `PlayerAnimatorParameters` skips the layer weight when the layer is missing. Q/E body lean is now procedural (see 3rd-person aim pose). Both weapon layers use `WeaponArmsMask` (head + arms only; spine stays on the base layer, otherwise crouch double-bends). `PlayerWeaponAnimator` (local MonoBehaviour, both player prefabs) sets `WeaponSlot` and the layer weights (UpperBody 0 when dead/no loadout; WeaponAim follows `aiming` over `AimTime`, off while reloading/prone). Q/E, Jump, Sit/Prone enter and Aim_start/stop clips are imported but unused.
- **3rd-person grip**: in those FBX files the gun is rigid on `B-handProp.R` (rig bone `Rig_rig/Bone`, deviation ≈0 over every clip), but the gun armature itself is not animated and the Humanoid import drops it. So `WeaponDefinition.useHandGrip` + `handGripOffset`/`handGripEuler` place the world model in a rig-independent **hand frame** (`PlayerWeaponView.TryGetHandFrame`: origin RightHand, +Z = hand→RightMiddleProximal, +Y = Cross(along, RightIndexProximal−RightLittleProximal)). M4A1 was measured from the FBX rest pose (aligned by bounds centre). The FBX rest relation is **wrong for the Glock**: the grip sits ~15 cm behind and below the hands. So the Glock was fitted in play mode on the Glock aim pose instead. Our grip centre (model (0, −0.075, −0.066)) is placed at the midpoint between the palm (RightHand↔RightMiddleProximal) and the curled middle/ring/little intermediate joints, giving `handGripOffset` (0.0639, 0.0231, 0.1696). When checking 3rd-person visuals in play mode, hide the owner's 1st-person view model with `Renderer.forceRenderingOff` (`enabled = false` is undone every frame), or screenshots show the view model instead. With `useHandGrip` off, the old body-forward `worldOffset`/`worldEuler` placement is used.
- **3rd-person aim pose**: `PlayerWeaponAimPose` (local MonoBehaviour, both player prefabs, `[DefaultExecutionOrder(-40)]`). Because the weapon layers mask out the spine, the authored torso twist is lost, and the raw 3rd-person gun points ~31–33° left and 7–18° up (stand/crouch; prone 7°/2.5°). Every frame it computes the gun direction exactly as `PlayerWeaponView` will (right hand frame × `HandGripRotation`). It takes `FromToRotation` to the aim direction (body yaw + `PlayerAimPitch.Pitch`, clamped 70°, prone 25°, correction ≤90°), weights it by the `UpperBody` layer weight, and splits it over Spine/Chest/UpperChest (0.35/0.4/0.25) around one axis. The result is measured at 0° yaw and exact pitch error. Unarmed, the torso just bends by `pitch × 0.5`. **Q/E lean** is applied first in the same component: `controller.Lean` (netLean) is smoothed at 5/s like the camera. The torso rolls about the body forward axis with the waist (Spine) as pivot, split Spine/Chest/UpperChest 0.6/0.3/0.1. The angle is `asin(leanDistance 0.22 / waist→head distance)` (≈21°, max 30°), so the head moves about as far as the 1st-person camera. The aim correction then re-aims the canted gun. Frame order: Animator → `PlayerAimPitch` (-50) → `PlayerWeaponAimPose` (-40) → `PlayerWeaponView` gun align (0) → `PlayerWeaponHandIK` (100).
- **Support-hand IK**: `PlayerWeaponHandIK` (local MonoBehaviour, `[DefaultExecutionOrder(100)]`, on both player prefabs) runs after `PlayerWeaponView.LateUpdate` has aligned the gun (`PlayerWeaponView.ActiveWorldModel`). It solves an analytic two-bone IK on LeftUpperArm/LeftLowerArm (keeping the animated elbow bend plane), then rotates LeftHand so the left hand frame matches `WeaponDefinition.supportHandOffset`/`supportHandEuler` (gun-model space; `useSupportHandIK` toggles it). The weight is the `UpperBody` layer weight. Values were measured on HumanDummy_F and are constant over every state (≤2 mm/0.3°), so on that body the IK is a no-op. It only matters for other bodies or blends. Out-of-reach targets clamp at full arm extension (no clavicle). Humanoid Animators rewrite bone local positions every frame, so test other proportions by shifting `handGripOffset` temporarily, not by stretching bones.
- `PlayerWeaponView` (local only): owner 1st-person model under the camera; 3rd-person model on the humanoid right hand (owner copy on `LocalPlayerVisual`). `PlayerWeaponHud`: ammo, slot list, crosshair, hit marker.
- **RangeCourse sessions**: `RangeSessionBootstrap` (`Assets/Scripts/Testing/`, GameObject `RangeCourse Session`) replaces the old auto-host. `MovementTestAutoHost` on `RangeCourse Auto Host` is kept but **disabled**. A centred IMGUI panel offers 호스트 시작 (UTP listens on 0.0.0.0:7777, so the same PC and LAN can join) and 주소로 참가 (IP field, default 127.0.0.1). It turns on ConnectionApproval (max 4; spawn slots at origin, +1.5 m, −1.5 m, +3 m on X) and calls `EnableGameplayInput()` on the local player once it spawns. It keeps the overview camera enabled until connected, and shows a status line (role, player count, LAN address) while in a session. It does not support Relay join codes. For two-player tests use Multiplayer Play Mode: host in the main editor, 주소로 참가 127.0.0.1 in the virtual player.
- `RangeTestGun` stays in RangeCourse for dummy labels / F5 reset but does nothing else when the player has `PlayerWeaponLoadout`.

## Session / Ready State Flow
```
MainMenu UI → Host/Join → SessionStarted → host: NetworkSessionManager.LoadLobbyScene()
Lobby UI → ready toggle → NetworkSessionManager.ToggleLocalReady()
         → PlayerNetworkController.SetReady() → SetReadyServerRpc() → isReady.Value = true
         → ReadyStateChangedGlobal event → NetworkSessionManager.ReadyStatesChanged → UI refresh
Lobby UI → Start → NetworkLobbyFlow.RequestStartGameFromLobby()
         → checks IsHost + CanStartGame() (players > 0, all ready)
         → NetworkSceneManager.LoadScene("GameMap")
```
- Late-joining clients receive current `isReady` values via NetworkVariable sync.
- `LobbyUIController` subscribes/unsubscribes events in `OnEnable`/`OnDisable`.

## Stamina (`PlayerStamina`)
- Server-only updates via `ServerTick()`. `NetworkVariable<float>` propagates to clients.
- Drain: sprint 15/s, overweight walk 5/s. Consumption: jump 10, parkour 12. Recovery 10/s.
- `ShouldSway` = low stamina OR post-sprint cooldown (used by aim sway).

## UI Conventions
- All UI text in **Korean** (e.g., "로딩 중...", "준비", "참가 코드", "세션: 호스트").
- `LobbyUIInstaller` + `LobbyUIInstallerEditor` is a one-shot editor tool to build lobby UI. Use `[ContextMenu("Create Lobby UI")]` or inspector button. Remove installer from scene after use.
- Font: `Assets/Fonts/Pretendard-Regular.otf` (Korean-friendly) with TMP SDF asset.

## Quirks & Gotchas
1. `PlayerNetworkController` binds input actions by **string name** via `playerInput.actions.FindAction(name)`. If rename actions in `.inputactions`, update the `[SerializeField]` strings too.
2. Action map name `"Keyboard&Mouse"` — do NOT change to `"Keyboard and Mouse"` unless you update `PlayerInput` on the prefab.
3. `NetworkSessionManager` and `GameManager` are **NOT** NetworkBehaviours — they're plain MonoBehaviours with `DontDestroyOnLoad`.
4. `SceneManager.LoadScene()` (UnityEngine) destroys player objects. Only `NetworkSceneManager.LoadScene()` preserves them.
5. `LoadingScreenManager.LoadNetworkScene()` is a stub — it only shows overlay. Actual scene load is driven by NGO.
6. No auto-tests exist yet. Verify regressions manually: scene transitions, 4-player join/spawn, host leave, join code display.

## Relevant Files
- `Assets/Scripts/Network/Player/PlayerNetworkController.cs` — main player controller
- `Assets/Scripts/Network/Player/PlayerStamina.cs` — stamina system
- `Assets/Scripts/Network/Player/PlayerMovementState.cs` — PlayerPosture/PlayerMoveState enums
- `Assets/Scripts/Network/NetworkSessionManager.cs` — session lifecycle, Relay, ready tracking
- `Assets/Scripts/Network/NetworkLobbyFlow.cs` — Lobby→GameMap transition
- `Assets/Scripts/UI/MainMenuUIController.cs` — host/join buttons
- `Assets/Scripts/UI/LobbyUIController.cs` — lobby UI (player list, ready, start, leave)
- `Assets/Scripts/Core/RuntimeBootstrap.cs` — singleton auto-spawner
- `Assets/Scripts/GameManager.cs` — scene name config + local scene loads
- `Assets/PlayerActions.inputactions` — gameplay input bindings
- `Assets/Prefabs/Player.prefab` — networked player prefab
- `MVP_TASK_TICKETS.md` — ticket breakdown for ready/ farming/ inventory sync

## Delivery Notes
- Preserve existing skeleton; extend incrementally. Prefer additive changes over rewrites.
- Before implementing, re-check this `AGENTS.md` and apply its project rules to the change.
- Do not directly edit scripts that are not clearly user-created/user-modified for the current work. Prefer additive changes: create a new partial/companion script or component and wire it through scene/prefab references instead.
- Use C# `partial` only when the target class already supports partial definitions; otherwise use a separate MonoBehaviour/helper component and connect it without rewriting the original script.
- Validate behavior after each networking/UI flow change (see Regression Checklist above).
