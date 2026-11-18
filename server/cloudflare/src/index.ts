// Servidor de partidas online do ADOFAI Online (Cloudflare Workers + Durable Objects, plano gratuito).
//
// Rotas:
//   GET  /                       -> status/versão
//   GET  /rooms                  -> salas públicas abertas (JSON)
//   POST /rooms                  -> cria sala; corpo {name, public, region}; resposta {code}
//   GET  /rooms/<CODE>/ws        -> WebSocket da sala (Upgrade: websocket)
//
// Cada sala é um Durable Object (Room) identificado pelo código. O objeto nasce no data center da Cloudflare
// mais próximo de quem criou a sala, ou na região pedida (locationHint), o que mantém o ping baixo para quem
// joga na mesma região. Uma lista de salas públicas fica num Durable Object único (Directory).
//
// A sala não depende de nenhum jogador: se o anfitrião sair, o próximo a ter entrado vira anfitrião.
// O servidor só repassa mensagens; quem julga acertos/erros de cada jogador é o próprio aparelho dele.
import { DurableObject } from "cloudflare:workers";

export interface Env {
  ROOM: DurableObjectNamespace<Room>;
  DIRECTORY: DurableObjectNamespace<Directory>;
}

const PROTOCOL_VERSION = 1;
const MAX_PLAYERS = 8;
const MAX_SPECTATORS = 4; // quem entra com a partida em andamento assiste até ela acabar
const CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem 0/O e 1/I
// região da sala = continente (código do Cloudflare); cada um aponta para um local de Durable Objects
const REGION_HINTS: Record<string, DurableObjectLocationHint> = {
  SA: "sam", NA: "enam", EU: "weur", AS: "apac", OC: "oc", AF: "afr",
};

async function sha256hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

// hash curto (IP + dia) para contar "ativos hoje" sem guardar o IP; só números saem do servidor
const QUEUE_MAX = 8;
const SUGGESTIONS_MAX = 20;
const SUGGESTIONS_PER_PLAYER = 3;

// uma fase sugerida: só os campos conhecidos, com tamanho limitado (a mesma limpeza da fila de fases)
function cleanLevel(item: unknown): Record<string, string> | null {
  if (!item || typeof item !== "object") return null;
  const src = item as Record<string, unknown>;
  const out: Record<string, string> = {};
  for (const f of ["source", "hash", "title", "artist", "author", "url", "key"]) {
    if (src[f] != null) out[f] = cleanText(src[f], f === "url" ? 400 : 120);
  }
  return out.title ? out : null;
}

async function statsHash(ip: string | null): Promise<string> {
  if (!ip) return "";
  return (await sha256hex(ip + ":" + new Date().toISOString().slice(0, 10) + ":stats")).slice(0, 16);
}

function json(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", "access-control-allow-origin": "*" },
  });
}

function newCode(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(5));
  return Array.from(bytes, (b) => CODE_ALPHABET[b % CODE_ALPHABET.length]).join("");
}

function cleanText(value: unknown, max: number): string {
  return String(value ?? "")
    .replace(/[\u0000-\u001f\u007f]/g, "")
    .trim()
    .slice(0, max);
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);

    if (parts.length === 0) {
      // o jogo usa "region" para mostrar primeiro as salas do próprio continente
      return json({ ok: true, protocol: PROTOCOL_VERSION, colo: request.cf?.colo ?? null, region: request.cf?.continent ?? null });
    }

    if (parts[0] === "stats" && parts.length === 1 && request.method === "GET") {
      const directory = env.DIRECTORY.get(env.DIRECTORY.idFromName("global"));
      return json(await directory.stats());
    }

    if (parts[0] === "rooms" && parts.length === 1) {
      const directory = env.DIRECTORY.get(env.DIRECTORY.idFromName("global"));
      if (request.method === "GET") {
        await directory.seen(await statsHash(request.headers.get("CF-Connecting-IP")));
        return json(await directory.list());
      }
      if (request.method === "POST") {
        let body: Record<string, unknown> = {};
        try {
          body = await request.json();
        } catch {
          // corpo vazio: usa os padrões
        }
        // região pedida pelo jogador, ou o continente de onde ele está conectando
        let region = String(body.region ?? "").toUpperCase();
        if (!(region in REGION_HINTS)) region = String(request.cf?.continent ?? "");
        const hint = REGION_HINTS[region];
        const code = newCode();
        const room = env.ROOM.get(env.ROOM.idFromName(code), hint ? { locationHint: hint } : undefined);
        const info = await room.init(code, cleanText(body.name, 32) || "Sala", body.public !== false, region, cleanText(body.password, 40));
        return json(info);
      }
      return json({ error: "method" }, 405);
    }

    if (parts[0] === "rooms" && parts.length === 3 && parts[2] === "ws") {
      if (request.headers.get("Upgrade") !== "websocket") {
        return json({ error: "expected websocket" }, 426);
      }
      const code = parts[1].toUpperCase();
      if (!/^[A-Z0-9]{5}$/.test(code)) {
        return json({ error: "bad code" }, 400);
      }
      const room = env.ROOM.get(env.ROOM.idFromName(code));
      return room.fetch(request);
    }

    return json({ error: "not found" }, 404);
  },
} satisfies ExportedHandler<Env>;

