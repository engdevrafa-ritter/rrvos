using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RrvOs;

const string Url = "http://127.0.0.1:8787";

// Apenas uma instância: se já estiver aberto, só abre o navegador
using var mutex = new Mutex(true, "Local\\rrv.o.s.single", out var primeiraInstancia);
if (!primeiraInstancia)
{
    Util.AbrirNavegador(Url);
    return;
}

var dir = Environment.GetEnvironmentVariable("RRVOS_DATA")
          ?? Path.GetDirectoryName(Environment.ProcessPath)
          ?? AppContext.BaseDirectory;
Store.Init(Path.Combine(dir, "rrv.o.s-dados.json"));

byte[] indexHtml;
using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("index.html")!)
using (var ms = new MemoryStream())
{
    s.CopyTo(ms);
    indexHtml = ms.ToArray();
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Url);
builder.Logging.ClearProviders();
var app = builder.Build();

// ---------- Interface ----------
app.MapGet("/", () => Results.Bytes(indexHtml, "text/html; charset=utf-8"));

// ---------- Login ----------
app.MapPost("/api/login", async (HttpContext ctx, LoginIn input) =>
{
    User? found = null;
    lock (Store.Lock)
    {
        found = Store.Data.Users.FirstOrDefault(u =>
            u.Ativo && string.Equals(u.Login, (input.Login ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
    }
    if (found is null || !Auth.Confere(input.Senha ?? "", found))
    {
        await Task.Delay(600);
        return Util.Fail(401, "usuário ou senha incorretos");
    }
    var token = Auth.Rand(32);
    Auth.Sessions[token] = (found.Id, DateTime.UtcNow.AddHours(12));
    ctx.Response.Cookies.Append("rrvos_sess", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        MaxAge = TimeSpan.FromHours(12),
        Path = "/"
    });
    return Results.Ok(PublicUser.From(found));
});

app.MapPost("/api/logout", (HttpContext ctx) =>
{
    var tok = ctx.Request.Cookies["rrvos_sess"];
    if (tok is not null) Auth.Sessions.TryRemove(tok, out _);
    ctx.Response.Cookies.Delete("rrvos_sess");
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/me", IResult (HttpContext ctx) =>
{
    var (u, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    return Results.Ok(PublicUser.From(u!));
});

// ---------- Clientes ----------
app.MapGet("/api/clientes", IResult (HttpContext ctx) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    lock (Store.Lock)
        return Results.Ok(Store.Data.Clientes.OrderBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase).ToList());
});

app.MapPost("/api/clientes", IResult (HttpContext ctx, Cliente c) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    c.Nome = (c.Nome ?? "").Trim();
    if (c.Nome == "") return Util.Fail(400, "informe o nome do cliente");
    lock (Store.Lock)
    {
        c.Id = Store.NextId("cliente");
        c.Criado = Util.Today();
        Store.Data.Clientes.Add(c);
        Store.Save();
        return Results.Ok(c);
    }
});

app.MapPut("/api/clientes/{id:int}", IResult (HttpContext ctx, int id, Cliente c) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    c.Nome = (c.Nome ?? "").Trim();
    if (c.Nome == "") return Util.Fail(400, "informe o nome do cliente");
    lock (Store.Lock)
    {
        var i = Store.Data.Clientes.FindIndex(x => x.Id == id);
        if (i < 0) return Util.Fail(404, "cliente não encontrado");
        c.Id = id;
        c.Criado = Store.Data.Clientes[i].Criado;
        Store.Data.Clientes[i] = c;
        Store.Save();
        return Results.Ok(c);
    }
});

app.MapDelete("/api/clientes/{id:int}", IResult (HttpContext ctx, int id) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    lock (Store.Lock)
    {
        if (Store.Data.Os.Any(o => o.ClienteId == id))
            return Util.Fail(409, "este cliente possui O.S cadastradas e não pode ser excluído");
        var n = Store.Data.Clientes.RemoveAll(x => x.Id == id);
        if (n == 0) return Util.Fail(404, "cliente não encontrado");
        Store.Save();
        return Results.Ok(new { ok = true });
    }
});

// ---------- Produtos ----------
app.MapGet("/api/produtos", IResult (HttpContext ctx) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    lock (Store.Lock)
        return Results.Ok(Store.Data.Produtos.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList());
});

