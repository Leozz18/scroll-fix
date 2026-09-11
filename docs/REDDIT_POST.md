# Reddit post drafts (copy/paste)

Use one post per community. Do not cross-post spammy; wait a day between subs if needed.

---

## Title options

1. `Free Windows tool to fix mouse scroll wheel jumping the wrong way (worn encoder)`
2. `Cooler Master / Logitech scroll wheel reverse “ghost” jumps — open source fix`
3. `I built a tiny tray app that blocks reverse scroll glitches from dying mouse wheels`

---

## Body (r/MouseReview, r/Windows11, r/CoolerMaster, r/LogitechG)

Hey — sharing a small open-source tool I built after my Cooler Master mouse started doing the classic bug:

**While scrolling down, it occasionally ticks upward** (and vice versa). Cleaning helped for a few minutes, then it came back. Turns out it’s usually a worn/dirty rotary encoder, and OEM software doesn’t fix it.

**Scroll Fix** runs in the Windows tray and filters those ghost opposite pulses before apps see them.

- Download (portable `.exe`): https://github.com/Leozz18/scroll-fix/releases/latest
- Source / roadmap: https://github.com/Leozz18/scroll-fix
- No install required — run the exe, green tray icon appears
- Aggressive mode is on by default (~220 ms reverse window)
- Works with basically any mouse, not just Cooler Master

If intentional direction changes feel sticky, lower the block window in Settings. If ghosts still leak, raise it to ~300 ms.

MIT licensed. Feedback welcome — especially if it helps your mouse too.

---

## Short version (comments on existing “scroll jumps opposite” threads)

If you’re hitting the “scroll down sometimes jumps up” encoder glitch, I open-sourced a tiny Windows tray filter for it:

https://github.com/Leozz18/scroll-fix/releases/latest

Blocks reverse ghost pulses system-wide. Free/MIT.

---

## v1.1 follow-up comment (reply on the original thread)

Update based on the feedback here: v1.1.0 adds a **Balanced mode** that fixes the "can't quickly change direction" issue.

Instead of blocking every opposite notch, it holds the first one and waits for the next. If you really reversed, that notch passes and the held one is replayed, so nothing is lost. If the wheel went back the other way, it was a ghost and gets dropped. Rapid up/down/up/down works again.

Also new: presets (Quick reverse / Worn encoder), a 10-minute pause, a small 0.3 MB exe next to the standalone one, and CI-built releases with checksums.

https://github.com/Leozz18/scroll-fix/releases/latest

## Tips

- Lead with the symptom people search for
- Put the **release exe link first**, repo second
- Add a screenshot of the tray/settings if the sub allows images
- Reply to questions quickly the first 24h (that’s when Reddit ranks the post)
