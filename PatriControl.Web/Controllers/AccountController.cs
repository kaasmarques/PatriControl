using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PatriControl.Web.Models;

namespace PatriControl.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<Usuario> _signInManager;
        private readonly UserManager<Usuario> _userManager;

        public AccountController(SignInManager<Usuario> signInManager, UserManager<Usuario> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        // GET: /Account/Login
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null, int? inativo = null, int? sessao = null)
        {
            if (User?.Identity?.IsAuthenticated ?? false)
                return RedirectToAction("Index", "Dashboard");

            ViewData["ReturnUrl"] = returnUrl;

            if (inativo == 1)
                ViewBag.MensagemInativo = "Seu usuário está inativo ou não existe mais. Solicite liberação a um administrador.";

            if (sessao == 1)
                ViewBag.MensagemSessao = "Sua sessão expirou ou foi atualizada. Faça login novamente.";

            return View(new LoginViewModel());
        }

        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(model);

            var email = (model.Email ?? "").Trim().ToLowerInvariant();

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Usuário ou senha inválidos.");
                return View(model);
            }

            if (!string.Equals(user.Status, "Ativo", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Login", "Account", new { inativo = 1, returnUrl });

            // ✅ Identity valida hash e faz sign-in
            var result = await _signInManager.PasswordSignInAsync(
                user,
                model.Senha ?? "",
                model.LembrarMe,
                lockoutOnFailure: true
            );

            if (result.Succeeded)
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                return RedirectToAction("Index", "Dashboard");
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Muitas tentativas. Aguarde alguns minutos e tente novamente.");
                return View(model);
            }

            ModelState.AddModelError(string.Empty, "Usuário ou senha inválidos.");
            return View(model);
        }

        // POST: /Account/Logout
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        [Authorize]
        public IActionResult AcessoNegado()
        {
            return View();
        }
    }
}
