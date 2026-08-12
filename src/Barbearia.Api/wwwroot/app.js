// Sem escolha salva, segue o tema do sistema.
const Tema = {
  get atual() {
    return localStorage.getItem('tema')
      || (matchMedia('(prefers-color-scheme: dark)').matches ? 'escuro' : 'claro');
  },

  aplicar(tema) {
    document.documentElement.dataset.tema = tema;
    localStorage.setItem('tema', tema);
    // Barra do navegador no celular acompanha o tema; sem isto ela fica branca
    // por cima de uma pagina escura.
    const meta = document.getElementById('metaTema');
    if (meta) meta.content = tema === 'escuro' ? '#0f1116' : '#f6f6f8';
    document.querySelectorAll('[data-icone-tema]').forEach(el => {
      el.innerHTML = tema === 'escuro' ? Tema.iconeSol : Tema.iconeLua;
    });
  },

  alternar() { this.aplicar(this.atual === 'escuro' ? 'claro' : 'escuro'); },

  iniciar() {
    document.documentElement.dataset.tema = this.atual;
    const meta = document.getElementById('metaTema');
    if (meta) meta.content = this.atual === 'escuro' ? '#0f1116' : '#f6f6f8';
  },

  iconeLua: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.8A9 9 0 1111.2 3a7 7 0 009.8 9.8z"/></svg>',
  iconeSol: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>'
};

Tema.iniciar();

const Auth = {
  get token() { return localStorage.getItem('token'); },
  get perfil() { return localStorage.getItem('perfil'); },
  get nome() { return localStorage.getItem('nome'); },
  get id() { return localStorage.getItem('id'); },

  get permissoes() {
    try { return JSON.parse(localStorage.getItem('permissoes')) || {}; }
    catch { return {}; }
  },

  get precisaTrocarSenha() { return localStorage.getItem('trocarSenha') === '1'; },

  entrar(dados) {
    localStorage.setItem('token', dados.token);
    localStorage.setItem('id', dados.id || '');
    localStorage.setItem('perfil', dados.perfil);
    localStorage.setItem('nome', dados.nome);
    localStorage.setItem('permissoes', JSON.stringify(dados.permissoes || {}));
    localStorage.setItem('trocarSenha', dados.precisaTrocarSenha ? '1' : '0');
  },

  sair() {
    localStorage.clear();
    location.href = 'index.html';
  },

  get ehAdmin() { return this.perfil === 'Admin'; },

  // Admin tem acesso total.
  pode(perfisPermitidos) {
    return this.ehAdmin || perfisPermitidos.includes(this.perfil);
  },
  // Admin e Gestor acessam tudo; as permissoes granulares valem para Barbeiro.
  permite(chave) {
    return this.ehAdmin || this.perfil === 'Gestor' || this.permissoes[chave] === true;
  },

  exigirLogin() {
    if (!this.token) { location.href = 'index.html'; return false; }
    // O backend bloqueia tudo nesse estado, entao nem carrega a tela.
    if (this.precisaTrocarSenha && !location.pathname.endsWith('senha.html')) {
      location.href = 'senha.html';
      return false;
    }
    return true;
  }
};

async function api(caminho, opcoes = {}) {
  const cabecalhos = { 'Content-Type': 'application/json', ...(opcoes.headers || {}) };
  if (Auth.token) cabecalhos['Authorization'] = 'Bearer ' + Auth.token;

  const resposta = await fetch(caminho, { ...opcoes, headers: cabecalhos });

  if (resposta.status === 401 && Auth.token) { Auth.sair(); throw new Error('Sessao expirada'); }

  const texto = await resposta.text();
  const corpo = texto ? JSON.parse(texto) : null;

  if (!resposta.ok) throw Object.assign(new Error(corpo?.erro || 'Erro inesperado'), { corpo, status: resposta.status });

  return corpo;
}

function aviso(elemento, mensagem, tipo = 'erro') {
  if (!mensagem) { elemento.className = 'oculto'; return; }
  elemento.className = 'aviso ' + tipo;
  elemento.textContent = mensagem;
}

