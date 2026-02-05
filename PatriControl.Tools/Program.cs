using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using System.Globalization;
using System.Text;

namespace PatriControl.Tools;

internal class Program
{
    // REGRA DO SISTEMA:
    // Importação sempre registra como ROOT (Id=1 / USR001)
    private const int ROOT_USER_ID = 1;

    // Colunas esperadas (cabeçalho do Excel)
    private const string COL_NUMERO = "N PATRIMONIO";
    private const string COL_DESC = "DESCRICAO";
    private const string COL_VALOR = "VALOR";
    private const string COL_TIPO = "TIPO DE PATRIMONIO";
    private const string COL_DATA = "DATA DA COMPRA";
    private const string COL_UNIDADE = "UNIDADE";
    private const string COL_FORN = "FORNECEDOR";
    private const string COL_SERIE = "N SERIE NF";
    private const string COL_NF = "N NF";
    private const string COL_STATUS = "STATUS";
    private const string COL_COND = "CONDICAO";

    // Canonização de STATUS (aceita variações sem acento)
    private static readonly Dictionary<string, string> StatusMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ATIVO"] = "Ativo",
        ["BAIXADO"] = "Baixado",
        ["EMPRESTADO"] = "Emprestado",

        ["AGUARDANDO MANUTENÇÃO"] = "Aguardando Manutenção",
        ["AGUARDANDO MANUTENCAO"] = "Aguardando Manutenção",

