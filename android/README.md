# Android port

Port of **A Dance of Fire and Ice** to Android, with online rooms. The port files are being added to this folder.

> This repository never contains game code, assets, APKs or OBBs — you need your own copy of the game.
> **Português:** [veja abaixo](#português).

## What the port adds

- Touch controls: tap to play, drag to scroll the custom level list, short tap to enter a level
- On-screen pause button
- Custom level import and ADOFAI.gg / TUF level browser (download and install on the device)
- Larger, touch-friendly level selector and menus; no Unity splash screen
- Online rooms (race ghosts, spectator, chat, modifiers) — the same code as the PC mod, in [`../shared`](../shared)
- Online portal in the main menu

## Online

The online code lives in [`../shared`](../shared) and talks to the room servers in [`../server`](../server). A phone
and a PC (with the [mod](../mod)) can be in the same room.

## Status

The Android project files will be added here. Until then, `shared/` and `server/` are the reusable parts.

---

## Português

Port de **A Dance of Fire and Ice** para Android, com salas online. Os arquivos do port estão sendo adicionados a
esta pasta.

> Este repositório nunca contém código, arquivos, APKs ou OBBs do jogo: é preciso ter a sua própria cópia.

### O que o port acrescenta

- Controles por toque: tocar para jogar, arrastar para rolar a lista de fases personalizadas, toque curto para entrar
- Botão de pausa na tela
- Importar fases e navegador do ADOFAI.gg / TUF (baixa e instala no aparelho)
- Seletor de fases e menus maiores e próprios para toque; sem a tela de abertura do Unity
- Salas online (fantasmas, espectador, chat, modificadores): o mesmo código do mod de PC, em [`../shared`](../shared)
- Portal Online no menu principal

### Online

O código online fica em [`../shared`](../shared) e fala com os servidores de [`../server`](../server). Um celular e um
PC (com o [mod](../mod)) podem estar na mesma sala.

### Situação

Os arquivos do projeto Android serão adicionados aqui. Até lá, `shared/` e `server/` são as partes reutilizáveis.
