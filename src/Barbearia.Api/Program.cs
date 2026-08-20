using System.Text;
using System.Threading.RateLimiting;
using Barbearia.Api;
using Barbearia.Api.Endpoints;
using Barbearia.Api.Seguranca;
using Barbearia.Api.WhatsApp;
using Barbearia.Domain;
using Barbearia.Infrastructure;
using Barbearia.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var porta = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(porta))
    builder.WebHost.UseUrls($"http://0.0.0.0:{porta}");

builder.Services.AddBarbearia(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<FilaDeMensagens>();
builder.Services.AddHostedService<ProcessadorDeMensagens>();

// Guarda o contador de tentativas de login por conta (defesa contra brute-force
// que nao depende do IP — o limite por IP e burlavel forjando X-Forwarded-For).
builder.Services.AddMemoryCache();

var jwtSecret = builder.Configuration["Jwt:Secret"]
                ?? throw new InvalidOperationException("Jwt:Secret nao configurado");

if (jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret precisa de no minimo 32 caracteres");

if (!builder.Environment.IsDevelopment())
{
    var conexao = builder.Configuration.GetConnectionString("Postgres") ?? "";
    var problemas = new List<string>();

    if (jwtSecret.Contains("troque-esta-chave"))
        problemas.Add("Jwt__Secret ainda e o valor de exemplo. Use 32+ caracteres aleatorios.");

    var sujeira = conexao.Where(c => c is '\n' or '\r' or '\t' || c > '~').Distinct().ToArray();

    if (string.IsNullOrWhiteSpace(conexao))
        problemas.Add("ConnectionStrings__Postgres esta vazia.");
    else if (sujeira.Length > 0)
        problemas.Add(
            "ConnectionStrings__Postgres contem quebra de linha ou caractere estranho — " +
            "sinal de valor copiado de dentro de uma tabela do terminal, com as bordas junto. " +
            "Cole em UMA linha so, sem espacos nas pontas.");
    else if (conexao.StartsWith("postgres", StringComparison.OrdinalIgnoreCase) && conexao.Contains("://"))
        problemas.Add(
            "ConnectionStrings__Postgres esta no formato URI do provedor. O Npgsql usa " +
            "chave=valor: Host=...;Database=...;Username=...;Password=...;SSL Mode=Require");
    else if (conexao.Contains("Password=postgres") || conexao.Contains("localhost"))
        problemas.Add("ConnectionStrings__Postgres aponta para o banco local de exemplo.");
    else if (!conexao.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        problemas.Add("ConnectionStrings__Postgres nao tem 'Host='. Confira o formato do Npgsql.");

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_SENHA")))
        problemas.Add("ADMIN_SENHA nao definida: o seed criaria o admin com a senha padrao.");

    if (problemas.Count > 0)
    {
        static string Situacao(string variavel)
        {
            var v = Environment.GetEnvironmentVariable(variavel);
            return string.IsNullOrWhiteSpace(v) ? "NAO DEFINIDA no Render" : "definida";
        }

        Console.WriteLine(
            "\n===== CONFIGURACAO INCOMPLETA — a aplicacao nao vai subir =====\n" +
            string.Join("\n", problemas.Select(p => "  - " + p)) +
            "\n\n  Variaveis de ambiente (valores nunca sao exibidos):\n" +
            $"    ConnectionStrings__Postgres : {Situacao("ConnectionStrings__Postgres")}\n" +
            $"    Jwt__Secret                 : {Situacao("Jwt__Secret")}\n" +
            $"    ADMIN_SENHA                 : {Situacao("ADMIN_SENHA")}\n" +
            $"    ADMIN_EMAIL                 : {Situacao("ADMIN_EMAIL")}\n" +
            $"    App__UrlPublica             : {Situacao("App__UrlPublica")}\n" +
            "===============================================================\n");

        throw new InvalidOperationException(
            $"Configuracao invalida em producao: {problemas.Count} problema(s) acima.");
    }
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        opt.MapInboundClaims = false;

        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = "role",
            NameClaimType = "name"
        };
    });

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ChaveIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1) }));

    o.AddPolicy("agendar", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ChaveIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1) }));

    // Avaliacao e pedido de produto sao gravacoes sem login. O indice unico ja
    // impede a mesma pessoa avaliar duas vezes o mesmo produto; este limite corta
    // quem tenta varrer produtos trocando de telefone.
    o.AddPolicy("vitrine", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ChaveIp(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));

    static string ChaveIp(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
});

