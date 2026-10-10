"""Checks the WebGL build in a real browser at full speed, without needing a visible window.

Drives the installed Edge (or Chrome) headless, on the real GPU, through Playwright: loads each level
with the built-in bot playing it, makes sure the page keeps answering (a WebGL-only endless loop freezes
the page: Level 5's dashed outline did exactly that while every editor check passed), measures the frame
rate, takes pictures at intervals of real time and reports console errors.

Serve the build first, for example:
    python -m http.server 5180 --directory %USERPROFILE%\\.cache\\tinkerstoybox\\Build\\WebGL

Examples:
    python tools/webgl_check.py --levels 1 2 3 4 5 6 7 8 9
    python tools/webgl_check.py --query "" --name title --seconds 10
    python tools/webgl_check.py --levels 1 --base https://sramireddy2.github.io/tinkerstoybox/

Needs: pip install playwright  (no browser download: it uses the Edge or Chrome already installed).
"""
import argparse
import os
import sys
import threading
import time

from playwright.sync_api import sync_playwright

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "tools", "out", "shots", "webgl")

# Frames in three seconds, and the longest of them. Gives up by itself if no frame comes.
FPS_JS = """() => Promise.race([
  new Promise(resolve => {
    let frames = 0, worst = 0, last = performance.now();
    const start = last;
    function tick(now) {
      frames++;
      worst = Math.max(worst, now - last);
      last = now;
      if (now - start < 3000) requestAnimationFrame(tick);
      else resolve({ fps: Math.round(frames * 1000 / (now - start)), worstMs: Math.round(worst) });
    }
    requestAnimationFrame(tick);
  }),
  new Promise(resolve => setTimeout(() => resolve({ fps: 0, worstMs: 5000 }), 5000))
])"""

RENDERER_JS = """() => {
  const gl = document.createElement('canvas').getContext('webgl2');
  if (!gl) return 'no webgl2';
  const ext = gl.getExtension('WEBGL_debug_renderer_info');
  return ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
}"""

NOISE = ("UnityCache", "IndexedDB", "favicon", "Failed to load resource")

# Listens to what the page plays: every node wired to the speakers is wired through an analyser first,
# and a timer notes how loud each 50 ms was. Installed before the page's own scripts run.
AUDIO_TAP_JS = """(() => {
  if (!window.AudioNode || !window.AudioDestinationNode) return;
  const meter = window.__toyboxAudio = { contexts: 0, windows: 0, loud: 0, peak: 0, rmsSum: 0, clipped: 0 };
  const taps = [];
  const connect = AudioNode.prototype.connect;
  AudioNode.prototype.connect = function (target, ...rest) {
    try {
      if (target instanceof AudioDestinationNode) {
        const ctx = this.context;
        if (!ctx.__toyboxTap) {
          ctx.__toyboxTap = ctx.createAnalyser();
          ctx.__toyboxTap.fftSize = 2048;
          connect.call(ctx.__toyboxTap, ctx.destination);
          taps.push(ctx.__toyboxTap);
          meter.contexts++;
        }
        if (this !== ctx.__toyboxTap) return connect.call(this, ctx.__toyboxTap, ...rest);
      }
    } catch (error) { /* fall through to the plain connection */ }
    return connect.call(this, target, ...rest);
  };
  const samples = new Float32Array(2048);
  setInterval(() => {
    for (const tap of taps) {
      tap.getFloatTimeDomainData(samples);
      let peak = 0, sum = 0;
      for (let i = 0; i < samples.length; i++) { const v = Math.abs(samples[i]); if (v > peak) peak = v; sum += samples[i] * samples[i]; }
      meter.windows++;
      meter.rmsSum += Math.sqrt(sum / samples.length);
      if (peak > 0.003) meter.loud++;
      if (peak >= 0.99) meter.clipped++;
      if (peak > meter.peak) meter.peak = peak;
    }
  }, 50);
})();"""


def audio_line(page, problems=None, expect_sound=True):
    """What the page played so far, as one report line."""
    meter = page.evaluate("() => window.__toyboxAudio || null")
    if not meter or not meter["windows"]:
        if problems is not None and expect_sound:
            problems.append("no audio output was created at all")
        return "  sound: none (no audio node reached the speakers)"
    share = meter["loud"] / meter["windows"]
    if problems is not None:
        if expect_sound and meter["loud"] == 0:
            problems.append("the page was silent the whole time")
        if meter["clipped"] > meter["windows"] * 0.02:
            problems.append(f"the sound clips ({meter['clipped']} of {meter['windows']} windows at full scale)")
    return (f"  sound: audible in {share:.0%} of the time, peak {meter['peak']:.2f}, "
            f"mean level {meter['rmsSum'] / meter['windows']:.3f}, clipped windows {meter['clipped']}")


