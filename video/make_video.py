"""Builds LogViewer-tour.mp4 from the screenshots in docs/images plus synthesized narration.

Requires: Pillow, imageio-ffmpeg (bundles an ffmpeg binary), Windows (SAPI voices via PowerShell).
Run:  python video/make_video.py
"""
import asyncio
import os
import re
import subprocess
import wave
from pathlib import Path

import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
IMG = ROOT / "docs" / "images"
WORK = Path(__file__).resolve().parent / "build"
VOICE = os.environ.get("LV_VOICE", "piper:en_US-lessac-medium")  # "piper:<model in video/voices>" = offline neural; "sapi:<name>" = Windows voice; otherwise an edge-tts neural voice name
OUT = Path(__file__).resolve().parent / os.environ.get("LV_OUT", "LogViewer-tour-v3.mp4")
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
W, H, FPS = 1920, 1080, 25

# (image, title, caption, narration)
SCENES = [
    ("main-window.png", "LogViewer",
     "A Windows log viewer that tails, parses and analyses logs as they are written",
     "Meet LogViewer: a Windows log viewer that tails, parses and analyses your logs as they are written. "
     "Here is a quick tour of what it can do."),
    ("sources-menu.png", "Every kind of log source",
     "Files, folders, Event Log, HTTP, WebSocket, commands, containers, SSH and ETW",
     "LogViewer tails text files without ever rereading them, and recovers on its own when a log is rotated or truncated. "
     "It also watches folders, opens compressed files, and reads the Windows Event Log. "
     "Beyond that, it can follow remote HTTP and WebSocket endpoints, command output, Docker and Kubernetes containers, SSH hosts and E T W providers."),
    ("merged-view.png", "Merge logs by timestamp",
     "Several files, one timeline, with a time-delta column",
     "Need to follow a request across services? Merge several files into a single view, interleaved by timestamp. "
     "Each line is labelled with its source, and the delta column shows the time elapsed since the previous line."),
    ("structured-view.png", "Structured logs",
     "Serilog, logfmt, JSON lines, syslog and W3C/IIS: detected automatically",
     "Structured formats are detected automatically: Serilog, logfmt, JSON lines, syslog and I I S logs. "
     "Select a line to see all of its properties, and even define your own formats with a regular expression."),
    ("column-view.png", "Column view",
     "Properties as sortable, filterable columns",
     "Prefer a table? Column view turns structured events into sortable, filterable columns. "
     "Double-click any row to jump straight to its line."),
    ("trace-tree.png", "Distributed tracing",
     "Span durations and the parent/child trace tree",
     "For distributed tracing, the trace tree shows the hierarchy of spans in a request, coloured by level. "
     "You can see exactly which child span failed, and jump to it."),
    ("live-filter.png", "Live filtering",
     "Substring or regex, show-only or hide, applied as lines arrive",
     "The live filter accepts plain text or regular expressions, and applies to new lines as they arrive. "
     "Combine it with level, trace and correlation I D filters to cut through the noise."),
    ("time-navigation.png", "Time navigation",
     "Go to a time, or filter by a time range",
     "Jump to any moment with go-to-time, using absolute times or relative ones like minus five minutes. "
     "Or restrict the view to a time range to focus on an incident."),
    ("timeline.png", "Volume timeline",
     "Errors, warnings and info stacked per time bucket",
     "The volume timeline shows how many lines arrive over time, with errors and warnings stacked in colour. "
     "Spikes stand out immediately, and a click takes you to that moment."),
    ("highlight-presets.png", "Highlighting and bookmarks",
     "Regex rules, built-in presets, bookmarks and notes",
     "Highlight rules colour lines as they arrive, using regular expressions and priorities. "
     "Use the built-in presets or craft your own, and mark important lines with bookmarks and notes."),
    ("exceptions.png", "Exceptions",
     "Every stack trace grouped by type and top frames",
     "The exceptions panel groups every stack trace by type and top frames, with counts and first and last occurrence. "
     "Stack traces collapse under the line that caused them, so your log stays readable."),
    ("search.png", "Full-file search",
     "Streaming, cancellable, independent of the live buffer",
     "Search runs over the whole file, not just what is in memory, and finds matches that have long scrolled away. "
     "It streams results, can be cancelled, and never pauses the tail. Even multi-gigabyte files stay smooth."),
    ("statistics.png", "Statistics",
     "Message counts, errors and warnings at a glance",
     "Statistics summarise the log: total lines, throughput, errors and warnings, and the most frequent messages."),
    ("command-palette.png", "Command palette",
     "Control+P: every command, document and preset",
     "Everything is one keystroke away. The command palette, on Control P, fuzzy-searches every action, document and highlight preset."),
    ("mdi-mode.png", "Flexible windows",
     "Tabbed, floating or classic MDI, sharing the same state",
     "Work the way you like: tabbed, floating, or classic M D I child windows. "
     "Switching modes never loses your scroll position, highlights or bookmarks."),
    ("settings.png", "Customize",
     "Themes, fonts, language and an optional MCP server",
     "Light and dark themes, adjustable fonts, and English or Portuguese. "
     "An optional built-in M C P server even lets an A I agent query the logs you have open."),
    ("main-window.png", "LogViewer",
     "Download the latest release from GitHub",
     "That is LogViewer. A single self-contained executable for Windows, no installation needed. Download it from GitHub and start tailing."),
]


def font(bold, size):
    return ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf" if bold else r"C:\Windows\Fonts\segoeui.ttf", size)


