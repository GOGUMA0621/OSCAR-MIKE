# Project Directives

## Vision
- Build a Unity 6000.3.9f1 FPS co-op scavenging game inspired by Lethal Company.
- Target up to 4 players.

## Networking and Platform
- Use NGO (Netcode for GameObjects), not Mirror.
- Keep host-authoritative gameplay ownership.
- Stay multi-platform ready (Steam + Epic potential) and avoid store-locked design.
- Keep UGS Multiplayer-centered stack; do not mix standalone Lobby/Relay package paths.

## MVP Flow (Do Not Break)
- Main Menu -> Lobby -> Game Map.
- 4-player spawn must remain functional.

## Session and Relay Rules
- Relay join-code is the primary session mechanism.
- Host/Join/Start/Leave lifecycle must be event-driven and stable.
- Preserve reconnection-safe state handling where possible.

## UI Rules
- Main menu: join-code input/host-create focused UX.
- Lobby: player list, session state, and join-code display are mandatory baseline elements.

## Input Rules
- Input System with PlayerInput action maps.
- Only the owning client processes player input.

## Expansion Priorities
1. Ready-state synchronization.
2. Farming/interaction synchronization.
3. Grid inventory network model (Tarkov-like direction).

## Regression Checklist (Required)
- Scene transitions work from Main Menu -> Lobby -> Game Map.
- First connection and reconnect flows are stable.
- 4 concurrent players can join and spawn.
- Host leave/shutdown path is handled safely.

## Delivery Notes
- Preserve existing skeleton and extend incrementally.
- Prefer additive changes over large rewrites.
- Validate behavior after each networking/UI flow change.
