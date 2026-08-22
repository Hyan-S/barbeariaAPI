const Tema = {
  get atual() {
    return localStorage.getItem('tema')
      || (matchMedia('(prefers-color-scheme: dark)').matches ? 'escuro' : 'claro');
  },

  aplicar(tema) {
    document.documentElement.dataset.tema = tema;
    localStorage.setItem('tema', tema);
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
    // Preserva a sessao do cliente: sair do painel nao pode deslogar o dono da
    // propria area de agendamento dele.
    const cliente = ['cliente.token', 'cliente.nome', 'cliente.telefone']
      .map(k => [k, localStorage.getItem(k)]);

    localStorage.clear();
    cliente.forEach(([k, v]) => { if (v !== null) localStorage.setItem(k, v); });

    location.href = 'index.html';
  },

  get ehAdmin() { return this.perfil === 'Admin'; },

  pode(perfisPermitidos) {
    return this.ehAdmin || perfisPermitidos.includes(this.perfil);
  },
  permite(chave) {
    return this.ehAdmin || this.perfil === 'Gestor' || this.permissoes[chave] === true;
  },

  exigirLogin() {
    if (!this.token) { location.href = 'index.html'; return false; }
    if (this.precisaTrocarSenha && !location.pathname.endsWith('senha.html')) {
      location.href = 'senha.html';
      return false;
    }
    return true;
  }
};

// Sessao do cliente, separada da do painel. Chaves com prefixo proprio porque as
// duas podem coexistir no mesmo navegador: o dono da barbearia tambem e cliente de
// si mesmo, e o Auth.sair() do painel faz localStorage.clear().
const ClienteAuth = {
  get token() { return localStorage.getItem('cliente.token'); },
  get nome() { return localStorage.getItem('cliente.nome'); },
  get telefone() { return localStorage.getItem('cliente.telefone'); },

  entrar(dados) {
    localStorage.setItem('cliente.token', dados.token);
    localStorage.setItem('cliente.nome', dados.nome || '');
    localStorage.setItem('cliente.telefone', dados.telefone || '');
  },

  limpar() {
    ['cliente.token', 'cliente.nome', 'cliente.telefone']
      .forEach(k => localStorage.removeItem(k));
  },

  sair() {
    this.limpar();
    location.replace('agendar.html');
  }
};



