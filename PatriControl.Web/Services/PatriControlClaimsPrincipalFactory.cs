using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PatriControl.Web.Models;
using System.Security.Claims;

namespace PatriControl.Web.Services
{
    public class PatriControlClaimsPrincipalFactory : UserClaimsPrincipalFactory<Usuario, IdentityRole<int>>
    {
        public PatriControlClaimsPrincipalFactory(
            UserManager<Usuario> userManager,
            RoleManager<IdentityRole<int>> roleManager,
            IOptions<IdentityOptions> optionsAccessor)
            : base(userManager, roleManager, optionsAccessor)
        {
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Usuario user)
        {
            var identity = await base.GenerateClaimsAsync(user);

            // Troca o ClaimTypes.Name para mostrar Nome + Sobrenome
            var existing = identity.FindAll(ClaimTypes.Name).ToList();
            foreach (var c in existing) identity.RemoveClaim(c);

            var nomeCompleto = $"{user.Nome} {user.Sobrenome}".Trim();
            identity.AddClaim(new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(nomeCompleto)
                ? (user.Email ?? user.UserName ?? "Usuário")
                : nomeCompleto));

            identity.AddClaim(new Claim("Codigo", user.Codigo ?? ""));
            identity.AddClaim(new Claim("Administrador", user.Administrador ? "True" : "False"));
            identity.AddClaim(new Claim("UsuarioAtualizadoEmTicks", user.AtualizadoEm.ToUniversalTime().Ticks.ToString()));

            return identity;
        }
    }
}
