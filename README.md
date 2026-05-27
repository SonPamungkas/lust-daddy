# LUST DADDY Unit Customization Overhaul
<img width="1594" height="1080" alt="L-Daddy (1)" src="https://github.com/user-attachments/assets/d98c2934-21cf-4eea-9e33-5403a5380622" />

## Description
LUST (Loadout Utility & Swapping Tool) is a powerful, real-time in-game editor and modification framework for Nuclear Option that grants you absolute control over the game's vehicles, aircraft, and weapon platforms. 

At the core of LUST is the **Dynamic Asset Deployment & Designation Yield (DADDY)** sub-system. DADDY intercepts the game's prefab instantiation sequence, allowing you to seamlessly transplant turrets, tweak base stats, and create monstrous hybrid units without ever touching a line of code. Because sometimes, units just need their DADDY to help them reach their true potential.

## How it Works
LUST DADDY features a robust, user-friendly in-game UI to manipulate unit attributes in real-time.

1. **In-Game Editor (Press F8)**
   - Open the LUST DADDY interface at any time to browse all loaded Units, Turrets, and Payloads.
   - Select any vehicle (e.g., `6x6_1_AA`) to expose its internal components.

2. **Turret & Weapon Swapping**
   - The UI automatically maps all available turret hardpoints on the selected unit.
   - Select from a drop-down list of every turret in the game to perform seamless swaps (e.g., mounting a heavy naval CIWS onto a light truck).
   - DADDY handles all the complex logic behind the scenes, safely stripping incompatible `ShipPart` components from naval turrets when mounted on land vehicles to prevent engine crashes, while keeping secondary child-guns perfectly synced.

3. **Live Parameter Tuning**
   - Dive into the component level to modify core vehicle stats like HitPoints, Radar Range, Armor, Mass, and Speed.
   - Edit numeric inputs, booleans, strings, and enums directly through the UI.

4. **Persistent JSON Injection**
   - Clicking "Save Config" automatically serializes your exact turret loadouts and field modifications into a clean JSON file.
   - Upon restarting the game, DADDY intercepts the prefab generation sequence. It surgically injects your JSON configurations *before* instances are spawned, ensuring flawless integration with vanilla gameplay and the mission editor.

## Configuration & Usage
1. Launch the game and press **F9** to open the LUST DADDY interface.
2. Select a vehicle from the "Units" tab.
3. Assign new turrets using the drop-down cycler, or scroll down to tweak base component stats (HP, speed, radar range, etc.).
4. Click **Save Config**.
5. Your saved configurations are located in `BepInEx/config/LustDaddy/<UnitName>.json`.
6. **Restart the game** to see your custom hybrid units tear up the battlefield!