// ------------------------------------------------------------------------------------------------ Directory

interface RoomListing {
  locked?: boolean;
  code: string;
  name: string;
  colo: string;
  region: string;
  players: number;
  maxPlayers: number;
  inGame: boolean;
  level: string;
  updated: number;
}

// A lista fica no armazenamento do objeto (não na memória): a Cloudflare descarrega objetos parados depois de
// alguns minutos, e uma lista só em memória sumiria junto (as salas paravam de aparecer para quem chegasse depois).
// Cada sala com jogadores renova a própria entrada a cada minuto (no ping); a que não renova em 3 min é removida.
const LISTING_TTL_MS = 180_000;

export class Directory extends DurableObject<Env> {
  async list(): Promise<RoomListing[]> {
    const now = Date.now();
    const entries = await this.ctx.storage.list<RoomListing>({ prefix: "room:" });
    const alive: RoomListing[] = [];
    const stale: string[] = [];
    for (const [key, r] of entries) {
      if (now - r.updated > LISTING_TTL_MS) stale.push(key);
      else alive.push(r);
    }
    if (stale.length > 0) await this.ctx.storage.delete(stale);
    return alive.sort((a, b) => b.updated - a.updated).slice(0, 100);
  }

  // Cada sala numera as próprias atualizações (epoch = quando o objeto da sala acordou, seq = contador). Quando vários
  // jogadores saem ao mesmo tempo as chamadas chegam fora de ordem; a mais antiga não pode sobrescrever a mais nova.
  private async isNewer(code: string, epoch: number, seq: number): Promise<boolean> {
    const cur = await this.ctx.storage.get<{ epoch: number; seq: number; at: number }>("seq:" + code);
    if (cur && (cur.epoch > epoch || (cur.epoch === epoch && cur.seq >= seq))) return false;
    await this.ctx.storage.put("seq:" + code, { epoch, seq, at: Date.now() });
    return true;
  }

  async upsert(listing: RoomListing, epoch = 0, seq = 0): Promise<void> {
    if (!(await this.isNewer(listing.code, epoch, seq))) return;
    await this.ctx.storage.put("room:" + listing.code, { ...listing, updated: Date.now() });
  }

  async remove(code: string, epoch = 0, seq = 0): Promise<void> {
    if (!(await this.isNewer(code, epoch, seq))) return;
    await this.ctx.storage.delete(["room:" + code, "priv:" + code]);
  }

  // salas privadas não aparecem na lista, mas os jogadores delas contam em /stats (0 jogadores = não conta)
  async setPrivate(code: string, players: number, epoch = 0, seq = 0): Promise<void> {
    if (!(await this.isNewer(code, epoch, seq))) return;
    await this.ctx.storage.put("priv:" + code, { players, updated: Date.now() });
  }

