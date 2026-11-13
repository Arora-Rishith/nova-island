import { $, text, element } from "./dom.js";

let displayed = new Date();
let selected = new Date();
let dateKey = "";
export function monthDays(year, month) {
  const first = new Date(year, month, 1);
  const start = 1 - ((first.getDay() + 6) % 7);
  return Array.from({ length: 42 }, (_, index) => new Date(year, month, start + index));
}
function renderMonth() {
  text("month-label", displayed.toLocaleDateString(undefined, { month: "long", year: "numeric" }));
  const today = new Date().toDateString();
  const days = monthDays(displayed.getFullYear(), displayed.getMonth()).map(date => {
    const button = element("button", date.getMonth() !== displayed.getMonth() ? "outside" : "", date.getDate());
    button.setAttribute("aria-label", date.toLocaleDateString(undefined, { weekday: "long", year: "numeric", month: "long", day: "numeric" }));
    if (date.toDateString() === today) button.setAttribute("aria-current", "date");
    button.setAttribute("aria-pressed", String(date.toDateString() === selected.toDateString()));
    button.addEventListener("click", () => { selected = date; displayed = new Date(date.getFullYear(), date.getMonth(), 1); renderMonth(); });
    return button;
  });
  $("calendar-days").replaceChildren(...days);
}
export function initializeCalendar() {
  for (const [id, offset] of [["month-previous", -1], ["month-next", 1]]) $(id).addEventListener("click", () => {
    displayed = new Date(displayed.getFullYear(), displayed.getMonth() + offset, 1);
    renderMonth();
  });
  $("today").addEventListener("click", () => { displayed = new Date(); selected = new Date(); renderMonth(); });
  renderMonth();
}
export function renderClock(value) {
  const now = new Date(value);
  text("calendar-weekday", now.toLocaleDateString(undefined, { weekday: "long" }));
  text("calendar-date", now.getDate());
  text("clock", now.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit", second: "2-digit" }));
  text("date-label", now.toLocaleDateString(undefined, { weekday: "long", month: "short", day: "numeric" }));
  text("compact-time", now.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" }));
  if (dateKey !== now.toDateString()) { dateKey = now.toDateString(); renderMonth(); }
}
