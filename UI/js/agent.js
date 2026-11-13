import { request } from "./bridge.js";
import { $, text, element, perform } from "./dom.js";

let processKey = "", sessionKey = "", permissionKey = "";
const statusLabels = { working: "Working", waiting: "Needs you", completed: "Finished", failed: "Needs attention", stale: "No recent updates", idle: "Idle", unknown: "App detected" };
export function initializeAgent() {
  $("ai-refresh").addEventListener("click", () => perform(() => request("ai.refresh"), $("ai-refresh")));
  $("ai-open").addEventListener("click", () => perform(() => request("ai.open"), $("ai-open")));
  $("system-refresh").addEventListener("click", () => perform(() => request("system.refresh"), $("system-refresh")));
}

export function renderAgent(ai = {}) {
  const current = ai.activities?.[0];
  text("ai-provider", current?.app || ai.provider || "AI activity");
  text("ai-status", statusLabels[current?.status || ai.status] || "No active apps");
  $("ai-status").dataset.status = current?.status || "idle";
  text("ai-model", current?.model || "");
  text("ai-task", current?.task || (current?.source === "App detected" ? "This app does not report its tasks." : "No recent activity"));
  text("ai-tool", current?.status === "working" && current.tool ? current.tool.replace(/^.*[.:]/, "").replaceAll("_", " ") : "");
  text("ai-detail", current ? current.status === "waiting" ? "Respond in the AI app." : current.workspace ? current.workspace.split(/[\\/]/).filter(Boolean).at(-1) : current.source : "Open an AI app to begin.");
  $("ai-running-dot").classList.toggle("active", current?.status === "working");
  $("ai-open").disabled = !(ai.processes || []).length;
  const policies = current ? [current.permission, current.sandbox, current.network].filter(value => value && value !== "Not reported") : [];
  const nextPermissionKey = JSON.stringify(policies);
  if (permissionKey !== nextPermissionKey) {
    permissionKey = nextPermissionKey;
    $("ai-permissions").replaceChildren(...policies.map(value => element("span", "permission-chip", value)));
  }
  $("ai-permissions").hidden = !policies.length;
  const otherSessions = (ai.activities || []).slice(1);
  const nextSessionKey = JSON.stringify(otherSessions.map(s => [s.app, s.status, s.task, s.session]));
  if (sessionKey !== nextSessionKey) {
    sessionKey = nextSessionKey;
    $("ai-sessions").replaceChildren(...otherSessions.map(session => {
      const row = element("article", "ai-session");
      row.append(element("strong", "", session.app), element("span", "", statusLabels[session.status] || "Detected"));
      if (session.task) row.append(element("p", "", session.task));
      return row;
    }));
  }
  const nextProcessKey = JSON.stringify(ai.processes || []);
  if (processKey !== nextProcessKey) {
    processKey = nextProcessKey;
    $("ai-processes").replaceChildren(...(ai.processes || []).map(name => element("span", "agent-process", name)));
  }
}