def compose(i, scene):
    img_name, title, caption, _ = scene
    bg = Image.new("RGB", (W, H), (18, 20, 26))
    d = ImageDraw.Draw(bg)
    for y in range(H):  # subtle vertical gradient
        c = int(18 + 14 * y / H)
        d.line([(0, y), (W, y)], fill=(c, c + 2, c + 10))
    shot = Image.open(IMG / img_name).convert("RGB")
    max_w, max_h = 1700, 790
    s = min(max_w / shot.width, max_h / shot.height, 1.6)
    shot = shot.resize((int(shot.width * s), int(shot.height * s)), Image.LANCZOS)
    x, y = (W - shot.width) // 2, 150 + (max_h - shot.height) // 2
    shadow = Image.new("RGBA", (shot.width + 80, shot.height + 80), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rectangle([40, 50, shot.width + 40, shot.height + 50], fill=(0, 0, 0, 170))
    shadow = shadow.filter(ImageFilter.GaussianBlur(18))
    bg.paste(shadow, (x - 40, y - 40), shadow)
    bg.paste(shot, (x, y))
    d.text((80, 38), title, font=font(True, 54), fill=(240, 242, 248))
    d.rectangle([80, 108, 200, 112], fill=(90, 160, 255))
    cap_font = font(False, 36)
    tw = d.textlength(caption, font=cap_font)
    d.rounded_rectangle([(W - tw) // 2 - 30, 972, (W + tw) // 2 + 30, 1040], radius=14, fill=(0, 0, 0))
    d.text(((W - tw) // 2, 982), caption, font=cap_font, fill=(235, 238, 245))
    p = WORK / f"scene{i:02d}.png"
    bg.save(p)
    return p


def narrate_neural(i, text):
    import edge_tts
    # Neural voices read acronyms correctly; undo the letter-spacing used for the SAPI voice.
    text = re.sub(r"([A-Z]) ([A-Z])(?: ([A-Z]))?", lambda m: "".join(g for g in m.groups() if g), text)
    mp3 = WORK / f"scene{i:02d}.mp3"
    asyncio.run(edge_tts.Communicate(text, VOICE, rate="-4%").save(str(mp3)))
    info = subprocess.run([FFMPEG, "-i", str(mp3)], capture_output=True, text=True).stderr
    h, m, sec = re.search(r"Duration: (\d+):(\d+):([\d.]+)", info).groups()
    return mp3, int(h) * 3600 + int(m) * 60 + float(sec)


def narrate_piper(i, text):
    model = ROOT / "video" / "voices" / f"{VOICE[6:]}.onnx"
    wav = WORK / f"scene{i:02d}.wav"
    subprocess.run(["python", "-m", "piper", "-m", str(model), "-f", str(wav), "--length-scale", "1.05",
                    "--sentence-silence", "0.25", "--", text], check=True, capture_output=True)
    with wave.open(str(wav)) as w:
        return wav, w.getnframes() / w.getframerate()


def narrate(i, text):
    if VOICE.startswith("piper:"):
        return narrate_piper(i, text)
    if not VOICE.startswith("sapi:"):
        return narrate_neural(i, text)
    wav = WORK / f"scene{i:02d}.wav"
    txt = WORK / f"scene{i:02d}.txt"
    txt.write_text(text, encoding="utf-8")
    ps = (
        "Add-Type -AssemblyName System.Speech;"
        "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer;"
        f"$s.SelectVoice('{VOICE[5:]}');$s.Rate=0;"
        f"$s.SetOutputToWaveFile('{wav}');"
        f"$s.Speak([IO.File]::ReadAllText('{txt}',[Text.Encoding]::UTF8));$s.Dispose()"
    )
    subprocess.run(["powershell", "-NoProfile", "-Command", ps], check=True)
    with wave.open(str(wav)) as w:
        return wav, w.getnframes() / w.getframerate()


def render(i, png, wav, dur):
    total = dur + 1.0
    out = WORK / f"clip{i:02d}.mp4"
    vf = (
        f"scale=3840:2160,zoompan=z='min(zoom+0.0003,1.06)':d=1:"
        f"x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s={W}x{H}:fps={FPS},"
        f"fade=t=in:st=0:d=0.4,fade=t=out:st={total-0.4:.2f}:d=0.4,format=yuv420p"
    )
    af = f"adelay=400|400,apad,afade=t=out:st={total-0.4:.2f}:d=0.4,atrim=0:{total:.2f}"
    subprocess.run([
        FFMPEG, "-y", "-loglevel", "error", "-loop", "1", "-framerate", str(FPS), "-t", f"{total:.2f}", "-i", str(png),
        "-i", str(wav), "-vf", vf, "-af", af, "-c:v", "libx264", "-preset", "medium", "-crf", "20",
        "-c:a", "aac", "-b:a", "160k", "-ar", "44100", "-ac", "2", "-t", f"{total:.2f}", str(out),
    ], check=True)
    return out


def main():
    WORK.mkdir(exist_ok=True)
    clips = []
    for i, sc in enumerate(SCENES):
        print(f"[{i+1}/{len(SCENES)}] {sc[1]}")
        png = compose(i, sc)
        wav, dur = narrate(i, sc[3])
        clips.append(render(i, png, wav, dur))
    lst = WORK / "list.txt"
    lst.write_text("".join(f"file '{c.as_posix()}'\n" for c in clips), encoding="utf-8")
    subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", str(lst),
                    "-c", "copy", str(OUT)], check=True)
    print("done:", OUT)


if __name__ == "__main__":
    main()
