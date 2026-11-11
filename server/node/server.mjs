// Servidor de partidas online do ADOFAI Online, versão Node.js (para uma máquina própria, como a Oracle Cloud "Always Free"
// em São Paulo). Fala exatamente o mesmo protocolo do servidor da Cloudflare (Server/online): o jogo usa um ou
// outro sem diferença. Tudo fica na memória do processo (uma máquina só): salas, jogadores e a lista pública.
//
// Rotas:
//   GET  /                   -> status/versão
//   GET  /rooms              -> salas públicas abertas (JSON)
//   POST /rooms              -> cria sala; corpo {name, public, region}; resposta {code}
//   GET  /rooms/<CODE>/ws    -> WebSocket da sala
//
// Variáveis de ambiente: PORT (padrão 8787), HOST (padrão 127.0.0.1; o Caddy na frente cuida do HTTPS),
// REGION (continente das salas, padrão SA) e SERVER_NAME (aparece como "servidor" na sala, padrão GRU).
import http from "node:http";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { pipeline } from "node:stream/promises";
import { WebSocketServer } from "ws";

const PROTOCOL_VERSION = 1;
const MAX_PLAYERS = 4;
const MAX_SPECTATORS = 4;
const CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem 0/O e 1/I
const EMPTY_ROOM_TTL_MS = 60_000;
const MAX_MESSAGE = 16_384;

const PORT = Number(process.env.PORT ?? 8787);
const HOST = process.env.HOST ?? "127.0.0.1";
const REGION = (process.env.REGION ?? "SA").toUpperCase();
const SERVER_NAME = process.env.SERVER_NAME ?? "GRU";

// fases enviadas pelo anfitrião para quem não as tem (não estão no ADOFAI.gg/TUF): ficam um tempo no disco
const FILES_DIR = process.env.FILES_DIR ?? path.join(process.cwd(), "files");
const MAX_FILE_BYTES = 150 * 1024 * 1024;
const MAX_TOTAL_BYTES = 3 * 1024 * 1024 * 1024;
const FILE_TTL_MS = 10 * 60 * 1000;
fs.mkdirSync(FILES_DIR, { recursive: true });
for (const n of fs.readdirSync(FILES_DIR)) if (n.endsWith(".part")) fs.rmSync(path.join(FILES_DIR, n), { force: true });

function filesTotal() {
  let total = 0;
  for (const n of fs.readdirSync(FILES_DIR)) {
    try {
      total += fs.statSync(path.join(FILES_DIR, n)).size;
    } catch {}
  }
  return total;
}

setInterval(() => {
  const now = Date.now();
  for (const n of fs.readdirSync(FILES_DIR)) {
    const p = path.join(FILES_DIR, n);
    try {
      if (now - fs.statSync(p).mtimeMs > FILE_TTL_MS) fs.rmSync(p, { force: true });
    } catch {}
  }
}, 60_000);

async function handleFile(req, res, key) {
  if (!/^[A-Za-z0-9_-]{4,64}$/.test(key)) return json(res, 400, { error: "bad key" });
  const file = path.join(FILES_DIR, key + ".zip");
  if (req.method === "GET" || req.method === "HEAD") {
    let st;
    try {
      st = fs.statSync(file);
    } catch {
      return json(res, 404, { error: "not found" });
    }
    const now = new Date();
    fs.utimes(file, now, now, () => {}); // quem baixa renova o prazo
    res.writeHead(200, { "content-type": "application/zip", "content-length": st.size });
    if (req.method === "HEAD") return res.end();
    return fs.createReadStream(file).pipe(res);
  }
  if (req.method === "PUT") {
    // só quem está numa sala existente pode enviar, e poucas vezes por hora
    if (rateLimited("upload", clientIp(req), UPLOADS_PER_IP_PER_HOUR)) return json(res, 429, { error: "muitos envios; tente mais tarde" });
    const room = String(req.headers["x-room"] ?? "").toUpperCase();
    if (!rooms.has(room)) return json(res, 403, { error: "sala inválida" });
    const declared = Number(req.headers["content-length"] ?? 0);
    if (declared > MAX_FILE_BYTES) return json(res, 413, { error: "arquivo grande demais" });
    if (filesTotal() + declared > MAX_TOTAL_BYTES) return json(res, 507, { error: "servidor sem espaço para fases" });
    const tmp = file + "." + crypto.randomBytes(4).toString("hex") + ".part";
    let size = 0;
    req.on("data", (c) => {
      size += c.length;
      if (size > MAX_FILE_BYTES) req.destroy();
    });
    try {
      await pipeline(req, fs.createWriteStream(tmp));
      fs.renameSync(tmp, file);
      return json(res, 200, { ok: true, size });
    } catch {
      fs.rmSync(tmp, { force: true });
      if (!res.headersSent) return json(res, 413, { error: "envio interrompido" });
      return;
    }
  }
  return json(res, 405, { error: "method" });
}

