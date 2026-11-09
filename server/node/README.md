# Servidor online na Oracle Cloud (São Paulo)

Versão Node.js do servidor de salas, para rodar numa máquina grátis da Oracle Cloud em São Paulo.

- Ping no Brasil esperado: **~20–40 ms**, contra ~130–200 ms do servidor da Cloudflare, cujas salas ficam nos EUA.
- Fala **exatamente o mesmo protocolo** do servidor da Cloudflare.
- O jogo tenta este servidor primeiro e usa a Cloudflare de reserva se ele não responder.

## 1. Conta na Oracle Cloud

1. Crie uma conta em <https://signup.cloud.oracle.com>.
   - A Oracle pede um cartão só para verificar a identidade.
   - O plano **Always Free** não cobra nada.
   - Na hora de converter para "Pay As You Go", não aceite.
2. **Na criação da conta, escolha a "Home Region" `Brazil East (São Paulo)`.**
   - Os recursos grátis só existem na região escolhida aqui.
   - Não dá para trocar a região depois.

## 2. Criar a máquina

1. No painel, abra **Compute → Instances → Create instance**.
2. **Image:** `Canonical Ubuntu 24.04` (ou 22.04).
3. **Shape:** clique em *Change shape*.
   - Escolha **Ampere → VM.Standard.A1.Flex** com 1 OCPU e 6 GB.
   - Se não houver capacidade, use **VM.Standard.E2.1.Micro** (AMD). As duas são "Always Free eligible".
4. **Networking:** deixe criar uma VCN nova, com **"Assign a public IPv4 address"** marcado.
5. **SSH keys:** baixe a chave privada (*Save private key*). Você vai usá-la para entrar na máquina.
6. Clique em **Create** e anote o **Public IP address**.

## 3. Abrir as portas 80 e 443 no painel

1. Na página da instância, clique na **Subnet**, depois na **Security List** padrão.
2. Clique em **Add Ingress Rules** e crie duas regras:
   - Source CIDR `0.0.0.0/0`, IP Protocol `TCP`, Destination Port `80`
   - Source CIDR `0.0.0.0/0`, IP Protocol `TCP`, Destination Port `443`

## 4. Instalar o servidor

1. No PC, abra o PowerShell na pasta onde salvou a chave e copie os arquivos para a máquina.
   - Troque `IP` pelo IP público da máquina.
   - Troque `chave.key` pelo nome do arquivo da chave.

   ```bash
   scp -i chave.key "./server.mjs" "./package.json" "./setup.sh" ubuntu@IP:~
   ```

   Se o Windows reclamar das permissões da chave, rode:

   ```bash
   icacls chave.key /inheritance:r /grant:r "%USERNAME%:R"
   ```

2. Entre na máquina:

   ```bash
   ssh -i chave.key ubuntu@IP
   ```

3. Lá dentro, rode o instalador:

   ```bash
   sudo bash setup.sh
   ```

   O script faz o seguinte:
   - instala o Node.js e o servidor;
   - cria um serviço que sobe sozinho com a máquina e reinicia se cair;
   - instala o **Caddy**, que tira um certificado HTTPS grátis para `IP-COM-TRACOS.sslip.io`, sem precisar comprar domínio;
   - libera as portas no firewall da própria máquina.

   No fim ele mostra o endereço do servidor, algo como `https://129-151-10-20.sslip.io`.
4. Abra esse endereço no navegador. Deve aparecer `{"ok":true,"protocol":1,...}`.
   - Na primeira vez, o certificado pode levar 1–2 minutos.

Me passe esse endereço e eu coloco no jogo (`PrimaryServerUrl` em `OnlineClient.cs`).

## Manutenção

| Para | Comando (dentro da máquina) |
|---|---|
| Ver se está rodando | `systemctl status adofai-online` |
| Ver o log | `journalctl -u adofai-online -f` |
| Reiniciar | `sudo systemctl restart adofai-online` |
| Atualizar o servidor | copie o `server.mjs` novo (passo 4) e rode `sudo bash setup.sh` de novo |

## Testar no PC (sem a Oracle)

```bash
npm install
npm start
```

Em outro terminal, rode `npm test`. É o mesmo teste do servidor da Cloudflare.

## Observações

- A Oracle pode **recuperar máquinas grátis paradas**, as que ficam com uso muito baixo por 7 dias. Com o jogo usando, isso não acontece. Se acontecer, basta recriar a máquina e rodar o `setup.sh` de novo.
- As salas ficam na memória do servidor: se ele reiniciar, as salas abertas somem e os jogadores criam outra.
- Todos os jogadores precisam estar no mesmo servidor para se ver. Com a mesma versão do jogo isso acontece sozinho: todos tentam a Oracle primeiro.
