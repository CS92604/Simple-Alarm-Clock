# Simple-Alarm-Clock
Small Windows alarm clock. Wakes the monitors, switches the audio output, plays a YouTube video until you hit stop.

<img width="437" height="699" alt="image" src="https://github.com/user-attachments/assets/8cab4241-be3e-45e7-bafc-aa9dd44805ef" />


Two tasks: alarm at the set time, window opens 30 min before (cosmetic).
Wake from sleep needs wake timers on. No wake from full shutdown. Laptops in modern standby often won't.
PC off at alarm time: it fires late when it comes back on.
Moving the exe breaks the schedule. Re-arm after moving.
One time for all days.
Locked screen: sound should play, Stop not visible until unlock (untested).
Windows default output switches system-wide during the alarm, then restores.
Volume is the Windows volume for that device. Can't power on speakers or change a monitor's own volume.
Monitor speakers only appear once the monitor is awake. Waits 20s, then errors.
Swapping monitors can invalidate the saved output device.
Needs yt-dlp and ffmpeg on PATH.
Audio is cached at Save & Arm. If the download fails it still arms, with a warning.
YouTube blocked repeated downloads (429, bot check). Cookie fallback may fail.
No clip length cap. About 10 MB of RAM per minute of audio, cache roughly double.
15-min second clip: random 0-5 min wait, plays over the loop, re-rolls. Can repeat back to back. Untested.
No X on the alarm window. Only Stop ends it.
Main window won't close during Test now.
Kill from Task Manager: ffplay dies, helper restores the audio device.
Status text shows Tues, Wednes, Thurs (cosmetic).
Not DPI-aware, so it could look blurry when scaled.
Keep the exe in a writable folder (alarm.ini, alarm.log, cache\).
