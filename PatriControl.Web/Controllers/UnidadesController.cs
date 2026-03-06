using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using PatriControl.Web.Services;
using System.Security.Claims;

namespace PatriControl.Web.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class UnidadesController : Controller
    {
        private readonly PatriControlContext _context;
        private readonly IAuditLogger _audit;

        public UnidadesController(PatriControlContext context, IAuditLogger audit)
        {
            _context = context;
            _audit = audit;
        }

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

        private bool IsAjax()
            => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        private object ListarLocalizacoes(int unidadeId)
        {
            return _context.Localizacoes
                .AsNoTracking()
                .Where(l => l.UnidadeId == unidadeId)
                .OrderBy(l => l.Nome)
                .Select(l => new { id = l.Id, nome = l.Nome })
                .ToList();
        }

        // ============================================================
        // LISTAGEM + FILTRO + PAGINAÇÃO
        // ============================================================
        public IActionResult Index(string? filtro, int page = 1, int pageSize = 10)
        {
            pageSize = 10;
            if (page < 1) page = 1;

            var queryBase = _context.Unidades
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                var f = filtro.Trim().ToLower();
                queryBase = queryBase.Where(u =>
                    u.Per.ToLower().Contains(f) ||
                    u.Nome.ToLower().Contains(f));
            }

            var total = queryBase.Count();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var lista = queryBase
                .Include(u => u.Localizacoes)
                .OrderBy(u => u.Per)
                .ThenBy(u => u.Nome)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.Filtro = filtro ?? "";
            ViewBag.Total = total;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalPages = totalPages;
            ViewBag.Exibindo = lista.Count;

            var routeValues = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(filtro)) routeValues["filtro"] = filtro.Trim();

            ViewBag.Paginacao = new PaginacaoViewModel
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                TotalPages = totalPages,
                Action = nameof(Index),
                Controller = "Unidades",
                RouteValues = routeValues
            };

            return View(lista);
        }

        // ============================================================
        // ENDPOINT JSON: listar localizações da unidade
        // ============================================================
        [HttpGet]
        public IActionResult Localizacoes(int unidadeId)
        {
            var unidadeExiste = _context.Unidades.AsNoTracking().Any(u => u.Id == unidadeId);
            if (!unidadeExiste)
            {
                TryAudit(GetUserId(), "Tentou listar localizações (falhou)", "Unidade", unidadeId, "Unidade não encontrada.");
                return NotFound();
            }

            var lista = ListarLocalizacoes(unidadeId);
            return Json(new { ok = true, localizacoes = lista });
        }

        // ============================================================
        // CRIAR UNIDADE (via modal)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string Per, string Nome)
        {
            var uid = GetUserId();

            Per = (Per ?? "").Trim();
            Nome = (Nome ?? "").Trim();

            if (string.IsNullOrWhiteSpace(Per) || string.IsNullOrWhiteSpace(Nome))
            {
                TryAudit(uid, "Tentou criar unidade (falhou)", "Unidade", null, $"Per='{Per}' Nome='{Nome}' (campos obrigatórios vazios).");
                return RedirectToAction(nameof(Index));
            }

            var unidade = new Unidade { Per = Per, Nome = Nome };
            _context.Unidades.Add(unidade);
            _context.SaveChanges();

            TryAudit(uid, "Criou unidade", "Unidade", unidade.Id, $"Per='{Per}' | Nome='{Nome}'");

            TempData["SuccessMessage"] = "Unidade criada com sucesso.";
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // EDITAR UNIDADE
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, string Per, string Nome)
        {
            var uid = GetUserId();

            var unidade = _context.Unidades.FirstOrDefault(u => u.Id == id);
            if (unidade == null)
            {
                TryAudit(uid, "Tentou editar unidade (falhou)", "Unidade", id, "Unidade não encontrada.");
                return NotFound();
            }

            var perNovo = (Per ?? "").Trim();
            var nomeNovo = (Nome ?? "").Trim();

            var unidadeAntiga = $"{unidade.Per} {unidade.Nome}".Trim();
            var unidadeNova = $"{perNovo} {nomeNovo}".Trim();

            var perAntigo = unidade.Per ?? "";
            var nomeAntigo = unidade.Nome ?? "";

            unidade.Per = perNovo;
            unidade.Nome = nomeNovo;

            var patrimonios = _context.Patrimonios
                .Where(p => p.Unidade == unidadeAntiga)
                .ToList();

            foreach (var p in patrimonios)
                p.Unidade = unidadeNova;

            _context.SaveChanges();

            TryAudit(
                uid,
                "Editou unidade",
                "Unidade",
                unidade.Id,
                $"Per: '{perAntigo}' -> '{perNovo}' | Nome: '{nomeAntigo}' -> '{nomeNovo}' | Propagou para patrimônios: {patrimonios.Count}"
            );

            TempData["SuccessMessage"] = "Unidade alterada com sucesso.";
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // ADICIONAR LOCALIZAÇÃO (AJAX-friendly)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AdicionarLocalizacao(int unidadeId, string nome)
        {
            var uid = GetUserId();

            nome = (nome ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou adicionar localização (falhou)", "Localizacao", null, $"UnidadeId={unidadeId} | Nome vazio.");
                if (IsAjax()) return Json(new { ok = false, message = "Informe o nome da localização." });
                return RedirectToAction(nameof(Index));
            }

            var unidade = _context.Unidades.FirstOrDefault(u => u.Id == unidadeId);
            if (unidade == null)
            {
                TryAudit(uid, "Tentou adicionar localização (falhou)", "Unidade", unidadeId, "Unidade não encontrada.");
                if (IsAjax()) return Json(new { ok = false, message = "Unidade não encontrada." });
                return NotFound();
            }

            var loc = new Localizacao { UnidadeId = unidadeId, Nome = nome };
            _context.Localizacoes.Add(loc);
            _context.SaveChanges();

            TryAudit(uid, "Adicionou localização", "Localizacao", loc.Id, $"UnidadeId={unidadeId} | Nome='{nome}'");

            if (IsAjax())
            {
                var lista = ListarLocalizacoes(unidadeId);
                return Json(new { ok = true, localizacoes = lista });
            }

            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // EDITAR LOCALIZAÇÃO (AJAX-friendly)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult EditarLocalizacao(int id, string nome)
        {
            var uid = GetUserId();

            nome = (nome ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou editar localização (falhou)", "Localizacao", id, "Nome vazio.");
                if (IsAjax()) return Json(new { ok = false, message = "Informe o nome da localização." });
                return RedirectToAction(nameof(Index));
            }

            var localizacao = _context.Localizacoes
                .Include(l => l.Unidade)
                .FirstOrDefault(l => l.Id == id);

            if (localizacao == null)
            {
                TryAudit(uid, "Tentou editar localização (falhou)", "Localizacao", id, "Localização não encontrada.");
                if (IsAjax()) return Json(new { ok = false, message = "Localização não encontrada." });
                return NotFound();
            }

            var nomeAntigo = localizacao.Nome ?? "";
            var unidadeTexto = $"{localizacao.Unidade?.Per} {localizacao.Unidade?.Nome}".Trim();

            localizacao.Nome = nome;

            var patrimonios = _context.Patrimonios
                .Where(p => p.Unidade == unidadeTexto && p.Localizacao == nomeAntigo)
                .ToList();

            foreach (var p in patrimonios)
                p.Localizacao = nome;

            _context.SaveChanges();

            TryAudit(
                uid,
                "Editou localização",
                "Localizacao",
                localizacao.Id,
                $"UnidadeId={localizacao.UnidadeId} | Nome: '{nomeAntigo}' -> '{nome}' | Propagou para patrimônios: {patrimonios.Count}"
            );

            if (IsAjax())
            {
                var lista = ListarLocalizacoes(localizacao.UnidadeId);
                return Json(new { ok = true, localizacoes = lista });
            }

            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // EXCLUIR LOCALIZAÇÃO (AJAX-friendly)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ExcluirLocalizacao(int id)
        {
            var uid = GetUserId();

            var localizacao = _context.Localizacoes
                .Include(l => l.Unidade)
                .FirstOrDefault(l => l.Id == id);

            if (localizacao == null)
            {
                TryAudit(uid, "Tentou excluir localização (falhou)", "Localizacao", id, "Localização não encontrada.");
                if (IsAjax()) return Json(new { ok = false, message = "Localização não encontrada." });
                return NotFound();
            }

            var nomeAntigo = localizacao.Nome ?? "";
            var unidadeTexto = $"{localizacao.Unidade?.Per} {localizacao.Unidade?.Nome}".Trim();
            var unidadeId = localizacao.UnidadeId;

            var patrimonios = _context.Patrimonios
                .Where(p => p.Unidade == unidadeTexto && p.Localizacao == nomeAntigo)
                .ToList();

            foreach (var p in patrimonios)
                p.Localizacao = null;

            _context.Localizacoes.Remove(localizacao);
            _context.SaveChanges();

            TryAudit(
                uid,
                "Excluiu localização",
                "Localizacao",
                id,
                $"UnidadeId={unidadeId} | Nome='{nomeAntigo}' | Limpou em patrimônios: {patrimonios.Count}"
            );

            if (IsAjax())
            {
                var lista = ListarLocalizacoes(unidadeId);
                return Json(new { ok = true, localizacoes = lista });
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
