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
            // ─────────────────────────────────────────────────────
            // FAST PATH: verificação leve via DbContext (1 query SQL)
            // Se ROOT + Role Admin já existem e estão consistentes,
            // pula TODAS as chamadas pesadas do Identity.
            // ─────────────────────────────────────────────────────
            using var fastScope = services.CreateScope();
            var db = fastScope.ServiceProvider.GetRequiredService<PatriControlContext>();

            var snapshot = await db.Users
                .Where(u => u.Codigo == ROOT_CODIGO)
                .Select(u => new
                {
                    u.Id,
                    u.Status,
                    u.Administrador,
                    TemRoleAdmin = db.UserRoles.Any(ur =>
                        ur.UserId == u.Id &&
                        db.Roles.Any(r => r.Id == ur.RoleId && r.Name == ROLE_ADMIN)),
                    RoleAdminExiste = db.Roles.Any(r => r.Name == ROLE_ADMIN)
                })
                .FirstOrDefaultAsync();

            // ROOT já existe, Status=Ativo, é Admin, e tem a Role vinculada → nada a fazer
            if (snapshot is not null
                && string.Equals(snapshot.Status, "Ativo", StringComparison.OrdinalIgnoreCase)
                && snapshot.Administrador
                && snapshot.TemRoleAdmin
                && snapshot.RoleAdminExiste)
            {
                return; // ← fast path: 0 chamadas ao Identity
            }

            // ─────────────────────────────────────────────────────
            // SLOW PATH: algo precisa ser criado ou corrigido
            // Usa UserManager/RoleManager normalmente
            // ─────────────────────────────────────────────────────
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
                // 4) Já existe: garante que ele está consistente
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