app.MapPost("/api/produtos", IResult (HttpContext ctx, Produto p) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    p.Nome = (p.Nome ?? "").Trim();
    if (p.Nome == "") return Util.Fail(400, "informe o nome do produto");
    if (p.Tipo != "servico") p.Tipo = "peca";
    lock (Store.Lock)
    {
        p.Id = Store.NextId("produto");
        Store.Data.Produtos.Add(p);
        Store.Save();
        return Results.Ok(p);
    }
});

app.MapPut("/api/produtos/{id:int}", IResult (HttpContext ctx, int id, Produto p) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    p.Nome = (p.Nome ?? "").Trim();
    if (p.Nome == "") return Util.Fail(400, "informe o nome do produto");
    if (p.Tipo != "servico") p.Tipo = "peca";
    lock (Store.Lock)
    {
        var i = Store.Data.Produtos.FindIndex(x => x.Id == id);
        if (i < 0) return Util.Fail(404, "produto não encontrado");
        p.Id = id;
        Store.Data.Produtos[i] = p;
        Store.Save();
        return Results.Ok(p);
    }
});

app.MapDelete("/api/produtos/{id:int}", IResult (HttpContext ctx, int id) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    lock (Store.Lock)
    {
        var n = Store.Data.Produtos.RemoveAll(x => x.Id == id);
        if (n == 0) return Util.Fail(404, "produto não encontrado");
        Store.Save();
        return Results.Ok(new { ok = true });
    }
});

// ---------- Ordens de serviço ----------
app.MapGet("/api/os", IResult (HttpContext ctx) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    lock (Store.Lock)
        return Results.Ok(Store.Data.Os.OrderByDescending(o => o.Numero).ToList());
});

app.MapPost("/api/os", IResult (HttpContext ctx, Ordem o) =>
{
    var (u, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    lock (Store.Lock)
    {
        var msg = OsLogic.Valida(o);
        if (msg != "") return Util.Fail(400, msg);
        o.Id = Store.NextId("os");
        o.Numero = Store.NextId("osnum");
        o.Abertura = Util.Today();
        o.Responsavel = u!.Nome;
        o.EstoqueBaixado = false;
        o.Conclusao = OsLogic.StatusBaixa(o.Status) ? Util.Today() : "";
        OsLogic.CalcTotal(o);
        OsLogic.SyncEstoque(o);
        Store.Data.Os.Add(o);
        Store.Save();
        return Results.Ok(o);
    }
});

app.MapPut("/api/os/{id:int}", IResult (HttpContext ctx, int id, Ordem o) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    lock (Store.Lock)
    {
        var i = Store.Data.Os.FindIndex(x => x.Id == id);
        if (i < 0) return Util.Fail(404, "O.S não encontrada");
        var msg = OsLogic.Valida(o);
        if (msg != "") return Util.Fail(400, msg);

        var old = Store.Data.Os[i];
        // devolve ao estoque o que já havia sido baixado e reaplica com os dados novos
        if (old.EstoqueBaixado) OsLogic.MovEstoque(old, +1);

        o.Id = id;
        o.Numero = old.Numero;
        o.Abertura = old.Abertura;
        o.Responsavel = old.Responsavel;
        o.EstoqueBaixado = false;
        o.Conclusao = OsLogic.StatusBaixa(o.Status)
            ? (string.IsNullOrEmpty(old.Conclusao) ? Util.Today() : old.Conclusao)
            : "";
        OsLogic.CalcTotal(o);
        OsLogic.SyncEstoque(o);
        Store.Data.Os[i] = o;
        Store.Save();
        return Results.Ok(o);
    }
});

app.MapDelete("/api/os/{id:int}", IResult (HttpContext ctx, int id) =>
{
    var (_, err) = Auth.Check(ctx, true);
    if (err is not null) return err;
    lock (Store.Lock)
    {
        var o = Store.Data.Os.FirstOrDefault(x => x.Id == id);
        if (o is null) return Util.Fail(404, "O.S não encontrada");
        if (o.EstoqueBaixado) OsLogic.MovEstoque(o, +1);
        Store.Data.Os.Remove(o);
        Store.Save();
        return Results.Ok(new { ok = true });
    }
});