  // jogador distinto visto hoje (hash curto de IP + dia); vira o dia quando a data muda
  async seen(hash: string): Promise<void> {
    if (!hash) return;
    const day = new Date().toISOString().slice(0, 10);
    if ((await this.ctx.storage.get<string>("statsDay")) !== day) {
      const old = await this.ctx.storage.list({ prefix: "seen:" });
      await this.ctx.storage.put("yesterday", old.size);
      const keys = [...old.keys()];
      for (let i = 0; i < keys.length; i += 120) await this.ctx.storage.delete(keys.slice(i, i + 120));
      await this.ctx.storage.put("statsDay", day);
    }
    const key = "seen:" + hash;
    if (!(await this.ctx.storage.get(key))) {
      const count = (await this.ctx.storage.get<number>("todayCount")) ?? 0;
      if (count < 20000) {
        await this.ctx.storage.put(key, 1);
        await this.ctx.storage.put("todayCount", count + 1);
      }
    }
  }

  async stats(): Promise<{ online: number; rooms: number; today: number; yesterday: number }> {
    const now = Date.now();
    let online = 0;
    let rooms = 0;
    for (const [, r] of await this.ctx.storage.list<{ players: number; updated: number }>({ prefix: "room:" })) {
      if (now - r.updated <= LISTING_TTL_MS && r.players > 0) { online += r.players; rooms++; }
    }
    for (const [, r] of await this.ctx.storage.list<{ players: number; updated: number }>({ prefix: "priv:" })) {
      if (now - r.updated <= LISTING_TTL_MS && r.players > 0) { online += r.players; rooms++; }
    }
    // limpeza: marcas de ordem e contagens de salas privadas que ninguém atualiza há tempo
    const old: string[] = [];
    for (const [key, r] of await this.ctx.storage.list<{ at: number }>({ prefix: "seq:" })) if (now - r.at > 600_000) old.push(key);
    for (const [key, r] of await this.ctx.storage.list<{ updated: number }>({ prefix: "priv:" })) if (now - r.updated > 600_000) old.push(key);
    for (let i = 0; i < old.length; i += 120) await this.ctx.storage.delete(old.slice(i, i + 120));
    const day = new Date().toISOString().slice(0, 10);
    const sameDay = (await this.ctx.storage.get<string>("statsDay")) === day;
    const today = sameDay ? (await this.ctx.storage.get<number>("todayCount")) ?? 0 : 0;
    const yesterday = (await this.ctx.storage.get<number>("yesterday")) ?? 0;
    return { online, rooms, today, yesterday };
  }
}

// ------------------------------------------------------------------------------------------------ Room

interface PlayerInfo {
  id: number;
  name: string;
  ready: boolean;
  joined: number;
  spectator?: boolean;
}

interface RoomState {
  code: string;
  name: string;
  isPublic: boolean;
  colo: string;
  region: string;
  nextId: number;
  hostId: number;
  mods?: Record<string, unknown>; // modificadores da partida (imortal, velocidade), definidos pelo anfitrião
  queue?: unknown[]; // fila de fases (o anfitrião avança quando a partida acaba)
  leaderId?: number; // líder das fases: escolhe e recusa fases (o anfitrião passa o papel)
  suggestions?: { id: number; by: number; byName: string; level: Record<string, string> }[]; // fases sugeridas pelos jogadores
  nextSuggestion?: number;
  level: unknown; // definido pelo anfitrião (fase oficial ou da comunidade); o servidor não interpreta
  inGame: boolean;
  order?: number[]; // jogadores da partida em andamento (para quem entra assistindo)
  pwh?: string; // hash da senha da sala (opcional)
  banned?: string[]; // hashes (curtos) dos IPs banidos pelo anfitrião
  ipById?: Record<string, string>; // hash do IP de cada jogador na sala, para banir
}

export class Room extends DurableObject<Env> {
  private state: RoomState | null = null;
  // só em memória (somem se a sala hibernar): anti-spam do chat, denúncias e últimas mensagens para anexar a elas
  private chatState = new Map<WebSocket, { stamps: number[]; last: string; lastAt: number }>();
  private reportAt = new Map<WebSocket, number>();
  private recentChat: { id: number; name: string; text: string }[] = [];

  private async load(): Promise<RoomState | null> {
    if (!this.state) {
      this.state = (await this.ctx.storage.get<RoomState>("state")) ?? null;
    }
    return this.state;
  }

  private async save(): Promise<void> {
    if (this.state) await this.ctx.storage.put("state", this.state);
  }

