// Teste rápido do servidor: cria sala, conecta dois jogadores, troca de anfitrião, pronto/início e repasse de eventos.
// Uso: npm run dev (em outro terminal) e depois: node test/smoke.mjs [http://127.0.0.1:8787]
const base = process.argv[2] ?? "http://127.0.0.1:8787";
const wsBase = base.replace(/^http/, "ws");

function connect(code, name) {
  const ws = new WebSocket(`${wsBase}/rooms/${code}/ws?v=1&name=${encodeURIComponent(name)}`);
  const inbox = [];
  const waiters = [];
  ws.addEventListener("message", (e) => {
    const msg = JSON.parse(e.data);
    const i = waiters.findIndex((w) => w.pred(msg));
    if (i >= 0) waiters.splice(i, 1)[0].resolve(msg);
    else inbox.push(msg);
  });
  const next = (pred, ms = 5000) => {
    const i = inbox.findIndex(pred);
    if (i >= 0) return Promise.resolve(inbox.splice(i, 1)[0]);
    return new Promise((resolve, reject) => {
      waiters.push({ pred, resolve });
      setTimeout(() => reject(new Error(`${name}: timeout`)), ms);
    });
  };
  const opened = new Promise((r) => ws.addEventListener("open", r));
  return { ws, next, opened, send: (m) => ws.send(JSON.stringify(m)) };
}

function check(cond, label) {
  if (!cond) throw new Error("FALHOU: " + label);
  console.log("ok -", label);
}

const created = await (await fetch(`${base}/rooms`, { method: "POST", body: JSON.stringify({ name: "Teste", public: true }) })).json();
check(/^[A-Z0-9]{5}$/.test(created.code), `sala criada ${created.code}`);

const a = connect(created.code, "Ana");
await a.opened;
const wa = await a.next((m) => m.t === "welcome");
check(wa.room.host === wa.you, "primeiro a entrar é o anfitrião");

const list = await (await fetch(`${base}/rooms`)).json();
check(list.some((r) => r.code === created.code && r.players === 1), "sala aparece na lista pública");

const b = connect(created.code, "Bia");
await b.opened;
const wb = await b.next((m) => m.t === "welcome");
check(wb.room.players.length === 2, "segundo jogador vê os dois");
await a.next((m) => m.t === "join" && m.player.name === "Bia");

a.send({ t: "ping", c: 123 });
const pong = await a.next((m) => m.t === "pong");
check(pong.c === 123 && typeof pong.s === "number", "ping/pong com hora do servidor");

a.send({ t: "level", level: { kind: "official", id: "1-X", title: "1-X" } });
await b.next((m) => m.t === "level");
a.send({ t: "start" });
await a.next((m) => m.t === "error" && m.code === "notready");
check(true, "não começa sem todos prontos");

a.send({ t: "ready", ready: true });
b.send({ t: "ready", ready: true });
await a.next((m) => m.t === "ready" && m.id === wb.you);
a.send({ t: "start" });
const start = await b.next((m) => m.t === "start");
console.log("  start.at - agora local =", start.at - Date.now(), "ms; ordem", start.order);
check(start.order.length === 2, "início com ordem dos jogadores");

const c = connect(created.code, "Caio");
await c.opened;
const wc = await c.next((m) => m.t === "welcome");
const me = wc.room.players.find((p) => p.id === wc.you);
check(me.spectator === true && wc.room.inGame && wc.room.order.length === 2, "quem entra na partida em andamento é espectador");

b.send({ t: "ev", d: { k: "hit", f: 12, ms: 3.5 } });
const ev = await a.next((m) => m.t === "ev");
check(ev.from === wb.you && ev.d.f === 12, "evento de jogo repassado");
await c.next((m) => m.t === "ev" && m.from === wb.you);
check(true, "espectador recebe os eventos da partida");

a.send({ t: "end" });
const end = await c.next((m) => m.t === "end");
check(end.players.every((p) => !p.spectator), "no fim da partida o espectador vira jogador");
c.ws.close();

a.ws.close();
const host = await b.next((m) => m.t === "host");
check(host.id === wb.you, "anfitrião passa para quem ficou");

b.ws.close();
console.log("tudo certo");
process.exit(0);
