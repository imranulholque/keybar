# KeyBar
Tiny always-on-top touch bar with keys 2 4 7 9 T. Hold physical Shift to get @ $ & ( .

## Build on GitHub
1. Create a new GitHub repo, upload everything in this folder (keep the `.github` folder).
2. Open the **Actions** tab -> "Build KeyBar" runs automatically (or press "Run workflow").
3. Open the finished run -> download **KeyBar** under Artifacts -> unzip -> `KeyBar.exe`.

## Install
Put KeyBar.exe in a permanent folder (e.g. C:\KeyBar\), run it once and accept the admin prompt.
It registers a Task Scheduler entry "KeyBar" (run at logon, highest privileges, no more prompts).
To remove: Task Scheduler -> delete "KeyBar", then exit from the tray icon.

## Use
- Drag the dotted grip (or empty background) to move; position is remembered.
- Slider = transparency. "-" button = hide to tray. Tray icon click = show/hide. Tray right-click = Exit.