        ["EM MANUTENÇÃO"] = "Em Manutenção",
        ["EM MANUTENCAO"] = "Em Manutenção"
    };

    // Canonização de CONDIÇÃO
    private static readonly Dictionary<string, string> CondicaoMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOVO"] = "Novo",
        ["SEMI-NOVO"] = "Semi-Novo",
        ["SEMI NOVO"] = "Semi-Novo",
        ["USADO"] = "Usado"
    };

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return 1;
        }

        var excelPath = args[0];
        var sheetName = GetArg(args, "--sheet");       // opcional
        var dbPath = GetArg(args, "--db");             // opcional
        var reportPathArg = GetArg(args, "--report");  // opcional

        if (!File.Exists(excelPath))
        {
            Console.WriteLine($"✖ Arquivo não encontrado: {excelPath}");
            return 2;
        }

        var sqliteFile = ResolveDbPath(dbPath);
        var connString = $"Data Source={sqliteFile}";

        Console.WriteLine($"📄 Excel: {excelPath}");
        Console.WriteLine($"🗄️ SQLite: {sqliteFile}");
        Console.WriteLine($"👤 Usuário (CriadoPorId): {ROOT_USER_ID} (ROOT)");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<PatriControlContext>()
            .UseSqlite(connString)
            .Options;

        using var db = new PatriControlContext(options);

        // aplica migrations (incluindo Identity)
        db.Database.Migrate();

        // garante FK de usuário e pega Nome pra tramites
        var root = EnsureUsuario(db, ROOT_USER_ID);

        // relatório
        var reportLines = new List<string>();
        void Rep(string msg) => reportLines.Add(msg);

        // 1) Lê planilha
        using var wb = new XLWorkbook(excelPath);
        var ws = !string.IsNullOrWhiteSpace(sheetName)
            ? wb.Worksheet(sheetName)
            : wb.Worksheets.First();

        Console.WriteLine($"✅ Aba: {ws.Name}");

        // 2) Mapear colunas
        var headerRow = ws.Row(1);
        var colMap = BuildColumnMap(headerRow);

        // valida cabeçalhos (como você definiu)
        var requiredHeaders = new[]
        {
            COL_NUMERO, COL_DESC, COL_VALOR, COL_TIPO, COL_DATA, COL_UNIDADE, COL_FORN, COL_SERIE, COL_NF, COL_STATUS, COL_COND
        };

        foreach (var h in requiredHeaders)
        {
            if (!colMap.ContainsKey(Norm(h)))
            {
                Console.WriteLine($"✖ Cabeçalho não encontrado: '{h}'");
                return 3;
            }
        }

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        // 3) Primeiro passe: criar Tipo / Unidade / Fornecedor (apenas se vier preenchido)
        var tiposSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fornSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // "PER|NOME"

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            var desc = GetString(row, colMap, COL_DESC);
            if (string.IsNullOrWhiteSpace(desc)) continue; // linha vazia

            var tipo = GetString(row, colMap, COL_TIPO);
            var forn = GetString(row, colMap, COL_FORN);
            var uniRaw = GetString(row, colMap, COL_UNIDADE);

            if (!string.IsNullOrWhiteSpace(tipo)) tiposSet.Add(tipo.Trim());
            if (!string.IsNullOrWhiteSpace(forn)) fornSet.Add(forn.Trim());

            var up = ParseUnidade(uniRaw);
            if (!string.IsNullOrWhiteSpace(up.Per) || !string.IsNullOrWhiteSpace(up.Nome))
                unSet.Add($"{up.Per}|{up.Nome}");
        }

        Console.WriteLine($"📌 Tipos encontrados: {tiposSet.Count}");
        Console.WriteLine($"📌 Fornecedores encontrados: {fornSet.Count}");
        Console.WriteLine($"📌 Unidades encontradas: {unSet.Count}");
        Console.WriteLine();

        var tiposDb = db.TiposPatrimonio.AsNoTracking().ToList();
        var fornDb = db.Fornecedores.AsNoTracking().ToList();
        var unDb = db.Unidades.AsNoTracking().ToList();

        var tiposIndex = tiposDb.ToDictionary(x => Key(x.Nome), x => x, StringComparer.OrdinalIgnoreCase);
        var fornIndex = fornDb.ToDictionary(x => Key(x.Nome), x => x, StringComparer.OrdinalIgnoreCase);
        var unIndex = unDb.ToDictionary(x => $"{Key(x.Per)}|{Key(x.Nome)}", x => x, StringComparer.OrdinalIgnoreCase);

        int createdTipos = 0, createdForn = 0, createdUn = 0;

        foreach (var t in tiposSet)
        {
            var k = Key(t);
            if (string.IsNullOrWhiteSpace(k)) continue;
            if (tiposIndex.ContainsKey(k)) continue;

            var novo = new TipoPatrimonio { Nome = t.Trim() };
            db.TiposPatrimonio.Add(novo);
            tiposIndex[k] = novo;
            createdTipos++;
        }

        foreach (var f in fornSet)
        {
            var k = Key(f);
            if (string.IsNullOrWhiteSpace(k)) continue;
            if (fornIndex.ContainsKey(k)) continue;

            var novo = new Fornecedor { Nome = f.Trim() };
            db.Fornecedores.Add(novo);
            fornIndex[k] = novo;
            createdForn++;
        }

        foreach (var u in unSet)
        {
            var parts = u.Split('|');
            var per = parts.ElementAtOrDefault(0) ?? "";
            var nome = parts.ElementAtOrDefault(1) ?? "";
            var k = $"{Key(per)}|{Key(nome)}";

            if (string.IsNullOrWhiteSpace(Key(per)) && string.IsNullOrWhiteSpace(Key(nome))) continue;
            if (unIndex.ContainsKey(k)) continue;

            var novo = new Unidade { Per = per.Trim(), Nome = nome.Trim() };
            db.Unidades.Add(novo);
            unIndex[k] = novo;
            createdUn++;
        }

        db.SaveChanges();
        db.ChangeTracker.Clear();

        Console.WriteLine($"✅ Criados: Tipos={createdTipos} | Fornecedores={createdForn} | Unidades={createdUn}");
        Console.WriteLine();

        // 4) Segundo passe: criar Patrimonios
        int inserted = 0, emptyRows = 0, duplicates = 0, failed = 0;

        // números já existentes no db
        var numerosDb = new HashSet<string>(
            db.Patrimonios.AsNoTracking()
                .Where(p => p.Numero != null && p.Numero.Trim() != "")
                .Select(p => p.Numero!.Trim())
                .Distinct(),
            StringComparer.OrdinalIgnoreCase
        );

        // duplicados dentro do arquivo
        var numerosArquivo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // batch para salvar e identificar falhas
        const int BATCH_SIZE = 200;
        var batch = new List<(int RowNumber, Patrimonio Pat, Tramite Tr)>();

        void FlushBatch()
        {
            if (batch.Count == 0) return;

            try
            {
                db.SaveChanges();
                db.ChangeTracker.Clear();
                batch.Clear();
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();

                foreach (var item in batch)
                {
                    try
                    {
                        db.Patrimonios.Add(item.Pat);
                        db.Tramites.Add(item.Tr);
                        db.SaveChanges();
                        db.ChangeTracker.Clear();
                    }
                    catch (Exception ex1)
                    {
                        failed++;
                        Rep($"[FALHA] Linha {item.RowNumber}: Numero='{item.Pat.Numero ?? ""}' | Desc='{item.Pat.Descricao}' | Erro: {GetBestError(ex1)}");
                        db.ChangeTracker.Clear();
                    }
                }

                batch.Clear();
                Rep($"[AVISO] Batch falhou e foi salvo item a item. Erro do batch: {GetBestError(ex)}");
            }
        }

        var nomeUsuarioTramite = BuildNomeUsuario(root);

        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);

            var desc = GetString(row, colMap, COL_DESC);
            if (string.IsNullOrWhiteSpace(desc))
            {
                emptyRows++;
                continue;
            }

            var numero = GetString(row, colMap, COL_NUMERO);
            numero = string.IsNullOrWhiteSpace(numero) ? null : numero.Trim();

            // duplicado (arquivo ou db)
            if (!string.IsNullOrWhiteSpace(numero))
            {
                if (numerosArquivo.Contains(numero))
                {
                    duplicates++;
                    Rep($"[DUPLICADO ARQUIVO] Linha {r}: Nº='{numero}' | Desc='{desc.Trim()}'");
                    continue;
                }

                if (numerosDb.Contains(numero))
                {
                    duplicates++;
                    Rep($"[DUPLICADO DB] Linha {r}: Nº='{numero}' | Desc='{desc.Trim()}'");
                    continue;
                }

                numerosArquivo.Add(numero);
            }

            var tipo = GetString(row, colMap, COL_TIPO);
            var forn = GetString(row, colMap, COL_FORN);
            var uniRaw = GetString(row, colMap, COL_UNIDADE);

            var valor = GetDecimal(row, colMap, COL_VALOR);
            var dataCompra = GetDate(row, colMap, COL_DATA);

            var serie = GetString(row, colMap, COL_SERIE);
            var nf = GetString(row, colMap, COL_NF);

            var status = NormalizeStatus(GetString(row, colMap, COL_STATUS));
            var cond = NormalizeCondicao(GetString(row, colMap, COL_COND));

            var up = ParseUnidade(uniRaw);
            var unidadeDisplay = $"{up.Per} {up.Nome}".Trim();

            // TIPO é [Required] no model -> mantém "" se vier vazio
            var tipoFinal = string.IsNullOrWhiteSpace(tipo) ? "" : tipo.Trim();

            var pat = new Patrimonio
            {
                Numero = numero,
                Descricao = desc.Trim(),
                Valor = valor,
                Tipo = tipoFinal,
                DataCompra = dataCompra,
                Unidade = string.IsNullOrWhiteSpace(unidadeDisplay) ? null : unidadeDisplay,
                Fornecedor = string.IsNullOrWhiteSpace(forn) ? null : forn.Trim(),
                NumeroSerieNF = string.IsNullOrWhiteSpace(serie) ? null : serie.Trim(),
                NumeroNF = string.IsNullOrWhiteSpace(nf) ? null : nf.Trim(),
                Status = string.IsNullOrWhiteSpace(status) ? "Ativo" : status,
                Condicao = string.IsNullOrWhiteSpace(cond) ? "Novo" : cond,
                CriadoPorId = ROOT_USER_ID,
                CriadoEm = DateTime.Now,
                ImagemPath = string.Empty
            };

            var tr = new Tramite
            {
                Patrimonio = pat,
                UsuarioId = ROOT_USER_ID,
                NomeUsuario = nomeUsuarioTramite,
                Tipo = "CRIACAO",
                DataHora = DateTime.Now
            };

            db.Patrimonios.Add(pat);
            db.Tramites.Add(tr);
            batch.Add((r, pat, tr));

            inserted++;

            if (inserted % BATCH_SIZE == 0)
                FlushBatch();
        }

        FlushBatch();

        // relatório
        var reportPath = ResolveReportPath(reportPathArg, excelPath);
        File.WriteAllLines(reportPath, reportLines, Encoding.UTF8);

        Console.WriteLine("🎉 Importação finalizada!");
        Console.WriteLine($"✅ Inseridos: {inserted - failed}");
        Console.WriteLine($"🟦 Linhas vazias (sem descrição): {emptyRows}");
        Console.WriteLine($"♻️ Duplicados (mesmo Nº Patrimonio): {duplicates}");
        Console.WriteLine($"❌ Falhas ao inserir: {failed}");
        Console.WriteLine($"📝 Relatório: {reportPath}");

        return 0;
    }

    // ================= helpers =================

    private static string BuildNomeUsuario(Usuario u)
    {
        var nome = $"{u.Nome} {u.Sobrenome}".Trim();
        if (!string.IsNullOrWhiteSpace(nome)) return nome;
        if (!string.IsNullOrWhiteSpace(u.Codigo)) return u.Codigo;
        return u.UserName ?? u.Email ?? "ROOT";
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine(@"  dotnet run -- ""C:\temp\patrimonios.xlsx"" --db ""..\PatriControl.Web\patricontrol.db""");
        Console.WriteLine();
        Console.WriteLine("Args:");
        Console.WriteLine("  [0]              Caminho do Excel (obrigatório)");
        Console.WriteLine("  --sheet          Nome da aba (opcional)");
        Console.WriteLine("  --db             Caminho do patricontrol.db (opcional)");
        Console.WriteLine("  --report         Caminho do relatório (opcional)");
        Console.WriteLine();
        Console.WriteLine("Obs:");
        Console.WriteLine("  O CriadoPorId SEMPRE será o ROOT (Id=1 / USR001).");
    }

    private static string ResolveDbPath(string? dbPath)
    {
        if (!string.IsNullOrWhiteSpace(dbPath))
            return Path.GetFullPath(dbPath);

        var here = Directory.GetCurrentDirectory();
        var try1 = Path.Combine(here, "patricontrol.db");
        if (File.Exists(try1)) return try1;

        var try2 = Path.GetFullPath(Path.Combine(here, "..", "PatriControl.Web", "patricontrol.db"));
        return try2;
    }

    private static string ResolveReportPath(string? reportArg, string excelPath)
    {
        if (!string.IsNullOrWhiteSpace(reportArg))
            return Path.GetFullPath(reportArg);

        var dir = Path.GetDirectoryName(excelPath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(excelPath);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(dir, $"{name}_ImportReport_{stamp}.txt");
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return (i + 1 < args.Length) ? args[i + 1] : null;
        }
        return null;
    }

    private static Dictionary<string, int> BuildColumnMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var cell in headerRow.CellsUsed())
        {
            var text = Norm(cell.GetString());
            if (!string.IsNullOrWhiteSpace(text))
                map[text] = cell.Address.ColumnNumber;
        }
        return map;
    }

    private static string GetString(IXLRow row, Dictionary<string, int> map, string colName)
    {
        var key = Norm(colName);
        if (!map.TryGetValue(key, out var col)) return "";

        var cell = row.Cell(col);
        if (cell.IsEmpty()) return "";

        var s = cell.GetString();
        if (!string.IsNullOrWhiteSpace(s)) return s.Trim();

        if (cell.DataType == XLDataType.Number)
            return cell.GetDouble().ToString(CultureInfo.InvariantCulture);

        return "";
    }

    private static decimal? GetDecimal(IXLRow row, Dictionary<string, int> map, string colName)
    {
        var key = Norm(colName);
        if (!map.TryGetValue(key, out var col)) return null;

        var cell = row.Cell(col);
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.Number)
            return (decimal)cell.GetDouble();

        var s = cell.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;

        s = s.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim();

        if (decimal.TryParse(s, NumberStyles.Number, new CultureInfo("pt-BR"), out var v))
            return v;

        return null;
    }

    private static DateTime? GetDate(IXLRow row, Dictionary<string, int> map, string colName)
    {
        var key = Norm(colName);
        if (!map.TryGetValue(key, out var col)) return null;

        var cell = row.Cell(col);
        if (cell.IsEmpty()) return null;

        if (cell.DataType == XLDataType.DateTime)
            return cell.GetDateTime();

        if (cell.DataType == XLDataType.Number)
        {
            var d = cell.GetDouble();
            try { return DateTime.FromOADate(d); } catch { return null; }
        }

        var s = cell.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (DateTime.TryParse(s, new CultureInfo("pt-BR"), DateTimeStyles.None, out var dt))
            return dt;

        return null;
    }

    private static (string Per, string Nome) ParseUnidade(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return ("", "");

        // "9921 - ESCRITORIO"
        var idx = s.IndexOf('-');
        if (idx >= 0)
        {
            var per = s[..idx].Trim();
            var nome = s[(idx + 1)..].Trim();
            return (per, nome);
        }

        var parts = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2) return (parts[0].Trim(), parts[1].Trim());

        return ("", s);
    }

    private static string NormalizeStatus(string? s)
    {
        var v = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v)) return "";
        var key = v.ToUpperInvariant();
        return StatusMap.TryGetValue(key, out var canon) ? canon : "";
    }

    private static string NormalizeCondicao(string? s)
    {
        var v = (s ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v)) return "";
        var key = v.ToUpperInvariant();
        return CondicaoMap.TryGetValue(key, out var canon) ? canon : "";
    }

    private static string Key(string? s) => (s ?? "").Trim();
    private static string Norm(string? s) => (s ?? "").Trim().ToUpperInvariant();

    private static string GetBestError(Exception ex)
    {
        if (ex is DbUpdateException dbu && dbu.InnerException != null)
            return dbu.InnerException.Message;
        return ex.Message;
    }

    // ========= FK fix: garante ROOT (Identity) =========
    private static Usuario EnsureUsuario(PatriControlContext db, int usuarioId)
    {
        var u = db.Usuarios.AsNoTracking().FirstOrDefault(x => x.Id == usuarioId);
        if (u != null) return u;

        throw new Exception(
            $"ROOT não existe no banco (AspNetUsers) com Id={usuarioId}. " +
            $"Rode o PatriControl.Web (que executa o IdentitySeeder) para criar o ROOT e tente novamente."
        );
    }
}

/*
COMANDO PRONTO (abrindo o terminal na pasta do projeto PatriControl.Tools):

dotnet run -- "C:\Users\User\Desktop\Patrimonios.xlsx" --db "..\PatriControl.Web\patricontrol.dev.db"

(opcional) especificar aba:
dotnet run -- "C:\Users\User\Desktop\Patrimonios.xlsx" --sheet "Planilha1" --db "..\PatriControl.Web\patricontrol.db"

(opcional) salvar relatório em caminho específico:
dotnet run -- "C:\Users\User\Desktop\Patrimonios.xlsx" --db "..\PatriControl.Web\patricontrol.db" --report "C:\Users\User\Desktop\RelatorioImport.txt"
*/