const ROTULOS = [
  ['POST',   /^\/api\/auth\/login$/,                            'Entrando',                   'Tudo certo'],
  ['POST',   /^\/api\/auth\/trocar-senha$/,                     'Trocando a senha',           'Senha trocada'],
  ['GET',    /^\/api\/me$/,                                     'Verificando a sessao',       'Sessao ativa'],

  ['GET',    /^\/api\/config$/,                                 'Carregando a barbearia',     'Barbearia carregada'],
  ['GET',    /^\/api\/sessao$/,                                 'Abrindo o seu link',         'Link aberto'],
  ['GET',    /^\/api\/servicos\/[^/]+\/barbeiros$/,             'Vendo quem atende',          'Barbeiros carregados'],
  ['GET',    /^\/api\/servicos$/,                               'Carregando servicos',        'Servicos carregados'],
  ['GET',    /^\/api\/barbeiros$/,                              'Carregando barbeiros',       'Barbeiros carregados'],
  ['GET',    /^\/api\/disponibilidade$/,                        'Buscando horarios livres',   'Horarios carregados'],

  ['POST',   /^\/api\/agendamentos\/[^/]+\/cancelar$/,          'Cancelando o horario',       'Horario cancelado'],
  ['POST',   /^\/api\/gestor\/agendamentos\/[^/]+\/fechar$/,    'Encerrando o atendimento',   'Atendimento encerrado'],
  ['POST',   /^\/api\/gestor\/agendamentos\/[^/]+\/reabrir$/,   'Reabrindo o atendimento',    'Atendimento reaberto'],
  ['GET',    /^\/api\/dashboard\/caixa$/,                     'Somando o caixa',            'Caixa somado'],
  ['POST',   /^\/api\/agendamentos$/,                           'Confirmando o seu horario',  'Horario confirmado'],
  ['GET',    /^\/api\/agendamentos$/,                           'Carregando os seus horarios','Horarios carregados'],

  ['GET',    /^\/api\/gestor\/agenda$/,                         'Carregando a agenda',        'Agenda carregada'],
  ['GET',    /^\/api\/gestor\/agendamentos\/[^/]+\/grade$/,     'Buscando horarios livres',   'Horarios carregados'],
  ['POST',   /^\/api\/gestor\/agendamentos\/[^/]+\/cancelar$/,  'Cancelando o agendamento',   'Agendamento cancelado'],
  ['POST',   /^\/api\/gestor\/agendamentos\/[^/]+\/reagendar$/, 'Reagendando',                'Horario remarcado'],
  ['POST',   /^\/api\/gestor\/agendamentos$/,                   'Agendando',                  'Agendamento criado'],
  ['GET',    /^\/api\/gestor\/barbeiros$/,                      'Carregando barbeiros',       'Barbeiros carregados'],
  ['GET',    /^\/api\/gestor\/servicos$/,                       'Carregando servicos',        'Servicos carregados'],
  ['POST',   /^\/api\/gestor\/servicos$/,                       'Salvando o servico',         'Servico salvo'],
  ['PUT',    /^\/api\/gestor\/servicos\/[^/]+$/,                'Salvando o servico',         'Servico salvo'],
  ['DELETE', /^\/api\/gestor\/servicos\/[^/]+$/,                'Excluindo o servico',        'Servico excluido'],
  ['GET',    /^\/api\/gestor\/expedientes$/,                    'Carregando o funcionamento', 'Funcionamento carregado'],
  ['POST',   /^\/api\/gestor\/expedientes$/,                    'Salvando o horario',         'Horario salvo'],
  ['POST',   /^\/api\/gestor\/expedientes\/copiar$/,            'Copiando o funcionamento',   'Funcionamento copiado'],
  ['DELETE', /^\/api\/gestor\/expedientes\/[^/]+$/,             'Removendo o horario',        'Horario removido'],
  ['GET',    /^\/api\/gestor\/bloqueios$/,                      'Carregando bloqueios',       'Bloqueios carregados'],
  ['POST',   /^\/api\/gestor\/bloqueios$/,                      'Salvando o bloqueio',        'Bloqueio salvo'],
  ['DELETE', /^\/api\/gestor\/bloqueios\/[^/]+$/,               'Removendo o bloqueio',       'Bloqueio removido'],

  ['GET',    /^\/api\/produtos$/,                               'Carregando produtos',        'Produtos carregados'],
  ['POST',   /^\/api\/produtos$/,                               'Salvando o produto',         'Produto salvo'],
  ['PUT',    /^\/api\/produtos\/[^/]+$/,                        'Salvando o produto',         'Produto salvo'],
  ['DELETE', /^\/api\/produtos\/[^/]+$/,                        'Excluindo o produto',        'Produto excluido'],

  ['POST',   /^\/api\/cliente\/cadastro$/,                'Criando seu cadastro',       'Cadastro criado'],
  ['POST',   /^\/api\/cliente\/login$/,                   'Entrando',                   'Bem-vindo'],
  ['GET',    /^\/api\/cliente\/me$/,                      'Carregando sua conta',       'Conta carregada'],
  ['POST',   /^\/api\/cliente\/agendamentos$/,            'Confirmando o horario',      'Horario confirmado'],
  ['POST',   /^\/api\/cliente\/agendamentos\/[^/]+\/cancelar$/, 'Cancelando o horario',  'Horario cancelado'],
  ['POST',   /^\/api\/cliente\/senha$/,                    'Trocando a senha',           'Senha trocada'],
  ['POST',   /^\/api\/clientes\/[^/]+\/zerar-acesso$/,       'Zerando o acesso',           'Acesso zerado'],

  ['GET',    /^\/api\/vitrine\/produtos$/,                      'Carregando produtos',        'Produtos carregados'],
  ['POST',   /^\/api\/vitrine\/produtos\/[^/]+\/avaliacoes$/,   'Enviando a avaliacao',       'Avaliacao enviada'],
  ['GET',    /^\/api\/vitrine\/produtos\/[^/]+$/,               'Abrindo o produto',          'Produto aberto'],
  ['POST',   /^\/api\/vitrine\/pedidos$/,                       'Marcando o produto',         'Produto marcado'],
  ['DELETE', /^\/api\/vitrine\/pedidos$/,                       'Desmarcando o produto',      'Produto desmarcado'],

  ['GET',    /^\/api\/gestor\/avaliacoes$/,                     'Carregando avaliacoes',      'Avaliacoes carregadas'],
  ['PUT',    /^\/api\/gestor\/avaliacoes\/[^/]+$/,              'Mudando a situacao',         'Situacao alterada'],
  ['DELETE', /^\/api\/gestor\/avaliacoes\/[^/]+$/,              'Apagando a avaliacao',       'Avaliacao apagada'],

  ['GET',    /^\/api\/clientes$/,                               'Carregando clientes',        'Clientes carregados'],
  ['GET',    /^\/api\/clientes\/[^/]+$/,                        'Abrindo o cliente',          'Cliente aberto'],
  ['PUT',    /^\/api\/clientes\/[^/]+$/,                        'Salvando o cliente',         'Cliente salvo'],
  ['DELETE', /^\/api\/clientes\/[^/]+$/,                        'Excluindo o cliente',        'Cliente excluido'],

  ['GET',    /^\/api\/dashboard/,                               'Montando o dashboard',       'Dashboard pronto'],

  ['GET',    /^\/api\/admin\/config$/,                          'Carregando a configuracao',  'Configuracao carregada'],
  ['PUT',    /^\/api\/admin\/config$/,                          'Salvando a configuracao',    'Configuracao salva'],
  ['GET',    /^\/api\/admin\/sistema$/,                         'Consultando o sistema',      'Diagnostico pronto'],
  ['GET',    /^\/api\/admin\/usuarios$/,                        'Carregando funcionarios',    'Funcionarios carregados'],
  ['POST',   /^\/api\/admin\/usuarios$/,                        'Cadastrando o funcionario',  'Funcionario cadastrado'],
  ['PUT',    /^\/api\/admin\/usuarios\/[^/]+$/,                 'Salvando o funcionario',     'Funcionario salvo'],
  ['DELETE', /^\/api\/admin\/usuarios\/[^/]+$/,                 'Excluindo o funcionario',    'Funcionario excluido'],
  ['GET',    /^\/api\/admin\/pessoas$/,                         'Carregando pessoas',         'Pessoas carregadas'],
  ['GET',    /^\/api\/admin\/permissoes$/,                      'Carregando permissoes',      'Permissoes carregadas'],
  ['POST',   /^\/api\/admin\/permissoes$/,                      'Salvando a permissao',       'Permissao salva'],
  ['POST',   /^\/api\/admin\/whatsapp\/teste$/,                 'Enviando o teste',           'Mensagem enviada']
];

