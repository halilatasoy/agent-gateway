namespace AgentGatewayConsole.Web.Data.Db.Repositories;

/// <summary>
/// Tüm repository'ler için temel CRUD interface'i (webds deseni).
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(params object[] keyValues);
    Task<List<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task AddRangeAsync(IEnumerable<T> entities);
    void Update(T entity);
    void Remove(T entity);
    void RemoveRange(IEnumerable<T> entities);
    Task<int> SaveChangesAsync();
}
