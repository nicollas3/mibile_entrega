using Brasa.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Brasa.Api;

public sealed class DatabaseFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDb>().UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? "Host=localhost;Database=brasa;Username=brasa;Password=local-development-only", o => o.MigrationsAssembly("Brasa.Api")).Options);
}
