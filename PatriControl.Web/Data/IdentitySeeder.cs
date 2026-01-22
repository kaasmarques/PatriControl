using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Models;

namespace PatriControl.Web.Data
{
    public static class IdentitySeeder
    {
        private const string ROLE_ADMIN = "Admin";
        private const string ROOT_CODIGO = "USR001";

        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();

            // 1) Garante Role Admin
            if (!await roleManager.RoleExistsAsync(ROLE_ADMIN))
            {
                var r = await roleManager.CreateAsync(new IdentityRole<int>(ROLE_ADMIN));
                if (!r.Succeeded)
                    throw new Exception("Falha ao criar Role Admin: " + string.Join(" | ", r.Errors.Select(e => e.Description)));
            }

            // 2) Procura ROOT SOMENTE por Codigo (USR001)
            var root = await userManager.Users
                .FirstOrDefaultAsync(u => u.Codigo == ROOT_CODIGO);

            // 3) Se não existe, cria
            if (root == null)
            {
                // Observação: Identity precisa de UserName único.
                // Como você NÃO quer depender de email, use um username fixo "usr001".
                root = new Usuario
                {
                    Codigo = ROOT_CODIGO,
                    Nome = "Root",
                    Sobrenome = "do Sistema",

                    // Email pode existir, mas NÃO é critério de busca.
                    Email = "root@patricontrol.com",
                    UserName = "usr001",
                    EmailConfirmed = true,

                    Status = "Ativo",
                    Administrador = true,

                    CriadoEm = DateTime.Now,
                    AtualizadoEm = DateTime.Now
                };

                var create = await userManager.CreateAsync(root, "Admin@123");
                if (!create.Succeeded)
                    throw new Exception("Falha ao criar ROOT: " + string.Join(" | ", create.Errors.Select(e => e.Description)));
            }
            else
            {
                // 4) Já existe: garante que ele está consistente (não cria de novo)
                var precisaUpdate = false;

                if (!string.Equals(root.Status, "Ativo", StringComparison.OrdinalIgnoreCase))
                {
                    root.Status = "Ativo";
                    precisaUpdate = true;
                }

                if (!root.Administrador)
                {
                    root.Administrador = true;
                    precisaUpdate = true;
                }

                if (precisaUpdate)
                {
                    root.AtualizadoEm = DateTime.Now;
                    var upd = await userManager.UpdateAsync(root);
                    if (!upd.Succeeded)
                        throw new Exception("Falha ao atualizar ROOT: " + string.Join(" | ", upd.Errors.Select(e => e.Description)));
                }
            }

            // 5) Garante vínculo com Role Admin
            if (!await userManager.IsInRoleAsync(root, ROLE_ADMIN))
            {
                var addRole = await userManager.AddToRoleAsync(root, ROLE_ADMIN);
                if (!addRole.Succeeded)
                    throw new Exception("Falha ao adicionar ROOT na Role Admin: " + string.Join(" | ", addRole.Errors.Select(e => e.Description)));
            }
        }
    }
}
