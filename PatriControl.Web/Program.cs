using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Data;
using PatriControl.Web.Middleware;
using PatriControl.Web.Models;
using PatriControl.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// =====================
// MVC + Antiforgery global (todo POST exige token)
// =====================
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// =====================
// DbContext SQLite (IdentityDbContext<Usuario,...>)
// =====================
builder.Services.AddDbContext<PatriControlContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// =====================
// Identity (Usuario + Role<int>)
// =====================
builder.Services
    .AddIdentity<Usuario, IdentityRole<int>>(options =>
    {
        // Senha (ajuste se quiser mais rígido)
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        // Lockout (anti brute-force)
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);

        // Usuário
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<PatriControlContext>()
    .AddDefaultTokenProviders();

// Cookie do Identity
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AcessoNegado";

    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);

    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.HttpOnly = true;

    options.Cookie.SecurePolicy =
        builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

    // Revalida usuário/ativo a cada request (sem “cookie mentir”)
    options.Events = new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationEvents
    {
        OnValidatePrincipal = async context =>
        {
            var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<Usuario>>();
            var user = await userManager.GetUserAsync(context.Principal!);

            if (user == null || !string.Equals(user.Status, "Ativo", StringComparison.OrdinalIgnoreCase))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return;
            }
        }
    };
});

// =====================
// Authorization (mantém sua Policy atual)
// =====================
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("Administrador", "True"));
});

// =====================
// Claims factory (injeta claim "Administrador"/"Codigo")
// =====================
builder.Services.AddScoped<IUserClaimsPrincipalFactory<Usuario>, PatriControlClaimsPrincipalFactory>();

// =====================
// Traduzir erros do Identity (pt-BR)
// =====================
builder.Services.AddScoped<Microsoft.AspNetCore.Identity.IdentityErrorDescriber, PatriControl.Web.Services.PtBrIdentityErrorDescriber>();


// =====================
// Audit Logger
// =====================
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// =====================
// DataProtection (evita “deslogar” ao reiniciar em produção)
// =====================
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")))
    .SetApplicationName("PatriControl");

var app = builder.Build();

// =====================
// MIGRATE + SEED (ROOT)
// =====================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<PatriControlContext>();

    await context.Database.MigrateAsync();

    // Seed do ROOT via Identity
    await IdentitySeeder.SeedAsync(services);
}

// =====================
// Pipeline
// =====================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Security headers básicos
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    await next();
});

app.UseRouting();

app.UseAuthentication();                       // Identity cookie
app.UseMiddleware<UsuarioAtivoMiddleware>();   // vamos ajustar ele já já (pra Identity)
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