// ---------- Usuários ----------
app.MapGet("/api/usuarios", IResult (HttpContext ctx) =>
{
    var (_, err) = Auth.Check(ctx, true);
    if (err is not null) return err;
    lock (Store.Lock)
        return Results.Ok(Store.Data.Users.Select(PublicUser.From).ToList());
});

app.MapPost("/api/usuarios", IResult (HttpContext ctx, UserCreateIn input) =>
{
    var (_, err) = Auth.Check(ctx, true);
    if (err is not null) return err;
    var nome = (input.Nome ?? "").Trim();
    var login = (input.Login ?? "").Trim();
    if (nome == "" || login == "" || (input.Senha ?? "").Length < 6)
        return Util.Fail(400, "preencha nome, login e uma senha com no mínimo 6 caracteres");
    lock (Store.Lock)
    {
        if (Store.Data.Users.Any(x => string.Equals(x.Login, login, StringComparison.OrdinalIgnoreCase)))
            return Util.Fail(409, "já existe um usuário com esse login");
        var nu = Auth.NewUser(nome, login, input.Senha!, input.Admin);
        Store.Data.Users.Add(nu);
        Store.Save();
        return Results.Ok(PublicUser.From(nu));
    }
});

app.MapPut("/api/usuarios/{id:int}", IResult (HttpContext ctx, int id, UserUpdateIn input) =>
{
    var (me, err) = Auth.Check(ctx, true);
    if (err is not null) return err;
    lock (Store.Lock)
    {
        var u = Store.Data.Users.FirstOrDefault(x => x.Id == id);
        if (u is null) return Util.Fail(404, "usuário não encontrado");
        if (id == me!.Id && (!input.Admin || !input.Ativo))
            return Util.Fail(400, "você não pode remover seu próprio acesso de administrador nem se desativar");
        if (!string.IsNullOrWhiteSpace(input.Senha) && input.Senha.Length < 6)
            return Util.Fail(400, "a senha deve ter no mínimo 6 caracteres");
        if (!string.IsNullOrWhiteSpace(input.Nome)) u.Nome = input.Nome.Trim();
        u.Admin = input.Admin;
        u.Ativo = input.Ativo;
        if (!string.IsNullOrEmpty(input.Senha))
        {
            u.Salt = Auth.Rand(16);
            u.Hash = Auth.Hash(input.Senha, u.Salt);
        }
        Store.Save();
        return Results.Ok(PublicUser.From(u));
    }
});

app.MapPost("/api/minha-senha", IResult (HttpContext ctx, SenhaIn input) =>
{
    var (me, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    if ((input.Nova ?? "").Length < 6) return Util.Fail(400, "a nova senha deve ter no mínimo 6 caracteres");
    lock (Store.Lock)
    {
        if (!Auth.Confere(input.Atual ?? "", me!)) return Util.Fail(400, "senha atual incorreta");
        me!.Salt = Auth.Rand(16);
        me.Hash = Auth.Hash(input.Nova!, me.Salt);
        Store.Save();
        return Results.Ok(new { ok = true });
    }
});

// ---------- Configurações e backup ----------
app.MapGet("/api/config", IResult (HttpContext ctx) =>
{
    var (_, err) = Auth.Check(ctx, false);
    if (err is not null) return err;
    lock (Store.Lock) return Results.Ok(Store.Data.Config);
});

app.MapPut("/api/config", IResult (HttpContext ctx, Config c) =>
{
    var (_, err) = Auth.Check(ctx, true);
    if (err is not null) return err;
    lock (Store.Lock)
    {
        Store.Data.Config = c;
        Store.Save();
        return Results.Ok(c);
    }
});

app.MapGet("/api/backup", IResult (HttpContext ctx) =>
{
    var (_, err) = Auth.Check(ctx, true);
    if (err is not null) return err;
    string json;
    lock (Store.Lock) json = JsonSerializer.Serialize(Store.Data, Store.Json);
    return Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"rrv.o.s-backup-{Util.Today()}.json");
});

// ---------- Abre o navegador e inicia ----------
if (Environment.GetEnvironmentVariable("RRVOS_NOBROWSER") is null)
{
    app.Lifetime.ApplicationStarted.Register(() => Util.AbrirNavegador(Url));
}
app.Run();

// =====================================================================

