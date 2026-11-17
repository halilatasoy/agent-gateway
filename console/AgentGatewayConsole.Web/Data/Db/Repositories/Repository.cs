using Microsoft.EntityFrameworkCore;

namespace AgentGatewayConsole.Web.Data.Db.Repositories;

/// <summary>
/// IRepository&lt;T&gt; için EF Core tabanlı temel implementasyon. Spesifik repository'ler bunu extend eder.
/// </summary>
public abstract class Repository<T> : IRepository<T> where T : class
{
    protected readonly ConsoleDbContext DbContext;

    protected Repository(ConsoleDbContext dbContext)
    {
        DbContext = dbContext;
    }

    public virtual async Task<T?> GetByIdAsync(params object[] keyValues)
        => await DbContext.Set<T>().FindAsync(keyValues);

    public virtual async Task<List<T>> GetAllAsync()
        => await DbContext.Set<T>().ToListAsync();

    public virtual async Task AddAsync(T entity)
        => await DbContext.Set<T>().AddAsync(entity);

    public virtual async Task AddRangeAsync(IEnumerable<T> entities)
        => await DbContext.Set<T>().AddRangeAsync(entities);

    public virtual void Update(T entity)
        => DbContext.Set<T>().Update(entity);

    public virtual void Remove(T entity)
        => DbContext.Set<T>().Remove(entity);

    public virtual void RemoveRange(IEnumerable<T> entities)
        => DbContext.Set<T>().RemoveRange(entities);

    public virtual async Task<int> SaveChangesAsync()
        => await DbContext.SaveChangesAsync();
}
