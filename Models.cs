using System.Text.Json;

namespace RrvOs;

public class User
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Login { get; set; } = "";
    public bool Admin { get; set; }
    public bool Ativo { get; set; } = true;
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
}

public record PublicUser(int Id, string Nome, string Login, bool Admin, bool Ativo)
{
    public static PublicUser From(User u) => new(u.Id, u.Nome, u.Login, u.Admin, u.Ativo);
}

public class Cliente
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Documento { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Obs { get; set; } = "";
    public string Criado { get; set; } = "";
}

public class Produto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Tipo { get; set; } = "peca"; // peca | servico
    public double Custo { get; set; }
    public double Preco { get; set; }
    public double Estoque { get; set; }
    public double EstMin { get; set; }
}

public class Item
{
    public int ProdutoId { get; set; }
    public string Descricao { get; set; } = "";
    public double Qtd { get; set; }
    public double Preco { get; set; }
}

public class Ordem
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public int ClienteId { get; set; }
    public string Veiculo { get; set; } = "";
    public string Placa { get; set; } = "";
    public string Km { get; set; } = "";
    public string Defeito { get; set; } = "";
    public string Diagnostico { get; set; } = "";
    public string Status { get; set; } = "Aberta";
    public List<Item> Itens { get; set; } = new();
    public double MaoDeObra { get; set; }
    public double Desconto { get; set; }
    public double Total { get; set; }
    public string Pagamento { get; set; } = "";
    public bool Pago { get; set; }
    public string Obs { get; set; } = "";
    public string Abertura { get; set; } = "";
    public string Conclusao { get; set; } = "";
    public bool EstoqueBaixado { get; set; }
    public string Responsavel { get; set; } = "";
}

public class Config
{
    public string Oficina { get; set; } = "Minha Oficina";
    public string Telefone { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Doc { get; set; } = "";
    public string MsgOS { get; set; } =
        "Olá {cliente}! Aqui é da {oficina}. Sua O.S nº {numero} ({veiculo}) está: {status}. Total: {total}.";
}

public class Db
{
    public List<User> Users { get; set; } = new();
    public List<Cliente> Clientes { get; set; } = new();
    public List<Produto> Produtos { get; set; } = new();
    public List<Ordem> Os { get; set; } = new();
    public Config Config { get; set; } = new();
    public Dictionary<string, int> Seq { get; set; } = new();
}

public record LoginIn(string? Login, string? Senha);
public record UserCreateIn(string? Nome, string? Login, string? Senha, bool Admin);
public record UserUpdateIn(string? Nome, string? Senha, bool Admin, bool Ativo);
public record SenhaIn(string? Atual, string? Nova);

public static class Store
{
    public static readonly object Lock = new();
    public static Db Data = new();
    static string _file = "";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int NextId(string key)
    {
        Data.Seq.TryGetValue(key, out var n);
        Data.Seq[key] = ++n;
        return n;
    }

    public static void Init(string file)
    {
        _file = file;
        try
        {
            if (File.Exists(file))
                Data = JsonSerializer.Deserialize<Db>(File.ReadAllText(file), Json) ?? new Db();
        }
        catch
        {
            // arquivo corrompido: preserva uma cópia e recomeça a partir do backup, se existir
            try { File.Copy(file, file + ".corrompido", true); } catch { }
            try
            {
                if (File.Exists(file + ".bak"))
                    Data = JsonSerializer.Deserialize<Db>(File.ReadAllText(file + ".bak"), Json) ?? new Db();
            }
            catch { Data = new Db(); }
        }

        lock (Lock)
        {
            if (Data.Users.Count == 0)
                Data.Users.Add(Auth.NewUser("Administrador", "admin", "admin123", true));
            Save();
        }
    }

    // Chamar sempre dentro de lock (Store.Lock)
    public static void Save()
    {
        try
        {
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Json));
            if (File.Exists(_file)) File.Copy(_file, _file + ".bak", true);
            File.Move(tmp, _file, true);
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(_file + ".log", DateTime.Now + " erro ao salvar: " + ex.Message + Environment.NewLine); } catch { }
        }
    }
}