def picture(page, name, shots):
    path = os.path.join(OUT, name + ".png")
    try:
        page.screenshot(path=path, timeout=8000)
        shots.append(os.path.basename(path))
    except Exception:
        shots.append(name + " (no picture)")


def play(page, base, width, height):
    """A person at the keyboard: title, click to play, mouse capture, look, walk, pick up, let go, pause.

    Returns (problems, lines). The pictures are for looking at; what can be measured is measured.
    """
    errors, logs = [], []
    page.on("console", lambda m: (errors if m.type == "error" else logs).append(m.text))
    page.on("pageerror", lambda e: errors.append("page error: " + str(e)))
    lines, problems, shots = [], [], []
    started = time.time()
    page.goto(base, wait_until="load")
    while time.time() - started < 120 and not any("UnloadTime" in line for line in logs):
        time.sleep(0.25)
        answers(page, 500)
    page.wait_for_timeout(4000)
    lines.append(f"play: title up after {time.time() - started:.1f} s")
    picture(page, "play-0-title", shots)

    def captured():
        return page.evaluate("() => !!document.pointerLockElement")

    # The big button sits at the middle, 78% down the 16:9 frame.
    cursor_y = height * 0.78      # keep the pointer at this height: moving it up or down would tilt the view
    page.mouse.click(width * 0.5, cursor_y)
    page.wait_for_timeout(1500)
    if not captured():
        page.mouse.click(width * 0.5, cursor_y)          # the "click to look around" prompt
        page.wait_for_timeout(1000)
    lines.append(f"  after 'Click to play': mouse captured = {captured()}")
    if not captured():
        problems.append("clicking 'Click to play' did not capture the mouse")
    picture(page, "play-1-started", shots)

    # Look: with the pointer captured the browser reports movement, not position.
    before = page.screenshot(timeout=8000)
    for _ in range(12):
        page.mouse.move(width * 0.5 + 25, cursor_y, steps=1)
        page.mouse.move(width * 0.5 + 50, cursor_y, steps=1)
        page.wait_for_timeout(30)
    page.wait_for_timeout(400)
    after = page.screenshot(timeout=8000)
    lines.append(f"  moving the mouse changed the view = {before != after}")
    picture(page, "play-2-looked", shots)
    for _ in range(12):                                   # and back again
        page.mouse.move(width * 0.5 + 25, cursor_y, steps=1)
        page.mouse.move(width * 0.5, cursor_y, steps=1)
        page.wait_for_timeout(30)

    # Pick up what is under the crosshair (every level starts looking at its toy), then let it go.
    page.wait_for_timeout(500)
    page.mouse.down()
    page.wait_for_timeout(120)
    page.mouse.up()
    page.wait_for_timeout(1200)
    picture(page, "play-3-picked-up", shots)
    page.keyboard.press("e")
    page.wait_for_timeout(1200)
    picture(page, "play-4-let-go", shots)

    # Walk: hold S (backwards is free at a spawn), then W with a jump.
    page.keyboard.down("s")
    page.wait_for_timeout(900)
    page.keyboard.up("s")
    picture(page, "play-5-walked-back", shots)
    page.keyboard.down("w")
    page.wait_for_timeout(600)
    page.keyboard.press("Space")
    page.wait_for_timeout(500)
    page.keyboard.up("w")
    picture(page, "play-6-walked-jumped", shots)

    # Pause frees the mouse and shows the menu.
    page.keyboard.press("Escape")
    page.wait_for_timeout(1200)
    lines.append(f"  after Esc: mouse captured = {captured()}")
    if captured():
        problems.append("Esc did not free the mouse")
    picture(page, "play-7-paused", shots)

    lines.append(audio_line(page, problems))
    for e in [e for e in errors if not any(n in e for n in NOISE)][:8]:
        problems.append("console error: " + e[:240].replace("\n", " "))
    lines.append("  pictures: " + ", ".join(shots))
    return problems, lines



def answers(page, within_ms=2500):
    """True if the page's main thread runs script within the time given."""
    try:
        page.wait_for_function("() => true", timeout=within_ms)
        return True
    except Exception:
        return False


