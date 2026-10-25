# Key Remapper

**Fix dead, misfiring and ghost-typing keys on an external keyboard, without touching your laptop's built-in keyboard.**

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)
![Language](https://img.shields.io/badge/language-C%23%20(.NET%20Framework)-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

Key Remapper is a small Windows app that changes what individual keys do on **one specific keyboard**. It was built for a common situation: a USB keyboard with a few broken keys, plugged into a laptop whose own keyboard works perfectly. A normal remapper would change both keyboards at once. This one tells them apart and only ever touches the keyboard you choose.

It is a single self-contained app. It downloads and installs its own driver, so there is nothing to set up by hand.

![Key Remapper window](docs/screenshot.png)

---

## Features

| Feature | What it does |
| --- | --- |
| **Per-keyboard remapping** | Rules apply only to the keyboard you pick. The laptop keyboard is never modified. |
| **Key replacement** | Make a working spare key type the character of a dead key (for example Right Alt types `A`). |
| **Disable a key** | Make a key do nothing. Useful for keys that misfire or type by themselves. |
| **Learn faulty key** | Press a misbehaving key a few times and every key it sends is disabled automatically. |
| **Learn pattern (burst filter)** | If one faulty press sends several keys at once (such as `T + P + 9`), keep only the valid one. |
| **Live key monitor** | Shows which key was pressed and which device sent it, so you can see what is going on. |
| **One-click driver setup** | Downloads the driver, verifies its checksum and installs it from inside the app. |
| **Runs quietly in the tray** | Optional start at sign-in. Exit or untick the checkbox and every key is normal again. |
| **Survives unplugging** | The keyboard is recognised by its hardware ID, not by a port number. |

---

## Which tool fixes which fault?

| What your keyboard does | Use this |
| --- | --- |
| A key does nothing at all (dead) | **Add...** and map a spare working key to it |
| A key types a character you did not press, or keeps repeating | **Add...** and choose *(Disable - do nothing)* |
| One faulty key types a **different** key on each press | **Learn faulty key** |
| One faulty press types **several keys at once** (a burst) | **Learn pattern** |

---

## Requirements

- Windows 10 or Windows 11
- Administrator rights (the keyboard driver and the app both need them)
- An internet connection for the one-time driver download
- The .NET Framework 4.x compiler, which is already included with Windows

Developed and used on Windows 11 with a USB keyboard. Windows 10 is expected to work.

---

## Quick start

1. **Download** this repository (green **Code** button, then **Download ZIP**) and **extract the whole folder**. Do not run it from inside the zip.
2. Double-click **`Start.bat`** and click **Yes** on the administrator prompt.
   The first run builds `KeyRemapper.exe` from the included source, which takes a few seconds.
3. **Step 1:** click **Install driver**. The app downloads the driver, checks its SHA-256 checksum and installs it. Restart Windows when asked.
4. After the restart, run **`Start.bat`** again.
5. **Step 2:** click **Detect my external keyboard** and press any key on the **external** keyboard.
6. **Step 3:** add your key rules (see below), then type in the **test area** to check them.

The driver and detection steps only need to be done once.

---

## Usage guide

### Replace a dead key with a working one

Example: the **A** key is dead, and you never use **Right Alt**.

1. Click **Add...**
2. In step 1, choose **Right Alt**. You can also click **Or press it on the keyboard** and press the key.
3. In step 2, choose **A**.
4. Click **Add**.

Pressing Right Alt on the external keyboard now types `A`.

### Disable a key

1. Click **Add...**
2. In step 1, choose the key to silence, or press it with **Or press it on the keyboard**.
3. In step 2, choose **(Disable - do nothing)**.
4. Click **Add**.

> **Note:** a rule matches the *code* a key sends. If you disable `T`, the real `T` key on that keyboard stops working too, because the app cannot tell the faulty key's `T` from the real one. Map a spare key to `T` if you still need to type it.

### Learn a faulty key (different key on every press)

Use this when one broken key types something different each time.

1. Click **Learn faulty key**.
2. Press **only the faulty key** a few times. Each key it sends is added to the list as disabled.
3. Click **Stop learning**. It also stops by itself after 60 seconds.

While learning, all other keys on the external keyboard type nothing. Your laptop keyboard keeps working.

### Learn a pattern (several keys at once)

Use this when **one press** of a faulty key sends a group of keys together, for example `T + P + 9`.

1. Click **Learn pattern**.
2. Press the faulty key **once**.
3. The app lists the keys that arrived together. Choose the **one valid key** to type, or *(nothing)*.

The rule appears in the list as `Burst: T + P + 9` and the key you chose. Real single presses of `T`, `P` or `9` still type normally, because only the complete group triggers the rule.

**Burst window (ms):** how long the app waits to see whether the whole group arrives. It is set automatically from what the app measured while learning. Raise it if bursts still slip through. Lower it if fast normal typing is being swallowed. Keys that belong to a pattern are delayed by up to this many milliseconds, and no other keys are delayed.

If Learn pattern reports only one key, the faulty key is not sending a burst. Use **Learn faulty key** instead.

### Live key monitor

The **Last key seen** line under the test area shows each key press and which device sent it. It marks whether the device is your *chosen keyboard* or an *other keyboard - untouched*. This is the quickest way to find out what a faulty key really sends.

### Turn it off, hide it, start it with Windows

| Control | Effect |
| --- | --- |
| **Remapping ON** checkbox | Untick it and every key behaves normally. |
| Close button (X) | Hides the window. The app keeps running in the system tray. |
| Tray icon, **Exit** | Stops the app. All keys go back to normal immediately. |
| **Start automatically** | Runs the app at sign-in, minimised to the tray. |

### Where settings are stored

`%APPDATA%\KeyRemapper\config.txt` is a plain text file with your keyboard ID, rules, patterns and burst window.

---

## Updating

1. Right-click the tray icon and choose **Exit**.
2. Extract the new version over the old folder.
3. **Delete `KeyRemapper.exe`** (leave `interception.dll`). `Start.bat` only rebuilds the app when the `.exe` is missing.
4. Run **`Start.bat`**.

Your settings and the installed driver are kept.

---

## Uninstalling

1. Untick **Start automatically**.
2. Click **Uninstall** in the app to remove the driver, then restart Windows.
3. Delete the folder. Optionally delete `%APPDATA%\KeyRemapper`.

---

## Troubleshooting

**"Driver is not active"**
Restart Windows if you just installed it. If it still is not active after a restart, Windows **Memory integrity** may be blocking this older driver: *Windows Security > Device security > Core isolation > Memory integrity*. Turning it off lowers protection, so only do that if you accept the trade-off.

**Antivirus flags the driver or the app**
Keyboard filter drivers and key remappers look similar to keyloggers to some security tools. The app is built on your PC from the source in this repository, so you can read exactly what it does. See [Security and privacy](#security-and-privacy).

**The build fails when I run `Start.bat`**
Copy the error text shown in the window and open an issue. Make sure you extracted the zip first.

**Ghost keys still appear**
Look at the **Last key seen** line while it happens. If it says *other keyboard - untouched*, the keys come from a different device than the one you chose. Click **Detect my external keyboard** again and press a key on the external keyboard.

**I detected the wrong keyboard**
Click **Detect my external keyboard** again and press a key on the external keyboard, not the laptop.

**Two identical keyboards behave the same**
Keyboards are recognised by their hardware ID. Two keyboards of the same model share an ID and cannot be told apart.

**My rules stopped working after a Windows update**
Run `Start.bat` again. If the driver shows as inactive, restart Windows.

---

## How it works

```
 External keyboard ─┐
                    ├─►  Interception driver  ─►  Key Remapper  ─►  Windows
 Laptop keyboard  ──┘    (sees each device       (changes only the
                          separately)             chosen keyboard)
```

1. The **Interception** driver sits in the keyboard driver stack and hands every key event, along with the **device it came from**, to user-mode code.
2. Key Remapper identifies each device by its hardware ID (the USB vendor and product ID) and only processes events from the keyboard you chose. Events from any other keyboard are passed through unchanged. Keyboards that expose several interfaces are treated as one.
3. Matching events are replaced, dropped or filtered by your rules. The result is handed back to the driver, which delivers it to Windows as if the keyboard had sent it.

If the app is closed or crashes, the driver goes back to passing everything through untouched.

---

## Security and privacy

- **No network use except the driver download.** The app does not log, store or send what you type. The *Last key seen* line is shown on screen only and is not saved.
- **The driver download is verified.** The app fetches a fixed release of Interception from its official GitHub repository and checks its SHA-256 checksum before installing anything. If the checksum does not match, nothing is installed.
- **Open source.** The whole app is in `KeyRemapper.cs`. It is compiled on your own PC, so you are not trusting a pre-built binary.
- **Administrator rights are required** because the driver and the key filtering operate at a low level.

---

## Limitations

- Windows only.
- Remaps single keys, bursts and disabled keys. It does not do key combinations or macros.
- Rules are matched by key code, so a disabled key is disabled for real presses as well as faulty ones.
- Two keyboards with identical hardware IDs cannot be told apart.
- Relies on an older third-party driver that Windows security features can block.

---

## Building from source

`Start.bat` does this for you. To build by hand from a normal Command Prompt in the project folder:

```bat
%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /out:KeyRemapper.exe /win32manifest:app.manifest /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll KeyRemapper.cs
```

The source is written in C# 5 so that the compiler built into every Windows installation can build it. No SDK or Visual Studio is needed.

---

## Project structure

```
KeyRemapper/
├── KeyRemapper.cs     Application source (UI, engine, driver installer)
├── app.manifest       Requests administrator rights
├── Start.bat          Builds the app on first run, then launches it
├── docs/
│   └── screenshot.png
├── README.md
├── LICENSE
└── .gitignore
```

`KeyRemapper.exe` and `interception.dll` are created on your PC and are not stored in the repository.

---

## Credits and licenses

- **Key Remapper** is released under the [MIT License](LICENSE).
- It uses the **[Interception](https://github.com/oblitum/Interception)** driver and library by Francisco Lopes. Interception is free for **non-commercial use** under the LGPL 3.0. **Commercial use requires a separate license from its author.** Interception is downloaded by the app at install time and is not included in this repository.

## Disclaimer

This software installs a low-level keyboard driver and is provided as is, without warranty. If you rely on it for important work, keep a second working keyboard within reach, such as your laptop's own.