function rotuloDaRota(caminho, metodo) {
  const rota = caminho.split('?')[0];

  for (const [m, padrao, fazendo, feito] of ROTULOS)
    if (m === metodo && padrao.test(rota)) return { fazendo, feito };

  return { fazendo: 'Carregando', feito: 'Pronto' };
}

const GIRO = '<span class="rede-giro"></span>';
const CERTO = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6L9 17l-5-5"/></svg>';
const ERRADO = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M12 8v5M12 16v.01"/></svg>';

const Rede = {
  ativas: 0,
  fazendo: 'Carregando',
  timer: null,
  _caixa: null,

  get caixa() {
    if (!this._caixa) {
      this._caixa = document.createElement('div');
      this._caixa.className = 'rede';
      this._caixa.setAttribute('role', 'status');
      this._caixa.setAttribute('aria-live', 'polite');
      document.body.appendChild(this._caixa);
    }
    return this._caixa;
  },

  pintar(tipo, texto, icone) {
    clearTimeout(this.timer);
    this.caixa.className = 'rede visivel ' + tipo;
    this.caixa.innerHTML = icone + '<span>' + texto + '</span>';
  },

  carregando() {
    const extra = this.ativas > 1 ? ` (+${this.ativas - 1})` : '';
    this.pintar('carrega', this.fazendo + '...' + extra, GIRO);
  },

  comecou(fazendo) {
    this.ativas++;
    this.fazendo = fazendo;
    this.carregando();
  },

  terminou(tipo, texto) {
    this.ativas = Math.max(0, this.ativas - 1);

    if (tipo === 'ok' && this.ativas > 0) { this.carregando(); return; }

    this.pintar(tipo, texto, tipo === 'ok' ? CERTO : ERRADO);

    this.timer = setTimeout(() => {
      if (this.ativas > 0) this.carregando();
      else this.caixa.className = 'rede';
    }, 3000);
  }
};