/** @type {Map<string, Room>} */
const rooms = new Map();

function cleanText(value, max) {
  return String(value ?? "")
    .replace(/[\u0000-\u001f\u007f]/g, "")
    .trim()
    .slice(0, max);
}

function sha256hex(text) {
  return crypto.createHash("sha256").update(text).digest("hex");
}

function newCode() {
  for (;;) {
    const bytes = crypto.randomBytes(5);
    const code = Array.from(bytes, (b) => CODE_ALPHABET[b % CODE_ALPHABET.length]).join("");
    if (!rooms.has(code)) return code;
  }
}

class Room {
  constructor(code, name, isPublic, password = "") {
    this.code = code;
    // senha da sala (opcional): o servidor guarda só o hash; o cliente manda sha256(CODIGO:senha) ao entrar
    this.pwh = password ? sha256hex(code + ":" + password) : null;
    this.banned = new Set(); // IPs banidos pelo anfitrião (só em memória, enquanto a sala existe)
    this.ipOf = new Map(); // conexão -> IP, para banir
    this.chatState = new Map(); // conexão -> { stamps: [ms], last, lastAt } (anti-spam do chat)
    this.reportAt = new Map(); // conexão -> hora da última denúncia
    this.recentChat = []; // últimas mensagens da sala (só em memória), para anexar às denúncias

    this.name = name;
    this.isPublic = isPublic;
    this.colo = SERVER_NAME;
    this.region = REGION;
    this.nextId = 1;
    this.hostId = 0;
    this.level = null;
    this.mods = {};
    this.queue = [];
    this.inGame = false;
    this.order = [];
    this.updated = Date.now();
    /** @type {Map<import("ws").WebSocket, {id:number,name:string,ready:boolean,joined:number,spectator:boolean}>} */
    this.sockets = new Map();
    this.emptyTimer = setTimeout(() => this.expireIfEmpty(), EMPTY_ROOM_TTL_MS);
  }

  players() {
    return [...this.sockets.entries()].map(([ws, info]) => ({ ws, info }));
  }

  send(ws, msg) {
    if (ws.readyState === ws.OPEN) ws.send(JSON.stringify(msg));
  }

  broadcast(msg, exceptId = -1) {
    const text = JSON.stringify(msg);
    for (const [ws, info] of this.sockets) {
      if (info.id !== exceptId && ws.readyState === ws.OPEN) ws.send(text);
    }
  }

  snapshot() {
    return {
      code: this.code,
      name: this.name,
      colo: this.colo,
      region: this.region,
      host: this.hostId,
      level: this.level,
      mods: this.mods,
      queue: this.queue,
      inGame: this.inGame,
      order: this.inGame ? this.order : [],
      players: this.players()
        .map((p) => p.info)
        .sort((a, b) => a.joined - b.joined),
    };
  }

  listing() {
    const level = this.level;
    return {
      code: this.code,
      name: this.name,
      colo: this.colo,
      region: this.region,
      players: this.sockets.size,
      maxPlayers: MAX_PLAYERS,
      locked: !!this.pwh,
      inGame: this.inGame,
      level: level && level.title ? String(level.title).slice(0, 64) : "",
      updated: this.updated,
    };
  }

