using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PatriControl.Web.Models;

namespace PatriControl.Web.Data
{
    public class PatriControlContext : IdentityDbContext<Usuario, IdentityRole<int>, int>
    {
        public PatriControlContext(DbContextOptions<PatriControlContext> options)
            : base(options)
        {
        }

        // Mantém o nome "Usuarios" para seu código atual
        public DbSet<Usuario> Usuarios => Set<Usuario>();

        public DbSet<Patrimonio> Patrimonios { get; set; } = null!;
        public DbSet<Tramite> Tramites { get; set; } = null!;
        public DbSet<Unidade> Unidades { get; set; } = null!;
        public DbSet<Localizacao> Localizacoes { get; set; } = null!;
        public DbSet<TipoPatrimonio> TiposPatrimonio { get; set; } = null!;
        public DbSet<Fornecedor> Fornecedores { get; set; } = null!;
        public DbSet<Manutencao> Manutencoes { get; set; } = null!;
        public DbSet<Manutentor> Manutentores { get; set; } = null!;
        public DbSet<TipoManutencao> TiposManutencao { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = default!;
        public DbSet<PatrimonioFoto> PatrimonioFotos { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =========================
            // Patrimônios: índice único no Numero
            // =========================
            modelBuilder.Entity<Patrimonio>(entity =>
            {
                entity.Property(p => p.Numero)
                      .HasMaxLength(60);

                entity.HasIndex(p => p.Numero)
                      .IsUnique();
            });

            // =========================
            // Localizações (Unidade 1:N Localizações) - sem cascade delete
            // =========================
            modelBuilder.Entity<Localizacao>()
                .HasOne(l => l.Unidade)
                .WithMany(u => u.Localizacoes)
                .HasForeignKey(l => l.UnidadeId)
                .OnDelete(DeleteBehavior.Restrict);

            // =========================
            // Tipos de Manutenção
            // - Nome único por TipoPatrimonio
            // - sem cascade delete do TipoPatrimonio
            // =========================
            modelBuilder.Entity<TipoManutencao>()
                .HasIndex(x => new { x.TipoPatrimonioId, x.Nome })
                .IsUnique();

            modelBuilder.Entity<TipoManutencao>()
                .HasOne(tm => tm.TipoPatrimonio)
                .WithMany()
                .HasForeignKey(tm => tm.TipoPatrimonioId)
                .OnDelete(DeleteBehavior.Restrict);

            // =========================
            // Usuários (Identity): Codigo único + Status válido
            // =========================
            modelBuilder.Entity<Usuario>(entity =>
            {
                entity.Property(u => u.Email).HasMaxLength(200);

                entity.Property(u => u.Status)
                      .IsRequired()
                      .HasMaxLength(10)
                      .HasDefaultValue("Ativo");

                entity.HasIndex(u => u.Codigo).IsUnique();

                entity.ToTable(t =>
                    t.HasCheckConstraint("CK_Usuarios_Status", "Status IN ('Ativo','Inativo')"));
            });

            // =========================
            // AuditLog: FK para Usuario (Identity User)
            // =========================
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.Usuario)
                .WithMany()
                .HasForeignKey(a => a.UsuarioId)
                .OnDelete(DeleteBehavior.SetNull);

            // =========================
            // PatrimonioFoto (Patrimonio 1:N Fotos) - cascade delete
            // =========================
            modelBuilder.Entity<PatrimonioFoto>()
                .HasOne(f => f.Patrimonio)
                .WithMany(p => p.Fotos)
                .HasForeignKey(f => f.PatrimonioId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
