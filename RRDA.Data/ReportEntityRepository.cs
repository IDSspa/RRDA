using Microsoft.EntityFrameworkCore;

namespace RRDA.Data
{
    public class ReportEntityRepository(RRDADbContext db)
    {
        private readonly RRDADbContext _db = db;

        public async Task<ReportEntity?> GetByIdAsync(int id) =>
            await _db.ReportEntities.Include(e => e.Properties).FirstOrDefaultAsync(e => e.Id == id);

        public async Task<List<ReportEntity>> ListByFileAsync(int reportFileId) =>
            await _db.ReportEntities.Where(e => e.ReportFileId == reportFileId)
                                     .Include(e => e.Properties)
                                     .ToListAsync();

        public async Task AddAsync(ReportEntity entity)
        {
            _db.ReportEntities.Add(entity);
            await _db.SaveChangesAsync();
        }

        public async Task UpdateAsync(ReportEntity entity)
        {
            _db.ReportEntities.Update(entity);
            await _db.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var e = await _db.ReportEntities.FindAsync(id);
            if (e == null) return;
            _db.ReportEntities.Remove(e);
            await _db.SaveChangesAsync();
        }
    }
}