using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using PatriControl.Web.Services;
using System.Security.Claims;

namespace PatriControl.Web.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class FornecedoresController : Controller
    {
        private readonly PatriControlContext _context;
        private readonly IAuditLogger _audit;

        public FornecedoresController(PatriControlContext context, IAuditLogger audit)
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

        // ---------------------------------------------------------
        // LISTAGEM + FILTRO (SERVER SIDE) + PAGINAÇÃO
        // ---------------------------------------------------------
        public IActionResult Index(string? filtro, int page = 1, int pageSize = 10)
        {
            // pageSize fixo (padrão do sistema)
            pageSize = 10;
            if (page < 1) page = 1;

            var queryBase = _context.Fornecedores
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                filtro = filtro.Trim();
                var fLower = filtro.ToLower();

                queryBase = queryBase.Where(x => (x.Nome ?? "").ToLower().Contains(fLower));
            }

            var total = queryBase.Count();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var lista = queryBase
                .OrderBy(x => x.Nome)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.Filtro = filtro ?? "";
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = totalPages;
            ViewBag.Exibindo = lista.Count;

            // ===== PAGINAÇÃO (PADRÃO DO SISTEMA) =====
            var routeValues = new Dictionary<string, object?>();

            if (!string.IsNullOrWhiteSpace(filtro))
                routeValues["filtro"] = filtro.Trim();

            ViewBag.Paginacao = new PaginacaoViewModel
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                TotalPages = totalPages,
                Action = nameof(Index),
                Controller = "Fornecedores",
                RouteValues = routeValues
            };

            return View(lista);
        }

        // ---------------------------------------------------------
        // CRIAR
        // ---------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string nome)
        {
            var uid = GetUserId();

            nome = (nome ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou criar fornecedor (falhou)", "Fornecedor", null, "Nome vazio/nulo.");
                TempData["ErrorMessage"] = "Informe o nome do fornecedor.";
                return RedirectToAction(nameof(Index));
            }

            var nomeLower = nome.ToLower();

            // Verifica duplicidade (case insensitive)
            var existe = _context.Fornecedores.Any(f => (f.Nome ?? "").ToLower() == nomeLower);
            if (existe)
            {
                TryAudit(uid, "Tentou criar fornecedor (falhou)", "Fornecedor", null, $"Duplicado: {nome}");
                TempData["ErrorMessage"] = "Já existe um fornecedor com esse nome.";
                return RedirectToAction(nameof(Index));
            }

            var fornecedor = new Fornecedor { Nome = nome };
            _context.Fornecedores.Add(fornecedor);
            _context.SaveChanges();

            // AUDIT (sucesso)
            TryAudit(uid, "Criou fornecedor", "Fornecedor", fornecedor.Id, $"Nome={nome}");

            TempData["SuccessMessage"] = "Fornecedor criado com sucesso.";
            return RedirectToAction(nameof(Index));
        }

        // ---------------------------------------------------------
        // EDITAR
        // ---------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, string nome)
        {
            var uid = GetUserId();

            nome = (nome ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou editar fornecedor (falhou)", "Fornecedor", id, "Nome vazio/nulo.");
                TempData["ErrorMessage"] = "Informe o nome do fornecedor.";
                return RedirectToAction(nameof(Index));
            }

            var fornecedor = _context.Fornecedores.FirstOrDefault(f => f.Id == id);
            if (fornecedor == null)
            {
                TryAudit(uid, "Tentou editar fornecedor (falhou)", "Fornecedor", id, "Fornecedor não encontrado.");
                TempData["ErrorMessage"] = "Fornecedor não encontrado.";
                return RedirectToAction(nameof(Index));
            }

            var nomeLower = nome.ToLower();

            // Se está renomeando para um nome já existente → bloqueia
            var duplicado = _context.Fornecedores.Any(f =>
                f.Id != id && (f.Nome ?? "").ToLower() == nomeLower);

            if (duplicado)
            {
                TryAudit(uid, "Tentou editar fornecedor (falhou)", "Fornecedor", id, $"Duplicado: {nome}");
                TempData["ErrorMessage"] = "Já existe outro fornecedor com esse nome.";
                return RedirectToAction(nameof(Index));
            }

            var nomeAntigo = fornecedor.Nome ?? "";
            var nomeAntigoLower = nomeAntigo.ToLower();

            fornecedor.Nome = nome;

            // Atualiza patrimônios que referenciavam o fornecedor (case-insensitive)
            var patrimonios = _context.Patrimonios
                .Where(p => p.Fornecedor != null && p.Fornecedor.ToLower() == nomeAntigoLower)
                .ToList();

            foreach (var p in patrimonios)
                p.Fornecedor = nome;

            _context.SaveChanges();

            // AUDIT (sucesso)
            var detalhes = $"Id={id} | Nome: {nomeAntigo} -> {nome} | PatrimoniosAtualizados={patrimonios.Count}";
            TryAudit(uid, "Editou fornecedor", "Fornecedor", id, detalhes);

            TempData["SuccessMessage"] = "Fornecedor alterado com sucesso.";
            return RedirectToAction(nameof(Index));
        }
    }
}
