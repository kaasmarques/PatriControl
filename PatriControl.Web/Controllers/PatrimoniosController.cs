using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using PatriControl.Web.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;

namespace PatriControl.Web.Controllers
{
    [Authorize]
    public class PatrimoniosController : Controller
    {
        private readonly PatriControlContext _context;
        private readonly IAuditLogger _audit;

        // Create/Edit (NÃO inclui "Em manutenção")
        private static readonly string[] _statusOptions =
        {
            "Ativo",
            "Baixado",
            "Emprestado",
            "Aguardando Manutenção",
        };

        // Filtros / Export (inclui "Em manutenção")
        private static readonly string[] _statusFiltroOptions =
        {
            "Ativo",
            "Baixado",
            "Emprestado",
            "Aguardando Manutenção",
            "Em manutenção"
        };

        private static readonly string[] _condicaoOptions =
        {
            "Novo",
            "Semi-Novo",
            "Usado"
        };

        // HashSets p/ validação (case-insensitive)
        private static readonly HashSet<string> _statusSet =
            new HashSet<string>(_statusOptions, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _condicaoSet =
            new HashSet<string>(_condicaoOptions, StringComparer.OrdinalIgnoreCase);

        public PatrimoniosController(PatriControlContext context, IAuditLogger audit)
        {
            _context = context;
            _audit = audit;
        }

        // =========================================================
        // Helpers
        // =========================================================
        private int? GetUserId()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdStr, out var id) && id > 0) return id;
            return null;
        }

        private void TryAudit(int? usuarioId, string acao, string? entidade = null, int? entidadeId = null, string? detalhes = null)
        {
            try
            {
                _audit.LogAsync(usuarioId, acao, entidade, entidadeId, detalhes).GetAwaiter().GetResult();
            }
            catch
            {
                // auditoria nunca pode quebrar o fluxo principal
            }
        }

        private void CarregarDropdowns(
            string? statusFiltroSelecionado = null,
            string? condicaoFiltroSelecionada = null)
        {
            // Create/Edit
            ViewBag.StatusLista = _statusOptions
                .Select(s => new SelectListItem
                {
                    Value = s,
                    Text = s,
                    Selected = string.Equals(s, "Ativo", StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            ViewBag.CondicaoLista = _condicaoOptions
                .Select(c => new SelectListItem
                {
                    Value = c,
                    Text = c,
                    Selected = string.Equals(c, "Novo", StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            // Filtro
            var selStatus = (statusFiltroSelecionado ?? "").Trim();
            ViewBag.StatusFiltroLista = _statusFiltroOptions
                .Select(s => new SelectListItem
                {
                    Value = s,
                    Text = s,
                    Selected = !string.IsNullOrWhiteSpace(selStatus) &&
                               string.Equals(selStatus, s, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();

            var selCond = (condicaoFiltroSelecionada ?? "").Trim();
            ViewBag.CondicaoFiltroLista = _condicaoOptions
                .Select(c => new SelectListItem
                {
                    Value = c,
                    Text = c,
                    Selected = !string.IsNullOrWhiteSpace(selCond) &&
                               string.Equals(selCond, c, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
        }

        private void CarregarDadosModais()
        {
            // Estes ViewBags são usados pela sua view (comboboxes Create/Edit + filtro de unidade)
            ViewBag.Tipos = _context.TiposPatrimonio.AsNoTracking().OrderBy(t => t.Nome).ToList();
            ViewBag.Fornecedores = _context.Fornecedores.AsNoTracking().OrderBy(f => f.Nome).ToList();
            ViewBag.Unidades = _context.Unidades.AsNoTracking().OrderBy(u => u.Per).ThenBy(u => u.Nome).ToList();
            ViewBag.Localizacoes = _context.Localizacoes.AsNoTracking().OrderBy(l => l.Nome).ToList();
        }

        private void CarregarDadosExportacao()
        {
            // Names SEPARADOS para não sobrescrever os ViewBags usados pela view
            // Unidades: usar as mesmas strings que ficam em Patrimonio.Unidade (ex: "9921 Sede")
            ViewBag.ExportUnidades = _context.Patrimonios
                .AsNoTracking()
                .Select(p => p.Unidade)
                .Where(x => x != null && x.Trim() != "")
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewBag.ExportTipos = _context.Patrimonios
                .AsNoTracking()
                .Select(p => p.Tipo)
                .Where(x => x != null && x.Trim() != "")
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            ViewBag.ExportFornecedores = _context.Patrimonios
                .AsNoTracking()
                .Select(p => p.Fornecedor)
                .Where(x => x != null && x.Trim() != "")
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            // Status/Condição para export (com "(Todos)" você coloca na view)
            ViewBag.ExportStatus = _statusFiltroOptions.ToList();
            ViewBag.ExportCondicoes = _condicaoOptions.ToList();

            // Localizações dependentes por Unidade (baseado nos dados cadastrados)
            var locMap = _context.Patrimonios
                .AsNoTracking()
                .Where(p =>
                    p.Unidade != null && p.Unidade.Trim() != "" &&
                    p.Localizacao != null && p.Localizacao.Trim() != "")
                .GroupBy(p => p.Unidade!.Trim())
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Localizacao!.Trim())
                          .Distinct()
                          .OrderBy(x => x)
                          .ToList()
                );

            ViewBag.ExportLocalizacoesPorUnidade = locMap;
        }

        // =========================================================
        // INDEX (Listagem + Filtro + Paginação)
        // =========================================================
        [HttpGet]
        public IActionResult Index(
            string? numero,
            string? descricao,
            string? unidade,
            string? status,
            string? condicao,
            int page = 1,
            int pageSize = 20)
        {
            // pageSize fixo em 20
            pageSize = 20;
            if (page < 1) page = 1;

            // Dados para a view (modais/comboboxes)
            CarregarDadosModais();

            // Dropdowns (create/edit + filtros)
            CarregarDropdowns(status, condicao);

            // Dados para modal de exportação (nomes separados)
            CarregarDadosExportacao();

            var query = _context.Patrimonios
                .AsNoTracking()
                .AsQueryable();

            // Filtros
            if (!string.IsNullOrWhiteSpace(numero))
            {
                numero = numero.Trim();
                query = query.Where(p => p.Numero != null && EF.Functions.Like(p.Numero, $"%{numero}%"));
            }

            if (!string.IsNullOrWhiteSpace(descricao))
            {
                descricao = descricao.Trim();
                query = query.Where(p => p.Descricao != null && EF.Functions.Like(p.Descricao, $"%{descricao}%"));
            }

            if (!string.IsNullOrWhiteSpace(unidade))
            {
                unidade = unidade.Trim();
                query = query.Where(p => p.Unidade != null && p.Unidade == unidade);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                status = status.Trim();
                query = query.Where(p => p.Status != null && p.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(condicao))
            {
                condicao = condicao.Trim();
                query = query.Where(p => p.Condicao != null && p.Condicao == condicao);
            }

            var total = query.Count();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var patrimonios = query
                .OrderByDescending(p => p.CriadoEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Paginação + filtros preservados
            ViewBag.Total = total;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;

            ViewBag.FiltroNumero = numero ?? "";
            ViewBag.FiltroDescricao = descricao ?? "";
            ViewBag.FiltroUnidade = unidade ?? "";
            ViewBag.FiltroStatus = status ?? "";
            ViewBag.FiltroCondicao = condicao ?? "";

            return View(patrimonios);
        }

        // =========================================================
        // EXPORT EXCEL (POST)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ExportExcel(string? unidade, string? localizacao, string? tipo, string? fornecedor, string? status, string? condicao)
        {
            var q = _context.Patrimonios.AsNoTracking().AsQueryable();

            bool Has(string? s) => !string.IsNullOrWhiteSpace(s);

            if (Has(unidade)) q = q.Where(p => p.Unidade != null && p.Unidade.Trim() == unidade!.Trim());
            if (Has(tipo)) q = q.Where(p => p.Tipo != null && p.Tipo.Trim() == tipo!.Trim());
            if (Has(fornecedor)) q = q.Where(p => p.Fornecedor != null && p.Fornecedor.Trim() == fornecedor!.Trim());
            if (Has(status)) q = q.Where(p => p.Status != null && p.Status.Trim() == status!.Trim());
            if (Has(condicao)) q = q.Where(p => p.Condicao != null && p.Condicao.Trim() == condicao!.Trim());

            // Localização só faz sentido se Unidade vier selecionada (dependente)
            if (Has(unidade) && Has(localizacao))
                q = q.Where(p => p.Localizacao != null && p.Localizacao.Trim() == localizacao!.Trim());

            var lista = q
                .OrderBy(p => p.Numero)
                .ToList();

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Patrimonios");

            var headers = new[]
            {
                "N° Patrimonio", "Descrição", "Unidade", "Localização", "Tipo", "Fornecedor",
                "N° Série NF", "N° NF", "Valor", "Data de compra", "Status", "Condição"
            };

            for (int i = 0; i < headers.Length; i++)
                ws.Cell(1, i + 1).Value = headers[i];

            ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
            ws.SheetView.FreezeRows(1);

            var row = 2;
            foreach (var p in lista)
            {
                ws.Cell(row, 1).Value = p.Numero ?? "";
                ws.Cell(row, 2).Value = p.Descricao ?? "";
                ws.Cell(row, 3).Value = p.Unidade ?? "";
                ws.Cell(row, 4).Value = p.Localizacao ?? "";
                ws.Cell(row, 5).Value = p.Tipo ?? "";
                ws.Cell(row, 6).Value = p.Fornecedor ?? "";
                ws.Cell(row, 7).Value = p.NumeroSerieNF ?? "";
                ws.Cell(row, 8).Value = p.NumeroNF ?? "";

                ws.Cell(row, 9).Value = p.Valor ?? 0m;
                ws.Cell(row, 10).Value = p.DataCompra.HasValue ? p.DataCompra.Value : (DateTime?)null;

                ws.Cell(row, 11).Value = p.Status ?? "";
                ws.Cell(row, 12).Value = p.Condicao ?? "";
                row++;
            }

            // Formatação
            ws.Column(9).Style.NumberFormat.Format = "#,##0.00";
            ws.Column(10).Style.DateFormat.Format = "dd/MM/yyyy";
            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            wb.SaveAs(stream);
            var bytes = stream.ToArray();

            var fileName = $"Patrimonios_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // =========================================================
        // CREATE (modal)
        // =========================================================
        public IActionResult Create()
        {
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Patrimonio patrimonio)
        {
            var uid = GetUserId();

            // Valor (pt-BR)
            var valorTextoCreate = Request.Form["ValorTexto"].ToString();
            if (!string.IsNullOrWhiteSpace(valorTextoCreate))
            {
                if (decimal.TryParse(valorTextoCreate, NumberStyles.Number, new CultureInfo("pt-BR"), out var valorDecimal))
                    patrimonio.Valor = valorDecimal;
            }
            else
            {
                patrimonio.Valor = null;
            }

            // Número opcional
            var numero = (patrimonio.Numero ?? "").Trim();
            patrimonio.Numero = string.IsNullOrWhiteSpace(numero) ? null : numero;

            // Se preencher, não pode duplicar
            if (!string.IsNullOrWhiteSpace(patrimonio.Numero))
            {
                if (_context.Patrimonios.Any(p => p.Numero == patrimonio.Numero))
                {
                    TryAudit(uid, "Tentou criar patrimônio (falhou)", "Patrimonio", null, $"Número duplicado: {patrimonio.Numero}");
                    TempData["ErrorMessage"] = "Já existe um patrimônio com este número.";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Bloqueia "Em manutenção"
            if (!string.IsNullOrWhiteSpace(patrimonio.Status) &&
                string.Equals(patrimonio.Status.Trim(), "Em manutenção", StringComparison.OrdinalIgnoreCase))
            {
                TryAudit(uid, "Tentou criar patrimônio (falhou)", "Patrimonio", null, "Tentou setar status 'Em manutenção'.");
                TempData["ErrorMessage"] = "O status 'Em manutenção' só é definido automaticamente ao abrir uma manutenção.";
                return RedirectToAction(nameof(Index));
            }

            // Status válido
            if (string.IsNullOrWhiteSpace(patrimonio.Status))
                patrimonio.Status = "Ativo";
            else if (!_statusSet.Contains(patrimonio.Status.Trim()))
            {
                TryAudit(uid, "Tentou criar patrimônio (falhou)", "Patrimonio", null, $"Status inválido: {patrimonio.Status}");
                TempData["ErrorMessage"] = "Status inválido.";
                return RedirectToAction(nameof(Index));
            }
            else
                patrimonio.Status = patrimonio.Status.Trim();

            // Condição válida
            if (string.IsNullOrWhiteSpace(patrimonio.Condicao))
                patrimonio.Condicao = "Novo";
            else if (!_condicaoSet.Contains(patrimonio.Condicao.Trim()))
            {
                TryAudit(uid, "Tentou criar patrimônio (falhou)", "Patrimonio", null, $"Condição inválida: {patrimonio.Condicao}");
                TempData["ErrorMessage"] = "Condição inválida.";
                return RedirectToAction(nameof(Index));
            }
            else
                patrimonio.Condicao = patrimonio.Condicao.Trim();

            if (!ModelState.IsValid)
            {
                TryAudit(uid, "Tentou criar patrimônio (falhou)", "Patrimonio", null, "ModelState inválido.");
                TempData["ErrorMessage"] = "Não foi possível criar. Verifique os campos obrigatórios.";
                return RedirectToAction(nameof(Index));
            }

            patrimonio.CriadoEm = DateTime.Now;

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdClaim, out var criadoPorId);
            patrimonio.CriadoPorId = criadoPorId;

            patrimonio.ImagemPath ??= string.Empty;

            var nomeUsuarioCriacao = User.Identity?.Name ?? "Desconhecido";

            _context.Patrimonios.Add(patrimonio);

            _context.Tramites.Add(new Tramite
            {
                Patrimonio = patrimonio,
                UsuarioId = criadoPorId,
                NomeUsuario = nomeUsuarioCriacao,
                Tipo = "CRIACAO",
                DataHora = DateTime.Now
            });

            _context.SaveChanges();

            TryAudit(uid, "Criou patrimônio", "Patrimonio", patrimonio.Id,
                $"Numero={patrimonio.Numero ?? ""} | Descricao={patrimonio.Descricao ?? ""}");

            TempData["SuccessMessage"] = "Patrimônio criado com sucesso.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // EDIT (modal)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(Patrimonio patrimonio)
        {
            var uid = GetUserId();

            // Valor (pt-BR)
            var valorTextoEdit = Request.Form["ValorTexto"].ToString();
            if (!string.IsNullOrWhiteSpace(valorTextoEdit))
            {
                if (decimal.TryParse(valorTextoEdit, NumberStyles.Number, new CultureInfo("pt-BR"), out var valorDecimal))
                    patrimonio.Valor = valorDecimal;
            }
            else
            {
                patrimonio.Valor = null;
            }

            var existente = _context.Patrimonios.FirstOrDefault(p => p.Id == patrimonio.Id);
            if (existente == null)
            {
                TryAudit(uid, "Tentou editar patrimônio (falhou)", "Patrimonio", patrimonio.Id, "Patrimônio não encontrado.");
                return NotFound();
            }

            // bloqueia se estiver em manutenção
            if (string.Equals(existente.Status?.Trim(), "Em manutenção", StringComparison.OrdinalIgnoreCase))
            {
                TryAudit(uid, "Tentou editar patrimônio (falhou)", "Patrimonio", existente.Id, "Patrimônio em manutenção (bloqueado).");
                TempData["ErrorMessage"] = "Este patrimônio está em manutenção e não pode ser editado.";
                return RedirectToAction(nameof(Index));
            }

            // Número opcional + único (exceto ele mesmo)
            var numero = (patrimonio.Numero ?? "").Trim();
            patrimonio.Numero = string.IsNullOrWhiteSpace(numero) ? null : numero;

            if (!string.IsNullOrWhiteSpace(patrimonio.Numero))
            {
                var duplicado = _context.Patrimonios.Any(p => p.Id != existente.Id && p.Numero == patrimonio.Numero);
                if (duplicado)
                {
                    TryAudit(uid, "Tentou editar patrimônio (falhou)", "Patrimonio", existente.Id, $"Número duplicado: {patrimonio.Numero}");
                    TempData["ErrorMessage"] = "Já existe outro patrimônio com este número.";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Status valida + bloqueia "Em manutenção"
            var statusNovo = string.IsNullOrWhiteSpace(patrimonio.Status) ? "Ativo" : patrimonio.Status.Trim();

            if (string.Equals(statusNovo, "Em manutenção", StringComparison.OrdinalIgnoreCase))
            {
                TryAudit(uid, "Tentou editar patrimônio (falhou)", "Patrimonio", existente.Id, "Tentou setar status 'Em manutenção'.");
                TempData["ErrorMessage"] = "O status 'Em manutenção' só é definido automaticamente ao abrir uma manutenção.";
                return RedirectToAction(nameof(Index));
            }

            if (!_statusSet.Contains(statusNovo))
                statusNovo = existente.Status ?? "Ativo";

            // Condição valida
            var condicaoNova = string.IsNullOrWhiteSpace(patrimonio.Condicao) ? "Novo" : patrimonio.Condicao.Trim();
            if (!_condicaoSet.Contains(condicaoNova))
                condicaoNova = existente.Condicao ?? "Novo";

            // Trâmites
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out var usuarioId);

            var nomeUsuario = User.Identity?.Name ?? "Desconhecido";
            var agora = DateTime.Now;

            var alteracoes = new List<Tramite>();

            void RegistrarAlteracao(string campo, string? antigo, string? novo)
            {
                antigo ??= string.Empty;
                novo ??= string.Empty;
                if (antigo == novo) return;

                alteracoes.Add(new Tramite
                {
                    PatrimonioId = existente.Id,
                    UsuarioId = usuarioId,
                    NomeUsuario = nomeUsuario,
                    Tipo = "ALTERACAO",
                    Campo = campo,
                    ValorAntigo = antigo,
                    ValorNovo = novo,
                    DataHora = agora
                });
            }

            void RegistrarAlteracaoDecimal(string campo, decimal? antigo, decimal? novo)
            {
                if (antigo == novo) return;

                var culture = new CultureInfo("pt-BR");
                var antigoStr = antigo.HasValue ? antigo.Value.ToString("N2", culture) : "";
                var novoStr = novo.HasValue ? novo.Value.ToString("N2", culture) : "";
                RegistrarAlteracao(campo, antigoStr, novoStr);
            }

            RegistrarAlteracao("Número do Patrimônio", existente.Numero, patrimonio.Numero);
            RegistrarAlteracao("Descrição", existente.Descricao, patrimonio.Descricao);
            RegistrarAlteracao("Unidade", existente.Unidade, patrimonio.Unidade);
            RegistrarAlteracao("Localização", existente.Localizacao, patrimonio.Localizacao);
            RegistrarAlteracao("Tipo", existente.Tipo, patrimonio.Tipo);
            RegistrarAlteracao("Fornecedor", existente.Fornecedor, patrimonio.Fornecedor);
            RegistrarAlteracao("Nº Série NF", existente.NumeroSerieNF, patrimonio.NumeroSerieNF);
            RegistrarAlteracao("Nº NF", existente.NumeroNF, patrimonio.NumeroNF);
            RegistrarAlteracaoDecimal("Valor", existente.Valor, patrimonio.Valor);
            RegistrarAlteracao("Data de Compra",
                existente.DataCompra?.ToString("dd/MM/yyyy"),
                patrimonio.DataCompra?.ToString("dd/MM/yyyy"));

            RegistrarAlteracao("Status", existente.Status, statusNovo);
            RegistrarAlteracao("Condição", existente.Condicao, condicaoNova);

            // Atualiza
            existente.Numero = patrimonio.Numero;
            existente.Descricao = patrimonio.Descricao;
            existente.Unidade = patrimonio.Unidade;
            existente.Localizacao = patrimonio.Localizacao;
            existente.Tipo = patrimonio.Tipo;
            existente.Fornecedor = patrimonio.Fornecedor;
            existente.NumeroSerieNF = patrimonio.NumeroSerieNF;
            existente.NumeroNF = patrimonio.NumeroNF;
            existente.Valor = patrimonio.Valor;
            existente.DataCompra = patrimonio.DataCompra;
            existente.Status = statusNovo;
            existente.Condicao = condicaoNova;

            if (alteracoes.Any())
                _context.Tramites.AddRange(alteracoes);

            _context.SaveChanges();

            if (alteracoes.Any())
            {
                var detalhes = string.Join(" | ", alteracoes.Select(a => $"{a.Campo}: '{a.ValorAntigo}' -> '{a.ValorNovo}'"));
                TryAudit(uid, "Editou patrimônio", "Patrimonio", existente.Id, detalhes);
            }
            else
            {
                TryAudit(uid, "Editou patrimônio (sem alterações)", "Patrimonio", existente.Id, "Nenhuma alteração detectada.");
            }

            TempData["SuccessMessage"] = "Patrimônio atualizado com sucesso.";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // TRÂMITE PERSONALIZADO (AJAX)
        // =========================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AdicionarTramitePersonalizado(int patrimonioId, string texto)
        {
            var uid = GetUserId();

            texto = (texto ?? "").Trim();
            if (patrimonioId <= 0)
                return Json(new { ok = false, message = "Patrimônio inválido." });

            if (string.IsNullOrWhiteSpace(texto))
                return Json(new { ok = false, message = "Informe o trâmite." });

            var existe = _context.Patrimonios.AsNoTracking().Any(p => p.Id == patrimonioId);
            if (!existe)
            {
                TryAudit(uid, "Tentou adicionar trâmite personalizado (falhou)", "Patrimonio", patrimonioId, "Patrimônio não encontrado.");
                return Json(new { ok = false, message = "Patrimônio não encontrado." });
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out var usuarioId);

            var nomeUsuario = User.Identity?.Name ?? "Desconhecido";
            var agora = DateTime.Now;

            _context.Tramites.Add(new Tramite
            {
                PatrimonioId = patrimonioId,
                UsuarioId = usuarioId,
                NomeUsuario = nomeUsuario,
                Tipo = "PERSONALIZADO",
                Campo = "Trâmite personalizado",
                ValorNovo = texto,
                DataHora = agora
            });

            _context.SaveChanges();

            TryAudit(uid, "Adicionou trâmite personalizado", "Patrimonio", patrimonioId, $"Texto='{texto}'");

            return Json(new { ok = true });
        }

        // =========================================================
        // TRÂMITES (AJAX)
        // =========================================================
        [HttpGet]
        public IActionResult Tramites(int id)
        {
            var tramites = _context.Tramites
                .Where(t => t.PatrimonioId == id)
                .OrderByDescending(t => t.DataHora)
                .ThenByDescending(t => t.Id)
                .ToList();

            return Json(tramites);
        }
    }
}