  // na lista pública: pública e (com jogadores, ou recém-criada esperando o criador entrar)
  listed() {
    return this.isPublic && (this.sockets.size > 0 || this.nextId === 1);
  }

  expireIfEmpty() {
    if (this.sockets.size === 0) rooms.delete(this.code);
  }

  join(ws, name, version, pwh = "", ip = "") {
    if (version !== PROTOCOL_VERSION) {
      this.send(ws, { t: "error", code: "version", message: "Versão do jogo diferente da sala." });
      ws.close(4001, "version");
      return;
    }
    if (ip && this.banned.has(ip)) {
      this.send(ws, { t: "error", code: "banned", message: "Você foi banido desta sala." });
      ws.close(4006, "banned");
      return;
    }
    if (this.pwh && pwh !== this.pwh) {
      this.send(ws, { t: "error", code: "password", message: "Senha da sala incorreta." });
      ws.close(4005, "password");
      return;
    }
    if (ip) this.ipOf.set(ws, ip);
    const all = this.players();
    const racers = all.filter((p) => !p.info.spectator).length;
    const spectators = all.length - racers;
    // com a partida em andamento, quem entra fica assistindo até ela acabar
    const asSpectator = this.inGame;
    if ((!asSpectator && racers >= MAX_PLAYERS) || (asSpectator && spectators >= MAX_SPECTATORS)) {
      this.send(ws, { t: "error", code: "full", message: "Sala cheia." });
      ws.close(4002, "full");
      return;
    }
    clearTimeout(this.emptyTimer);
    const info = { id: this.nextId++, name, ready: false, joined: Date.now(), spectator: asSpectator };
    this.sockets.set(ws, info);
    if (this.hostId === 0 || !this.players().some((p) => p.info.id === this.hostId)) {
      this.hostId = info.id;
    }
    this.updated = Date.now();
    this.send(ws, { t: "welcome", you: info.id, room: this.snapshot(), serverTime: Date.now() });
    this.broadcast({ t: "join", player: info }, info.id);

    ws.on("message", (data, isBinary) => {
      if (isBinary || data.length > MAX_MESSAGE) return;
      let msg;
      try {
        msg = JSON.parse(data.toString());
      } catch {
        return;
      }
      this.handle(ws, msg);
    });
    ws.on("close", () => this.leave(ws));
    ws.on("error", () => this.leave(ws));
  }

