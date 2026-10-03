"use strict";

const state = { token: "", revision: "", challenges: [], targets: [], lootPacks: [], selected: -1, dirty: false };
const $ = id => document.getElementById(id);
const fields = ["enabled","type","id","title","description","startUtc","endUtc","durationHours","goalScope","goalLogic","minimumParticipationValue","quizNumber","quizImagePath","quizQuestion","quizAcceptedAnswers","rewardMoney","rewardFame","rewardDistribution","rewardText","completedText"];
const numeric = new Set(["durationHours","minimumParticipationValue","quizNumber","rewardMoney","rewardFame"]);
let toastTimer;

document.addEventListener("DOMContentLoaded", async () => {
  const params = new URLSearchParams(location.search);
  state.token = params.get("token") || sessionStorage.getItem("redravenToken") || "";
  if (state.token) sessionStorage.setItem("redravenToken", state.token);
  history.replaceState(null, "", location.pathname);
  bindEvents();
  await loadData();
});

function bindEvents() {
  $("reloadButton").addEventListener("click", () => confirmReload() && loadData());
  $("newButton").addEventListener("click", addChallenge);
  $("saveButton").addEventListener("click", saveAll);
  $("duplicateButton").addEventListener("click", duplicateChallenge);
  $("deleteButton").addEventListener("click", deleteChallenge);
  $("addGoalButton").addEventListener("click", () => { current().goals.push(defaultGoal()); markDirty(); renderGoals(); });
  $("searchInput").addEventListener("input", renderList);
  fields.forEach(name => {
    const element = $(name);
    element.addEventListener(element.type === "checkbox" || element.tagName === "SELECT" ? "change" : "input", () => updateField(name));
  });
  addEventListener("beforeunload", event => { if (state.dirty) { event.preventDefault(); event.returnValue = ""; } });
}

async function api(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { "X-RedRaven-Token": state.token, "Content-Type": "application/json", ...(options.headers || {}) } });
  let payload = {};
  try { payload = await response.json(); } catch { payload = { error: "invalid_response" }; }
  if (!response.ok) { const error = new Error(payload.error || `HTTP ${response.status}`); error.status = response.status; error.payload = payload; throw error; }
  return payload;
}

async function loadData() {
  setSaveState("Wird geladen …", "neutral");
  try {
    const data = await api("/api/challenges");
    state.revision = data.revision;
    state.challenges = Array.isArray(data.challenges) ? data.challenges : [];
    state.targets = Array.isArray(data.targets) ? data.targets : [];
    state.lootPacks = Array.isArray(data.lootPacks) ? data.lootPacks : [];
    state.challenges.forEach(normalizeChallenge);
    state.selected = state.challenges.length ? Math.min(Math.max(state.selected, 0), state.challenges.length - 1) : -1;
    state.dirty = false;
    renderAll();
    setSaveState("Gespeichert", "clean");
  } catch (error) {
    setSaveState("Verbindung fehlgeschlagen", "error");
    toast(error.status === 403 ? "Der lokale Zugriffsschlüssel ist ungültig. Öffne die Seite erneut aus RedRaven." : `Laden fehlgeschlagen: ${error.message}`, "error");
  }
}

function normalizeChallenge(item) {
  item.goals = Array.isArray(item.goals) && item.goals.length ? item.goals : [{ statTable: item.statTable || "survival_stats", statColumn: item.statColumn || "puppets_killed", target: Number(item.target) || 1 }];
  item.rewardLootPackNames = Array.isArray(item.rewardLootPackNames) ? item.rewardLootPackNames : (item.rewardLootPackName ? [item.rewardLootPackName] : []);
  item.type ||= "Weekly"; item.goalScope ||= "Community"; item.goalLogic ||= "Any"; item.rewardDistribution ||= "PerParticipant";
}

function renderAll() { renderList(); renderEditor(); }

function renderList() {
  const list = $("challengeList"); list.replaceChildren();
  const query = $("searchInput").value.trim().toLocaleLowerCase("de");
  state.challenges.forEach((challenge, index) => {
    if (query && !`${challenge.title} ${challenge.id} ${challenge.type}`.toLocaleLowerCase("de").includes(query)) return;
    const button = document.createElement("button"); button.type = "button"; button.className = "challenge-card" + (index === state.selected ? " selected" : "");
    const top = document.createElement("div"); top.className = "card-top";
    const title = document.createElement("strong"); title.textContent = challenge.title || "Ohne Titel";
    const badge = document.createElement("span"); badge.className = "badge"; badge.textContent = challenge.type || "Weekly";
    const meta = document.createElement("small");
    const dot = document.createElement("span"); dot.className = "dot" + (challenge.enabled ? " on" : "");
    meta.append(dot, document.createTextNode(`${challenge.id || "ohne-id"} · ${scheduleLabel(challenge)}`));
    top.append(title, badge); button.append(top, meta);
    button.addEventListener("click", () => { state.selected = index; renderAll(); }); list.append(button);
  });
  $("challengeCount").textContent = `${state.challenges.length} ${state.challenges.length === 1 ? "Challenge" : "Challenges"}`;
}

