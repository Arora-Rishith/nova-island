import { request, subscribe } from "./bridge.js";
import { $, text, perform, formatTime } from "./dom.js";

let seeking = false, lyricsKey = "", playbackIcon = "", lyricIndex = -1, lyricScrollUntil = 0;
const icon = name => `<svg class="icon"><use href="#${name}-icon"/></svg>`;
export function initializeMedia() {
  for (const [id, action] of [["previous", "previous"], ["next", "next"], ["play-pause", "playPause"]]) {
    $(id).addEventListener("click", () => perform(() => request(`media.${action}`), $(id)));
  }
  const lyricsToggle = $("lyrics-toggle");
  if (lyricsToggle) lyricsToggle.addEventListener("click", () => {
    const sheet = $("lyrics-sheet");
    if (!sheet) return;
    sheet.hidden = !sheet.hidden;
    lyricsToggle.textContent = sheet.hidden ? "Lyrics" : "Done";
    lyricsToggle.setAttribute("aria-expanded", String(!sheet.hidden));
  });
  $("lyrics-retry").addEventListener("click", () => perform(() => request("lyrics.refresh"), $("lyrics-retry")));
  $("lyrics-sheet").addEventListener("wheel", () => lyricScrollUntil = Date.now() + 6000, { passive: true });
  $("seek").addEventListener("pointerdown", () => seeking = true);
  $("seek").addEventListener("input", () => { seeking = true; text("position", formatTime(Number($("seek").value))); updateSeekFill(); });
  $("seek").addEventListener("change", async () => {
    await perform(() => request("media.seek", { seconds: Number($("seek").value) }));
    seeking = false;
  });
  $("seek").addEventListener("pointercancel", () => seeking = false);
  window.addEventListener("pointerup", () => seeking = false);
  $("seek").addEventListener("blur", () => seeking = false);
  subscribe("artwork", source => {
    for (const image of [...document.querySelectorAll(".album-art"), $("quick-album-art")]) {
      if (!image) continue;
      image.hidden = !source;
      if (source) image.src = source;
      else image.removeAttribute("src");
    }
    for (const placeholder of document.querySelectorAll(".art-placeholder")) placeholder.toggleAttribute("hidden", Boolean(source));
  });
  subscribe("audio", ({ levels, peak }) => {
    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;
    for (const wave of document.querySelectorAll(".wave, .music-bars")) {
      [...wave.children].forEach((bar, index) => {
        const level = Math.max(0, Math.min(1, Number(levels[index]) || 0));
        bar.style.transform = `scaleY(${reducedMotion ? .35 : .18 + level * .82})`;
        bar.style.opacity = String(.35 + level * .65);
      });
    }
    peak = Math.max(0, Math.min(1, Number(peak) || 0));
    // Opacity is composited; do not animate blur, borders or layout at audio rate.
    $("island").style.setProperty("--glow-opacity", `${reducedMotion ? .55 : .5 + peak * .4}`);
  });
}
export function renderMedia(media) {
  text("compact-title", media.available ? media.title : "Nothing playing");
  text("compact-artist", media.available ? media.artist : "Time and weather");
  text("track-title", media.title);
  text("track-artist", media.artist);
  text("track-album", media.album);
  if ($("playback-status")) text("playback-status", !media.available ? "READY WHEN YOU ARE" : media.playing ? "NOW PLAYING" : "PAUSED");
  const nextIcon = media.playing ? "pause" : "play";
  if (nextIcon !== playbackIcon) { $("play-pause").innerHTML = icon(nextIcon); playbackIcon = nextIcon; }
  $("play-pause").setAttribute("aria-label", media.playing ? "Pause" : "Play");
  for (const id of ["previous", "next", "play-pause"]) $(id).disabled = !media.available;
  $("seek").disabled = !media.canSeek;
  $("seek").max = String(media.duration || 100);
  if (!seeking) { $("seek").value = media.position; text("position", formatTime(media.position)); updateSeekFill(); }
  text("duration", media.duration ? formatTime(media.duration) : "—:—");
  text("media-note", media.available ? "Windows media" : "Play from your favourite music app.");
  if ($("current-lyric")) text("current-lyric", media.lyricsLoading ? "Finding lyrics…" : media.lyric || "Lyrics unavailable");
  if ($("lyrics-status")) text("lyrics-status", media.lyricsStatus);
  const sheet = $("lyrics-sheet");
  const nextLyricsKey = JSON.stringify(media.lyrics || []);
  if (sheet && nextLyricsKey !== lyricsKey) {
    lyricsKey = nextLyricsKey;
    lyricIndex = -1;
    sheet.scrollTop = 0;
    $("lyrics-lines").replaceChildren(...(media.lyrics || []).map(line => {
    const node = document.createElement("span");
    node.textContent = line;
    return node;
    }));
  }
  $("lyrics-empty").hidden = Boolean((media.lyrics || []).length);
  $("lyrics-retry").disabled = !media.available || Boolean(media.lyricsLoading);
  $("lyrics-toggle").disabled = !media.available;
  const nextIndex = media.lyricsSynced ? media.lyricIndex ?? -1 : -1;
  if (lyricIndex !== nextIndex) {
    lyricIndex = nextIndex;
    [...$("lyrics-lines").children].forEach((line, index) => line.classList.toggle("current", index === lyricIndex));
    const active = $("lyrics-lines").children[lyricIndex];
    if (active && !sheet.hidden && Date.now() > lyricScrollUntil) sheet.scrollTo({ top: active.offsetTop - sheet.clientHeight / 2, behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth" });
  }
  const accent = media.accent || { r: 138, g: 180, b: 255 };
  document.documentElement.style.setProperty("--album-rgb", `${accent.r} ${accent.g} ${accent.b}`);
  document.documentElement.style.setProperty("--media-rgb", `${accent.r} ${accent.g} ${accent.b}`);
}

function updateSeekFill() {
  const seek = $("seek");
  seek.style.setProperty("--progress", `${100 * Number(seek.value) / Math.max(1, Number(seek.max))}%`);
}
