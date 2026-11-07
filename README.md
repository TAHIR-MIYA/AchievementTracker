# Universal Achievement Tracker 🏆

A lightweight, standalone Windows desktop application that provides a universal, PS5-style achievement overlay for local, emulated, and Steam games. Built with C# and .NET 8, it utilizes asynchronous file monitoring and native Windows GDI32 rendering to deliver zero-latency UI popups without hooking into game memory.

---

## 🏗️ System Architecture

| Component | Technology | Description |
| :--- | :--- | :--- |
| **Core Engine** | `FileSystemWatcher` | Asynchronously monitors local state files (`.ini` / `.txt`) for state changes, bypassing file-lock collisions. |
| **Overlay UI** | Windows Forms + GDI32 | Renders a borderless, rounded-corner notification system that bypasses standard Windows focus stealing. |
| **Audio Subsystem** | `winmm.dll` API | Directly interfaces with the OS for native, zero-dependency `.mp3` playback. |
| **Steam Engine** | Steam Web API | Fetches authenticated user data, library details, and game icons directly from Steam's remote servers. |

---

## 🚀 Installation Guide

Because this application is built as a self-contained `.exe`, no installation wizards or framework downloads are required.

1. Navigate to the [Releases](../../releases) tab on the right side of this repository.
2. Download the latest `AchievementTracker_vX.X.zip`.
3. Extract the `.zip` file into a dedicated folder on your PC (e.g., `C:\Games\AchievementTracker\`).
4. **Crucial:** Ensure `AchievementTracker.exe`, `app_icon.ico`, and `unlock.mp3` remain in the exact same folder.
5. Double-click `AchievementTracker.exe` to launch the application. The app will minimize to your System Tray (bottom right of your taskbar).

---

## ⚙️ Configuration & Steam Setup

To track your actual Steam library and fetch high-resolution game icons, the application requires read-only access to your public Steam profile via a developer API key.

### Step 1: Obtain a Steam Web API Key
1. Go to the official Steam Developer portal: [https://steamcommunity.com/dev/apikey](https://steamcommunity.com/dev/apikey)
2. Log in with your Steam account.
3. In the "Domain Name" box, you can enter `localhost` or `AchievementTracker`.
4. Check the terms box and click **Register**.
5. Copy the 32-character alphanumeric **Key** that is generated.

### Step 2: Obtain your Steam ID64
1. Open your Steam client, click your profile name in the top right, and select **Account Details**.
2. Your 17-digit Steam ID will be displayed in large text just beneath your account name (e.g., `76561198XXXXXXX`).

### Step 3: Link to the Application
1. Open the Universal Achievement Tracker dashboard from your System Tray.
2. Navigate to the **Settings** or **Steam Config** section.
3. Paste your **Steam API Key** and your **Steam ID64** into their respective fields.
4. Save the configuration. The app will immediately authenticate and begin downloading your library metadata.

*(Note: Your Steam profile must be set to "Public" for the API to read your game list and achievement progress).*

---

## 🎮 How to Use & Test the Tracker

### Testing the PS5 Overlay (Local Watcher)
The application monitors your designated local achievements file for state changes. 
1. Open your local tracking file (e.g., `achievements.ini`) in Notepad.
2. Find an achievement marked as `Achieved=0`.
3. Change the value to `Achieved=1` and press **Ctrl+S** (Save).
4. The file-watcher engine will instantly detect the write operation, trigger the PS5-style UI overlay, and play the notification audio.
5. To test again, change it back to `0`, save, then change to `1` and save.

### Customizing the Audio and Icon
The application is designed to be modular. You can inject your own assets without recompiling the code:
* **Custom Sound:** Replace `unlock.mp3` in the application folder with any other `.mp3` file. You **must** rename your new file to exactly `unlock.mp3`.
* **Custom UI Icon:** Replace `app_icon.ico` with any other `.ico` file. You **must** rename your new file to exactly `app_icon.ico`. 

*(Restart the application after changing assets to clear the memory cache).*

---

## 🎥 Recording & Streaming (OBS)

OBS **Game Capture** only records what the game itself draws, so a separate popup window never shows up in it. Pick one of these:

| Method | How | Notes |
| :--- | :--- | :--- |
| **Browser Source (recommended)** | In the tracker dashboard click **Copy OBS URL**. In OBS: *Sources → + → Browser*, paste the URL, set Width/Height to your canvas (e.g. `1920 x 1080`). | Works together with Game Capture, no matter how the game renders. The popup is drawn by OBS itself. |
| **Display Capture** | Record the whole monitor instead of Game Capture. | Captures the desktop popup too. Don't enable the Browser Source as well, or you'll get two popups. |
| **Window Capture** | Add a *Window Capture* for the window titled `AchievementTracker Overlay`. | Least reliable, because the window only exists for ~5 seconds per unlock. |

**Browser Source options** (add to the end of the URL):

* `?pos=top-left`, `?pos=bottom-right`, `?pos=bottom-left` – move the popup (default is `top-right`).
* `?sound=1` – play the unlock sound inside the browser source. Tick *Control audio via OBS* on the source so it lands in your recording.

Open **Settings** in the app to use the **Test popup** button, switch the **Desktop popup** and **OBS browser overlay** on or off, and set the **Sound volume** of the desktop popup.

* `&volume=0.3` - volume of the sound inside the browser source (0 to 1, used together with `?sound=1`).
* `&pos=bottom-left` and `&seconds=8` - override the position and duration chosen in **Settings** (otherwise the app's settings are used, live).

---

## ✨ Features

* **Rarity:** the popup and the achievement list show how rare each achievement is (for example "Rare 4.2%"), from Steam's global unlock percentages. Rare and ultra rare ones are highlighted in gold. No API key needed.
* **Auto-fill and covers:** type a Steam App ID and the game's real name is filled in from the Steam store. A wrong App ID is flagged right away. Games show their Steam cover art in the Library (cached in `%APPDATA%\AchievementTracker\covers`).
* **Popup settings:** choose the corner (top left, top right, bottom left, bottom right) and how long the popup stays (3, 5, 8 or 12 seconds). The OBS overlay follows the same settings.
* **Activity:** every unlock is recorded (newest 500 are kept) and listed on the **Activity** tab with the game, time and rarity. Turn on **Screenshot on unlock** to also save a picture of your game to `Pictures\AchievementTracker\<game>\`; double-click an entry to open its screenshot. Screenshots work in Borderless or Windowed mode, in true fullscreen they can come out black.
* **Search and filters:** in a game's achievement list, search by name or description and switch between All, Unlocked and Locked.
* **Timeline:** each unlocked achievement shows the date you got it, a bar chart in the header shows when you unlocked things (per day, week or month) and how many days it took, and the **Recent** filter lists your unlocks newest first. The dates come from the emulator's own files (`UnlockTime` in CODEX-style files, `earned_time` for Goldberg).
* **Progress in the Library:** every game card shows how many achievements you have (for example "12 of 24 unlocked"), a percentage and a progress bar that turns green when you finish the game. It needs your Steam API key, because the totals come from Steam. Ultra rare unlocks (under 5% of players) get a gold border on the popup, on screen and in OBS.
* **Milestone popups:** when an unlock takes a game past 25, 50, 75 or 100 percent of its achievements, a special popup shows the game's cover art and how far you are (for example "Elden Ring: 50% complete, 12 of 24"). It needs the game's achievement names, so save your Steam API key first. Switch it off under **Settings, General**, or try it with **Test milestone popup** in the tray menu.
* **Start with Windows:** turn it on in **Settings, General** (or from the tray menu) and the tracker starts hidden in the tray when you log in, so no unlock is missed. The tray menu also has **Open Activity** and **Test popup**. It uses your own Run list in the registry, so no administrator rights are needed. If you move the app, start it once from its new place and it fixes its own entry.
* **Stats and export:** the **Activity** tab shows unlocks this week, your rarest unlock, your most active game and your daily streak, and **Export CSV** saves the whole history as a file Excel opens correctly.
* **Backup (including game saves):** on the **Backup** tab, pick a folder (inside Google Drive, OneDrive or Dropbox if you want it in the cloud) and the app saves a zip with the **save files of your tracked games**, their achievement files, your unlock history and game list. It runs when you press **Back up now**, 30 seconds after an unlock (at most every 10 minutes), and once a day at startup. Save locations come from the community Ludusavi database (downloaded once, about 18 MB); in the Library, **Find saves** shows what was found for a game and **Add folder** adds one by hand. The 10 newest zips are kept, your Steam API key is never included, and a `README.txt` inside each zip explains how to restore by hand.
* **Restore:** **Restore from backup** (on the Backup tab) lists your backups, shows exactly what each one contains and where it will be written, and restores only what you tick. Files that would be replaced are first copied into a safety zip in `%APPDATA%\AchievementTracker\restore_safety`, restores can only write to save, achievement and app-data locations, and restored achievements do not trigger popups. Backups made before this version have no restore map and must be restored by hand.

> Note: popups can't appear over a game in *exclusive fullscreen*. Use *Borderless/Windowed* mode in the game's video settings.

---

## 📦 Installer

To give the app to friends as a single setup file, run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\build-installer.ps1` (needs the free Inno Setup, see `installer\INSTALLER.md`). The result, `installer\Output\AchievementTracker-Setup-1.0.0.exe`, installs for the current user without an administrator prompt, includes the .NET runtime, adds Start menu and optional desktop shortcuts, can start with Windows, and updates cleanly over an older version. Only one copy of the app runs at a time: starting it again just shows the window of the copy that is already in the tray.

---

## 💻 Developer Guide: Building from Source

If you wish to fork this repository and compile the binary yourself, follow these steps:

**Prerequisites:**
* [Visual Studio Code](https://code.visualstudio.com/)
* [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

**Build Instructions:**
1. Clone the repository: `git clone https://github.com/TAHIR-MIYA/AchievementTracker.git`
2. Open the directory in VS Code.
3. Open a new Terminal (`Ctrl + ~`).
4. Run the production build command to bundle the runtime and trim dependencies:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
5. Your compiled .exe will be generated in /bin/Release/net8.0-windows/win-x64/publish/.