function renderEditor() {
  const challenge = current();
  $("emptyState").classList.toggle("hidden", !!challenge); $("editorForm").classList.toggle("hidden", !challenge);
  if (!challenge) return;
  $("editorTitle").textContent = challenge.title || "Neue Herausforderung";
  fields.forEach(name => {
    const element = $(name); let value = challenge[name];
    if (name === "startUtc" || name === "endUtc") value = toLocalInput(value);
    if (element.type === "checkbox") element.checked = !!value; else element.value = value ?? "";
  });
  const quiz = String(challenge.type).toLowerCase() === "quiz";
  $("goalsSection").classList.toggle("hidden", quiz); $("quizSection").classList.toggle("hidden", !quiz);
  renderGoals(); renderLootPacks();
}

function renderGoals() {
  const list = $("goalsList"); list.replaceChildren(); const challenge = current(); if (!challenge) return;
  challenge.goals.forEach((goal, index) => {
    const row = document.createElement("div"); row.className = "goal-row";
    const targetLabel = document.createElement("label"); const targetText = document.createElement("span"); targetText.textContent = "Statistikwert";
    const select = document.createElement("select");
    state.targets.forEach(target => { const option = document.createElement("option"); option.value = target.key; option.textContent = `${target.category} · ${target.name} (${target.key})`; select.append(option); });
    select.value = `${goal.statTable}.${goal.statColumn}`;
    select.addEventListener("change", () => { const [table, ...column] = select.value.split("."); goal.statTable = table; goal.statColumn = column.join("."); syncLegacyGoal(); markDirty(); });
    targetLabel.append(targetText, select);
    const amountLabel = document.createElement("label"); const amountText = document.createElement("span"); amountText.textContent = "Zielwert";
    const amount = document.createElement("input"); amount.type = "number"; amount.min = "1"; amount.step = "1"; amount.value = goal.target || 1;
    amount.addEventListener("input", () => { goal.target = Math.max(1, Number(amount.value) || 1); syncLegacyGoal(); markDirty(); }); amountLabel.append(amountText, amount);
    const remove = document.createElement("button"); remove.type = "button"; remove.className = "icon-button"; remove.textContent = "×"; remove.title = "Ziel entfernen";
    remove.disabled = challenge.goals.length <= 1; remove.addEventListener("click", () => { challenge.goals.splice(index, 1); syncLegacyGoal(); markDirty(); renderGoals(); });
    row.append(targetLabel, amountLabel, remove); list.append(row);
  });
}

function renderLootPacks() {
  const list = $("lootPackList"); list.replaceChildren(); const challenge = current(); if (!challenge) return;
  if (!state.lootPacks.length) { const note = document.createElement("div"); note.className = "empty-note"; note.textContent = "Keine aktiven Lootpacks vorhanden."; list.append(note); return; }
  state.lootPacks.forEach(name => {
    const label = document.createElement("label"); label.className = "pack-option";
    const box = document.createElement("input"); box.type = "checkbox"; box.checked = challenge.rewardLootPackNames.some(x => x.toLocaleLowerCase() === name.toLocaleLowerCase());
    box.addEventListener("change", () => { challenge.rewardLootPackNames = box.checked ? [...challenge.rewardLootPackNames, name] : challenge.rewardLootPackNames.filter(x => x.toLocaleLowerCase() !== name.toLocaleLowerCase()); challenge.rewardLootPackName = challenge.rewardLootPackNames[0] || ""; markDirty(); });
    const text = document.createElement("span"); text.textContent = name; label.append(box, text); list.append(label);
  });
}

function updateField(name) {
  const challenge = current(); if (!challenge) return; const element = $(name); let value;
  if (element.type === "checkbox") value = element.checked;
  else if (numeric.has(name)) value = Number(element.value) || 0;
  else if (name === "startUtc" || name === "endUtc") value = fromLocalInput(element.value);
  else value = element.value;
  challenge[name] = value;
  if (name === "type") renderEditor();
  if (["title","id","type","enabled","startUtc","endUtc","durationHours"].includes(name)) renderList();
  if (name === "title") $("editorTitle").textContent = value || "Neue Herausforderung";
  markDirty();
}

function addChallenge() {
  const id = uniqueId("challenge");
  state.challenges.push({ enabled:true,id,type:"Weekly",title:"Neue Herausforderung",description:"",statTable:"survival_stats",statColumn:"puppets_killed",target:1000,goals:[defaultGoal()],goalLogic:"Any",goalScope:"Community",startUtc:"",durationHours:168,minimumParticipationValue:1,minimumParticipationPercent:0,endUtc:"",rewardText:"",rewardMode:"FreeText",rewardDistribution:"PerParticipant",rewardItem:"",rewardItemQuantity:1,rewardItemStackCount:0,rewardItems:[],rewardLootPackName:"",rewardLootPackNames:[],rewardMoney:0,rewardFame:0,completedText:"Ziel erreicht!",quizNumber:nextQuizNumber(),quizQuestion:"",quizAcceptedAnswers:"",quizImagePath:"" });
  state.selected = state.challenges.length - 1; markDirty(); renderAll(); $("title").focus(); $("title").select();
}

