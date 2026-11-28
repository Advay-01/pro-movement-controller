# Movement Controller for Ubuntu

This is an x64 Linux build of the same controller used on Windows. It uses X11 for window capture, input and global shortcuts, and Tesseract for on-screen text.

## Run on Ubuntu

1. Sign in to an X11 desktop session. On Ubuntu with GNOME, choose **Ubuntu on Xorg** at the sign-in screen. For a server accessed through SSH, use a graphical desktop session with its own X11 display.
2. Install the runtime dependencies.

   ```sh
   sudo apt install mono-runtime libmono-system-windows-forms4.0-cil \
     libmono-system-web-extensions4.0-cil libgdiplus libx11-6 libxtst6 \
     wmctrl tesseract-ocr tesseract-ocr-eng
   ```

3. Extract the release archive and run the controller from its directory.

   ```sh
   ./run.sh
   ```

4. Open the Linux PRO client in the same desktop session. Select it in the controller and enable keyboard input. Configure your target and catch settings if you want encounter handling. **F8** starts or stops and **F10** is the emergency stop.

The default launch is practice mode. The Linux port requires a visible, focused X11 window for game input and recognition. The two-window option shares that focus between the selected windows. Background key delivery and Wayland sessions are outside this build's support.

To bring over a saved single-window catch recipe, copy your existing `catch-settings.json` beside the extracted `MovementController.exe` while the controller is closed.

## Build from source

Install `mono-mcs` in addition to the runtime dependencies. From the repository root run `sh linux/build.sh` and then `linux/build/run.sh`. To run the controller with a single open client already selected, use `linux/build/run.sh --prepare-pro Mimikyu`.

The Linux checks use Ubuntu, Xvfb and Openbox for capture, focus, shortcuts and input, plus synthetic battle and catch screens for OCR. A signed-in battle on the native Linux PRO client has yet to be verified.