static bool SenhaOk(System.Security.Claims.ClaimsPrincipal u) =>
    !u.HasClaim("trocar_senha", "1");

static bool EhAdmin(System.Security.Claims.ClaimsPrincipal u) =>
    u.IsInRole(nameof(Perfil.Admin));

static bool Permitido(System.Security.Claims.ClaimsPrincipal u, string permissao) =>
    SenhaOk(u) && (EhAdmin(u)
                   || u.IsInRole(nameof(Perfil.Gestor))
                   || u.HasClaim("perm", permissao));

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireAssertion(c => SenhaOk(c.User) && EhAdmin(c.User)))
    .AddPolicy("Gestao", p => p.RequireAssertion(c =>
        SenhaOk(c.User) && (EhAdmin(c.User) || c.User.IsInRole(nameof(Perfil.Gestor)))))
    .AddPolicy("Painel", p => p.RequireAssertion(c => SenhaOk(c.User) && (
        EhAdmin(c.User) || c.User.IsInRole(nameof(Perfil.Gestor)) || c.User.IsInRole(nameof(Perfil.Barbeiro)))))
    .AddPolicy("Servicos", p => p.RequireAssertion(c => Permitido(c.User, "servicos")))
    .AddPolicy("Produtos", p => p.RequireAssertion(c => Permitido(c.User, "produtos")))
    .AddPolicy("Clientes", p => p.RequireAssertion(c => Permitido(c.User, "clientes")))
    .AddPolicy("Dashboard", p => p.RequireAssertion(c =>
        SenhaOk(c.User) && (EhAdmin(c.User) || c.User.HasClaim("perm", "dashboard"))))
    // Area do cliente. Exige o papel exato: um token do painel nao serve aqui, senao
    // o "sub" de um barbeiro seria lido como se fosse o Id de um cliente.
    .AddPolicy("Cliente", p => p.RequireAssertion(c => c.User.IsInRole(Papeis.Cliente)));

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration["App:OrigensPermitidas"]?.Split(',',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? ["http://localhost:5173"])
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST", "PUT", "DELETE")));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await Seed.ExecutarAsync(db);
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/health"), ramo =>
    {
        ramo.UseHsts();
        ramo.UseHttpsRedirection();
    });
}

app.UseDefaultFiles();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // O HTML nao pode ficar preso no cache do navegador. Sao as paginas que
        // carregam "app.js?v=...", entao um index.html velho continua pedindo o
        // js velho: depois de um deploy a pessoa segue vendo a tela antiga e o
        // bug que ja foi corrigido. "no-cache" nao proibe guardar, so obriga a
        // revalidar — quando nada mudou volta 304, que e barato.
        // Os arquivos com ?v= no nome podem ser guardados a vontade.
        var ehPagina = ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

        ctx.Context.Response.Headers.CacheControl =
            ehPagina ? "no-cache, must-revalidate" : "public, max-age=604800";
    }
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();

// Depois do UseAuthentication (precisa do ctx.User pronto) e antes do
// UseAuthorization: token de funcionario desativado, com permissao trocada ou com
// a senha alterada morre aqui, sem chegar em policy nenhuma.
app.UseGuardaDeSessao();

app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", agora = Fuso.AgoraLocal() }));

app.MapAuth();
app.MapPublico();
app.MapGestor();
app.MapCatalogo();
app.MapVitrine();
app.MapCliente();
app.MapPermissoes();
app.MapDashboard();
app.MapAdmin();
app.MapWebhook();

app.Run();