function duplicateChallenge() {
  const challenge = current(); if (!challenge) return; const copy = JSON.parse(JSON.stringify(challenge));
  copy.id = uniqueId(`${challenge.id || "challenge"}-copy`); copy.title = `${challenge.title || "Challenge"} (Kopie)`; copy.enabled = false;
  if (String(copy.type).toLowerCase() === "quiz") copy.quizNumber = nextQuizNumber();
  state.challenges.splice(state.selected + 1, 0, copy); state.selected++; markDirty(); renderAll();
}

function deleteChallenge() {
  const challenge = current(); if (!challenge || !confirm(`„${challenge.title || challenge.id}“ wirklich löschen? Die Löschung wird erst mit „Alle speichern“ übernommen.`)) return;
  state.challenges.splice(state.selected, 1); state.selected = Math.min(state.selected, state.challenges.length - 1); markDirty(); renderAll();
}

async function saveAll() {
  syncLegacyGoal(); setSaveState("Speichert …", "neutral"); $("saveButton").disabled = true;
  try {
    let result;
    try {
      result = await submitSave(null);
    } catch (error) {
      if (error.status !== 428 || error.payload?.error !== "reset_confirmation_required") throw error;
      const entries = Array.isArray(error.payload.challenges) ? error.payload.challenges : [];
      const names = entries.map(x => `• ${x.title || x.id}`).join("\n");
      const reset = confirm(`Folgende abgeschlossene oder inaktive Challenges werden reaktiviert:\n\n${names}\n\nSoll der bisherige Fortschritt für den neuen Lauf zurückgesetzt werden?\n\nOK = neuer Nullpunkt / neue Quizversuche\nAbbrechen = bisherigen Stand weiterverwenden`);
      result = await submitSave(reset);
    }
    state.revision = result.revision; state.dirty = false; setSaveState("Gespeichert", "clean"); toast(`${state.challenges.length} Herausforderungen gespeichert.`, "success");
  } catch (error) {
    if (error.status === 409) { setSaveState("Konflikt – neu laden", "error"); toast("Die Challenges wurden inzwischen im Desktop-Tool oder einer anderen Browseransicht geändert. Bitte neu laden und deine Änderungen erneut übernehmen.", "error"); }
    else if (error.status === 400 && error.payload?.details) { setSaveState("Eingaben prüfen", "error"); toast(error.payload.details.join("\n"), "error"); }
    else { setSaveState("Speichern fehlgeschlagen", "error"); toast(`Speichern fehlgeschlagen: ${error.message}`, "error"); }
  } finally { $("saveButton").disabled = false; }
}

function submitSave(resetReactivated) {
  const payload = { revision:state.revision, challenges:state.challenges };
  if (resetReactivated !== null) payload.resetReactivated = resetReactivated;
  return api("/api/challenges", { method:"POST", body:JSON.stringify(payload) });
}

function current() { return state.selected >= 0 ? state.challenges[state.selected] : null; }
function defaultGoal() { const t = state.targets[0] || { table:"survival_stats", column:"puppets_killed" }; return { statTable:t.table, statColumn:t.column, target:1000 }; }
function syncLegacyGoal() { const c = current(); if (!c || !c.goals?.length) return; c.statTable=c.goals[0].statTable; c.statColumn=c.goals[0].statColumn; c.target=c.goals[0].target; }
function uniqueId(seed) { let base=String(seed).toLowerCase().replace(/[^a-z0-9_.-]+/g,"-").replace(/^-+|-+$/g,"") || "challenge"; let id=base,n=2; while(state.challenges.some(x=>String(x.id).toLowerCase()===id.toLowerCase())) id=`${base}-${n++}`; return id; }
function nextQuizNumber() { const used=state.challenges.map(x=>Number(x.quizNumber)||0); let n=1; while(used.includes(n)) n++; return n; }
function markDirty() { state.dirty=true; setSaveState("Nicht gespeichert", "dirty"); }
function setSaveState(text, kind) { const e=$("saveState"); e.textContent=text; e.className=`state ${kind}`; }
function confirmReload() { return !state.dirty || confirm("Nicht gespeicherte Änderungen verwerfen und neu laden?"); }
function toLocalInput(value) { if(!value) return ""; const d=new Date(value); if(Number.isNaN(d.valueOf())) return ""; const local=new Date(d.getTime()-d.getTimezoneOffset()*60000); return local.toISOString().slice(0,16); }
function fromLocalInput(value) { if(!value) return ""; const d=new Date(value); return Number.isNaN(d.valueOf()) ? "" : d.toISOString(); }
function scheduleLabel(c) { if(c.startUtc) { const d=new Date(c.startUtc); if(!Number.isNaN(d.valueOf())) return d.toLocaleString("de-DE",{dateStyle:"short",timeStyle:"short"}); } return c.enabled ? "sofort aktiv" : "deaktiviert"; }
function toast(message, kind="") { const e=$("toast"); e.textContent=message; e.className=`toast show ${kind}`; clearTimeout(toastTimer); toastTimer=setTimeout(()=>e.classList.remove("show"),6500); }