function moeda(centavos) {
  return (centavos / 100).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

function dataHoje() {
  const agora = new Date();
  const fuso = new Date(agora.getTime() - agora.getTimezoneOffset() * 60000);
  return fuso.toISOString().slice(0, 10);
}

function dataLegivel(iso) {
  return new Date(iso).toLocaleString('pt-BR', {
    weekday: 'short', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
  });
}

const DIAS = ['Domingo', 'Segunda', 'Terca', 'Quarta', 'Quinta', 'Sexta', 'Sabado'];

const MARCA_SVG = `<svg viewBox="0 0 24 24" fill="none" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">
  <circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/>
  <path d="M20 4L8.12 15.88M14.47 14.48L20 20M8.12 8.12L12 12"/></svg>`;

function botaoTema() {
  return `<button class="tema" onclick="Tema.alternar()" aria-label="Alternar tema"
    data-icone-tema>${Tema.atual === 'escuro' ? Tema.iconeSol : Tema.iconeLua}</button>`;
}

function montarTopo(titulo, comMenu = false) {
  const nome = Auth.nome || '';
  const perfil = Auth.perfil || '';
  return `${comMenu ? botaoMenu() : ''}
    <div class="marca-topo"><span class="marca-mark">${MARCA_SVG}</span><h1>${titulo}</h1></div>
    <span class="espaco"></span>
    <span class="quem">${nome} &middot; ${perfil}</span>
    ${botaoTema()}
    <button class="btn pequeno neutro" onclick="Auth.sair()">Sair</button>`;
}

const ICONES = {
  agenda: '<path d="M8 2v4M16 2v4M3 10h18"/><rect x="3" y="4" width="18" height="18" rx="2"/>',
  novo: '<circle cx="12" cy="12" r="9"/><path d="M12 8v8M8 12h8"/>',
  servicos: '<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M20 4L8.12 15.88M14.47 14.48L20 20M8.12 8.12L12 12"/>',
  produtos: '<path d="M20 7l-8-4-8 4v10l8 4 8-4z"/><path d="M4 7l8 4 8-4M12 11v10"/>',
  horarios: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  bloqueios: '<circle cx="12" cy="12" r="9"/><path d="M5.6 5.6l12.8 12.8"/>',
  funcionarios: '<path d="M16 20v-2a4 4 0 00-4-4H6a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 20v-2a4 4 0 00-3-3.87"/>',
  clientes: '<path d="M20 20v-2a4 4 0 00-4-4H8a4 4 0 00-4 4v2"/><circle cx="12" cy="7" r="4"/>',
  pessoas: '<path d="M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 00-3-3.87M16 3.13a4 4 0 010 7.75"/>',
  config: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 00.3 1.9l.1.1a2 2 0 11-2.8 2.8l-.1-.1a1.7 1.7 0 00-2.9 1.2V21a2 2 0 11-4 0v-.1A1.7 1.7 0 007 19.4a1.7 1.7 0 00-1.9.3l-.1.1a2 2 0 11-2.8-2.8l.1-.1a1.7 1.7 0 00-1.2-2.9H1a2 2 0 110-4h.1A1.7 1.7 0 004.6 7a1.7 1.7 0 00-.3-1.9l-.1-.1a2 2 0 112.8-2.8l.1.1a1.7 1.7 0 001.9.3H9a1.7 1.7 0 001-1.5V1a2 2 0 114 0v.1a1.7 1.7 0 001 1.5 1.7 1.7 0 001.9-.3l.1-.1a2 2 0 112.8 2.8l-.1.1a1.7 1.7 0 00-.3 1.9V9a1.7 1.7 0 001.5 1H23a2 2 0 110 4h-.1a1.7 1.7 0 00-1.5 1z"/>',
  sistema: '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/>'
};

function icone(nome) {
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
    stroke-linecap="round" stroke-linejoin="round">${ICONES[nome] || ''}</svg>`;
}

/**
 * Monta o menu lateral. grupos: [{ titulo, itens: [{ chave, nome, perfis?, permissao? }] }].
 * Renderiza so o que o perfil e as permissoes do usuario liberam.
 */
function menuLateral(container, grupos, aoTrocar) {
  const podeVer = i =>
    (i.permissao ? Auth.permite(i.permissao) : true) &&
    (i.perfis ? Auth.pode(i.perfis) : true);

  const visiveis = grupos.flatMap(g => g.itens.filter(podeVer));
  if (visiveis.length === 0) return [];

  container.innerHTML = grupos.map(g => {
    const itens = g.itens.filter(podeVer);
    if (itens.length === 0) return '';

    return `<div class="grupo"><h3>${g.titulo}</h3>` + itens.map(i =>
      `<button data-chave="${i.chave}" class="${i.chave === visiveis[0].chave ? 'ativa' : ''}">
         ${icone(i.chave)}<span>${i.nome}</span></button>`).join('') + '</div>';
  }).join('');

  const mostrar = chave => {
    container.querySelectorAll('button').forEach(b =>
      b.classList.toggle('ativa', b.dataset.chave === chave));

    visiveis.forEach(i =>
      document.getElementById('secao-' + i.chave)?.classList.toggle('oculto', i.chave !== chave));

    fecharMenu();
    window.scrollTo(0, 0);
    aoTrocar?.(chave);
  };

  container.querySelectorAll('button').forEach(b => {
    b.onclick = () => mostrar(b.dataset.chave);
  });

  // Todas as secoes que o usuario nao pode ver ficam escondidas.
  grupos.flatMap(g => g.itens).forEach(i => {
    const secao = document.getElementById('secao-' + i.chave);
    if (secao) secao.classList.toggle('oculto', i.chave !== visiveis[0].chave);
  });

  return visiveis;
}

function alternarMenu() {
  const lateral = document.querySelector('.lateral');
  const aberto = lateral.classList.toggle('aberta');

  document.querySelector('.veu')?.remove();
  if (!aberto) return;

  const veu = document.createElement('div');
  veu.className = 'veu';
  veu.onclick = fecharMenu;
  document.body.appendChild(veu);
}

function fecharMenu() {
  document.querySelector('.lateral')?.classList.remove('aberta');
  document.querySelector('.veu')?.remove();
}

function botaoMenu() {
  return `<button class="abrir-menu" onclick="alternarMenu()" aria-label="Abrir menu">
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">
      <path d="M3 6h18M3 12h18M3 18h18"/></svg></button>`;
}
