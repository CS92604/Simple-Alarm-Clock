# Simple-Alarm-Clock
Small Windows alarm clock. Wakes the monitors, switches the audio output, plays a YouTube video until you hit stop.
## Requirements

- Windows 10 or 11
- `yt-dlp` and `ffmpeg` on your PATH (`ffplay` comes with ffmpeg)

  
<img width="437" height="699" alt="image" src="https://github.com/user-attachments/assets/8cab4241-be3e-45e7-bafc-aa9dd44805ef" />



**Scheduling**
- Two tasks: the alarm at the set time, and the window opening 30 minutes before (cosmetic).
- Waking from sleep needs wake timers turned on. It can't wake from a full shutdown, and laptops in modern standby often won't.
- If the PC is off at alarm time, the alarm fires late when it comes back on.
- Moving the exe breaks the schedule. Re-arm after moving it.
- One time for all selected days.
- Locked screen: sound should play, but the Stop button isn't visible until you unlock (untested).

**Audio**
- The default output switches system-wide during the alarm, then goes back.
- Volume is the Windows volume for that device. It can't power on speakers or change a monitor's own volume.
- Monitor speakers only show up once the monitor is awake. The app waits 20 seconds, then errors.
- Swapping monitors can invalidate the saved output device.

**Downloads**
- Audio is cached at Save & Arm. If the download fails it still arms, with a warning.
- YouTube blocked repeated downloads (429, bot check). The cookie fallback may fail.
- No clip length cap. Expect about 10 MB of RAM per minute of audio, and the cache is roughly double that.
- The 15-minute second clip can repeat back to back (untested).

**Stopping**
- No X on the alarm window. Only Stop ends it.
- The main window won't close during Test now.
- If the process is killed from Task Manager, `ffplay` dies and a helper restores the audio device.

**Other**
- Status text shows `Tues`, `Wednes`, `Thurs` (cosmetic).
- Not DPI-aware, so it can look blurry when scaled.
- Keep the exe in a writable folder, since `alarm.ini`, `alarm.log` and `cache\` live next to it.