  handle(ws, msg) {
    const me = this.sockets.get(ws);
    if (!me || !msg || typeof msg !== "object") return;
    const isHost = me.id === this.hostId;
    switch (msg.t) {
      case "ping":
        // sincronização de relógio: o cliente estima o atraso e a diferença para serverTime
        this.send(ws, { t: "pong", c: msg.c, s: Date.now() });
        this.updated = Date.now();
        break;

      case "ready":
        me.ready = !!msg.ready;
        this.broadcast({ t: "ready", id: me.id, ready: me.ready });
        break;

      case "mods":
        // modificadores da partida (só o anfitrião, fora de partida); o servidor só guarda valores conhecidos
        if (!isHost || this.inGame) return;
        this.mods = cleanMods(msg.mods);
        this.broadcast({ t: "mods", mods: this.mods });
        break;

      case "queue":
        // fila de fases (só o anfitrião, a qualquer momento): o anfitrião avança a fila quando a partida acaba
        if (!isHost) return;
        this.queue = cleanQueue(msg.queue);
        this.broadcast({ t: "queue", queue: this.queue });
        break;

      case "level":
        if (!isHost || this.inGame) return;
        this.level = msg.level ?? null;
        // trocar de fase desmarca o "pronto" de todos (precisam baixar/confirmar de novo)
        for (const info of this.sockets.values()) info.ready = false;
        this.broadcast({ t: "level", level: this.level });
        break;

      case "start": {
        if (!isHost || this.inGame) return;
        const all = this.players();
        if (!this.level || all.some((p) => !p.info.ready)) {
          this.send(ws, { t: "error", code: "notready", message: "Nem todos estão prontos." });
          return;
        }
        this.inGame = true;
        this.order = all.sort((a, b) => a.info.joined - b.info.joined).map((p) => p.info.id);
        // começa em 3 s no relógio do servidor; cada cliente converte para o próprio relógio
        this.broadcast({ t: "start", at: Date.now() + 3000, order: this.order });
        break;
      }

      case "end":
        if (!isHost) return;
        console.log(`[${new Date().toISOString()}] sala ${this.code}: partida encerrada pelo anfitrião`);
        this.inGame = false;
        this.order = [];
        for (const info of this.sockets.values()) {
          // espectadores viram jogadores para a próxima partida
          info.ready = false;
          info.spectator = false;
        }
        this.broadcast({ t: "end", results: msg.results ?? null, players: this.players().map((p) => p.info) });
        break;

      case "ev":
        // evento de jogo (acerto, queda, vitória...): só repassa para os outros
        this.broadcast({ t: "ev", from: me.id, d: msg.d }, me.id);
        break;

      case "chat": {
        const text = cleanText(msg.text, 140);
        if (!text) break;
        // anti-spam: no máximo 5 mensagens em 8 s, intervalo mínimo de 0,6 s e sem repetir a mesma em 6 s
        const now = Date.now();
        const st = this.chatState.get(ws) ?? { stamps: [], last: "", lastAt: 0 };
        st.stamps = st.stamps.filter((t) => now - t < 8000);
        if (st.stamps.length >= 5 || (st.stamps.length > 0 && now - st.stamps[st.stamps.length - 1] < 600) || (text === st.last && now - st.lastAt < 6000)) {
          this.chatState.set(ws, st);
          this.send(ws, { t: "error", code: "slow", message: "Calma! Você está mandando mensagens rápido demais." });
          break;
        }
        st.stamps.push(now);
        st.last = text;
        st.lastAt = now;
        this.chatState.set(ws, st);
        this.recentChat.push({ id: me.id, name: me.name, text });
        if (this.recentChat.length > 30) this.recentChat.shift();
        this.broadcast({ t: "chat", from: me.id, text });
        break;
      }

      case "report": {
        // denúncia de um jogador: vai para o registro do servidor (nome e últimas mensagens dele) e avisa o anfitrião
        const target = this.players().find((p) => p.info.id === msg.id);
        if (!target || target.info.id === me.id) break;
        const now = Date.now();
        if (now - (this.reportAt.get(ws) ?? 0) < 30_000) {
          this.send(ws, { t: "error", code: "slow", message: "Calma! Espere um pouco para denunciar de novo." });
          break;
        }
        this.reportAt.set(ws, now);
        const lines = this.recentChat.filter((c) => c.id === target.info.id).slice(-8).map((c) => c.text);
        console.log(`[${new Date().toISOString()}] DENUNCIA sala ${this.code}: "${me.name}" denunciou "${target.info.name}" (id ${target.info.id}); ultimas mensagens dele: ${JSON.stringify(lines)}`);
        this.send(ws, { t: "notice", code: "report_sent" });
        if (this.hostId !== me.id) {
          const host = this.players().find((p) => p.info.id === this.hostId);
          if (host) this.send(host.ws, { t: "notice", code: "report_host", name: target.info.name });
        }
        break;
      }

      case "kick": {
        if (!isHost) return;
        const target = this.players().find((p) => p.info.id === msg.id);
        if (target) {
          this.send(target.ws, { t: "error", code: "kicked", message: "Você foi removido da sala." });
          target.ws.close(4004, "kicked");
        }
        break;
      }

      case "ban": {
        // remove e impede de voltar (pelo IP) enquanto a sala existir; o anfitrião não bane a si mesmo
        if (!isHost) return;
        const target = this.players().find((p) => p.info.id === msg.id);
        if (!target || target.info.id === me.id) break;
        const ip = this.ipOf.get(target.ws);
        if (ip && this.banned.size < 50) this.banned.add(ip);
        this.send(target.ws, { t: "error", code: "banned", message: "Você foi banido desta sala." });
        target.ws.close(4006, "banned");
        break;
      }
    }
  }

