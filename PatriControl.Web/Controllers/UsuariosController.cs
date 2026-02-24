using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Models;
using PatriControl.Web.Services;
using System.Globalization;
using System.Security.Claims;

namespace PatriControl.Web.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    public class UsuariosController : Controller
    {
        private readonly PatriControlContext _context;
        private readonly IAuditLogger _audit;
        private readonly UserManager<Usuario> _userManager;
        private readonly RoleManager<IdentityRole<int>> _roleManager;

        private static readonly string[] _statusValidos = new[] { "Ativo", "Inativo" };
        private const string ROLE_ADMIN = "Admin";

        public UsuariosController(
            PatriControlContext context,
            IAuditLogger audit,
            UserManager<Usuario> userManager,
            RoleManager<IdentityRole<int>> roleManager)
        {
            _context = context;
            _audit = audit;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        private int? GetUserId()
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdStr, out var id) && id > 0) return id;
            return null;
        }

        private async Task TryAuditAsync(int? usuarioId, string acao, string? entidade = null, int? entidadeId = null, string? detalhes = null)
        {
            try
            {
                await _audit.LogAsync(usuarioId, acao, entidade, entidadeId, detalhes);
            }
            catch
            {
                // auditoria nunca pode quebrar o fluxo principal
            }
        }

        private static string NormalizarEmail(string? email)
            => (email ?? "").Trim().ToLowerInvariant();

        private static string NormalizarStatus(string? status)
        {
            status = (status ?? "").Trim();
            if (string.IsNullOrWhiteSpace(status)) return "Ativo";

            var s = char.ToUpperInvariant(status[0]) + status.Substring(1).ToLowerInvariant();
            return s;
        }

        private async Task EnsureAdminRoleAsync()
        {
            if (!await _roleManager.RoleExistsAsync(ROLE_ADMIN))
                await _roleManager.CreateAsync(new IdentityRole<int>(ROLE_ADMIN));
        }

        private async Task<string> GerarProximoCodigoAsync()
        {
            //  pega o maior número dentro de USR### (não depende do Id)
            var codigos = await _userManager.Users
                .Select(u => u.Codigo)
                .ToListAsync();

            int max = 0;

            foreach (var c in codigos)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;

                var s = c.Trim().ToUpperInvariant();
                if (!s.StartsWith("USR")) continue;

                var numPart = s.Substring(3);
                if (int.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    if (n > max) max = n;
            }

            return $"USR{(max + 1):D3}";
        }

        // ============================================================
        // LISTA + FILTRO + PAGINAÇÃO
        // ============================================================
        public IActionResult Index(string? termo, string? status, int page = 1, int pageSize = 10)
        {
            pageSize = 10;
            if (page < 1) page = 1;

            status = (status ?? "Todos").Trim();
            if (!status.Equals("Todos", StringComparison.OrdinalIgnoreCase) &&
                !status.Equals("Ativo", StringComparison.OrdinalIgnoreCase) &&
                !status.Equals("Inativo", StringComparison.OrdinalIgnoreCase))
            {
                status = "Todos";
            }

            var queryBase = _userManager.Users.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(termo))
            {
                termo = termo.Trim();
                var t = termo.ToLowerInvariant();

                queryBase = queryBase.Where(u =>
                    ((u.Nome ?? "").ToLower().Contains(t)) ||
                    ((u.Sobrenome ?? "").ToLower().Contains(t)) ||
                    ((u.Email ?? "").ToLower().Contains(t)) ||
                    ((u.Codigo ?? "").ToLower().Contains(t)));
            }

            if (!status.Equals("Todos", StringComparison.OrdinalIgnoreCase))
            {
                var st = NormalizarStatus(status);
                if (_statusValidos.Contains(st))
                    queryBase = queryBase.Where(u => u.Status == st);

                status = st;
            }

            var total = queryBase.Count();

            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var usuarios = queryBase
                .OrderBy(u => u.Codigo)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.Termo = termo ?? "";
            ViewBag.Status = status ?? "Todos";

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = totalPages;
            ViewBag.Exibindo = usuarios.Count;

            // ===== PAGINAÇÃO (PADRÃO DO SISTEMA) =====
            var routeValues = new Dictionary<string, object?>();

            if (!string.IsNullOrWhiteSpace(termo)) routeValues["termo"] = termo.Trim();
            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("Todos", StringComparison.OrdinalIgnoreCase))
                routeValues["status"] = status;

            ViewBag.Paginacao = new PaginacaoViewModel
            {
                Page = page,
                PageSize = pageSize,
                Total = total,
                TotalPages = totalPages,
                Action = nameof(Index),
                Controller = "Usuarios",
                RouteValues = routeValues
            };

            return View(usuarios);
        }

        // ============================================================
        // CRIAR USUÁRIO (via modal)
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Usuario usuario)
        {
            var actorId = GetUserId();

            if (string.IsNullOrWhiteSpace(usuario.Nome) ||
                string.IsNullOrWhiteSpace(usuario.Sobrenome) ||
                string.IsNullOrWhiteSpace(usuario.Email) ||
                string.IsNullOrWhiteSpace(usuario.Senha))
            {
                await TryAuditAsync(actorId, "Tentou criar usuário (falhou)", "Usuario", null, "Campos obrigatórios ausentes (Nome/Sobrenome/Email/Senha).");
                TempData["ErrorMessage"] = "Preencha Nome, Sobrenome, Email e Senha.";
                return RedirectToAction(nameof(Index));
            }

            usuario.Email = NormalizarEmail(usuario.Email);

            // status
            usuario.Status = NormalizarStatus(usuario.Status);
            if (!_statusValidos.Contains(usuario.Status))
            {
                await TryAuditAsync(actorId, "Tentou criar usuário (falhou)", "Usuario", null, $"Status inválido: '{usuario.Status}'.");
                TempData["ErrorMessage"] = "Status inválido. Use Ativo ou Inativo.";
                return RedirectToAction(nameof(Index));
            }

            // impedir duplicidade de email
            var existenteEmail = await _userManager.FindByEmailAsync(usuario.Email);
            if (existenteEmail != null)
            {
                await TryAuditAsync(actorId, "Tentou criar usuário (falhou)", "Usuario", null, $"E-mail duplicado: '{usuario.Email}'.");
                TempData["ErrorMessage"] = "Já existe um usuário com este e-mail.";
                return RedirectToAction(nameof(Index));
            }

            await EnsureAdminRoleAsync();

            // gerar código USR###
            var codigo = await GerarProximoCodigoAsync();

            var novo = new Usuario
            {
                Codigo = codigo,
                Nome = usuario.Nome.Trim(),
                Sobrenome = usuario.Sobrenome.Trim(),
                Email = usuario.Email,
                UserName = usuario.Email,          // ✅ Identity precisa de UserName
                Status = usuario.Status,
                Administrador = usuario.Administrador,
                CriadoEm = DateTime.Now,
                AtualizadoEm = DateTime.Now,
                EmailConfirmed = true
            };

            var senha = (usuario.Senha ?? "").Trim();

            var result = await _userManager.CreateAsync(novo, senha);
            if (!result.Succeeded)
            {
                var erros = result.Errors.Select(e => e.Description).ToList();
                var msg = string.Join(" | ", erros);

                await TryAuditAsync(actorId, "Tentou criar usuário (falhou)", "Usuario", null, $"Erro Identity: {msg}");

                TempData["ErrorMessage"] = string.Join(" ", erros);
                TempData["OpenModal"] = "novo-usuario";

                return RedirectToAction(nameof(Index));
            }

            // role admin
            if (novo.Administrador)
                await _userManager.AddToRoleAsync(novo, ROLE_ADMIN);

            await TryAuditAsync(
                actorId,
                "Criou usuário",
                "Usuario",
                novo.Id,
                $"Codigo={novo.Codigo} | Nome='{novo.Nome} {novo.Sobrenome}' | Email='{novo.Email}' | Admin={(novo.Administrador ? "True" : "False")} | Status='{novo.Status}'"
            );

            TempData["SuccessMessage"] = "Usuário criado com sucesso.";
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // EDITAR DADOS
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Usuario usuario)
        {
            var actorId = GetUserId();

            var existente = await _userManager.FindByIdAsync(usuario.Id.ToString());
            if (existente == null)
            {
                await TryAuditAsync(actorId, "Tentou editar usuário (falhou)", "Usuario", usuario.Id, "Usuário não encontrado.");
                return NotFound();
            }

            // ROOT
            if (string.Equals(existente.Codigo, "USR001", StringComparison.OrdinalIgnoreCase))
            {
                await TryAuditAsync(actorId, "Tentou editar usuário ROOT (bloqueado)", "Usuario", existente.Id, "USR001 não pode ser editado.");
                TempData["ErrorMessage"] = "O usuário ROOT (USR001) não pode ser editado.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(usuario.Nome) ||
                string.IsNullOrWhiteSpace(usuario.Sobrenome) ||
                string.IsNullOrWhiteSpace(usuario.Email))
            {
                await TryAuditAsync(actorId, "Tentou editar usuário (falhou)", "Usuario", existente.Id, $"Codigo={existente.Codigo} | Campos obrigatórios ausentes (Nome/Sobrenome/Email).");
                TempData["ErrorMessage"] = "Preencha Nome, Sobrenome e Email.";
                return RedirectToAction(nameof(Index));
            }

            var emailNormalizado = NormalizarEmail(usuario.Email);

            // impedir duplicidade de email (exceto ele mesmo)
            var outro = await _userManager.FindByEmailAsync(emailNormalizado);
            if (outro != null && outro.Id != existente.Id)
            {
                await TryAuditAsync(actorId, "Tentou editar usuário (falhou)", "Usuario", existente.Id, $"Codigo={existente.Codigo} | E-mail duplicado: '{emailNormalizado}'.");
                TempData["ErrorMessage"] = "Já existe outro usuário com este e-mail.";
                return RedirectToAction(nameof(Index));
            }

            var statusNormalizado = NormalizarStatus(usuario.Status);
            if (!_statusValidos.Contains(statusNormalizado))
            {
                await TryAuditAsync(actorId, "Tentou editar usuário (falhou)", "Usuario", existente.Id, $"Codigo={existente.Codigo} | Status inválido: '{statusNormalizado}'.");
                TempData["ErrorMessage"] = "Status inválido. Use Ativo ou Inativo.";
                return RedirectToAction(nameof(Index));
            }

            await EnsureAdminRoleAsync();

            // snapshot para audit
            var antigoNome = existente.Nome ?? "";
            var antigoSobrenome = existente.Sobrenome ?? "";
            var antigoEmail = existente.Email ?? "";
            var antigoAdmin = existente.Administrador;
            var antigoStatus = existente.Status ?? "";

            existente.Nome = usuario.Nome.Trim();
            existente.Sobrenome = usuario.Sobrenome.Trim();
            existente.Email = emailNormalizado;
            existente.UserName = emailNormalizado; // ✅ manter login consistente
            existente.Administrador = usuario.Administrador;
            existente.Status = statusNormalizado;

            existente.AtualizadoEm = DateTime.Now; // usado no cookie-version

            var upd = await _userManager.UpdateAsync(existente);
            if (!upd.Succeeded)
            {
                var msg = string.Join(" | ", upd.Errors.Select(e => e.Description));
                await TryAuditAsync(actorId, "Tentou editar usuário (falhou)", "Usuario", existente.Id, $"Erro Identity: {msg}");
                TempData["ErrorMessage"] = "Erro ao atualizar usuário: " + msg;
                return RedirectToAction(nameof(Index));
            }

            // sincronizar role Admin
            var ehAdminNoRole = await _userManager.IsInRoleAsync(existente, ROLE_ADMIN);

            if (existente.Administrador && !ehAdminNoRole)
                await _userManager.AddToRoleAsync(existente, ROLE_ADMIN);

            if (!existente.Administrador && ehAdminNoRole)
                await _userManager.RemoveFromRoleAsync(existente, ROLE_ADMIN);

            await TryAuditAsync(
                actorId,
                "Editou usuário",
                "Usuario",
                existente.Id,
                $"Codigo={existente.Codigo} | Nome: '{antigoNome} {antigoSobrenome}' -> '{existente.Nome} {existente.Sobrenome}' | Email: '{antigoEmail}' -> '{existente.Email}' | Admin: {(antigoAdmin ? "True" : "False")} -> {(existente.Administrador ? "True" : "False")} | Status: '{antigoStatus}' -> '{existente.Status}'"
            );

            TempData["SuccessMessage"] = "Dados do usuário foram atualizados.";
            return RedirectToAction(nameof(Index));
        }

        // ============================================================
        // ALTERAR SENHA
        // ============================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AlterarSenha(int id, string novaSenha)
        {
            var actorId = GetUserId();

            var usuario = await _userManager.FindByIdAsync(id.ToString());
            if (usuario == null)
            {
                await TryAuditAsync(actorId, "Tentou alterar senha (falhou)", "Usuario", id, "Usuário não encontrado.");
                return NotFound();
            }

            if (string.Equals(usuario.Codigo, "USR001", StringComparison.OrdinalIgnoreCase))
            {
                await TryAuditAsync(actorId, "Tentou alterar senha do ROOT (bloqueado)", "Usuario", usuario.Id, "USR001 não pode ter a senha alterada.");
                TempData["ErrorMessage"] = "A senha do ROOT (USR001) não pode ser alterada.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(novaSenha))
            {
                await TryAuditAsync(actorId, "Tentou alterar senha (falhou)", "Usuario", usuario.Id, $"Codigo={usuario.Codigo} | Nova senha vazia.");
                TempData["ErrorMessage"] = "Informe a nova senha.";
                return RedirectToAction(nameof(Index));
            }

            // ✅ reset de senha sem senha antiga (admin)
            var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);
            var res = await _userManager.ResetPasswordAsync(usuario, token, novaSenha.Trim());

            if (!res.Succeeded)
            {
                var msg = string.Join(" | ", res.Errors.Select(e => e.Description));
                await TryAuditAsync(actorId, "Tentou alterar senha (falhou)", "Usuario", usuario.Id, $"Codigo={usuario.Codigo} | Erro Identity: {msg}");
                TempData["ErrorMessage"] = "Erro ao alterar senha: " + msg;
                return RedirectToAction(nameof(Index));
            }

            usuario.AtualizadoEm = DateTime.Now;
            await _userManager.UpdateAsync(usuario);

            await TryAuditAsync(actorId, "Alterou senha do usuário", "Usuario", usuario.Id, $"Codigo={usuario.Codigo} | Email='{usuario.Email}'");
            TempData["SuccessMessage"] = "Senha alterada com sucesso!";

            return RedirectToAction(nameof(Index));
        }
    }
}
