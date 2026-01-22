using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using PatriControl.Web.Services;
using System.Globalization;
using System.Security.Claims;

namespace PatriControl.Web.Controllers
{
    [Authorize]
    public class ManutencoesController : Controller
    {
        private readonly PatriControlContext _context;
        private readonly IAuditLogger _audit;

        public ManutencoesController(PatriControlContext context, IAuditLogger audit)
        {
            _context = context;
            _audit = audit;
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

        // GET: /Manutencoes (COM PAGINAÇÃO - PADRÃO DO SISTEMA)
        public IActionResult Index(
            int? patrimonioId,
            int? usuarioId,
            string? patrimonio,
            string? usuario,
            int page = 1,
            int pageSize = 50)
        {
            // pageSize fixo (10 por coluna)
            pageSize = 10;
            if (page < 1) page = 1;

            // ===== Query base (sem Include) para filtros/contagens =====
            var queryBase = _context.Manutencoes
                .AsNoTracking()
                .AsQueryable();

            // Filtro por Patrimônio
            if (patrimonioId.HasValue && patrimonioId.Value > 0)
            {
                queryBase = queryBase.Where(m => m.PatrimonioId == patrimonioId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(patrimonio))
            {
                var t = patrimonio.Trim().ToLowerInvariant();
                queryBase = queryBase.Where(m =>
                    m.Patrimonio != null &&
                    (
                        (m.Patrimonio.Numero ?? "").ToLower().Contains(t) ||
                        (m.Patrimonio.Descricao ?? "").ToLower().Contains(t)
                    )
                );
            }

            // Filtro por Usuário (quem abriu)
            if (usuarioId.HasValue && usuarioId.Value > 0)
            {
                queryBase = queryBase.Where(m => m.AbertaPorId == usuarioId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(usuario))
            {
                var t = usuario.Trim().ToLowerInvariant();
                queryBase = queryBase.Where(m =>
                    m.AbertaPor != null &&
                    (
                        (m.AbertaPor.Codigo ?? "").ToLower().Contains(t) ||
                        (m.AbertaPor.Nome ?? "").ToLower().Contains(t) ||
                        (m.AbertaPor.Sobrenome ?? "").ToLower().Contains(t) ||
                        (m.AbertaPor.Email ?? "").ToLower().Contains(t) ||
                        (((m.AbertaPor.Nome ?? "") + " " + (m.AbertaPor.Sobrenome ?? "")).ToLower().Contains(t))
                    )
                );
            }

            // ===== 3 queries por status (já filtradas) =====
            var qEmAndamento = queryBase.Where(m => m.Status != "Finalizada" && m.Status != "Cancelada");
            var qFinalizadas = queryBase.Where(m => m.Status == "Finalizada");
            var qCanceladas = queryBase.Where(m => m.Status == "Cancelada");

            // ===== Totais por status (para o board) =====
            var emAndamentoTotal = qEmAndamento.Count();
            var finalizadasTotal = qFinalizadas.Count();
            var canceladasTotal = qCanceladas.Count();

            // Total geral (se você quiser exibir em algum lugar)
            var total = emAndamentoTotal + finalizadasTotal + canceladasTotal;

            // ===== TotalPages agora é o MAIOR entre os 3 (página por coluna) =====
            int Pages(int count) => (int)Math.Ceiling(count / (double)pageSize);
            var totalPages = new[] { Pages(emAndamentoTotal), Pages(finalizadasTotal), Pages(canceladasTotal) }.Max();
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            // ===== Helper Includes =====
            IQueryable<Manutencao> WithIncludes(IQueryable<Manutencao> q) => q
                .Include(m => m.Patrimonio)
                .Include(m => m.Manutentor)
                .Include(m => m.TipoManutencao)
                .Include(m => m.AbertaPor)
                .Include(m => m.FinalizadaPor);

            // ===== Pega ATÉ 10 de cada coluna na página atual =====
            var emAndamentoList = WithIncludes(qEmAndamento)
                .OrderByDescending(m => m.AbertaEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var finalizadasList = WithIncludes(qFinalizadas)
                .OrderByDescending(m => m.AbertaEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var canceladasList = WithIncludes(qCanceladas)
                .OrderByDescending(m => m.AbertaEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Model final: até 30 itens (10 por coluna)
            var lista = emAndamentoList
                .Concat(finalizadasList)
                .Concat(canceladasList)
                .ToList();

            // ===== Dados para filtros/modais =====
            ViewBag.Patrimonios = _context.Patrimonios
                .OrderBy(p => p.Numero)
                .ToList();

            ViewBag.Manutentores = _context.Manutentores
                .OrderBy(x => x.Nome)
                .ToList();

            ViewBag.Usuarios = _context.Usuarios
                .OrderBy(u => u.Codigo)
                .ToList();

            // ===== manter valores no filtro =====
            ViewBag.PatrimonioId = patrimonioId;
            ViewBag.UsuarioId = usuarioId;
            ViewBag.PatrimonioTexto = patrimonio ?? "";
            ViewBag.UsuarioTexto = usuario ?? "";

            // ===== Totais por status (board) =====
            ViewBag.EmAndamentoTotal = emAndamentoTotal;
            ViewBag.FinalizadasTotal = finalizadasTotal;
            ViewBag.CanceladasTotal = canceladasTotal;

            // ===== paginação (para a view) =====
            ViewBag.Total = total;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;

            // ===== PAGINAÇÃO (PADRÃO DO SISTEMA) =====
            var routeValues = new Dictionary<string, object?>();

            if (patrimonioId.HasValue && patrimonioId.Value > 0) routeValues["patrimonioId"] = patrimonioId.Value;
            if (usuarioId.HasValue && usuarioId.Value > 0) routeValues["usuarioId"] = usuarioId.Value;

            if (!string.IsNullOrWhiteSpace(patrimonio)) routeValues["patrimonio"] = patrimonio.Trim();
            if (!string.IsNullOrWhiteSpace(usuario)) routeValues["usuario"] = usuario.Trim();

            // "Total" aqui precisa bater com o totalPages (paginação por coluna),
            // então usamos o MAIOR total entre as colunas.
            var maiorTotal = new[] { emAndamentoTotal, finalizadasTotal, canceladasTotal }.Max();

            ViewBag.Paginacao = new PaginacaoViewModel
            {
                Page = page,
                PageSize = pageSize,
                Total = maiorTotal,
                TotalPages = totalPages,
                Action = nameof(Index),
                Controller = "Manutencoes",
                RouteValues = routeValues
            };

            var lastIdGlobal = _context.Manutencoes
                .AsNoTracking()
                .Max(m => (int?)m.Id) ?? 0;

            ViewBag.LastManutencaoId = lastIdGlobal;

            return View(lista);
        }

        // POST: /Manutencoes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(int patrimonioId, int tipoManutencaoId, int? manutentorId, decimal? custoEstimado)
        {
            var patrimonio = _context.Patrimonios.FirstOrDefault(p => p.Id == patrimonioId);
            if (patrimonio == null)
            {
                TempData["ErrorMessage"] = "Patrimônio não encontrado.";
                return RedirectToAction(nameof(Index));
            }

            var tipoManut = _context.TiposManutencao.FirstOrDefault(t => t.Id == tipoManutencaoId);
            if (tipoManut == null)
            {
                TempData["ErrorMessage"] = "Tipo de manutenção inválido.";
                return RedirectToAction(nameof(Index));
            }

            var existeEmAberto = _context.Manutencoes.Any(m =>
                m.PatrimonioId == patrimonioId &&
                m.Status != "Finalizada" &&
                m.Status != "Cancelada");

            if (existeEmAberto || string.Equals(patrimonio.Status?.Trim(), "Em manutenção", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Este patrimônio já possui uma manutenção em andamento.";
                return RedirectToAction(nameof(Index));
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out var userId);

            var nomeUsuario = User.Identity?.Name ?? "Desconhecido";
            var agora = DateTime.Now;

            var ultimoId = _context.Manutencoes
                .OrderByDescending(m => m.Id)
                .Select(m => m.Id)
                .FirstOrDefault();

            var codigo = $"#MAN{(ultimoId + 1):0000}";

            using var tx = _context.Database.BeginTransaction();
            try
            {
                var manut = new Manutencao
                {
                    Codigo = codigo,
                    PatrimonioId = patrimonio.Id,
                    Status = "Em andamento",
                    AbertaEm = agora,
                    AbertaPorId = userId,
                    TipoManutencaoId = tipoManutencaoId,
                    ManutentorId = manutentorId,
                    CustoEstimado = custoEstimado
                };

                _context.Manutencoes.Add(manut);

                var statusAnterior = patrimonio.Status ?? "";
                patrimonio.Status = "Em manutenção";

                _context.Tramites.Add(new Tramite
                {
                    PatrimonioId = patrimonio.Id,
                    UsuarioId = userId,
                    NomeUsuario = nomeUsuario,
                    Tipo = "ALTERACAO",
                    DataHora = agora,
                    Campo = "Status",
                    ValorAntigo = statusAnterior,
                    ValorNovo = "Em manutenção"
                });

                _context.SaveChanges();
                tx.Commit();

                var patrimonioInfo = $"{(patrimonio.Numero ?? "")} - {(patrimonio.Descricao ?? "")}".Trim();
                var detalhes = $"{codigo} | Patrimônio: {patrimonioInfo} | TipoManutencaoId={tipoManutencaoId} | ManutentorId={(manutentorId?.ToString() ?? "-")} | CustoEstimado={(custoEstimado?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-")} | PatrimônioStatus: {statusAnterior} -> Em manutenção";
                TryAudit(userId, "Abriu manutenção", "Manutencao", manut.Id, detalhes);

                TempData["SuccessMessage"] = "Manutenção criada com sucesso.";
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                tx.Rollback();
                TempData["ErrorMessage"] = "Erro ao criar manutenção.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: /Manutencoes/Cancelar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Cancelar(int id)
        {
            var manut = _context.Manutencoes
                .Include(m => m.Patrimonio)
                .FirstOrDefault(m => m.Id == id);

            if (manut == null)
            {
                TempData["ErrorMessage"] = "Manutenção não encontrada.";
                return RedirectToAction(nameof(Index));
            }

            if (manut.Status == "Finalizada" || manut.Status == "Cancelada")
            {
                TempData["ErrorMessage"] = "Esta manutenção já foi encerrada.";
                return RedirectToAction(nameof(Index));
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out var userId);

            var nomeUsuario = User.Identity?.Name ?? "Desconhecido";
            var agora = DateTime.Now;

            using var tx = _context.Database.BeginTransaction();
            try
            {
                var statusManutAnterior = manut.Status;

                manut.Status = "Cancelada";
                manut.FinalizadaEm = agora;
                manut.FinalizadaPorId = userId;

                string patrimonioStatusAnterior = "";
                string patrimonioInfo = "";

                if (manut.Patrimonio != null)
                {
                    patrimonioStatusAnterior = manut.Patrimonio.Status ?? "";
                    manut.Patrimonio.Status = "Ativo";

                    patrimonioInfo = $"{(manut.Patrimonio.Numero ?? "")} - {(manut.Patrimonio.Descricao ?? "")}".Trim();

                    _context.Tramites.Add(new Tramite
                    {
                        PatrimonioId = manut.Patrimonio.Id,
                        UsuarioId = userId,
                        NomeUsuario = nomeUsuario,
                        Tipo = "ALTERACAO",
                        DataHora = agora,
                        Campo = "Status",
                        ValorAntigo = patrimonioStatusAnterior,
                        ValorNovo = "Ativo"
                    });
                }

                _context.SaveChanges();
                tx.Commit();

                var detalhes = $"{manut.Codigo} | Status: {statusManutAnterior} -> Cancelada | Patrimônio: {patrimonioInfo} | PatrimônioStatus: {patrimonioStatusAnterior} -> Ativo";
                TryAudit(userId, "Cancelou manutenção", "Manutencao", manut.Id, detalhes);

                TempData["SuccessMessage"] = "Manutenção cancelada e patrimônio liberado.";
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                tx.Rollback();
                TempData["ErrorMessage"] = "Erro ao cancelar manutenção.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: /Manutencoes/Finalizar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Finalizar(int id, string? observacoesFinais, string? dataFinalizada, decimal? custoFinal, string statusFinalPatrimonio)
        {
            var manut = _context.Manutencoes
                .Include(m => m.Patrimonio)
                .FirstOrDefault(m => m.Id == id);

            if (manut == null)
            {
                TempData["ErrorMessage"] = "Manutenção não encontrada.";
                return RedirectToAction(nameof(Index));
            }

            if (manut.Status == "Finalizada" || manut.Status == "Cancelada")
            {
                TempData["ErrorMessage"] = "Esta manutenção já foi encerrada.";
                return RedirectToAction(nameof(Index));
            }

            if (statusFinalPatrimonio != "Ativo" && statusFinalPatrimonio != "Baixado")
            {
                TempData["ErrorMessage"] = "Status final do patrimônio inválido.";
                return RedirectToAction(nameof(Index));
            }

            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            int.TryParse(userIdStr, out var userId);

            var nomeUsuario = User.Identity?.Name ?? "Desconhecido";
            var agora = DateTime.Now;

            DateTime finalizadaEm = agora;

            if (!string.IsNullOrWhiteSpace(dataFinalizada))
            {
                if (DateTime.TryParseExact(dataFinalizada, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    finalizadaEm = dt.Date
                        .AddHours(agora.Hour)
                        .AddMinutes(agora.Minute)
                        .AddSeconds(agora.Second);
                }
            }

            using var tx = _context.Database.BeginTransaction();
            try
            {
                var statusManutAnterior = manut.Status;

                manut.Status = "Finalizada";
                manut.FinalizadaEm = finalizadaEm;
                manut.FinalizadaPorId = userId;

                manut.ObservacoesFinais = string.IsNullOrWhiteSpace(observacoesFinais)
                    ? null
                    : observacoesFinais.Trim();

                manut.CustoFinal = custoFinal;
                manut.StatusFinalPatrimonio = statusFinalPatrimonio;

                string patrimonioStatusAnterior = "";
                string patrimonioInfo = "";

                if (manut.Patrimonio != null)
                {
                    patrimonioStatusAnterior = manut.Patrimonio.Status ?? "";
                    manut.Patrimonio.Status = statusFinalPatrimonio;

                    patrimonioInfo = $"{(manut.Patrimonio.Numero ?? "")} - {(manut.Patrimonio.Descricao ?? "")}".Trim();

                    _context.Tramites.Add(new Tramite
                    {
                        PatrimonioId = manut.Patrimonio.Id,
                        UsuarioId = userId,
                        NomeUsuario = nomeUsuario,
                        Tipo = "ALTERACAO",
                        DataHora = agora,
                        Campo = "Status",
                        ValorAntigo = patrimonioStatusAnterior,
                        ValorNovo = statusFinalPatrimonio
                    });
                }

                _context.SaveChanges();
                tx.Commit();

                var detalhes = $"{manut.Codigo} | Status: {statusManutAnterior} -> Finalizada | FinalizadaEm={finalizadaEm:dd/MM/yyyy HH:mm} | CustoFinal={(custoFinal?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-")} | Patrimônio: {patrimonioInfo} | PatrimônioStatus: {patrimonioStatusAnterior} -> {statusFinalPatrimonio}";
                TryAudit(userId, "Finalizou manutenção", "Manutencao", manut.Id, detalhes);

                TempData["SuccessMessage"] = "Manutenção finalizada e patrimônio liberado.";
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                tx.Rollback();
                TempData["ErrorMessage"] = "Erro ao finalizar manutenção.";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}
