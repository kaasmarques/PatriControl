using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using PatriControl.Web.Services;
using System.Security.Claims;

namespace PatriControl.Web.Controllers
{
    [Authorize]
    public class TiposManutencaoController : Controller
    {
        private readonly PatriControlContext _context;
        private readonly IAuditLogger _audit;

        public TiposManutencaoController(PatriControlContext context, IAuditLogger audit)
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

        [HttpGet]
        public IActionResult ListarPorTipo(string nomeTipoPatrimonio)
        {
            if (string.IsNullOrWhiteSpace(nomeTipoPatrimonio))
                return Json(new List<object>());

            var tipo = _context.TiposPatrimonio.FirstOrDefault(t => t.Nome == nomeTipoPatrimonio);
            if (tipo == null) return Json(new List<object>());

            var lista = _context.TiposManutencao
                .Where(x => x.TipoPatrimonioId == tipo.Id)
                .OrderBy(x => x.Nome)
                .Select(x => new { id = x.Id, nome = x.Nome })
                .ToList();

            return Json(lista);
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string nomeTipoPatrimonio, string nome)
        {
            var uid = GetUserId();

            if (string.IsNullOrWhiteSpace(nomeTipoPatrimonio) || string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou criar tipo de manutenção (falhou)", "TipoManutencao", null, "Campos obrigatórios vazios.");
                return BadRequest();
            }

            var tipo = _context.TiposPatrimonio.FirstOrDefault(t => t.Nome == nomeTipoPatrimonio);
            if (tipo == null)
            {
                TryAudit(uid, "Tentou criar tipo de manutenção (falhou)", "TipoManutencao", null, $"Tipo patrimônio não encontrado: {nomeTipoPatrimonio}");
                return NotFound();
            }

            var nomeLimpo = nome.Trim();

            var novo = new TipoManutencao
            {
                TipoPatrimonioId = tipo.Id,
                Nome = nomeLimpo
            };

            _context.TiposManutencao.Add(novo);
            _context.SaveChanges();

            TryAudit(
                uid,
                "Criou tipo de manutenção",
                "TipoManutencao",
                novo.Id,
                $"TipoPatrimonio='{nomeTipoPatrimonio}' (Id={tipo.Id}) | Nome='{nomeLimpo}'"
            );

            return Ok();
        }

        [Authorize(Policy = "AdminOnly")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, string nome)
        {
            var uid = GetUserId();

            var existente = _context.TiposManutencao.FirstOrDefault(x => x.Id == id);
            if (existente == null)
            {
                TryAudit(uid, "Tentou editar tipo de manutenção (falhou)", "TipoManutencao", id, "Registro não encontrado.");
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(nome))
            {
                TryAudit(uid, "Tentou editar tipo de manutenção (falhou)", "TipoManutencao", id, "Nome vazio.");
                return BadRequest();
            }

            var nomeAntigo = existente.Nome ?? "";
            var nomeNovo = nome.Trim();

            existente.Nome = nomeNovo;
            _context.SaveChanges();

            TryAudit(
                uid,
                "Editou tipo de manutenção",
                "TipoManutencao",
                existente.Id,
                $"Nome: '{nomeAntigo}' -> '{nomeNovo}' | TipoPatrimonioId={existente.TipoPatrimonioId}"
            );

            return Ok();
        }
    }
}