static class Util
{
    public static void AbrirNavegador(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    public static string Today() => DateTime.Now.ToString("yyyy-MM-dd");

    public static IResult Fail(int code, string msg) => Results.Json(new { erro = msg }, statusCode: code);
}

static class Auth
{
    public static readonly ConcurrentDictionary<string, (int UserId, DateTime Exp)> Sessions = new();

    public static string Rand(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    public static string Hash(string pass, string salt) =>
        Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pass), Encoding.UTF8.GetBytes(salt), 120_000, HashAlgorithmName.SHA256, 32));

    public static bool Confere(string pass, User u) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(pass, u.Salt)), Encoding.UTF8.GetBytes(u.Hash));

    // Chamar dentro de lock (Store.Lock) ou durante a inicialização
    public static User NewUser(string nome, string login, string senha, bool admin)
    {
        var salt = Rand(16);
        return new User
        {
            Id = Store.NextId("user"),
            Nome = nome,
            Login = login,
            Admin = admin,
            Ativo = true,
            Salt = salt,
            Hash = Hash(senha, salt)
        };
    }

    public static (User? user, IResult? err) Check(HttpContext ctx, bool exigeAdmin)
    {
        var tok = ctx.Request.Cookies["rrvos_sess"];
        if (tok is null || !Sessions.TryGetValue(tok, out var s))
            return (null, Util.Fail(401, "não autenticado"));
        if (DateTime.UtcNow > s.Exp)
        {
            Sessions.TryRemove(tok, out _);
            return (null, Util.Fail(401, "sessão expirada"));
        }

        // proteção básica contra requisições vindas de outros sites
        if (!HttpMethods.IsGet(ctx.Request.Method))
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (origin != "" && !origin.EndsWith("127.0.0.1:8787") && !origin.EndsWith("localhost:8787"))
                return (null, Util.Fail(403, "origem inválida"));
        }

        User? user;
        lock (Store.Lock)
            user = Store.Data.Users.FirstOrDefault(u => u.Id == s.UserId && u.Ativo);
        if (user is null) return (null, Util.Fail(401, "não autenticado"));
        if (exigeAdmin && !user.Admin) return (null, Util.Fail(403, "apenas administradores"));
        return (user, null);
    }
}

static class OsLogic
{
    static readonly HashSet<string> StatusValidos = new()
    {
        "Aberta", "Em andamento", "Aguardando peça", "Aguardando aprovação", "Concluída", "Entregue", "Cancelada"
    };

    public static bool StatusBaixa(string s) => s == "Concluída" || s == "Entregue";

    // Chamar dentro de lock
    public static string Valida(Ordem o)
    {
        if (!Store.Data.Clientes.Any(c => c.Id == o.ClienteId)) return "selecione um cliente";
        if (!StatusValidos.Contains(o.Status)) o.Status = "Aberta";
        o.Itens ??= new List<Item>();
        if (o.MaoDeObra < 0 || o.Desconto < 0) return "valores não podem ser negativos";
        if (o.Itens.Any(i => i.Qtd <= 0 || i.Preco < 0)) return "itens com quantidade ou preço inválido";
        return "";
    }

    public static void CalcTotal(Ordem o)
    {
        var t = o.MaoDeObra + o.Itens.Sum(i => i.Qtd * i.Preco) - o.Desconto;
        o.Total = Math.Round(Math.Max(t, 0), 2, MidpointRounding.AwayFromZero);
    }

    public static void MovEstoque(Ordem o, double sinal)
    {
        foreach (var it in o.Itens)
        {
            var p = Store.Data.Produtos.FirstOrDefault(x => x.Id == it.ProdutoId);
            if (p is not null && p.Tipo == "peca") p.Estoque += sinal * it.Qtd;
        }
    }

    // baixa o estoque ao concluir/entregar e devolve se a O.S voltar a ficar em aberto
    public static void SyncEstoque(Ordem o)
    {
        if (StatusBaixa(o.Status) && !o.EstoqueBaixado)
        {
            MovEstoque(o, -1);
            o.EstoqueBaixado = true;
        }
        else if (!StatusBaixa(o.Status) && o.EstoqueBaixado)
        {
            MovEstoque(o, +1);
            o.EstoqueBaixado = false;
        }
    }
}
