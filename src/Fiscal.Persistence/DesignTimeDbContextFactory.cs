using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fiscal.Persistence;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FiscalDbContext>
{
    public FiscalDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FiscalDbContext>()
            .UseNpgsql("Host=localhost;Database=fiscal_design;Username=fiscal;Password=fiscal")
            .Options;
        return new FiscalDbContext(options);
    }
}
