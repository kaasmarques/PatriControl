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
    public class TiposController : Controller
    {
        private readonly PatriControlContext _context;
        private readonly IAuditLogger _audit;

        public TiposController(PatriControlContext context, IAuditLogger audit)
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

        // ============================================================
        // LISTAGEM + FILTRO (SERVER SIDE) + PAGINAÇÃO
        // ============================================================
        public IActionResult Index(string? filtro, int page = 1, int pageSize = 10)
        {
            // pageSize fixo (mude aqui se quiser)
            pageSize = 10;
            if (page < 1) page = 1;

            var queryBase = _context.TiposPatrimonio
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                filtro = filtro.Trim();
                queryBase = queryBase.Where(t => t.Nome.Contains(filtro));
            }

            var total = queryBase.Count();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var lista = queryBase
                .OrderBy(t => t.Nome)
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

            if (!string.IsNullOrWhiteSpace(filtro)) routeValues["filtro"] = filtro.Trim();

            ViewBag.Paginacao = new PaginacaoViewModel
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                TotalPages = totalPages,
                Action = nameof(Index),
                Controller = "Tipos",
                RouteValues = routeValues
            };

            return View(lista);
        }

        // ============================================================
        // CRIA um novo tipo (via modal)
        // (Opcional: preserva filtro/página se vierem no POST)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string nome, string? filtro, int page = 1, int pageSize = 10)
        {
            var uid = GetUserId();

            nome = (nome ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou criar tipo (falhou)", "TipoPatrimonio", null, "Nome vazio.");
                return RedirectToAction(nameof(Index), new { filtro, page, pageSize });
            }

            // Evita duplicado simples (mesmo nome exato)
            if (_context.TiposPatrimonio.Any(t => t.Nome == nome))
            {
                TryAudit(uid, "Tentou criar tipo (falhou)", "TipoPatrimonio", null, $"Duplicado: {nome}");
                return RedirectToAction(nameof(Index), new { filtro, page, pageSize });
            }

            var tipo = new TipoPatrimonio { Nome = nome };
            _context.TiposPatrimonio.Add(tipo);
            _context.SaveChanges();

            TryAudit(uid, "Criou tipo de patrimônio", "TipoPatrimonio", tipo.Id, $"Nome={nome}");

            TempData["SuccessMessage"] = "Tipo de patrimônio criado com sucesso.";
            return RedirectToAction(nameof(Index), new { filtro, page, pageSize });
        }

        // ============================================================
        // EDITA o nome de um tipo e propaga para os patrimônios
        // (Opcional: preserva filtro/página se vierem no POST)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, string nome, string? filtro, int page = 1, int pageSize = 10)
        {
            var uid = GetUserId();

            nome = (nome ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou editar tipo (falhou)", "TipoPatrimonio", id, "Nome vazio.");
                return RedirectToAction(nameof(Index), new { filtro, page, pageSize });
            }

            var tipo = _context.TiposPatrimonio.FirstOrDefault(t => t.Id == id);
            if (tipo == null)
            {
                TryAudit(uid, "Tentou editar tipo (falhou)", "TipoPatrimonio", id, "Tipo não encontrado.");
                return NotFound();
            }

            // Evita duplicar com outro tipo (mesmo nome exato)
            if (_context.TiposPatrimonio.Any(t => t.Id != id && t.Nome == nome))
            {
                TryAudit(uid, "Tentou editar tipo (falhou)", "TipoPatrimonio", id, $"Duplicado: {nome}");
                return RedirectToAction(nameof(Index), new { filtro, page, pageSize });
            }

            var nomeAntigo = tipo.Nome;

            // Atualiza tipo
            tipo.Nome = nome;

            // Atualiza todos os patrimônios que usavam esse tipo
            var patrimonios = _context.Patrimonios
                .Where(p => p.Tipo == nomeAntigo)
                .ToList();

            foreach (var p in patrimonios)
                p.Tipo = nome;

            _context.SaveChanges();

            TryAudit(
                uid,
                "Editou tipo de patrimônio",
                "TipoPatrimonio",
                tipo.Id,
                $"Nome: '{nomeAntigo}' -> '{nome}' | PatrimoniosAtualizados={patrimonios.Count}"
            );

            TempData["SuccessMessage"] = "Tipo de patrimônio alterado com sucesso.";
            return RedirectToAction(nameof(Index), new { filtro, page, pageSize });
        }
    }
}