  leave(ws) {
    const me = this.sockets.get(ws);
    if (!me) return;
    this.sockets.delete(ws);
    this.ipOf.delete(ws);
    this.chatState.delete(ws);
    this.reportAt.delete(ws);
    const rest = this.players().sort((a, b) => a.info.joined - b.info.joined);
    this.broadcast({ t: "leave", id: me.id });
    if (me.id === this.hostId) {
      this.hostId = rest.length > 0 ? rest[0].info.id : 0;
      if (rest.length > 0) this.broadcast({ t: "host", id: this.hostId });
    }
    if (rest.length === 0) {
      this.inGame = false;
      // sala vazia: some depois de 1 min se ninguém voltar
      this.emptyTimer = setTimeout(() => this.expireIfEmpty(), EMPTY_ROOM_TTL_MS);
    }
  }
}

function json(res, status, data) {
  res.writeHead(status, {
    "content-type": "application/json; charset=utf-8",
    "access-control-allow-origin": "*",
  });
  res.end(JSON.stringify(data));
}

function readBody(req, limit = 4096) {
  return new Promise((resolve) => {
    let body = "";
    req.on("data", (chunk) => {
      body += chunk;
      if (body.length > limit) req.destroy();
    });
    req.on("end", () => resolve(body));
    req.on("error", () => resolve(""));
  });
}

// proteção contra abuso (o servidor é público): limites por IP. O Node só escuta em 127.0.0.1, atrás do Caddy,
// que informa o IP real em X-Forwarded-For.
const MAX_ROOMS_TOTAL = 300;
const ROOMS_PER_IP_PER_HOUR = 20;
const UPLOADS_PER_IP_PER_HOUR = 20;
const MAX_SOCKETS_PER_IP = 12;
const hits = new Map(); // "tipo|ip" -> horas (ms) dos últimos usos
const socketsPerIp = new Map();

function clientIp(req) {
  const fwd = String(req.headers["x-forwarded-for"] ?? "").split(",")[0].trim();
  return fwd || req.socket.remoteAddress || "?";
}

function rateLimited(kind, ip, max) {
  const now = Date.now();
  const key = kind + "|" + ip;
  const list = (hits.get(key) ?? []).filter((t) => now - t < 3600_000);
  if (list.length >= max) {
    hits.set(key, list);
    return true;
  }
  list.push(now);
  hits.set(key, list);
  return false;
}

setInterval(() => {
  const now = Date.now();
  for (const [key, list] of hits) {
    const fresh = list.filter((t) => now - t < 3600_000);
    if (fresh.length === 0) hits.delete(key);
    else hits.set(key, fresh);
  }
}, 10 * 60_000);

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url ?? "/", "http://localhost");
  const parts = url.pathname.split("/").filter(Boolean);

  if (parts.length === 0 && req.method === "GET") {
    // o jogo usa "region" para mostrar primeiro as salas do próprio continente
    return json(res, 200, { ok: true, protocol: PROTOCOL_VERSION, colo: SERVER_NAME, region: REGION, files: true });
  }

  if (parts[0] === "files" && parts.length === 2) {
    return handleFile(req, res, parts[1]);
  }

  if (parts[0] === "rooms" && parts.length === 1) {
    if (req.method === "GET") {
      const list = [...rooms.values()]
        .filter((r) => r.listed())
        .map((r) => r.listing())
        .sort((a, b) => b.updated - a.updated)
        .slice(0, 100);
      return json(res, 200, list);
    }
    if (req.method === "POST") {
      if (rooms.size >= MAX_ROOMS_TOTAL || rateLimited("room", clientIp(req), ROOMS_PER_IP_PER_HOUR)) {
        return json(res, 429, { error: "muitas salas; tente de novo mais tarde" });
      }
      let body = {};
      try {
        body = JSON.parse((await readBody(req)) || "{}");
      } catch {
        // corpo vazio ou inválido: usa os padrões
      }
      const code = newCode();
      const room = new Room(code, cleanText(body.name, 32) || "Sala", body.public !== false, cleanText(body.password, 40));
      rooms.set(code, room);
      return json(res, 200, { code, colo: room.colo, region: room.region });
    }
    return json(res, 405, { error: "method" });
  }

  return json(res, 404, { error: "not found" });
});

