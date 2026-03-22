using Microsoft.EntityFrameworkCore;
using Upscale_web.Models;

namespace Upscale_web.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios { get; set; }
}