  async init(code: string, name: string, isPublic: boolean, region: string, password = ""): Promise<{ code: string; colo: string; region: string }> {
    const colo = await this.detectColo();
    // senha opcional: guarda só o hash; o cliente manda sha256(CODIGO:senha) ao entrar
    const pwh = password ? await sha256hex(code + ":" + password) : undefined;
    this.state = { code, name, isPublic, colo, region, nextId: 1, hostId: 0, level: null, inGame: false, pwh };
    await this.save();
    await this.publish();
    // sala criada e abandonada sem ninguém entrar some sozinha
    await this.ctx.storage.setAlarm(Date.now() + 60_000);
    return { code, colo, region };
  }

  private async detectColo(): Promise<string> {
    try {
      const r = await fetch("https://www.cloudflare.com/cdn-cgi/trace");
      const m = /colo=(\w+)/.exec(await r.text());
      return m ? m[1] : "";
    } catch {
      return "";
    }
  }

  private players(): Array<{ ws: WebSocket; info: PlayerInfo }> {
    return this.ctx.getWebSockets().flatMap((ws) => {
      const info = ws.deserializeAttachment() as PlayerInfo | null;
      return info ? [{ ws, info }] : [];
    });
  }

  private send(ws: WebSocket, msg: unknown): void {
    try {
      ws.send(JSON.stringify(msg));
    } catch {
      // socket fechando
    }
  }

  private broadcast(msg: unknown, exceptId = -1): void {
    const text = JSON.stringify(msg);
    for (const p of this.players()) {
      if (p.info.id === exceptId) continue;
      try {
        p.ws.send(text);
      } catch {
        // ignora
      }
    }
  }

  private snapshot() {
    const s = this.state!;
    return {
      code: s.code,
      name: s.name,
      colo: s.colo,
      region: s.region,
      host: s.hostId,
      level: s.level,
      mods: s.mods ?? {},
      queue: s.queue ?? [],
      leader: s.leaderId ?? 0,
      suggestions: s.suggestions ?? [],
      inGame: s.inGame,
      order: s.inGame ? s.order ?? [] : [],
      players: this.players()
        .map((p) => p.info)
        .sort((a, b) => a.joined - b.joined),
    };
  }

  private lastPublish = 0;
  private publishEpoch = Date.now();
  private publishSeq = 0;

  private async publish(): Promise<void> {
    const s = this.state;
    if (!s) return;
    this.lastPublish = Date.now();
    const directory = this.env.DIRECTORY.get(this.env.DIRECTORY.idFromName("global"));
    const count = this.players().length;
    // nextId > 1: alguém já entrou; sala que esvaziou sai da lista (a recém-criada aparece até o criador entrar)
    const epoch = this.publishEpoch;
    const seq = ++this.publishSeq;
    if (!s.isPublic) {
      await directory.setPrivate(s.code, count, epoch, seq);
      return;
    }
    if (count === 0 && s.nextId > 1) {
      await directory.remove(s.code, epoch, seq);
      return;
    }
    const level = s.level as { title?: string } | null;
    await directory.upsert({
      code: s.code,
      name: s.name,
      colo: s.colo,
      region: s.region,
      players: count,
      maxPlayers: MAX_PLAYERS,
      locked: !!s.pwh,
      inGame: s.inGame,
      level: level?.title ? String(level.title).slice(0, 64) : "",
      updated: Date.now(),
    }, epoch, seq);
  }

