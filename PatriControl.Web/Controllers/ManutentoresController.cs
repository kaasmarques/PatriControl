using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using PatriControl.Web.Services;
using System.Security.Claims;

namespace PatriControl.Web.Controllers
{
    [Authorize]
    public class ManutentoresController : Controller
    {
        private readonly PatriControlContext _context;
        private readonly IAuditLogger _audit;

        public ManutentoresController(PatriControlContext context, IAuditLogger audit)
        {
            _context = context;
            _audit = audit;
        }

        private bool UsuarioEhAdmin()
        {
            return User?.Claims.FirstOrDefault(c => c.Type == "Administrador")?.Value == "True";
        }

        private IActionResult AcessoNegado()
        {
            return RedirectToAction("AcessoNegado", "Account");
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

        // ============================================================
        // LISTAGEM + FILTRO (SERVER SIDE) + PAGINAÇÃO
        // ============================================================
        public IActionResult Index(string? filtro, int page = 1, int pageSize = 10)
        {
            if (!UsuarioEhAdmin())
                return AcessoNegado();

            // pageSize fixo (padrão do sistema)
            pageSize = 10;
            if (page < 1) page = 1;

            var queryBase = _context.Manutentores
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                filtro = filtro.Trim();
                queryBase = queryBase.Where(m => m.Nome.Contains(filtro));
            }

            var total = queryBase.Count();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var lista = queryBase
                .OrderBy(m => m.Nome)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.Filtro = filtro ?? "";
            ViewBag.Total = total;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
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
                Controller = "Manutentores",
                RouteValues = routeValues
            };

            return View(lista);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string nome)
        {
            if (!UsuarioEhAdmin())
                return AcessoNegado();

            var uid = GetUserId();

            nome = (nome ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou criar manutentor (falhou)", "Manutentor", null, "Nome vazio/nulo.");
                TempData["ErrorMessage"] = "Informe o nome do manutentor.";
                return RedirectToAction(nameof(Index));
            }

            // Evita duplicidade (case-insensitive)
            var existe = _context.Manutentores.Any(x => x.Nome.ToLower() == nome.ToLower());
            if (existe)
            {
                TryAudit(uid, "Tentou criar manutentor (falhou)", "Manutentor", null, $"Duplicado: {nome}");
                TempData["ErrorMessage"] = "Já existe um manutentor com esse nome.";
                return RedirectToAction(nameof(Index));
            }

            var manutentor = new Manutentor { Nome = nome };
            _context.Manutentores.Add(manutentor);
            _context.SaveChanges();

            TryAudit(uid, "Criou manutentor", "Manutentor", manutentor.Id, $"Nome={nome}");

            TempData["SuccessMessage"] = "Manutentor criado com sucesso.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, string nome)
        {
            if (!UsuarioEhAdmin())
                return AcessoNegado();

            var uid = GetUserId();

            nome = (nome ?? "").Trim();

            var existente = _context.Manutentores.FirstOrDefault(x => x.Id == id);
            if (existente == null)
            {
                TryAudit(uid, "Tentou editar manutentor (falhou)", "Manutentor", id, "Manutentor não encontrado.");
                TempData["ErrorMessage"] = "Manutentor não encontrado.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou editar manutentor (falhou)", "Manutentor", id, "Nome vazio/nulo.");
                TempData["ErrorMessage"] = "Informe o nome do manutentor.";
                return RedirectToAction(nameof(Index));
            }

            // Evita trocar para um nome que já existe em outro registro
            var duplicado = _context.Manutentores.Any(x =>
                x.Id != id && x.Nome.ToLower() == nome.ToLower());

            if (duplicado)
            {
                TryAudit(uid, "Tentou editar manutentor (falhou)", "Manutentor", id, $"Duplicado: {nome}");
                TempData["ErrorMessage"] = "Já existe um outro manutentor com esse nome.";
                return RedirectToAction(nameof(Index));
            }

            var nomeAntigo = existente.Nome ?? "";
            existente.Nome = nome;
            _context.SaveChanges();

            TryAudit(uid, "Editou manutentor", "Manutentor", id, $"Id={id} | Nome: {nomeAntigo} -> {nome}");

            TempData["SuccessMessage"] = "Manutentor atualizado com sucesso.";
            return RedirectToAction(nameof(Index));
        }
    }
}
