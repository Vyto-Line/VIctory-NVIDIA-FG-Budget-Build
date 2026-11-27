# Frame generation on an RTX 30 card

NVIDIA enables DLSS Frame Generation only on RTX 40 and newer GPUs. On an RTX 30 card, Streamline reports
"Disabling DLSS-G since it is not supported on current hardware" and the F3 panel shows frame generation
as off. A third-party unlocker can enable it on RTX 30 cards. This repository does not include one; download
it yourself.

These steps were tested on an RTX 3080 10 GB with MFG Unlocker v1.7.2, game in Series S mode with
DLSS Performance. The game rendered about 18 fps and the screen showed about 36 with frame generation.

## Setup

1. **Build:** `cargo build --release --locked -p victory`.
2. **Get NVIDIA's DLSS files:** run `Get-NVIDIA-DLSS.cmd`. Without them there is no DLSS and no frame
   generation; every log says "No NVIDIA files".
3. **Install the unlocker:** download MFG Unlocker from its author, then run
   `Install-MFG-Unlocker.cmd "C:\path\to\MFGUnlocker.dll"`. This copies it beside `victory.exe` as
   `dxgi.dll`.
   - Don't rename it by hand in Explorer. Hidden extensions make that `dxgi.dll.dll`, which Windows never
     loads.
   - Install only one copy.
4. **Turn it on:** in the F1 menu, set DLSS (Performance suits a 3080) and frame generation 2x.
   - The menu saves its choices to `victory-settings.txt`, and those override the launcher's options.
   - If the menu has a 30 fps cap set, remove it.
5. **Play once, then prewarm:** run `Prewarm-Shaders.cmd "D:\path\to\your dump"` with the game closed.
   - It translates every shader, then builds every pipeline the recorded runs used: about 84,000, in about
     17 minutes on a Ryzen 7 9800X3D.
   - Places visited after that still stutter once.
   - Run it again after a rebuild or a driver update.

Check that it works: the F3 panel shows frame generation on, and the log in `%LOCALAPPDATA%\victory\logs`
shows "DLSS Frame Generation: N presents with it on".

## Recommended: turn SKIP BIG COPY-BACKS on

The F1 menu saves all of its choices to `victory-settings.txt`, including switches you never changed. If
the file shows `VICTORY_WC_NO_BACK=` (empty), that switch is off, even though the launcher turns it on by
default. Turn **SKIP BIG COPY-BACKS** on in F1, or set `VICTORY_WC_NO_BACK=1` in the file.

When this switch is on, shader output written into large write-combined copies stays on the graphics card
instead of being copied back each frame. The game is limited by CPU work and copies, not by the graphics
card (in these runs, about 2-4 ms of GPU work in a 55-70 ms frame), so this cut matters.

Measured on an RTX 3080 10 GB with a Ryzen 7 9800X3D. Same town streets, about 3 minutes each, rendered
frames only (frame generation on):

| Test | Change | Median fps | Worst 10% | Copied back per frame |
|---|---|---|---|---|
| 43 | F1 file as saved (copy-backs on) | 14.0 | 13.0 | 8.8 ms, 7.1 MB |
| 44 | + `VICTORY_WC_NO_BACK=1` | **17.2** | 11.9 | 6.0 ms, 1.1 MB |
| 45 | 44 + `VICTORY_TEXTURE_NO_WAIT=1` | 15.3 | 2.7 | 6.4 ms, 1.1 MB |

Keep TEXTURE NO WAIT off on a 10 GB card: in test 45 it caused long freezes. Turn SKIP BIG COPY-BACKS
back off if you see flickering or stale-looking effects. When it's on, the game's CPU can read older
values from that memory.

## Things to know

- **Base frame rate:** frame generation doubles what you see, not how the game responds. At an 18 fps base,
  input still reacts at 18 fps, and generated frames can smear in fast motion.
- **Video memory:** with frame generation on, the launcher keeps 14-20% of video memory free for it
  (`VICTORY_TEXTURE_ROOM_PERCENT`). On a 10 GB card this can cost texture detail.
- **Removing it:** run `Install-MFG-Unlocker.cmd remove`. A rebuild doesn't delete `dxgi.dll`.