async function api(caminho, opcoes = {}) {
  const metodo = (opcoes.method || 'GET').toUpperCase();
  const rotulo = rotuloDaRota(caminho, metodo);
  Rede.comecou(rotulo.fazendo);

  const cabecalhos = { 'Content-Type': 'application/json', ...(opcoes.headers || {}) };

  // Qual sessao mandar e decidido pela rota, nao por qual token existe. O dono da
  // barbearia pode estar logado nos dois papeis ao mesmo tempo; mandar o token do
  // painel para /api/cliente daria 403, e o token do cliente no painel daria 401.
  // A barra no fim importa: '/api/clientes', que e a gestao de clientes no painel,
  // tambem comeca com '/api/cliente'. Sem ela, abrir Painel > Clientes manda o token
  // do cliente (ou nenhum), toma 401 e derruba o dono para agendar.html.
  const paraCliente = caminho === '/api/cliente' || caminho.startsWith('/api/cliente/');
  const portador = paraCliente ? ClienteAuth.token : Auth.token;

  if (portador) cabecalhos['Authorization'] = 'Bearer ' + portador;

  let resposta;
  try {
    resposta = await fetch(caminho, { ...opcoes, headers: cabecalhos });
  } catch {
    Rede.terminou('erro', 'Sem conexao com o servidor');
    throw new Error('Sem conexao com o servidor');
  }

  // So derruba a sessao se havia uma para derrubar: o 401 de senha errada no
  // login chega sem token e nao pode virar "sessao expirada".
  if (resposta.status === 401 && portador) {
    Rede.terminou('erro', 'Sessao expirada');
    if (paraCliente) ClienteAuth.sair(); else Auth.sair();
    throw new Error('Sessao expirada');
  }

  const texto = await resposta.text();

  let corpo = null;
  try { corpo = texto ? JSON.parse(texto) : null; } catch { corpo = null; }

  if (!resposta.ok) {
    const mensagem = corpo?.erro
      || (resposta.status === 429 ? 'Muitas tentativas. Espere um minuto.'
      : resposta.status >= 502 ? 'Servidor iniciando. Tente de novo em alguns segundos.'
      : 'Erro inesperado');

    Rede.terminou('erro', mensagem);
    throw Object.assign(new Error(mensagem), { corpo, status: resposta.status });
  }

  Rede.terminou('ok', rotulo.feito);
  return corpo;
}

function aviso(elemento, mensagem, tipo = 'erro') {
  clearTimeout(elemento._timer);

  if (!mensagem) { elemento.className = 'oculto'; return; }

  elemento.className = 'aviso ' + tipo;
  elemento.textContent = mensagem;

  if (tipo !== 'erro')
    elemento._timer = setTimeout(() => { elemento.className = 'oculto'; }, 3000);
}

