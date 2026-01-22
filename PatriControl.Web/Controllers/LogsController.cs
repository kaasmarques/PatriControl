using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using System;
using System.Linq;
using System.Security.Claims;

namespace PatriControl.Web.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class LogsController : Controller
    {
        private readonly PatriControlContext _context;

        public LogsController(PatriControlContext context)
        {
            _context = context;
        }

        // GET: /Logs
        public IActionResult Index(int? usuarioId, string? termo, int page = 1, int pageSize = 20)
        {
            // pageSize fixo (padrão do sistema) - mude aqui se quiser 10/20
            pageSize = 20;
            if (page < 1) page = 1;

            // Query base (sem Include)
            var queryBase = _context.AuditLogs
                .AsNoTracking()
                .AsQueryable();

            if (usuarioId.HasValue && usuarioId.Value > 0)
            {
                queryBase = queryBase.Where(a => a.UsuarioId == usuarioId.Value);
            }

            if (!string.IsNullOrWhiteSpace(termo))
            {
                termo = termo.Trim();
                var t = termo.ToLowerInvariant();

                queryBase = queryBase.Where(a =>
                    (a.Acao ?? "").ToLower().Contains(t) ||
                    (a.Entidade ?? "").ToLower().Contains(t) ||
                    (a.Detalhes ?? "").ToLower().Contains(t) ||
                    (a.Usuario != null && (
                        (a.Usuario.Codigo ?? "").ToLower().Contains(t) ||
                        (a.Usuario.Nome ?? "").ToLower().Contains(t) ||
                        (a.Usuario.Sobrenome ?? "").ToLower().Contains(t) ||
                        (a.Usuario.Email ?? "").ToLower().Contains(t)
                    ))
                );
            }

            var total = queryBase.Count();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            // Lista (com Include) + ordenação
            var lista = queryBase
                .Include(a => a.Usuario)
                .OrderByDescending(a => a.DataHora)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Combo do filtro (usuários)
            ViewBag.Usuarios = _context.Usuarios
                .AsNoTracking()
                .OrderBy(u => u.Codigo)
                .ToList();

            ViewBag.UsuarioId = usuarioId;
            ViewBag.Termo = termo ?? "";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = totalPages;
            ViewBag.Exibindo = lista.Count;

            // ===== PAGINAÇÃO (PADRÃO DO SISTEMA) =====
            var routeValues = new Dictionary<string, object?>();

            if (usuarioId.HasValue && usuarioId.Value > 0) routeValues["usuarioId"] = usuarioId.Value;
            if (!string.IsNullOrWhiteSpace(termo)) routeValues["termo"] = termo.Trim();

            ViewBag.Paginacao = new PaginacaoViewModel
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                TotalPages = totalPages,
                Action = nameof(Index),
                Controller = "Logs",
                RouteValues = routeValues
            };

            return View(lista);
        }
    }
}
