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

## 💻 Developer Guide: Building from Source

If you wish to fork this repository and compile the binary yourself, follow these steps:

**Prerequisites:**
* [Visual Studio Code](https://code.visualstudio.com/)
* [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

**Build Instructions:**
1. Clone the repository: `git clone https://github.com/TAHIRDON/AchievementTracker.git`
2. Open the directory in VS Code.
3. Open a new Terminal (`Ctrl + ~`).
4. Run the production build command to bundle the runtime and trim dependencies:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
5. Your compiled .exe will be generated in /bin/Release/net8.0-windows/win-x64/publish/.