// Escapa texto antes de injetar via innerHTML. Todo dado vindo do servidor
// (nome de cliente, observacao, e-mail, etc.) passa por aqui: alguem pode se
// cadastrar sem login com um nome tipo "<img onerror=...>" e o painel do admin
// executaria o script. Numeros e nulos viram string segura.
function esc(valor) {
  if (valor === null || valor === undefined) return '';
  return String(valor)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function moeda(centavos) {
  return (centavos / 100).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
}

// ---------- Telefone ----------

// Mascara de digitacao: (11) 9 8765-4321.
// O formato sai do primeiro digito depois do DDD, e nao da quantidade digitada:
// no Brasil celular sempre comeca com 9 e fixo comeca com 2-5. Decidir assim
// evita o campo pular de "(11) 9876-5432" para "(11) 9 8765-4321" no meio da
// digitacao, que e o que acontece quando a mascara espera o numero terminar.
// Fixo fica (11) 3456-7890 — impor o formato de celular nele escreveria um
// numero que nao existe.
function mascaraTelefone(bruto) {
  let digitos = String(bruto ?? '').replace(/\D/g, '');

  // Numero colado da agenda do celular ou do WhatsApp vem com o codigo do pais:
  // 55 + DDD + 8 ou 9 digitos. So tira o 55 quando o que sobra ainda e um numero
  // inteiro, porque DDD 55 existe (Santa Maria, RS) — em um numero de 11 digitos
  // comecando com 55 o proprio 55 e o DDD.
  if (digitos.length > 11 && digitos.startsWith('55')) digitos = digitos.slice(2);

  digitos = digitos.slice(0, 11);

  if (!digitos) return '';
  if (digitos.length <= 2) return '(' + digitos;

  const ddd = '(' + digitos.slice(0, 2) + ') ';
  const numero = digitos.slice(2);

  if (numero[0] === '9') {
    if (numero.length === 1) return ddd + numero;
    if (numero.length <= 5) return ddd + numero[0] + ' ' + numero.slice(1);
    return ddd + numero[0] + ' ' + numero.slice(1, 5) + '-' + numero.slice(5);
  }

  if (numero.length <= 4) return ddd + numero;
  return ddd + numero.slice(0, 4) + '-' + numero.slice(4);
}

function apenasDigitos(valor) {
  return String(valor ?? '').replace(/\D/g, '');
}

// Liga a mascara em um campo. Aplica tambem no que ja estiver preenchido, entao
// serve para o telefone que vem da sessao ou de um cadastro carregado.
function ligarMascaraTelefone(input) {
  if (!input || input.dataset.mascara === 'telefone') return;
  input.dataset.mascara = 'telefone';

  input.setAttribute('inputmode', 'numeric');
  input.setAttribute('autocomplete', 'tel');
  input.maxLength = 16;

  const digitosAte = fim => (input.value.slice(0, fim).match(/\d/g) || []).length;

  // A mascara reescreve o campo inteiro a cada tecla, entao o cursor precisa ser
  // recolocado contando digitos, e nao caracteres — senao editar o meio do numero
  // joga o cursor para o fim.
  const redesenhar = (bruto, digitosAntes) => {
    input.value = mascaraTelefone(bruto);

    let pos = 0;
    let vistos = 0;
    while (pos < input.value.length && vistos < digitosAntes) {
      if (/\d/.test(input.value[pos])) vistos++;
      pos++;
    }

    if (document.activeElement === input) input.setSelectionRange(pos, pos);
  };

  input.addEventListener('input', () =>
    redesenhar(input.value, digitosAte(input.selectionStart ?? input.value.length)));

  // Backspace parado logo depois de um separador apagaria so o separador, que a
  // mascara redesenha em seguida — a tecla pareceria nao ter feito nada. Aqui ele
  // come o digito que esta atras do separador, que e o que a pessoa quis apagar.
  input.addEventListener('keydown', e => {
    const cursor = input.selectionStart;

    if (e.key !== 'Backspace' || cursor !== input.selectionEnd || cursor === 0) return;
    if (/\d/.test(input.value[cursor - 1])) return;

    e.preventDefault();

    const antes = digitosAte(cursor);
    const inicio = input.value.slice(0, cursor).replace(/\d(?=\D*$)/, '');
    redesenhar(inicio + input.value.slice(cursor), antes - 1);
  });

  redesenhar(input.value, Infinity);
}

// Atalho para ligar varios campos de uma vez, ignorando os que nao existem na
// pagina — cada tela tem um subconjunto dos ids.
function ligarMascarasTelefone(...ids) {
  ids.forEach(id => ligarMascaraTelefone(document.getElementById(id)));
}

// ---------- Altura do cabecalho ----------

// Tudo que fica grudado embaixo do cabecalho — o aviso, as abas, o menu lateral —
// se apoia em --altura-topo. Aqui ela e medida em vez de chutada: a altura do topo
// muda com a largura da tela, com a fonte do sistema e com a faixa do notch, e cada
// numero fixo que existia no CSS (59px, 67px, 55px, 62px) errava em algum aparelho,
// deixando conteudo aparecer por baixo da barra.
function medirTopo() {
  const topo = document.querySelector('header.topo');
  if (!topo) return;

  const altura = Math.round(topo.getBoundingClientRect().height);
  if (altura > 0) document.documentElement.style.setProperty('--altura-topo', altura + 'px');
}

function acompanharTopo() {
  medirTopo();

  const topo = document.querySelector('header.topo');

  // O cabecalho e preenchido depois, pelo montarCabecalho de cada tela, e ainda
  // muda de altura quando o conteudo quebra em duas linhas. O observador cobre os
  // dois casos sem precisar de gancho em cada pagina.
  if (topo && 'ResizeObserver' in window) new ResizeObserver(medirTopo).observe(topo);

  // Girar o aparelho troca as faixas do sistema de lugar; o iOS mede errado se a
  // conta acontecer no mesmo instante do evento.
  addEventListener('orientationchange', () => setTimeout(medirTopo, 150));
}

if (document.readyState === 'loading') addEventListener('DOMContentLoaded', acompanharTopo);
else acompanharTopo();

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

// Na agenda a hora e a informacao principal e a data fica em segundo plano,
// entao as duas partes vem separadas em vez da string unica de dataLegivel.
function soHora(iso) {
  return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
}

function soDia(iso) {
  return new Date(iso).toLocaleDateString('pt-BR', {
    weekday: 'short', day: '2-digit', month: '2-digit'
  });
}

const DIAS = ['Domingo', 'Segunda', 'Terca', 'Quarta', 'Quinta', 'Sexta', 'Sabado'];

const ESTRELA_SVG = 'M12 2.5l2.95 5.98 6.6.96-4.77 4.65 1.12 6.57L12 17.55l-5.9 3.11 1.13-6.57L2.46 9.44l6.6-.96z';

// Estrelas de leitura. Sem meia estrela de proposito: a nota vem arredondada e,
// em 14px, meia estrela nao se distingue de uma cheia — daria a impressao de
// numero exato onde nao ha. O texto ao lado mostra a nota real.
function estrelas(nota, total, classe = '') {
  const cheias = Math.round(nota || 0);

  const desenho = Array.from({ length: 5 }, (_, i) =>
    `<svg viewBox="0 0 24 24" class="${i < cheias ? 'cheia' : ''}" aria-hidden="true">
       <path d="${ESTRELA_SVG}"/></svg>`).join('');

  const semNota = !total;
  const conta = total === undefined ? ''
    : semNota ? '<span class="conta">sem avaliacoes</span>'
    : `<span class="conta">${String(nota.toFixed(1)).replace('.', ',')} (${total})</span>`;

  // Sem o total (uma avaliacao sozinha na lista) o rotulo descreve as estrelas
  // desenhadas; com total, descreve a media do produto.
  const rotulo = total === undefined ? `Nota ${cheias} de 5`
    : semNota ? 'Sem avaliacoes'
    : `Nota ${nota.toFixed(1)} de 5, ${total} avaliacoes`;

  return `<span class="estrelas ${classe}" role="img" aria-label="${rotulo}">${desenho}${conta}</span>`;
}

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
    <span class="quem">${esc(nome)} &middot; ${esc(perfil)}</span>
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
  sistema: '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/>',
  dashboard: '<path d="M3 3v18h18"/><path d="M7 16v-5M12 16V7M17 16v-8"/>',
  permissoes: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/><path d="M9 12l2 2 4-4"/>',
  avaliacoes: '<path d="M12 2.5l2.95 5.98 6.6.96-4.77 4.65 1.12 6.57L12 17.55l-5.9 3.11 1.13-6.57L2.46 9.44l6.6-.96z"/>'
};

function icone(nome) {
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
    stroke-linecap="round" stroke-linejoin="round">${ICONES[nome] || ''}</svg>`;
}

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

// Publica a altura real do cabecalho em --altura-topo. O topo tem flex-wrap e
// quebra em duas linhas em tela estreita; com a altura chutada no CSS a lateral
// fixa desalinha e sobra (ou falta) uma faixa no fim do painel. Roda de novo ao
// girar o aparelho e sempre que o topo mudar de tamanho.
function medirTopo() {
  const topo = document.querySelector('header.topo');
  if (!topo) return;
  document.documentElement.style.setProperty('--altura-topo', `${topo.offsetHeight}px`);
}

addEventListener('DOMContentLoaded', () => {
  medirTopo();

  const topo = document.querySelector('header.topo');
  if (topo && 'ResizeObserver' in window) new ResizeObserver(medirTopo).observe(topo);
});

addEventListener('resize', medirTopo);
addEventListener('orientationchange', medirTopo);