def run(page, url, name, seconds, every):
    """Loads url and watches it for `seconds`; returns (problems, lines)."""
    errors, logs = [], []
    page.on("console", lambda m: (errors if m.type == "error" else logs).append(m.text))
    page.on("pageerror", lambda e: errors.append("page error: " + str(e)))
    page.on("crash", lambda: errors.append("the page crashed"))

    lines, problems, shots = [], [], []
    started = time.time()
    page.goto(url, wait_until="load")
    while time.time() - started < 120 and not any("UnloadTime" in line or "Input System initialize" in line for line in logs):
        time.sleep(0.25)
        answers(page, 500)   # pumps the console events
    booted = time.time() - started
    lines.append(f"{name}: player up after {booted:.1f} s")
    if booted >= 119:
        problems.append("the player did not start within 120 s")
        return problems, lines

    t0 = time.time()
    index, silent, rate = 0, 0, None
    next_shot = 2.0
    while time.time() - t0 < seconds:
        if not answers(page):
            silent += 1
            if silent >= 4:
                problems.append(f"the page stopped answering {time.time() - t0:.0f} s after the player started (an endless loop or a frame of more than 10 s)")
                break
            continue
        silent = 0
        elapsed = time.time() - t0
        if rate is None and elapsed >= 6:
            rate = page.evaluate(FPS_JS)
            lines.append(f"  frame rate while playing: {rate['fps']} fps, longest frame {rate['worstMs']} ms")
            if rate["fps"] < 30:
                problems.append(f"only {rate['fps']} frames a second")
        if elapsed >= next_shot:
            path = os.path.join(OUT, f"{name}-{index:02d}-t{int(elapsed):03d}.png")
            try:
                page.screenshot(path=path, timeout=8000)
                shots.append(os.path.basename(path))
            except Exception:
                lines.append(f"  no picture at {elapsed:.0f} s (the browser did not hand one over in 8 s)")
            index += 1
            next_shot = elapsed + every
        time.sleep(0.5)

    for line in [l for l in logs if l.startswith("[Toybox]")][:10]:
        lines.append("  " + line[:220].replace("\n", " "))
    for e in [e for e in errors if not any(n in e for n in NOISE)][:8]:
        problems.append("console error: " + e[:240].replace("\n", " "))
    lines.append(audio_line(page, problems, expect_sound=False))
    lines.append("  pictures: " + (", ".join(shots) if shots else "none"))
    return problems, lines


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--levels", type=int, nargs="*", default=[])
    parser.add_argument("--query", default=None, help="a query string to load instead of a level, e.g. '' for the title")
    parser.add_argument("--name", default="page")
    parser.add_argument("--seconds", type=float, default=40)
    parser.add_argument("--every", type=float, default=6)
    parser.add_argument("--base", default="http://localhost:5180/")
    parser.add_argument("--size", default="1280x720")
    parser.add_argument("--headed", action="store_true", help="show the browser window")
    parser.add_argument("--play", action="store_true", help="also play the first level by hand: click, mouse capture, look, walk, pick up, pause")
    args = parser.parse_args()

    os.makedirs(OUT, exist_ok=True)
    width, height = (int(v) for v in args.size.split("x"))
    jobs = [(f"level{n:02d}", f"?level={n}&autoplay=1") for n in args.levels]
    if args.query is not None:
        jobs.append((args.name, args.query))

    # A frozen page can freeze the driver with it: never run longer than the jobs can take.
    limit = len(jobs) * (args.seconds + 150) + 60 + (240 if args.play else 0)
    threading.Timer(limit, lambda: (print(f"RESULT: FAILED - gave up after {limit:.0f} s", flush=True), os._exit(3))).start()

    failed = 0
    with sync_playwright() as p:
        browser, last = None, None
        for channel in ("msedge", "chrome"):
            try:
                browser = p.chromium.launch(channel=channel, headless=not args.headed, args=[
                    "--use-angle=d3d11", "--enable-gpu", "--ignore-gpu-blocklist",
                    "--autoplay-policy=no-user-gesture-required", "--disable-background-timer-throttling",
                    "--disable-renderer-backgrounding", "--disable-backgrounding-occluded-windows"])
                break
            except Exception as error:  # the channel is not installed
                last = error
        if browser is None:
            print("RESULT: FAILED - neither Edge nor Chrome could be started:", last)
            os._exit(2)

        for number, (name, query) in enumerate(jobs):
            context = browser.new_context(viewport={"width": width, "height": height})
            context.add_init_script(AUDIO_TAP_JS)
            page = context.new_page()
            if number == 0:
                page.goto("about:blank")
                print("renderer:", page.evaluate(RENDERER_JS), flush=True)
            problems, lines = run(page, args.base + query, name, args.seconds, args.every)
            for line in lines:
                print(line, flush=True)
            for problem in problems:
                print("  PROBLEM: " + problem, flush=True)
            failed += 1 if problems else 0
            try:
                context.close()
            except Exception:
                pass
        if args.play:
            context = browser.new_context(viewport={"width": width, "height": height})
            context.add_init_script(AUDIO_TAP_JS)
            problems, lines = play(context.new_page(), args.base, width, height)
            for line in lines:
                print(line, flush=True)
            for problem in problems:
                print("  PROBLEM: " + problem, flush=True)
            failed += 1 if problems else 0
            jobs.append(("play", ""))
            try:
                context.close()
            except Exception:
                pass
        try:
            browser.close()
        except Exception:
            pass

    print("RESULT: OK" if failed == 0 else f"RESULT: FAILED ({failed} of {len(jobs)})", flush=True)
    os._exit(0 if failed == 0 else 1)


if __name__ == "__main__":
    main()