const wss = new WebSocketServer({ noServer: true, maxPayload: MAX_MESSAGE });

server.on("upgrade", (req, socket, head) => {
  const url = new URL(req.url ?? "/", "http://localhost");
  const parts = url.pathname.split("/").filter(Boolean);
  const code = (parts[1] ?? "").toUpperCase();
  const room = parts[0] === "rooms" && parts[2] === "ws" ? rooms.get(code) : undefined;
  if (!room) {
    socket.write("HTTP/1.1 404 Not Found\r\n\r\n");
    socket.destroy();
    return;
  }
  const ip = clientIp(req);
  if ((socketsPerIp.get(ip) ?? 0) >= MAX_SOCKETS_PER_IP) {
    socket.write("HTTP/1.1 429 Too Many Requests\r\n\r\n");
    socket.destroy();
    return;
  }
  socketsPerIp.set(ip, (socketsPerIp.get(ip) ?? 0) + 1);
  wss.handleUpgrade(req, socket, head, (ws) => {
    ws.on("close", () => {
      const n = (socketsPerIp.get(ip) ?? 1) - 1;
      if (n <= 0) socketsPerIp.delete(ip);
      else socketsPerIp.set(ip, n);
    });
    ws.missed = 0;
    ws.on("pong", () => (ws.missed = 0));
    ws.on("message", () => (ws.missed = 0));
    ws.on("close", (code, reason) => console.log(`[${new Date().toISOString()}] conexão fechada code=${code} ${reason}`));
    const name = cleanText(url.searchParams.get("name"), 24) || "Jogador";
    room.join(ws, name, Number(url.searchParams.get("v") ?? 0), url.searchParams.get("pwh") ?? "", ip);
  });
});

// conexões que caíram sem avisar (jogador que perdeu o sinal): o ping do protocolo WebSocket as detecta
setInterval(() => {
  for (const ws of wss.clients) {
    // o aparelho pode ficar uns segundos sem responder (carregando fase, tela apagada): só derruba depois de
    // ~2 min sem nenhum sinal (pong ou qualquer mensagem do jogo, que manda ping próprio a cada poucos segundos)
    ws.missed = (ws.missed ?? 0) + 1;
    if (ws.missed > 4) {
      console.log(`[${new Date().toISOString()}] derrubando conexão sem sinal`);
      ws.terminate();
      continue;
    }
    ws.ping();
  }
}, 30_000);
const QUEUE_MAX = 8;
const QUEUE_FIELDS = ["source", "hash", "title", "artist", "author", "url", "key"];

function cleanQueue(q) {
  const out = [];
  if (!Array.isArray(q)) return out;
  for (const item of q.slice(0, QUEUE_MAX)) {
    if (!item || typeof item !== "object") continue;
    const lv = {};
    for (const f of QUEUE_FIELDS) if (item[f] != null) lv[f] = cleanText(item[f], f === "url" ? 400 : 120);
    if (lv.title) out.push(lv);
  }
  return out;
}

function cleanMods(m) {
  const out = {};
  if (m && m.immortal === true) out.immortal = true;
  const sp = Number(m && m.speed);
  if (Number.isFinite(sp) && sp >= 0.5 && sp <= 2 && Math.abs(sp - 1) > 0.001) out.speed = Math.round(sp * 100) / 100;
  return out;
}


server.listen(PORT, HOST, () => {
  console.log(`servidor online do ADOFAI Online em http://${HOST}:${PORT} (região ${REGION}, ${SERVER_NAME})`);
});
