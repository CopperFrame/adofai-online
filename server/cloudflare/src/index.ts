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
const MAX_PLAYERS = 4;
const MAX_SPECTATORS = 4; // quem entra com a partida em andamento assiste até ela acabar
const CODE_ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // sem 0/O e 1/I
// região da sala = continente (código do Cloudflare); cada um aponta para um local de Durable Objects
const REGION_HINTS: Record<string, DurableObjectLocationHint> = {
  SA: "sam", NA: "enam", EU: "weur", AS: "apac", OC: "oc", AF: "afr",
};

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

    if (parts[0] === "rooms" && parts.length === 1) {
      const directory = env.DIRECTORY.get(env.DIRECTORY.idFromName("global"));
      if (request.method === "GET") {
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
        const info = await room.init(code, cleanText(body.name, 32) || "Sala", body.public !== false, region);
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

  async upsert(listing: RoomListing): Promise<void> {
    await this.ctx.storage.put("room:" + listing.code, { ...listing, updated: Date.now() });
  }

  async remove(code: string): Promise<void> {
    await this.ctx.storage.delete("room:" + code);
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
  level: unknown; // definido pelo anfitrião (fase oficial ou da comunidade); o servidor não interpreta
  inGame: boolean;
  order?: number[]; // jogadores da partida em andamento (para quem entra assistindo)
}

export class Room extends DurableObject<Env> {
  private state: RoomState | null = null;

  private async load(): Promise<RoomState | null> {
    if (!this.state) {
      this.state = (await this.ctx.storage.get<RoomState>("state")) ?? null;
    }
    return this.state;
  }

  private async save(): Promise<void> {
    if (this.state) await this.ctx.storage.put("state", this.state);
  }

  async init(code: string, name: string, isPublic: boolean, region: string): Promise<{ code: string; colo: string; region: string }> {
    const colo = await this.detectColo();
    this.state = { code, name, isPublic, colo, region, nextId: 1, hostId: 0, level: null, inGame: false };
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
      inGame: s.inGame,
      order: s.inGame ? s.order ?? [] : [],
      players: this.players()
        .map((p) => p.info)
        .sort((a, b) => a.joined - b.joined),
    };
  }

  private lastPublish = 0;

  private async publish(): Promise<void> {
    const s = this.state;
    if (!s) return;
    this.lastPublish = Date.now();
    const directory = this.env.DIRECTORY.get(this.env.DIRECTORY.idFromName("global"));
    const count = this.players().length;
    // nextId > 1: alguém já entrou; sala que esvaziou sai da lista (a recém-criada aparece até o criador entrar)
    if (!s.isPublic || (count === 0 && s.nextId > 1)) {
      await directory.remove(s.code);
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
      inGame: s.inGame,
      level: level?.title ? String(level.title).slice(0, 64) : "",
      updated: Date.now(),
    });
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
        if (!isHost) return;
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
        if (!isHost || s.inGame) return;
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

      case "chat":
        this.broadcast({ t: "chat", from: me.id, text: cleanText(msg.text, 140) });
        break;

      case "kick": {
        if (!isHost) return;
        const target = this.players().find((p) => p.info.id === msg.id);
        if (target) {
          this.send(target.ws, { t: "error", code: "kicked", message: "Você foi removido da sala." });
          target.ws.close(4004, "kicked");
        }
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
