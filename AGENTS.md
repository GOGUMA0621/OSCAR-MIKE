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
- **ServerRpc (2 total)**: `MoveServerRpc` (owner→server, every Update, bundles all input) + `SetReadyServerRpc`
- **ClientRpc**: NONE — transform synced via `NetworkTransform`, state via `NetworkVariable`
- **NetworkVariable write**: always `Server` permission
- **Player prefab** (`Assets/Prefabs/Player.prefab`) registered in DefaultNetworkPrefabs.asset. Components: `CharacterController`, `NetworkObject`, `NetworkTransform` (Server auth mode), `PlayerInput` (actions: `PlayerActions.inputactions`), `PlayerNetworkController`, `PlayerStamina` (auto via `[RequireComponent]`)

## Input System
- Action map name: **`"Keyboard&Mouse"`** (note the ampersand)
- 7 actions: `Move`, `Look`, `Jump`, `Sprint`, `Crouch`, `Prone`, `Interact` (Interact not wired yet)
- Owner-client only processes input. Non-owners: `playerInput.enabled = false`.
- Scene-aware: input enabled only in `GameMap`/`Hideout` scenes. Disabled in Lobby/MainMenu.
- Look input has `ScaleVector2(x=0.1,y=0.1)` processor (scaled at input level, NOT in code).
- Two action assets exist: `PlayerActions.inputactions` (wired to Player prefab) and `InputSystem_Actions.inputactions` (StarterAssets default, unused by gameplay).

## PlayerNetworkController (`Assets/Scripts/Network/Player/PlayerNetworkController.cs`)
- `[RequireComponent(CharacterController, NetworkTransform, PlayerStamina)]`
- **CharacterController** — not Rigidbody. Custom gravity: `verticalVelocity += gravity * dt`.
- `Update()` on owner only: reads input → calls `MoveServerRpc()`. Server validates movement, stamina, posture, parkour.
- Camera pitch is purely local (never sent over network). Clamped [-80, 80].
- Aim sway: local-only procedural camera shake when `stamina.ShouldSway`.
- Movement is host-authoritative: client sends move/rotate/jump/sprint/crouch/prone in one RPC, server executes `CharacterController.Move()`.
- Crouch/Prone: toggles (C → crouch toggle, Z → prone toggle). CC height lerped via `postureTransitionSpeed`.
- Player speed config on prefab: walk 4.5, sprint 8.0, crouch 2.5, prone 1.2. Jump height 1.2, gravity -15.
- NetworkVariables: `isReady` (bool), `netPosture` (PlayerPosture byte) — both `WritePermission.Server`.
- **RobotKyle.prefab** is a non-networked visual model (StarterAssets third-person, no `NetworkObject`). Leftover template content.

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
- Validate behavior after each networking/UI flow change (see Regression Checklist above).