  async fetch(request: Request): Promise<Response> {
    const s = await this.load();
    if (!s) {
      return json({ error: "room not found" }, 404);
    }
    const url = new URL(request.url);
    const name = cleanText(url.searchParams.get("name"), 24) || "Jogador";
    const pair = new WebSocketPair();
    const [client, server] = [pair[0], pair[1]];
    this.ctx.acceptWebSocket(server);

    const clientProtocol = Number(url.searchParams.get("v") ?? 0);
    if (clientProtocol !== PROTOCOL_VERSION) {
      this.send(server, { t: "error", code: "version", message: "Versão do jogo diferente da sala." });
      server.close(4001, "version");
      return new Response(null, { status: 101, webSocket: client });
    }
    // banido pelo anfitrião (hash do IP) ou senha da sala errada
    const ipHash = request.headers.get("CF-Connecting-IP") ? (await sha256hex(request.headers.get("CF-Connecting-IP") + ":" + s.code)).slice(0, 16) : "";
    if (ipHash && (s.banned ?? []).includes(ipHash)) {
      this.send(server, { t: "error", code: "banned", message: "Você foi banido desta sala." });
      server.close(4006, "banned");
      return new Response(null, { status: 101, webSocket: client });
    }
    if (s.pwh && url.searchParams.get("pwh") !== s.pwh) {
      this.send(server, { t: "error", code: "password", message: "Senha da sala incorreta." });
      server.close(4005, "password");
      return new Response(null, { status: 101, webSocket: client });
    }
    const current = this.players();
    const racers = current.filter((p) => !p.info.spectator).length;
    const spectators = current.length - racers;
    // com a partida em andamento, quem entra fica assistindo até ela acabar
    const asSpectator = s.inGame;
    if ((!asSpectator && racers >= MAX_PLAYERS) || (asSpectator && spectators >= MAX_SPECTATORS)) {
      this.send(server, { t: "error", code: "full", message: "Sala cheia." });
      server.close(4002, "full");
      return new Response(null, { status: 101, webSocket: client });
    }

    const info: PlayerInfo = { id: s.nextId++, name, ready: false, joined: Date.now(), spectator: asSpectator };
    server.serializeAttachment(info);
    if (ipHash) s.ipById = { ...(s.ipById ?? {}), [String(info.id)]: ipHash };
    await this.env.DIRECTORY.get(this.env.DIRECTORY.idFromName("global")).seen(await statsHash(request.headers.get("CF-Connecting-IP")));
    if (s.hostId === 0 || !this.players().some((p) => p.info.id === s.hostId)) {
      s.hostId = info.id;
    }
    await this.save();
    await this.ctx.storage.deleteAlarm();

    this.send(server, { t: "welcome", you: info.id, room: this.snapshot(), serverTime: Date.now() });
    this.broadcast({ t: "join", player: info }, info.id);
    await this.publish();
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws: WebSocket, raw: string | ArrayBuffer): Promise<void> {
    const s = await this.load();
    const me = ws.deserializeAttachment() as PlayerInfo | null;
    if (!s || !me || typeof raw !== "string" || raw.length > 16_384) return;
    let msg: Record<string, unknown>;
    try {
      msg = JSON.parse(raw);
    } catch {
      return;
    }
    const isHost = me.id === s.hostId;
    const canPick = isHost || (!!s.leaderId && me.id === s.leaderId); // escolher e recusar fases

    switch (msg.t) {
      case "ping":
        // sincronização de relógio: o cliente estima o atraso e a diferença para serverTime
        this.send(ws, { t: "pong", c: msg.c, s: Date.now() });
        // mantém a sala na lista pública (renova no máximo 1x por minuto)
        if (Date.now() - this.lastPublish > 60_000) await this.publish();
        break;

      case "ready":
        me.ready = !!msg.ready;
        ws.serializeAttachment(me);
        this.broadcast({ t: "ready", id: me.id, ready: me.ready });
        break;

      case "mods": {
        if (!isHost || s.inGame) return;
        const m = (msg.mods ?? {}) as { immortal?: unknown; speed?: unknown };
        const out: Record<string, unknown> = {};
        if (m.immortal === true) out.immortal = true;
        const sp = Number(m.speed);
        if (Number.isFinite(sp) && sp >= 0.5 && sp <= 2 && Math.abs(sp - 1) > 0.001) out.speed = Math.round(sp * 100) / 100;
        s.mods = out;
        await this.save();
        this.broadcast({ t: "mods", mods: out });
        break;
      }

      case "queue": {
        if (!canPick) return;
        const out: Record<string, string>[] = [];
        const fields = ["source", "hash", "title", "artist", "author", "url", "key"];
        const q = Array.isArray(msg.queue) ? (msg.queue as Record<string, unknown>[]).slice(0, 8) : [];
        for (const item of q) {
          if (!item || typeof item !== "object") continue;
          const lv: Record<string, string> = {};
          for (const f of fields) {
            if (item[f] != null) lv[f] = String(item[f]).replace(/[ -]/g, "").trim().slice(0, f === "url" ? 400 : 120);
          }
          if (lv.title) out.push(lv);
        }
        s.queue = out;
        await this.save();
        this.broadcast({ t: "queue", queue: out });
        break;
      }

      case "level":
        if (!canPick || s.inGame) return;
        s.level = msg.level ?? null;
        for (const p of this.players()) {
          // trocar de fase desmarca o "pronto" de todos (precisam baixar/confirmar de novo)
          p.info.ready = false;
          p.ws.serializeAttachment(p.info);
        }
        await this.save();
        this.broadcast({ t: "level", level: s.level });
        await this.publish();
        break;

      case "start": {
        if (!isHost || s.inGame) return;
        const all = this.players();
        if (!s.level || all.some((p) => !p.info.ready)) {
          this.send(ws, { t: "error", code: "notready", message: "Nem todos estão prontos." });
          return;
        }
        s.inGame = true;
        s.order = all.sort((a, b) => a.info.joined - b.info.joined).map((p) => p.info.id);
        await this.save();
        // começa em 3 s no relógio do servidor; cada cliente converte para o próprio relógio
        const at = Date.now() + 3000;
        this.broadcast({ t: "start", at, order: s.order });
        await this.publish();
        break;
      }

      case "end":
        if (!isHost) return;
        s.inGame = false;
        s.order = [];
        for (const p of this.players()) {
          // espectadores viram jogadores para a próxima partida
          p.info.ready = false;
          p.info.spectator = false;
          p.ws.serializeAttachment(p.info);
        }
        await this.save();
        this.broadcast({ t: "end", results: msg.results ?? null, players: this.players().map((p) => p.info) });
        await this.publish();
        break;

      case "ev":
        // evento de jogo (acerto, erro, morte, revive...): só repassa para os outros
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
        // denúncia de um jogador: vai para o registro do Worker (nome e últimas mensagens dele) e avisa o anfitrião
        const target = this.players().find((p) => p.info.id === msg.id);
        if (!target || target.info.id === me.id) break;
        const now = Date.now();
        if (now - (this.reportAt.get(ws) ?? 0) < 30_000) {
          this.send(ws, { t: "error", code: "slow", message: "Calma! Espere um pouco para denunciar de novo." });
          break;
        }
        this.reportAt.set(ws, now);
        const lines = this.recentChat.filter((c) => c.id === target.info.id).slice(-8).map((c) => c.text);
        console.log(`DENUNCIA sala ${s.code}: "${me.name}" denunciou "${target.info.name}" (id ${target.info.id}); ultimas mensagens dele: ${JSON.stringify(lines)}`);
        this.send(ws, { t: "notice", code: "report_sent" });
        if (s.hostId !== me.id) {
          const host = this.players().find((p) => p.info.id === s.hostId);
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

      case "leader": {
        // só o anfitrião (criador) escolhe quem é o líder das fases; id 0 (ou o próprio anfitrião) tira o líder
        if (!isHost) return;
        const wanted = Number(msg.id);
        if (!wanted || wanted === s.hostId) {
          s.leaderId = 0;
        } else {
          const target = this.players().find((p) => p.info.id === wanted);
          if (!target) break;
          s.leaderId = wanted;
        }
        await this.save();
        this.broadcast({ t: "leader", id: s.leaderId });
        break;
      }

      case "suggest": {
        // qualquer jogador pode sugerir uma fase; o anfitrião ou o líder toca, enfileira ou recusa
        if (me.spectator) return;
        const level = cleanLevel(msg.level);
        if (!level) break;
        const list = s.suggestions ?? [];
        const same = (a: Record<string, string>, b: Record<string, string>) =>
          (!!a.key && a.key === b.key) || (!!a.hash && a.hash === b.hash) || (a.title === b.title && a.artist === b.artist && a.author === b.author);
        if (list.some((x) => same(x.level, level))) break;
        if (list.length >= SUGGESTIONS_MAX) {
          this.send(ws, { t: "error", code: "slow", message: "A lista de sugestões está cheia." });
          break;
        }
        if (list.filter((x) => x.by === me.id).length >= SUGGESTIONS_PER_PLAYER) {
          this.send(ws, { t: "error", code: "slow", message: "Você já tem 3 sugestões na lista: espere uma ser aceita ou retire uma." });
          break;
        }
        const id = s.nextSuggestion ?? 1;
        s.nextSuggestion = id + 1;
        s.suggestions = [...list, { id, by: me.id, byName: me.name, level }];
        await this.save();
        this.broadcast({ t: "suggestions", suggestions: s.suggestions });
        break;
      }

      case "suggestion_remove": {
        // quem sugeriu retira a própria; o anfitrião ou o líder recusa qualquer uma
        const list = s.suggestions ?? [];
        const i = list.findIndex((x) => x.id === msg.id);
        if (i < 0 || (list[i].by !== me.id && !canPick)) break;
        s.suggestions = list.filter((_, k) => k !== i);
        await this.save();
        this.broadcast({ t: "suggestions", suggestions: s.suggestions });
        break;
      }

      case "suggestion_accept": {
        // anfitrião ou líder: "now" vira a fase da sala (fora de partida); "queue" vai para o fim da fila
        if (!canPick) return;
        const list = s.suggestions ?? [];
        const i = list.findIndex((x) => x.id === msg.id);
        if (i < 0) break;
        const picked = list[i];
        if (msg.mode === "queue") {
          if ((s.queue ?? []).length >= QUEUE_MAX) {
            this.send(ws, { t: "error", code: "slow", message: "A fila está cheia." });
            break;
          }
          s.queue = [...(s.queue ?? []), picked.level];
          this.broadcast({ t: "queue", queue: s.queue });
        } else {
          if (s.inGame) break;
          s.level = picked.level;
          for (const p of this.players()) {
            p.info.ready = false;
            p.ws.serializeAttachment(p.info);
          }
          this.broadcast({ t: "level", level: s.level });
        }
        s.suggestions = list.filter((_, k) => k !== i);
        await this.save();
        this.broadcast({ t: "suggestions", suggestions: s.suggestions });
        if (msg.mode !== "queue") await this.publish();
        break;
      }

      case "ban": {
        // remove e impede de voltar (pelo IP) enquanto a sala existir; o anfitrião não bane a si mesmo
        if (!isHost) return;
        const target = this.players().find((p) => p.info.id === msg.id);
        if (!target || target.info.id === me.id) break;
        const hash = s.ipById?.[String(target.info.id)];
        if (hash && (s.banned ?? []).length < 50) {
          s.banned = [...(s.banned ?? []), hash];
          await this.save();
        }
        this.send(target.ws, { t: "error", code: "banned", message: "Você foi banido desta sala." });
        target.ws.close(4006, "banned");
        break;
      }
    }
  }

  async webSocketClose(ws: WebSocket): Promise<void> {
    await this.leave(ws);
  }

  async webSocketError(ws: WebSocket): Promise<void> {
    await this.leave(ws);
  }

  private async leave(ws: WebSocket): Promise<void> {
    const s = await this.load();
    const me = ws.deserializeAttachment() as PlayerInfo | null;
    ws.serializeAttachment(null);
    if (!s || !me) return;
    const rest = this.players()
      .filter((p) => p.ws !== ws)
      .sort((a, b) => a.info.joined - b.info.joined);
    this.broadcast({ t: "leave", id: me.id });
    if (s.ipById) delete s.ipById[String(me.id)];
    if (s.leaderId === me.id) {
      s.leaderId = 0;
      this.broadcast({ t: "leader", id: 0 });
    }
    if (me.id === s.hostId) {
      s.hostId = rest.length > 0 ? rest[0].info.id : 0;
      if (rest.length > 0) this.broadcast({ t: "host", id: s.hostId });
    }
    if (rest.length === 0) {
      s.inGame = false;
      // sala vazia: some depois de 1 min se ninguém voltar
      await this.ctx.storage.setAlarm(Date.now() + 60_000);
    }
    await this.save();
    await this.publish();
  }

  async alarm(): Promise<void> {
    const s = await this.load();
    if (s && this.players().length === 0) {
      const directory = this.env.DIRECTORY.get(this.env.DIRECTORY.idFromName("global"));
      await directory.remove(s.code);
      await this.ctx.storage.deleteAll();
      this.state = null;
    }
  }
}
