# Online Multiplayer Battleship: Unity 6 + Netcode for GameObjects

<!-- Add a gameplay screenshot or GIF here: ![Battleship gameplay](docs/gameplay.gif) -->

A 2-player **online** Battleship game built in **Unity 6** with **C#**, **Netcode for GameObjects** and **Unity Multiplayer Services** (Authentication, Lobby, Relay). Players sign in, create or join a game with a lobby code, place their fleets, then take turns firing across the internet. A **host-authoritative** referee keeps every ship hidden until it's sunk.

▶️ **[Watch gameplay on YouTube](YOUTUBE_LINK_HERE)**

---

## Highlights

### Server-authoritative multiplayer design
The host runs all game rules (`BattleshipManager.cs`); clients can only *ask*.

| Network tool | Used for |
|---|---|
| `NetworkVariable` | Shared facts both players must agree on: game phase, whose turn, ready states, winner |
| `NetworkList<ShotInfo>` | Every shot and its hit/miss result, visible to both players |
| `ServerRpc` | Client requests: submit fleet, fire at a cell, rematch, quick-chat |
| Targeted `ClientRpc` | Ship sunk (both players), fleet rejected (sender only), enemy fleet reveal (loser only, at game over) |

**Anti-cheat by design:**
- Ship positions are **never sent over the network** while a game is running. The host keeps both fleets in plain C# fields, so no client can read the opponent's board.
- The **same placement rules** (`BattleshipTypes.cs`) run on both sides. The client uses them to show a valid/invalid preview, and the host checks every submitted fleet again.
- Out-of-turn shots, repeat shots and illegal fleets are rejected on the host.
- **Quick-chat** sends only a message ID, rate-limited by the host to one per second per player.

### Gameplay
- **Placement phase:** place 3 ships (Battleship, Cruiser, Destroyer) on a 10×10 grid (A–J, 1–10) with a live preview. Rotate with `R`, random placement and clear. Nothing is sent until you press Ready.
- **Battle phase:** turn-based firing on the enemy grid, with hit, miss and sunk feedback.
- **Game over:** winner banner, the loser sees where the enemy fleet was, and players can start a rematch.

### Game feel (`BattleshipFx.cs`)
- **Hit:** cannon shot, then a white flash, fireball, sparks, smoke and screen shake
- **Miss:** water splash with expanding rings and droplets
- **Sunk:** ship horn plus a chain of explosions along the whole ship and a big screen shake
- Win fanfare, lose trombone and an animated ocean background

### UI built entirely from code
- `BattleshipUI.cs` and `GridView.cs` generate the whole game screen at runtime: top bar, both grids with labels, ship list, buttons, message bar and game-over panel.
- **Three switchable themes:** Comic Book, Naval Command and Tropical Islands.
- `MenuStyler.cs` restyles the Login and Lobby screens to match the selected theme, without modifying the original UI scripts.

### Editor tooling
- `MultiplayerSceneBuilder.cs` adds a `Tools > Multiplayer Setup` menu that builds the Login, Lobby and Battleship scenes and wires up the NetworkManager, managers and prefab list.

---

## Flow

```
LoginScene ──► LobbyScene ──► BattleshipScene
 (anonymous     (create game /     (placement → battle → game over → rematch)
  sign-in)       join by code,
                 Relay connection)
```

## Tech

| | |
|---|---|
| Engine | Unity 6 (6000.5.9f1), URP 2D |
| Language | C# |
| Networking | Netcode for GameObjects 2.13, Unity Multiplayer Services 2.3 (Auth, Lobby, Relay) |
| Testing | Multiplayer Play Mode (two players in one editor) |
| UI | uGUI + TextMeshPro, generated from code |

## Project structure

```
Assets/Scripts/
├── Battleship/   # my Battleship game: referee, rules, UI, grids, effects, themes   (~3,100 lines)
├── Auth/         # anonymous sign-in (Unity Authentication)
├── Lobby/        # create / join a lobby by code
├── Relay/        # start host / client through Unity Relay
├── Game/         # base networked Tic-Tac-Toe the project started from
└── Editor/       # one-click scene builder
```

The Auth, Lobby, Relay and Game folders come from the course's networked Tic-Tac-Toe starter framework. I built the Battleship game on top of it: everything in `Scripts/Battleship/`, plus the scene builder.

## How to run

1. Clone the repo and open it in **Unity 6000.5.9f1** via Unity Hub
2. In **Edit > Project Settings > Services**, link the project to your own Unity Cloud project, and enable **Lobby** and **Relay** in the Unity Dashboard
3. Open `Assets/Scenes/LoginScene.unity`
4. Open **Window > Multiplayer > Multiplayer Play Mode** and enable a second player
5. Press **Play**. Sign in on both players, **Create Game** on one, then **Join** with the lobby code on the other

## About

Built by **Yashvanthini Uppala**, MS Computer Science, University of Dayton (Fall 2026).

[LinkedIn](https://linkedin.com/in/yashvanthiniuppala-a926203b5) · yashvanthiniuppala@gmail.com
