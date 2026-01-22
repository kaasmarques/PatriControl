using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Models;

namespace PatriControl.Web.Middleware
{
    public class UsuarioAtivoMiddleware
    {
        private readonly RequestDelegate _next;

        public UsuarioAtivoMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            UserManager<Usuario> userManager,
            SignInManager<Usuario> signInManager)
        {
            var path = context.Request.Path.Value ?? "";

            // não roda em Account/* e nem em estáticos
            if (path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/images", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            if (context.User?.Identity?.IsAuthenticated == true)
            {
                // pega usuário do Identity a partir do principal
                var usuario = await userManager.GetUserAsync(context.User);

                // usuário apagado do banco (ou cookie inválido)
                if (usuario == null)
                {
                    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                    context.Response.Redirect("/Account/Login?sessao=1");
                    return;
                }

                // garante que o usuário está "Ativo"
                if (!string.Equals(usuario.Status, "Ativo", StringComparison.OrdinalIgnoreCase))
                {
                    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                    context.Response.Redirect("/Account/Login?inativo=1");
                    return;
                }

                // refresh de claims se o usuário mudou (admin/nome/código/etc)
                var claimTicks = context.User.FindFirst("UsuarioAtualizadoEmTicks")?.Value ?? "";
                var dbTicks = usuario.AtualizadoEm.ToUniversalTime().Ticks.ToString();

                if (!string.Equals(claimTicks, dbTicks, StringComparison.Ordinal))
                {
                    // preserva as properties do cookie atual (persistência/expiração)
                    var auth = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                    var props = auth?.Properties;

                    // cria principal novo (usa sua ClaimsPrincipalFactory custom)
                    var principal = await signInManager.CreateUserPrincipalAsync(usuario);

                    // re-emite o cookie já com as claims atualizadas
                    await context.SignInAsync(IdentityConstants.ApplicationScheme, principal, props);

                    // atualiza o User desta request
                    context.User = principal;
                }
            }

            await _next(context);
        }
    }
}
